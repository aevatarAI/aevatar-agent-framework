import { useEffect, useRef, useCallback } from "react"
import { useSisyphusStore, type AgentStateData } from "@/store/sisyphus-store"
import { getAgentStates, type AgentStateBundle } from "@/lib/axiom-client"

// ============================================================================
//  Agent States Polling Hook
//  Fetches precise agent data from Session API at regular intervals
// ============================================================================

interface UseAgentStatesOptions {
  /** Session ID to fetch states for */
  sessionId: string | null
  /** Polling interval in milliseconds (default: 5000) */
  intervalMs?: number
  /** Whether to include chat history (default: true) */
  includeHistory?: boolean
  /** Max history entries per agent (default: 20) */
  historyLimit?: number
  /** Whether polling is enabled (default: true) */
  enabled?: boolean
}

/**
 * Hook to fetch and poll agent states from Session API
 * Provides precise token usage, history, and last activity data
 */
export function useAgentStates({
  sessionId,
  intervalMs = 5000,
  includeHistory = true,
  historyLimit = 20,
  enabled = true,
}: UseAgentStatesOptions) {
  const setAgentStates = useSisyphusStore((s) => s.setAgentStates)
  const setAgentStatesLoading = useSisyphusStore((s) => s.setAgentStatesLoading)
  const clearAgentStates = useSisyphusStore((s) => s.clearAgentStates)
  
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const isFetchingRef = useRef(false)

  // Transform API response to store format
  const transformStates = useCallback((bundles: AgentStateBundle[]): AgentStateData[] => {
    return bundles.map((bundle) => ({
      agentId: bundle.agentId,
      history: bundle.state.history.map((msg) => ({
        id: msg.id,
        role: msg.role,
        content: msg.content,
        toolCalls: msg.toolCalls?.map((tc) => ({
          id: tc.id,
          toolName: tc.toolName,
          arguments: tc.arguments,
        })),
        tokenUsed: msg.tokenUsed,
        timestamp: msg.timestamp,
        metadata: msg.metadata,
      })),
      totalTokenUsed: bundle.state.totalTokenUsed,
      lastActivity: bundle.state.lastActivity,
      context: bundle.state.context,
    }))
  }, [])

  // Fetch states from API
  const fetchStates = useCallback(async () => {
    if (!sessionId || isFetchingRef.current) return

    isFetchingRef.current = true
    setAgentStatesLoading(true)

    try {
      const bundles = await getAgentStates(sessionId, includeHistory, historyLimit)
      if (bundles.length > 0) {
        const states = transformStates(bundles)
        setAgentStates(states)
      }
    } catch (error) {
      // Silently fail - API may not be available yet
      console.debug("[useAgentStates] Failed to fetch states:", error)
    } finally {
      isFetchingRef.current = false
      setAgentStatesLoading(false)
    }
  }, [sessionId, includeHistory, historyLimit, setAgentStates, setAgentStatesLoading, transformStates])

  // Setup polling
  useEffect(() => {
    if (!sessionId || !enabled) {
      // Clear states when disabled or no session
      clearAgentStates()
      return
    }

    // Initial fetch
    fetchStates()

    // Setup interval
    intervalRef.current = setInterval(fetchStates, intervalMs)

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current)
        intervalRef.current = null
      }
    }
  }, [sessionId, enabled, intervalMs, fetchStates, clearAgentStates])

  // Manual refresh function
  const refresh = useCallback(() => {
    fetchStates()
  }, [fetchStates])

  return { refresh }
}

/**
 * Hook to get agent state for a specific agent (read-only)
 */
export function useAgentState(agentId: string | undefined) {
  return useSisyphusStore((s) => agentId ? s.agentStates[agentId] : undefined)
}

/**
 * Hook to get agent LLM status (real-time from SSE)
 */
export function useAgentLlmStatus(agentId: string | undefined) {
  return useSisyphusStore((s) => agentId ? s.agentLlmStatus[agentId] : undefined)
}

/**
 * Selector for all agent states
 */
export function useAllAgentStates() {
  return useSisyphusStore((s) => s.agentStates)
}
