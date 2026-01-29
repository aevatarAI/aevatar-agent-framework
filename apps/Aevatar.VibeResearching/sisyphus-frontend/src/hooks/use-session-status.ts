import { useEffect, useRef, useCallback } from "react"
import { useSisyphusStore, type SessionStatus } from "@/store/sisyphus-store"
import { getSessionStatus as fetchSessionStatus } from "@/lib/axiom-client"

// ============================================================================
//  Session Status Polling Hook
//  Fetches workflow steps, running tools, and agent status from API
// ============================================================================

interface UseSessionStatusOptions {
  /** Session ID to fetch status for */
  sessionId: string | null
  /** Polling interval in milliseconds (default: 3000) */
  intervalMs?: number
  /** Whether polling is enabled (default: true) */
  enabled?: boolean
}

/**
 * Hook to fetch and poll session status from API
 * Provides workflow steps, running tools, and agent status
 */
export function useSessionStatus({
  sessionId,
  intervalMs = 3000,
  enabled = true,
}: UseSessionStatusOptions) {
  const setSessionStatus = useSisyphusStore((s) => s.setSessionStatus)
  const setSessionStatusLoading = useSisyphusStore((s) => s.setSessionStatusLoading)
  
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const isFetchingRef = useRef(false)

  // Fetch status from API
  const fetchStatus = useCallback(async () => {
    if (!sessionId || isFetchingRef.current) return

    isFetchingRef.current = true
    setSessionStatusLoading(true)

    try {
      const status = await fetchSessionStatus(sessionId)
      if (status) {
        // Transform API response to store format
        const sessionStatus: SessionStatus = {
          runId: status.runId,
          updatedAt: status.updatedAt,
          steps: {
            order: status.steps.order,
            map: status.steps.map,
            running: status.steps.running,
            done: status.steps.done,
          },
          agents: status.agents,
          runningTools: status.runningTools,
        }
        setSessionStatus(sessionStatus)
      }
    } catch (error) {
      console.debug("[useSessionStatus] Failed to fetch status:", error)
    } finally {
      isFetchingRef.current = false
      setSessionStatusLoading(false)
    }
  }, [sessionId, setSessionStatus, setSessionStatusLoading])

  // Setup polling
  useEffect(() => {
    if (!sessionId || !enabled) {
      setSessionStatus(null)
      return
    }

    // Initial fetch
    fetchStatus()

    // Setup interval
    intervalRef.current = setInterval(fetchStatus, intervalMs)

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current)
        intervalRef.current = null
      }
    }
  }, [sessionId, enabled, intervalMs, fetchStatus, setSessionStatus])

  // Manual refresh function
  const refresh = useCallback(() => {
    fetchStatus()
  }, [fetchStatus])

  return { refresh }
}

/**
 * Hook to get session status (read-only)
 */
export function useSessionStatusData() {
  return useSisyphusStore((s) => s.sessionStatus)
}

/**
 * Hook to get running tools for a specific agent
 */
export function useAgentRunningTools(agentName: string | undefined) {
  return useSisyphusStore((s) => {
    if (!agentName || !s.sessionStatus) return []
    return s.sessionStatus.runningTools.filter(
      (t) => t.targetAgent.toLowerCase() === agentName.toLowerCase()
    )
  })
}

/**
 * Hook to get current step for an agent
 */
export function useAgentCurrentStep(agentName: string | undefined) {
  return useSisyphusStore((s) => {
    if (!agentName || !s.sessionStatus) return null
    const agent = s.sessionStatus.agents.find(
      (a) => a.agent.toLowerCase() === agentName.toLowerCase()
    )
    return agent?.stepName || null
  })
}

/**
 * Hook to get step status
 */
export function useStepStatus(stepName: string | undefined) {
  return useSisyphusStore((s) => {
    if (!stepName || !s.sessionStatus) return null
    return s.sessionStatus.steps.map[stepName] || null
  })
}
