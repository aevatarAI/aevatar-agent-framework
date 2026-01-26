// ============================================================================
//  Axiom Client - Knowledge Graph APIs
// ============================================================================

import { fetchJson, validateSessionId } from './fetch'
import type { NodeExplanation, SessionSummary, DagSummary, PivotSnapshot } from './types'

// === Node Explanation ===

export async function getNodeExplanation(
  sessionId: string | null | undefined,
  nodeId: string
): Promise<NodeExplanation | null> {
  if (!sessionId || !nodeId) {
    return null
  }
  try {
    const result = await fetchJson<{ explanation?: NodeExplanation }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/${encodeURIComponent(nodeId)}/explain`
    )
    return result?.explanation ?? null
  } catch {
    return null
  }
}

// === Session Summary ===

export async function getSessionSummary(
  sessionId: string | null | undefined
): Promise<SessionSummary | null> {
  if (!validateSessionId(sessionId, 'getSessionSummary')) {
    return null
  }
  try {
    const result = await fetchJson<{ summary?: SessionSummary }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/summary`
    )
    return result?.summary ?? null
  } catch {
    return null
  }
}

// === DAG Summary ===

export async function getFullDagSummary(
  sessionId: string | null | undefined
): Promise<DagSummary | null> {
  if (!validateSessionId(sessionId, 'getFullDagSummary')) {
    return null
  }
  try {
    const result = await fetchJson<{ summary?: DagSummary }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/dag-summary`
    )
    return result?.summary ?? null
  } catch {
    return null
  }
}

// === Pivot Snapshots ===

export async function createPivotSnapshot(
  sessionId: string | null | undefined,
  reason: string
): Promise<{ snapshotId?: string; error?: string }> {
  if (!validateSessionId(sessionId, 'createPivotSnapshot')) {
    return { error: 'Invalid sessionId' }
  }
  try {
    return await fetchJson<{ snapshotId?: string; error?: string }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/pivot`,
      {
        method: 'POST',
        body: JSON.stringify({ reason }),
      }
    )
  } catch (e) {
    return { error: (e as Error)?.message || 'Unknown error' }
  }
}

export async function getPivotSnapshots(
  sessionId: string | null | undefined
): Promise<PivotSnapshot[]> {
  if (!validateSessionId(sessionId, 'getPivotSnapshots')) {
    return []
  }
  try {
    const result = await fetchJson<{ snapshots?: PivotSnapshot[] }>(
      `/api/sessions/${encodeURIComponent(sessionId)}/graph/pivots`
    )
    return result?.snapshots ?? []
  } catch {
    return []
  }
}
