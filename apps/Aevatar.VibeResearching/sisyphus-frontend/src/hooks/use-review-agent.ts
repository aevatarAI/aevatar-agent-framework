import { useEffect, useRef, useCallback, useState } from 'react'
import type {
  ReviewAgentStatus,
  ReviewAgentSettings,
  ReviewIterationSummary,
  ReviewAgentCard,
  ReviewAgentStatusValue,
  ReviewResult,
} from '@/types/review-agent'

// ============================================================================
//  Review Agent SSE Hook
//  Connects to /api/review-agent/events for real-time updates
// ============================================================================

const API_BASE = '/api/review-agent'
const SSE_RECONNECT_DELAY_MS = 3000
const SSE_MAX_RECONNECT_ATTEMPTS = 10

// Session storage key for persisting review log within same iteration
const STORAGE_KEY_REVIEW_DATA = 'review-agent-data'

interface StoredReviewData {
  iterationId: string | null
  reviewLog: Array<{
    nodeId: string
    sessionId?: string | null
    nodeLabel: string
    coreDescription?: string | null
    explainContent?: string | null
    result: 'Passed' | 'Failed' | 'Skipped' | null
    timestamp: number
    deactivatedReason?: string | null
    verificationContent?: string | null
  }>
  currentNodeId: string | null
  currentNodeLabel: string | null
}

// Helper to load stored review data
function loadStoredReviewData(): StoredReviewData | null {
  try {
    const stored = sessionStorage.getItem(STORAGE_KEY_REVIEW_DATA)
    if (stored) {
      return JSON.parse(stored) as StoredReviewData
    }
  } catch (e) {
    console.warn('[useReviewAgent] Failed to load from storage:', e)
  }
  return null
}

// Helper to save review data
function saveReviewData(data: StoredReviewData): void {
  try {
    sessionStorage.setItem(STORAGE_KEY_REVIEW_DATA, JSON.stringify(data))
  } catch (e) {
    console.warn('[useReviewAgent] Failed to save to storage:', e)
  }
}

// Helper to clear review data
function clearReviewData(): void {
  try {
    sessionStorage.removeItem(STORAGE_KEY_REVIEW_DATA)
  } catch (e) {
    console.warn('[useReviewAgent] Failed to clear storage:', e)
  }
}

interface UseReviewAgentOptions {
  enabled?: boolean
  onError?: (error: Error) => void
}

interface UseReviewAgentReturn {
  // Connection state
  isConnected: boolean
  reconnectAttempts: number

  // Data state
  status: ReviewAgentStatus | null
  settings: ReviewAgentSettings | null
  currentNodeId: string | null
  currentNodeLabel: string | null
  agentCards: ReviewAgentCard[]
  reviewLog: Array<{
    nodeId: string
    nodeLabel: string
    result: ReviewResult | null
    timestamp: number
  }>

  // Counters (real-time from SSE)
  nodesReviewed: number
  nodesPending: number
  nodesDeactivated: number
  nodesRemoved: number

  // ETA calculation (passed to Progress component for real-time calculation)
  iterationStartTime: number | null

  // Actions
  refreshStatus: () => Promise<void>
  refreshSettings: () => Promise<void>
  updateSettings: (update: Partial<ReviewAgentSettings>) => Promise<ReviewAgentSettings>
  disconnect: () => void
  reconnect: () => void
}

