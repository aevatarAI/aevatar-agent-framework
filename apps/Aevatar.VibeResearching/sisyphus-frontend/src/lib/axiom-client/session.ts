// ============================================================================
//  Axiom Client - Session API
// ============================================================================

import { fetchJson, validateSessionId, getSessionAbortController, API_BASE } from './fetch'
import type {
  AxiomSession,
  RunResult,
  SendMessagePayload,
  SessionAgentsInfo,
  AgentStateBundle,
  AgentChatMessage,
  SessionStatus,
  DagSnapshot,
  DagNodeExplain,
} from './types'

// === Session CRUD ===

export async function listSessions(): Promise<AxiomSession[]> {
  const data = await fetchJson<{ count: number; sessions: AxiomSession[] }>('/api/sessions')
  return data?.sessions || []
}

export async function listWorkflows(): Promise<string[]> {
  const data = await fetchJson<string[]>('/api/workflows')
  return data || ['hypothesis_promotion_loop']
}

export async function createSession(
  providerName?: string
): Promise<{ ok: boolean; sessionId?: string; error?: string }> {
  return fetchJson<{ ok: boolean; sessionId?: string; error?: string }>('/api/sessions', {
    method: 'POST',
    body: JSON.stringify(providerName ? { providerName } : {}),
  })
}

export async function runSession(sessionId: string | null | undefined): Promise<RunResult> {
  if (!validateSessionId(sessionId, 'runSession')) {
    return { success: false, error: 'Invalid sessionId' }
  }
  return fetchJson<RunResult>(`/api/sessions/${sessionId}/run`, { method: 'POST' })
}

export async function sendMessage(
  sessionId: string | null | undefined,
  payload: SendMessagePayload
): Promise<{ ok: boolean; runId?: string; error?: string }> {
  if (!validateSessionId(sessionId, 'sendMessage')) {
    return { ok: false, error: 'Invalid sessionId' }
  }

  const endpoint = `/api/sessions/${encodeURIComponent(sessionId)}/input`
  const body: Record<string, unknown> = {
    message: payload.text,
    mode: payload.mode || 'vibe',
  }

  if (payload.toAgents && payload.toAgents.length > 0) {
    body.toAgents = payload.toAgents
  }
  if (payload.attachmentPaths && payload.attachmentPaths.length > 0) {
    body.attachmentPaths = payload.attachmentPaths
  }
  if (payload.mode === 'vibe_loop') {
    body.loop = { maxIterations: 10, maxTotalDurationMs: 300000 }
  }

  return fetchJson<{ ok: boolean; runId?: string; error?: string }>(endpoint, {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export async function stopSession(sessionId: string | null | undefined): Promise<RunResult> {
  if (!validateSessionId(sessionId, 'stopSession')) {
    return { success: false, error: 'Invalid sessionId' }
  }
  return fetchJson<RunResult>(`/api/sessions/${sessionId}/stop`, { method: 'POST' })
}

export async function getSessionResult(sessionId: string | null | undefined): Promise<unknown> {
  if (!validateSessionId(sessionId, 'getSessionResult')) {
    return null
  }
  return fetchJson<unknown>(`/api/sessions/${sessionId}/result`)
}

// === Agent State APIs ===

export async function getSessionAgents(
  sessionId: string | null | undefined
): Promise<SessionAgentsInfo | null> {
  if (!validateSessionId(sessionId, 'getSessionAgents')) {
    return null
  }
  return fetchJson<SessionAgentsInfo>(`/api/sessions/${sessionId}/agents`)
}

export async function getAgentStates(
  sessionId: string | null | undefined,
  includeHistory = true,
  historyLimit = 50
): Promise<AgentStateBundle[]> {
  if (!validateSessionId(sessionId, 'getAgentStates')) {
    return []
  }
  const params = new URLSearchParams({
    include_history: String(includeHistory),
    history_limit: String(historyLimit),
  })
  const result = await fetchJson<{ agents?: AgentStateBundle[] }>(
    `/api/sessions/${sessionId}/agents/states?${params}`,
    undefined,
    { cache: false }
  )
  return result?.agents || []
}

export async function getAgentHistory(
  sessionId: string | null | undefined,
  agentId: string,
  limit = 50
): Promise<AgentChatMessage[]> {
  if (!sessionId || !agentId) {
    return []
  }
  const result = await fetchJson<{ history?: AgentChatMessage[] }>(
    `/api/sessions/${sessionId}/agents/${encodeURIComponent(agentId)}/history?limit=${limit}`,
    undefined,
    { cache: false }
  )
  return result?.history || []
}

// === Session Status ===

export async function getSessionStatus(
  sessionId: string | null | undefined
): Promise<SessionStatus | null> {
  if (!validateSessionId(sessionId, 'getSessionStatus')) {
    return null
  }
  const result = await fetchJson<SessionStatus>(
    `/api/sessions/${sessionId}/status`,
    undefined,
    { cache: false }
  )
  return result ?? null
}

// === DAG APIs ===

export async function getDagSnapshot(
  sessionId: string | null | undefined
): Promise<DagSnapshot | null> {
  if (!validateSessionId(sessionId, 'getDagSnapshot')) {
    return null
  }
  const result = await fetchJson<{ dag?: DagSnapshot }>(`/api/sessions/${sessionId}/dag`)
  return result?.dag ?? null
}

export async function getGlobalDagSnapshot(): Promise<DagSnapshot | null> {
  const result = await fetchJson<{ dag?: DagSnapshot }>('/api/dag/global')
  return result?.dag ?? null
}

export async function getDagNodeExplain(
  sessionId: string | null | undefined,
  nodeId: string
): Promise<DagNodeExplain | null> {
  if (!sessionId || !nodeId) {
    return null
  }
  const result = await fetchJson<{ explain?: DagNodeExplain }>(
    `/api/sessions/${encodeURIComponent(sessionId)}/dag/${encodeURIComponent(nodeId)}/explain`
  )
  return result?.explain ?? null
}

export async function getKnowledgeChain(
  sessionId: string | null | undefined,
  nodeId: string
): Promise<string> {
  if (!sessionId || !nodeId) {
    return ''
  }
  try {
    const result = await fetchJson<{ markdown?: string }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/${encodeURIComponent(nodeId)}/chain`
    )
    return result?.markdown ?? ''
  } catch {
    return ''
  }
}

// === Session Events ===

export async function getSessionEvents(sessionId: string | null | undefined): Promise<string> {
  if (!validateSessionId(sessionId, 'getSessionEvents')) {
    return ''
  }
  const sessionController = getSessionAbortController()
  const res = await fetch(`${API_BASE}/api/sessions/${sessionId}/agui/events`, {
    headers: { Accept: 'text/event-stream' },
    signal: sessionController.signal,
  })
  if (!res.ok) {
    throw new Error(`Failed to fetch events: ${res.statusText}`)
  }
  return res.text()
}
