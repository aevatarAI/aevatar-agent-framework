// ============================================================================
//  Axiom Client - Tools & MCP APIs
// ============================================================================

import { fetchJson, validateSessionId } from './fetch'
import type { ToolSummary } from './types'

// === Tools Snapshot ===

export async function getToolsSnapshot(
  sessionId: string | null | undefined
): Promise<{ tools: ToolSummary[] }> {
  if (!validateSessionId(sessionId, 'getToolsSnapshot')) {
    return { tools: [] }
  }
  return fetchJson<{ tools: ToolSummary[] }>(
    `/api/sessions/${encodeURIComponent(sessionId)}/tools`
  )
}

// === MCP Management ===

export async function reconnectMcp(sessionId: string | null | undefined): Promise<unknown> {
  if (!validateSessionId(sessionId, 'reconnectMcp')) {
    return { ok: false, error: 'Invalid sessionId' }
  }
  return fetchJson<unknown>(
    `/api/sessions/${encodeURIComponent(sessionId)}/mcp/reconnect`,
    { method: 'POST' }
  )
}

// === Skills Sync ===

export async function syncSkills(): Promise<unknown> {
  return fetchJson<unknown>('/api/skills/sync', { method: 'POST' })
}

export async function getSkillsSyncStatus(): Promise<unknown> {
  return fetchJson<unknown>('/api/skills/sync/status')
}

// === Per-Agent Provider Configuration ===

export async function getAgentProviders(sessionId: string | null | undefined): Promise<unknown> {
  if (!validateSessionId(sessionId, 'getAgentProviders')) {
    return {}
  }
  return fetchJson<unknown>(`/api/sessions/${encodeURIComponent(sessionId)}/agent-providers`)
}

export async function setAgentProvider(
  sessionId: string | null | undefined,
  agent: string,
  providerName: string
): Promise<unknown> {
  if (!validateSessionId(sessionId, 'setAgentProvider')) {
    return { ok: false, error: 'Invalid sessionId' }
  }
  return fetchJson<unknown>(
    `/api/sessions/${encodeURIComponent(sessionId)}/agent-providers`,
    {
      method: 'PUT',
      body: JSON.stringify({ agent, providerName }),
    }
  )
}
