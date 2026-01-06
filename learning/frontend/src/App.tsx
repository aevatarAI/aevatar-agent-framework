import { useEffect, useRef, useState } from 'react'
import { connectAgUi, type AgUiConnection, type AgUiMessage } from './lib/agui'

type UiMessage = {
  id: string
  role: string
  content: string
}

type DebugEvent = {
  type: string
  payload: unknown
}

const STORAGE_KEY = 'aevatar.learning.sessionIds'

function loadSessionIds(): string[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return []
    const v = JSON.parse(raw)
    return Array.isArray(v) ? v.filter((x) => typeof x === 'string') : []
  } catch {
    return []
  }
}

function saveSessionIds(ids: string[]) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(ids))
  } catch {
    // ignore
  }
}

export function App() {
  const [sessionIds, setSessionIds] = useState<string[]>(() => loadSessionIds())
  const [sessionId, setSessionId] = useState<string>(sessionIds[0] ?? '')
  const [providerName, setProviderName] = useState<string>('')
  const [input, setInput] = useState<string>('')
  const [messages, setMessages] = useState<UiMessage[]>([])
  const [debugEvents, setDebugEvents] = useState<DebugEvent[]>([])
  const [isConnected, setIsConnected] = useState(false)

  const connRef = useRef<AgUiConnection | null>(null)

  useEffect(() => {
    saveSessionIds(sessionIds)
  }, [sessionIds])

  useEffect(() => {
    if (!sessionId) return

    // Cleanup previous connection
    connRef.current?.close()
    connRef.current = null

    setIsConnected(false)
    setDebugEvents([])

    const url = `/api/sessions/${sessionId}/agui/events`

    const pushDebug = (type: string, payload: unknown) => {
      setDebugEvents((prev) => {
        const next = [...prev, { type, payload }]
        return next.length > 200 ? next.slice(next.length - 200) : next
      })
    }

    const upsertMessage = (id: string, role: string, patch: Partial<UiMessage>) => {
      setMessages((prev) => {
        const idx = prev.findIndex((m) => m.id === id)
        const base: UiMessage = idx >= 0 ? prev[idx] : { id, role, content: '' }
        const merged: UiMessage = {
          ...base,
          ...patch,
          id,
          role: patch.role ?? base.role ?? role,
          content: patch.content ?? base.content,
        }
        if (idx >= 0) {
          const next = prev.slice()
          next[idx] = merged
          return next
        }
        return [...prev, merged]
      })
    }

    const appendDelta = (id: string, role: string, delta: string) => {
      if (!id) return
      if (!delta) return

      setMessages((prev) => {
        const idx = prev.findIndex((m) => m.id === id)
        if (idx < 0) {
          return [...prev, { id, role, content: delta }]
        }
        const next = prev.slice()
        next[idx] = { ...next[idx], role: next[idx].role || role, content: (next[idx].content || '') + delta }
        return next
      })
    }

    connRef.current = connectAgUi({
      url,
      onMessagesSnapshot: (msgs: AgUiMessage[]) => {
        const next: UiMessage[] = msgs
          .filter((m) => !!m.id)
          .map((m) => ({ id: m.id, role: m.role || 'assistant', content: m.content || '' }))
        setMessages(next)
        pushDebug('MESSAGES_SNAPSHOT', { count: next.length })
        setIsConnected(true)
      },
      onTextStart: (e) => {
        upsertMessage(String(e?.messageId ?? ''), String(e?.role ?? 'assistant'), { content: '' })
      },
      onTextContent: (e) => {
        const id = String(e?.messageId ?? '')
        if (!id) return
        appendDelta(id, 'assistant', String(e?.delta ?? ''))
      },
      onTextEnd: (e) => pushDebug('TEXT_MESSAGE_END', { messageId: e?.messageId }),
      onRun: (type, e) => pushDebug(type, e),
      onStep: (type, e) => pushDebug(type, e),
      onCustom: (e) => pushDebug('CUSTOM', e),
      onState: (type, e) => pushDebug(type, e),
    })

    return () => {
      connRef.current?.close()
    }
  }, [sessionId])

  async function createSession() {
    const resp = await fetch('/api/sessions', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ providerName: providerName.trim() || undefined }),
    })
    const data = await resp.json()
    if (!resp.ok) throw new Error(data?.error ?? 'failed to create session')
    const id = String(data.sessionId || '')
    if (!id) throw new Error('missing sessionId')

    setSessionIds((prev) => (prev.includes(id) ? prev : [id, ...prev]))
    setSessionId(id)
  }

  async function sendInput() {
    const msg = input.trim()
    if (!msg || !sessionId) return
    setInput('')

    await fetch(`/api/sessions/${sessionId}/input`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: msg, providerName: providerName.trim() || undefined }),
    })
  }

  return (
    <div style={{ height: '100vh', display: 'flex', flexDirection: 'column', fontFamily: 'system-ui, sans-serif' }}>
      <header style={{ padding: 12, borderBottom: '1px solid #e5e5e5', display: 'flex', gap: 12 }}>
        <strong>Aevatar.Learning</strong>
        <span style={{ color: '#666' }}>{isConnected ? 'connected' : 'connecting...'}</span>

        <div style={{ marginLeft: 'auto', display: 'flex', gap: 8, alignItems: 'center' }}>
          <label style={{ fontSize: 12, color: '#444' }}>provider</label>
          <input
            value={providerName}
            onChange={(e) => setProviderName(e.target.value)}
            placeholder="(optional) providerName"
            style={{ padding: '6px 8px', width: 220 }}
          />
        </div>
      </header>

      <div style={{ padding: 12, borderBottom: '1px solid #e5e5e5', display: 'flex', gap: 12 }}>
        <button onClick={() => createSession()} style={{ padding: '6px 10px' }}>
          New Session
        </button>
        <select
          value={sessionId}
          onChange={(e) => setSessionId(e.target.value)}
          style={{ padding: '6px 8px', minWidth: 320 }}
        >
          <option value="" disabled>
            Select session...
          </option>
          {sessionIds.map((id) => (
            <option key={id} value={id}>
              {id}
            </option>
          ))}
        </select>
      </div>

      <div style={{ flex: 1, display: 'flex', minHeight: 0 }}>
        <main style={{ flex: 2, display: 'flex', flexDirection: 'column', borderRight: '1px solid #e5e5e5' }}>
          <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
            {messages.map((m) => (
              <div key={m.id} style={{ marginBottom: 10 }}>
                <div style={{ fontSize: 12, color: '#666' }}>
                  <strong>{m.role}</strong> <span style={{ color: '#999' }}>{m.id}</span>
                </div>
                <div style={{ whiteSpace: 'pre-wrap', lineHeight: 1.45 }}>{m.content}</div>
              </div>
            ))}
          </div>

          <div style={{ borderTop: '1px solid #e5e5e5', padding: 12, display: 'flex', gap: 8 }}>
            <input
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault()
                  void sendInput()
                }
              }}
              placeholder={sessionId ? 'Type message and press Enter...' : 'Create/select a session first...'}
              disabled={!sessionId}
              style={{ flex: 1, padding: '10px 12px' }}
            />
            <button onClick={() => void sendInput()} disabled={!sessionId || !input.trim()} style={{ padding: '10px 12px' }}>
              Send
            </button>
          </div>
        </main>

        <aside style={{ flex: 1, minWidth: 360, overflow: 'auto', padding: 12 }}>
          <div style={{ fontSize: 12, color: '#666', marginBottom: 8 }}>
            <strong>State / Events (debug)</strong>
          </div>
          <pre style={{ fontSize: 12, lineHeight: 1.35, background: '#fafafa', padding: 10, border: '1px solid #eee' }}>
            {JSON.stringify(debugEvents.slice(-40), null, 2)}
          </pre>
        </aside>
      </div>
    </div>
  )
}


