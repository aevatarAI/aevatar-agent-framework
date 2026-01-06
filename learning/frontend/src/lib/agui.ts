import { AgUiClient } from '@agui/sdk'

// ============================================================
//  AG-UI Client Wrapper (snapshot-first)
//
//  Principles:
//  - Reconnect should NOT rely on replay.
//  - UI state should be restored from MESSAGES_SNAPSHOT (and optional STATE_SNAPSHOT).
// ============================================================

export type AgUiMessage = {
  id: string
  role: string
  content: string
}

export type AgUiConnectOptions = {
  url: string
  onMessagesSnapshot?: (messages: AgUiMessage[], raw: any) => void
  onTextStart?: (e: any) => void
  onTextContent?: (e: any) => void
  onTextEnd?: (e: any) => void
  onRun?: (type: string, e: any) => void
  onStep?: (type: string, e: any) => void
  onCustom?: (e: any) => void
  onState?: (type: string, e: any) => void
}

export type AgUiConnection = {
  close: () => void
}

export function connectAgUi(options: AgUiConnectOptions): AgUiConnection {
  const client: any = new (AgUiClient as any)(options.url)

  const safeOn = (type: string, handler: (e: any) => void) => {
    try {
      client.on?.(type, handler)
    } catch {
      // ignore
    }
  }

  // Snapshot-first recovery
  safeOn('MESSAGES_SNAPSHOT', (e: any) => {
    const messages = Array.isArray(e?.messages) ? e.messages : []
    options.onMessagesSnapshot?.(
      messages.map((m: any) => ({
        id: String(m?.id ?? ''),
        role: String(m?.role ?? 'assistant'),
        content: String(m?.content ?? ''),
      })),
      e,
    )
  })

  safeOn('TEXT_MESSAGE_START', (e: any) => options.onTextStart?.(e))
  safeOn('TEXT_MESSAGE_CONTENT', (e: any) => options.onTextContent?.(e))
  safeOn('TEXT_MESSAGE_END', (e: any) => options.onTextEnd?.(e))

  safeOn('RUN_STARTED', (e: any) => options.onRun?.('RUN_STARTED', e))
  safeOn('RUN_FINISHED', (e: any) => options.onRun?.('RUN_FINISHED', e))
  safeOn('RUN_ERROR', (e: any) => options.onRun?.('RUN_ERROR', e))

  safeOn('STEP_STARTED', (e: any) => options.onStep?.('STEP_STARTED', e))
  safeOn('STEP_FINISHED', (e: any) => options.onStep?.('STEP_FINISHED', e))

  safeOn('CUSTOM', (e: any) => options.onCustom?.(e))
  safeOn('STATE_SNAPSHOT', (e: any) => options.onState?.('STATE_SNAPSHOT', e))
  safeOn('STATE_DELTA', (e: any) => options.onState?.('STATE_DELTA', e))

  try {
    client.connect?.()
  } catch {
    // ignore
  }

  return {
    close: () => {
      try {
        client.close?.()
        client.disconnect?.()
      } catch {
        // ignore
      }
    },
  }
}


