// ============================================================================
//  Axiom Client - Review Agent APIs
// ============================================================================

import { fetchJson } from './fetch'
import type {
  ReviewAgentSettings,
  ReviewAgentSettingsUpdate,
  ReviewAgentStatus,
  IterationListResponse,
  ReviewIteration,
  ReviewGraphResponse,
} from '../../types/review-agent'

// === Review Agent Status ===

export async function getReviewAgentStatus(): Promise<
  ReviewAgentStatus & { isRunning?: boolean; hasStarted?: boolean }
> {
  const data = await fetchJson<Record<string, unknown>>('/api/review-agent/status', undefined, {
    cache: false,
  })
  return {
    status: (data.status ?? data.Status ?? 'Idle') as ReviewAgentStatus['status'],
    currentIterationId: (data.currentIterationId ?? data.CurrentIterationId ?? null) as
      | string
      | null,
    lastCompletedAt: (data.lastCompletedAt ?? data.LastCompletedAt ?? null) as string | null,
    nextScheduledAt: (data.nextScheduledAt ?? data.NextScheduledAt ?? null) as string | null,
    nodesReviewed: (data.nodesReviewed ?? data.NodesReviewed ?? 0) as number,
    nodesPending: (data.nodesPending ?? data.NodesPending ?? 0) as number,
    nodesDeactivated: (data.nodesDeactivated ?? data.NodesDeactivated ?? 0) as number,
    nodesRemoved: (data.nodesRemoved ?? data.NodesRemoved ?? 0) as number,
    errorMessage: (data.errorMessage ?? data.ErrorMessage ?? null) as string | null,
    isRunning: (data.isRunning ?? data.IsRunning) as boolean | undefined,
    hasStarted: (data.hasStarted ?? data.HasStarted) as boolean | undefined,
  }
}

// === Review Agent Settings ===

export async function getReviewAgentSettings(): Promise<ReviewAgentSettings> {
  const data = await fetchJson<Record<string, unknown>>('/api/review-agent/settings')
  return {
    iterationIntervalMinutes: (data.iterationIntervalMinutes ??
      data.IterationIntervalMinutes ??
      30) as number,
    outOfDateThresholdMinutes: (data.outOfDateThresholdMinutes ??
      data.OutOfDateThresholdMinutes ??
      60) as number,
    toDeleteThresholdMinutes: (data.toDeleteThresholdMinutes ??
      data.ToDeleteThresholdMinutes ??
      1440) as number,
    llmProviderName: (data.llmProviderName ?? data.LLMProviderName ?? 'default') as string,
    perNodeTimeoutSeconds: (data.perNodeTimeoutSeconds ??
      data.PerNodeTimeoutSeconds ??
      120) as number,
  }
}

export async function updateReviewAgentSettings(
  settings: ReviewAgentSettingsUpdate
): Promise<ReviewAgentSettings> {
  return fetchJson<ReviewAgentSettings>('/api/review-agent/settings', {
    method: 'PUT',
    body: JSON.stringify(settings),
  })
}

// === Review Agent Iterations ===

export async function getReviewAgentIterations(
  limit = 10,
  offset = 0
): Promise<IterationListResponse> {
  return fetchJson<IterationListResponse>(
    `/api/review-agent/iterations?limit=${limit}&offset=${offset}`
  )
}

export async function getReviewAgentIteration(iterationId: string): Promise<ReviewIteration> {
  return fetchJson<ReviewIteration>(
    `/api/review-agent/iterations/${encodeURIComponent(iterationId)}`
  )
}

// === Review Agent Graph ===

export async function getReviewAgentGraph(): Promise<ReviewGraphResponse> {
  return fetchJson<ReviewGraphResponse>('/api/review-agent/graph')
}

// === Review Agent Current Entries ===

export async function getReviewAgentCurrentEntries(): Promise<{
  iterationId?: string
  entries: Array<{
    entryId?: string
    nodeId: string
    nodeLabel: string
    explainContent?: string | null
    result: string
    deactivatedReason?: string | null
    timestamp: string
    verificationContent?: string | null
  }>
}> {
  return fetchJson('/api/review-agent/current-entries')
}

// === Review Agent Trigger ===

export async function triggerReviewAgent(): Promise<{
  triggered: boolean
  message: string
  isRunning?: boolean
  hasStarted?: boolean
}> {
  return fetchJson('/api/review-agent/trigger', { method: 'POST' })
}
