// ============================================================================
//  Axiom Client - Agent & Worker Utilities
// ============================================================================

import type { ParsedWorker, ParsedHistoryItem } from './types'

// === Worker Name Helper ===

function getWorkerName(id: string): string {
  const match = id.match(/worker-(\d+)/)
  if (match) return `Worker ${match[1]}`
  return id
}

// === Parse Workers from SSE Events ===

export function parseWorkersFromEvents(eventsText: string): Map<string, ParsedWorker> {
  const workers = new Map<string, ParsedWorker>()

  const lines = eventsText.split('\n')
  for (const line of lines) {
    if (!line.startsWith('data: ')) continue
    try {
      const jsonStr = line.slice(6)
      const event = JSON.parse(jsonStr)

      if (event.type !== 'ProgressEvent') continue
      if (!event.workerId || event.workerId === 'coordinator') continue

      const workerId = event.workerId as string
      const existing = workers.get(workerId) || {
        id: workerId,
        name: getWorkerName(workerId),
        status: 'running' as const,
        history: [],
      }

      let status: 'pending' | 'running' | 'completed' | 'error' = 'running'
      if (event.stepStatus === 'Completed') status = 'completed'
      else if (event.stepStatus === 'Failed') status = 'error'

      const stepId = event.stepId || ''
      const systemPrompt = event.systemPrompt || ''
      const userPrompt = event.userPrompt || ''
      const tokenDelta = event.tokenDelta || ''
      const responseContent = event.assistantResponse || event.assistantResponsePreview || ''

      let newHistory = [...existing.history]
      if (stepId && (systemPrompt || userPrompt || tokenDelta || responseContent)) {
        const existingHistoryIdx = newHistory.findIndex((h) => h.stepId === stepId)

        if (existingHistoryIdx >= 0) {
          const existingItem = newHistory[existingHistoryIdx]
          newHistory[existingHistoryIdx] = {
            ...existingItem,
            system: existingItem.system || systemPrompt,
            user: existingItem.user || userPrompt,
            response: tokenDelta
              ? (existingItem.response || '') + tokenDelta
              : responseContent || existingItem.response,
          }
        } else {
          newHistory = [
            {
              stepId,
              timestamp: event.timestamp || Date.now(),
              phase: event.phase || '',
              status: event.stepStatus || 'Running',
              system: systemPrompt || undefined,
              user: userPrompt || undefined,
              response: tokenDelta || responseContent || undefined,
            } as ParsedHistoryItem,
            ...newHistory.slice(0, 11),
          ]
        }
      }

      workers.set(workerId, {
        id: workerId,
        name: getWorkerName(workerId),
        status: status,
        provider: event.providerName || existing.provider,
        stepId: event.stepId || existing.stepId,
        stepType: event.stepType || existing.stepType,
        tokenIndex: event.tokenIndex ?? existing.tokenIndex,
        lastResponse: responseContent || event.message || existing.lastResponse,
        history: newHistory,
      })
    } catch {
      // Skip invalid JSON lines
    }
  }

  return workers
}