export function useReviewAgent({
  enabled = true,
  onError,
}: UseReviewAgentOptions = {}): UseReviewAgentReturn {
  // Connection state
  const [isConnected, setIsConnected] = useState(false)
  const [reconnectAttempts, setReconnectAttempts] = useState(0)

  // Data state
  const [status, setStatus] = useState<ReviewAgentStatus | null>(null)
  const [settings, setSettings] = useState<ReviewAgentSettings | null>(null)

  // Load stored data for persistence across tab close/reopen (within same iteration)
  const storedData = loadStoredReviewData()

  // Real-time progress state
  const [currentNodeId, setCurrentNodeId] = useState<string | null>(storedData?.currentNodeId ?? null)
  const [currentNodeLabel, setCurrentNodeLabel] = useState<string | null>(storedData?.currentNodeLabel ?? null)
  const [nodesReviewed, setNodesReviewed] = useState(0)
  const [nodesPending, setNodesPending] = useState(0)
  const [nodesDeactivated, setNodesDeactivated] = useState(0)
  const [nodesRemoved, setNodesRemoved] = useState(0)

  // Iteration timing for ETA calculation
  const [iterationStartTime, setIterationStartTime] = useState<number | null>(null)

  // Agent cards for token streaming visualization
  const [agentCards, setAgentCards] = useState<ReviewAgentCard[]>([])

  // Review log for current iteration (with extended details for popup and table view)
  // Persisted to sessionStorage, cleared when iteration changes
  const [reviewLog, setReviewLog] = useState<Array<{
    nodeId: string
    sessionId?: string | null
    nodeLabel: string
    coreDescription?: string | null
    explainContent?: string | null
    result: ReviewResult | null
    timestamp: number
    deactivatedReason?: string | null
    verificationContent?: string | null
  }>>(storedData?.reviewLog ?? [])

  // Track current iteration ID for storage validation
  const currentIterationIdRef = useRef<string | null>(storedData?.iterationId ?? null)

  // Refs
  const eventSourceRef = useRef<EventSource | null>(null)
  const reconnectTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const mountedRef = useRef(true)
  const agentCardsRef = useRef<ReviewAgentCard[]>([])

  // Keep agentCardsRef in sync with state for use in callbacks
  agentCardsRef.current = agentCards

  // ── API Calls ──

  const refreshStatus = useCallback(async () => {
    try {
      const response = await fetch(`${API_BASE}/status`)
      if (!response.ok) {
        throw new Error(`Failed to fetch status: ${response.status}`)
      }
      const data = await response.json()
      console.log('[useReviewAgent] Status response:', data)

      if (mountedRef.current) {
        // Handle both camelCase and PascalCase responses for robustness
        const normalizedData: ReviewAgentStatus = {
          status: data.status ?? data.Status ?? 'Idle',
          currentIterationId: data.currentIterationId ?? data.CurrentIterationId ?? null,
          lastCompletedAt: data.lastCompletedAt ?? data.LastCompletedAt ?? null,
          nextScheduledAt: data.nextScheduledAt ?? data.NextScheduledAt ?? null,
          nodesReviewed: data.nodesReviewed ?? data.NodesReviewed ?? 0,
          nodesPending: data.nodesPending ?? data.NodesPending ?? 0,
          nodesDeactivated: data.nodesDeactivated ?? data.NodesDeactivated ?? 0,
          nodesRemoved: data.nodesRemoved ?? data.NodesRemoved ?? 0,
          errorMessage: data.errorMessage ?? data.ErrorMessage ?? null,
        }

        setStatus(normalizedData)
        // Sync counters from status
        setNodesReviewed(normalizedData.nodesReviewed)
        setNodesPending(normalizedData.nodesPending)
        setNodesDeactivated(normalizedData.nodesDeactivated)
        setNodesRemoved(normalizedData.nodesRemoved ?? 0)

        // Validate stored data against current iteration
        const storedIterationId = currentIterationIdRef.current
        const currentIterId = normalizedData.currentIterationId

        // If iteration changed or status is Idle with no active iteration, clear stale data
        if (storedIterationId && storedIterationId !== currentIterId) {
          console.log('[useReviewAgent] Iteration changed, clearing stale review log')
          setReviewLog([])
          setCurrentNodeId(null)
          setCurrentNodeLabel(null)
          clearReviewData()
        }

        // Update iteration ID ref
        currentIterationIdRef.current = currentIterId
      }
    } catch (error) {
      console.error('[useReviewAgent] Failed to refresh status:', error)
      onError?.(error instanceof Error ? error : new Error(String(error)))
    }
  }, [onError])

  const refreshSettings = useCallback(async () => {
    try {
      const response = await fetch(`${API_BASE}/settings`)
      if (!response.ok) {
        throw new Error(`Failed to fetch settings: ${response.status}`)
      }
      const data = await response.json()
      console.log('[useReviewAgent] Settings response:', data)

      if (mountedRef.current) {
        // Handle both camelCase and PascalCase responses for robustness
        const normalizedData: ReviewAgentSettings = {
          iterationIntervalMinutes: data.iterationIntervalMinutes ?? data.IterationIntervalMinutes ?? 30,
          outOfDateThresholdMinutes: data.outOfDateThresholdMinutes ?? data.OutOfDateThresholdMinutes ?? 60,
          toDeleteThresholdMinutes: data.toDeleteThresholdMinutes ?? data.ToDeleteThresholdMinutes ?? 1440,
          llmProviderName: data.llmProviderName ?? data.LLMProviderName ?? 'default',
          perNodeTimeoutSeconds: data.perNodeTimeoutSeconds ?? data.PerNodeTimeoutSeconds ?? 120,
        }
        setSettings(normalizedData)
      }
    } catch (error) {
      console.error('[useReviewAgent] Failed to refresh settings:', error)
      onError?.(error instanceof Error ? error : new Error(String(error)))
    }
  }, [onError])

  // Fetch current iteration entries from backend (for recovering state after tab reopen)
  const refreshCurrentEntries = useCallback(async () => {
    try {
      const response = await fetch(`${API_BASE}/current-entries`)
      if (!response.ok) {
        throw new Error(`Failed to fetch current entries: ${response.status}`)
      }
      const data = await response.json()
      console.log('[useReviewAgent] Current entries response:', data)

      if (mountedRef.current && data.entries && Array.isArray(data.entries)) {
        // Convert backend entries to frontend format
        const backendEntries = data.entries.map((e: {
          entryId?: string
          nodeId: string
          nodeLabel: string
          explainContent?: string | null
          result: string
          deactivatedReason?: string | null
          timestamp: string
          verificationContent?: string | null
        }) => ({
          nodeId: e.nodeId,
          sessionId: null,
          nodeLabel: e.nodeLabel,
          coreDescription: null,
          explainContent: e.explainContent ?? null,
          result: e.result as ReviewResult,
          timestamp: new Date(e.timestamp).getTime(),
          deactivatedReason: e.deactivatedReason ?? null,
          verificationContent: e.verificationContent ?? null,
        }))

        // Merge with existing reviewLog (avoid duplicates by sessionId+nodeId)
        setReviewLog(prev => {
          const existingKeys = new Set(prev.map(e => `${e.sessionId ?? ''}_${e.nodeId}`))
          const newEntries = backendEntries.filter(
            (e: { sessionId?: string | null; nodeId: string }) => !existingKeys.has(`${e.sessionId ?? ''}_${e.nodeId}`)
          )
          if (newEntries.length === 0) return prev
          // Sort by timestamp descending (newest first)
          return [...prev, ...newEntries].sort((a, b) => b.timestamp - a.timestamp)
        })

        // Update iteration ID ref
        if (data.iterationId) {
          currentIterationIdRef.current = data.iterationId
        }
      }
    } catch (error) {
      console.error('[useReviewAgent] Failed to refresh current entries:', error)
      // Don't propagate error - this is a non-critical fetch
    }
  }, [])

  const updateSettings = useCallback(
    async (update: Partial<ReviewAgentSettings>): Promise<ReviewAgentSettings> => {
      const response = await fetch(`${API_BASE}/settings`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(update),
      })
      if (!response.ok) {
        throw new Error(`Failed to update settings: ${response.status}`)
      }
      const data: ReviewAgentSettings = await response.json()
      if (mountedRef.current) {
        setSettings(data)
      }
      return data
    },
    []
  )

  // ── SSE Event Handlers ──

  const handleStatusChange = useCallback(
    (data: { status: string; nextScheduledAt: string | null }) => {
      // Validate that we have a valid status before updating state
      if (!data.status || typeof data.status !== 'string') {
        console.warn('[useReviewAgent] Received invalid status_change data, skipping state update:', data)
        return
      }

      setStatus((prev) => {
        if (!prev) return prev
        return {
          ...prev,
          status: data.status as ReviewAgentStatusValue,
          nextScheduledAt: data.nextScheduledAt,
        }
      })

      // Track iteration start time for ETA calculation
      if (data.status === 'WorkingReviewRound') {
        setIterationStartTime(Date.now())
        // Note: Don't clear reviewLog here - it's handled by iteration_complete event
        // and refreshStatus validation. This prevents clearing on tab reopen.
        setNodesReviewed(0)
      }

      // Clear current node when status changes to idle or error
      if (data.status === 'Idle' || data.status === 'Error') {
        setCurrentNodeId(null)
        setCurrentNodeLabel(null)
        setAgentCards([])
        setIterationStartTime(null)
      }
    },
    []
  )

  const handleNodeProgress = useCallback(
    (data: {
      nodeId: string
      sessionId?: string | null
      nodeLabel: string
      coreDescription?: string | null
      explainContent?: string | null
      nodesReviewed: number
      nodesPending: number
      nodesDeactivated: number
      result: ReviewResult | null
      deactivatedReason?: string | null
      verificationContent?: string | null
    }) => {
      // Validate that we have the required numeric fields before updating state
      // This prevents resetting counters when receiving incomplete SSE events
      if (typeof data.nodesReviewed !== 'number' ||
          typeof data.nodesPending !== 'number' ||
          typeof data.nodesDeactivated !== 'number') {
        console.warn('[useReviewAgent] Received invalid node_review_progress data, skipping state update:', data)
        return
      }

      // Log the completed node's result (if result is provided, this node review is complete)
      if (data.result) {
        // Capture coordinator's tokens as verification content (contains LLM reasoning)
        const coordinatorCard = agentCardsRef.current.find(c => c.agentRole === 'coordinator')
        const capturedVerificationContent = data.verificationContent ?? coordinatorCard?.tokens ?? null

        setReviewLog(prev => [
          {
            nodeId: data.nodeId,
            sessionId: data.sessionId ?? null,
            nodeLabel: data.nodeLabel,
            coreDescription: data.coreDescription ?? null,
            explainContent: data.explainContent ?? null,
            result: data.result,
            timestamp: Date.now(),
            deactivatedReason: data.deactivatedReason ?? null,
            verificationContent: capturedVerificationContent,
          },
          ...prev // newest first
        ].slice(0, 100)) // keep last 100 entries for table view
      }

      setCurrentNodeId(data.nodeId)
      setCurrentNodeLabel(data.nodeLabel)
      setNodesReviewed(data.nodesReviewed)
      setNodesPending(data.nodesPending)
      setNodesDeactivated(data.nodesDeactivated)

      // Clear agent cards when moving to a new node
      setAgentCards([])
    },
    []
  )

  const handleTokenStream = useCallback(
    (data: {
      agentId: string
      agentRole: 'coordinator' | 'worker'
      token: string
      isComplete: boolean
    }) => {
      // Validate required fields before updating state
      if (!data.agentId || typeof data.token !== 'string') {
        console.warn('[useReviewAgent] Received invalid token_stream data, skipping state update:', data)
        return
      }

      setAgentCards((prev) => {
        const existing = prev.find((c) => c.agentId === data.agentId)
        if (existing) {
          return prev.map((c) =>
            c.agentId === data.agentId
              ? {
                  ...c,
                  tokens: c.tokens + data.token,
                  isComplete: data.isComplete ?? false,
                }
              : c
          )
        }
        return [
          ...prev,
          {
            agentId: data.agentId,
            agentRole: data.agentRole ?? 'worker',
            tokens: data.token,
            isComplete: data.isComplete ?? false,
          },
        ]
      })
    },
    []
  )

  const handleIterationComplete = useCallback(
    (data: { iterationId: string; summary: ReviewIterationSummary }) => {
      // Validate summary data before updating state
      if (!data.summary || typeof data.summary.nodesReviewed !== 'number') {
        console.warn('[useReviewAgent] Received invalid iteration_complete data, skipping state update:', data)
        return
      }

      // Update status with completed iteration data
      setStatus((prev) => {
        if (!prev) return prev
        return {
          ...prev,
          currentIterationId: null,
          lastCompletedAt: data.summary.completedAt,
          nodesReviewed: data.summary.nodesReviewed,
          nodesDeactivated: data.summary.nodesDeactivated,
        }
      })
      setNodesReviewed(data.summary.nodesReviewed)
      setNodesDeactivated(data.summary.nodesDeactivated ?? 0)
      setCurrentNodeId(null)
      setCurrentNodeLabel(null)
      setAgentCards([])

      // Clear review log when iteration completes (it belongs to this iteration only)
      setReviewLog([])
      // Clear storage and reset iteration ID
      currentIterationIdRef.current = null
      clearReviewData()
    },
    []
  )

  const handleCleanupProgress = useCallback(
    (data: { nodesRemoved: number; removedNodes: Array<{ nodeId: string; nodeLabel: string }> }) => {
      // Validate nodesRemoved before updating state
      if (typeof data.nodesRemoved !== 'number') {
        console.warn('[useReviewAgent] Received invalid cleanup_progress data, skipping state update:', data)
        return
      }
      setNodesRemoved(data.nodesRemoved)
    },
    []
  )

  // ── SSE Connection Management ──

  const disconnect = useCallback(() => {
    if (reconnectTimeoutRef.current) {
      clearTimeout(reconnectTimeoutRef.current)
      reconnectTimeoutRef.current = null
    }
    if (eventSourceRef.current) {
      eventSourceRef.current.close()
      eventSourceRef.current = null
    }
    setIsConnected(false)
  }, [])

  const connect = useCallback(() => {
    if (!enabled || !mountedRef.current) return

    // Close existing connection
    if (eventSourceRef.current) {
      eventSourceRef.current.close()
    }

    console.log('[useReviewAgent] Connecting to SSE...')

    const eventSource = new EventSource(`${API_BASE}/events`)
    eventSourceRef.current = eventSource

    eventSource.onopen = () => {
      console.log('[useReviewAgent] SSE connected')
      if (mountedRef.current) {
        setIsConnected(true)
        setReconnectAttempts(0)
      }
    }

    eventSource.onerror = (error) => {
      console.error('[useReviewAgent] SSE error:', error)
      if (mountedRef.current) {
        setIsConnected(false)

        // Attempt reconnection with exponential backoff
        setReconnectAttempts((prev) => {
          const next = prev + 1
          if (next <= SSE_MAX_RECONNECT_ATTEMPTS) {
            const delay = Math.min(SSE_RECONNECT_DELAY_MS * Math.pow(2, prev), 30000)
            console.log(`[useReviewAgent] Reconnecting in ${delay}ms (attempt ${next})`)
            reconnectTimeoutRef.current = setTimeout(() => {
              if (mountedRef.current && enabled) {
                connect()
              }
            }, delay)
          } else {
            console.error('[useReviewAgent] Max reconnect attempts reached')
            onError?.(new Error('SSE connection failed after max retries'))
          }
          return next
        })
      }
    }

    // Event handlers
    eventSource.addEventListener('status_change', (event) => {
      console.log('[useReviewAgent] Received status_change:', event.data)
      try {
        const data = JSON.parse(event.data)
        handleStatusChange(data)
      } catch (error) {
        console.error('[useReviewAgent] Failed to parse status_change event:', error)
      }
    })

    eventSource.addEventListener('node_review_progress', (event) => {
      console.log('[useReviewAgent] Received node_review_progress:', event.data)
      try {
        const data = JSON.parse(event.data)
        console.log('[useReviewAgent] Parsed node_review_progress:', data)
        handleNodeProgress(data)
      } catch (error) {
        console.error('[useReviewAgent] Failed to parse node_review_progress event:', error)
      }
    })

    eventSource.addEventListener('token_stream', (event) => {
      console.log('[useReviewAgent] Received token_stream:', event.data)
      try {
        const data = JSON.parse(event.data)
        handleTokenStream(data)
      } catch (error) {
        console.error('[useReviewAgent] Failed to parse token_stream event:', error)
      }
    })

    eventSource.addEventListener('iteration_complete', (event) => {
      console.log('[useReviewAgent] Received iteration_complete:', event.data)
      try {
        const data = JSON.parse(event.data)
        handleIterationComplete(data)
      } catch (error) {
        console.error('[useReviewAgent] Failed to parse iteration_complete event:', error)
      }
    })

    eventSource.addEventListener('cleanup_progress', (event) => {
      console.log('[useReviewAgent] Received cleanup_progress:', event.data)
      try {
        const data = JSON.parse(event.data)
        handleCleanupProgress(data)
      } catch (error) {
        console.error('[useReviewAgent] Failed to parse cleanup_progress event:', error)
      }
    })
  }, [
    enabled,
    onError,
    handleStatusChange,
    handleNodeProgress,
    handleTokenStream,
    handleIterationComplete,
    handleCleanupProgress,
  ])

  const reconnect = useCallback(() => {
    setReconnectAttempts(0)
    connect()
  }, [connect])

  // ── Effects ──

  // Initial data fetch and SSE connection
  useEffect(() => {
    mountedRef.current = true

    if (enabled) {
      // Fetch initial state
      refreshStatus()
      refreshSettings()
      // Fetch current iteration entries (for tab reopen recovery)
      refreshCurrentEntries()
      // Connect to SSE
      connect()
    }

    return () => {
      mountedRef.current = false
      disconnect()
    }
  }, [enabled, connect, disconnect, refreshStatus, refreshSettings, refreshCurrentEntries])

  // Sync state on reconnect
  useEffect(() => {
    if (isConnected && reconnectAttempts === 0) {
      // Refresh state after reconnection to ensure consistency
      refreshStatus()
      // Also fetch current entries to recover any missed during disconnect
      refreshCurrentEntries()
    }
  }, [isConnected, reconnectAttempts, refreshStatus, refreshCurrentEntries])

  // Persist review data to sessionStorage for tab close/reopen within same iteration
  useEffect(() => {
    // Only save if we have an active iteration
    if (currentIterationIdRef.current || reviewLog.length > 0) {
      saveReviewData({
        iterationId: currentIterationIdRef.current,
        reviewLog,
        currentNodeId,
        currentNodeLabel,
      })
    }
  }, [reviewLog, currentNodeId, currentNodeLabel])

  return {
    // Connection state
    isConnected,
    reconnectAttempts,

    // Data state
    status,
    settings,
    currentNodeId,
    currentNodeLabel,
    agentCards,
    reviewLog,

    // Counters
    nodesReviewed,
    nodesPending,
    nodesDeactivated,
    nodesRemoved,

    // ETA (passed to Progress component for real-time calculation)
    iterationStartTime,

    // Actions
    refreshStatus,
    refreshSettings,
    updateSettings,
    disconnect,
    reconnect,
  }
}
