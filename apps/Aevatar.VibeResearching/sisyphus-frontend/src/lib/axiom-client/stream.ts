// ============================================================================
//  Axiom Client - Event Stream Factory
// ============================================================================

import {
  createEventStream,
  type EventStream,
  type AevatarProgressEvent,
} from '@aevatar/kit-protocol'
import { streamLogger } from '../logger'
import { API_BASE } from './fetch'

// === Custom Event Types ===

interface AxiomCustomEvents {
  [key: string]: unknown
  'aevatar.progress': AevatarProgressEvent
  'aevatar.axiom.status_snapshot': {
    status: string
    phase: string
    progressPercent: number
    totalTokens: number
    totalLlmCalls: number
  }
  'aevatar.axiom.graph': {
    iteration: number
    axioms: unknown[]
    assumptions: unknown[]
    theorems: unknown[]
  }
}

// === Event Stream Factory ===

export function createAxiomEventStream(sessionId: string): EventStream<AxiomCustomEvents> {
  const url = `${API_BASE}/api/chat/sessions/${sessionId}/agui/events`

  return createEventStream<AxiomCustomEvents>({
    url,
    autoReconnect: true,
    reconnectDelayMs: 2000,
    maxReconnectAttempts: 5,
    onError: (error, context) => {
      streamLogger.error('EventStream error:', error, context)
    },
    onReconnecting: () => {
      // Silent reconnection
    },
    onReconnectFailed: () => {
      streamLogger.error('All reconnection attempts failed')
    },
  })
}

export type { AxiomCustomEvents, EventStream }
