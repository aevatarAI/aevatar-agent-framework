import { useEffect, useRef, useState } from 'react'
import { connectAgUi, type AgUiConnection, type AgUiMessage } from './lib/agui'
import { NotebookPicker } from './features/notebooks/NotebookPicker'
import { SourcesPanel } from './features/sources/SourcesPanel'
import { ReportsPanel } from './features/reports/ReportsPanel'
import { EncyclopediaPanel } from './features/encyclopedia/EncyclopediaPanel'
import { CardsPanel } from './features/cards/CardsPanel'
import { QuizPanel } from './features/quiz/QuizPanel'
import { SkillsPanel } from './features/skills/SkillsPanel'

type UiMessage = {
  id: string
  role: string
  content: string
}

type DebugEvent = {
  type: string
  payload: unknown
}

const NOTEBOOK_SESSIONS_KEY = 'aevatar.learning.notebookSessions'
const SELECTED_NOTEBOOK_KEY = 'aevatar.learning.selectedNotebookId'

function loadNotebookSessions(): Record<string, string> {
  try {
    const raw = localStorage.getItem(NOTEBOOK_SESSIONS_KEY)
    if (!raw) return {}
    const v = JSON.parse(raw)
    if (!v || typeof v !== 'object') return {}
    const out: Record<string, string> = {}
    for (const [k, val] of Object.entries(v)) {
      if (typeof k === 'string' && typeof val === 'string' && k.trim() && val.trim()) {
        out[k.trim()] = val.trim()
      }
    }
    return out
  } catch {
    return {}
  }
}

function saveNotebookSessions(map: Record<string, string>) {
  try {
    localStorage.setItem(NOTEBOOK_SESSIONS_KEY, JSON.stringify(map))
  } catch {
    // ignore
  }
}

function loadSelectedNotebookId(): string {
  try {
    return (localStorage.getItem(SELECTED_NOTEBOOK_KEY) ?? '').trim()
  } catch {
    return ''
  }
}

function saveSelectedNotebookId(id: string) {
  try {
    localStorage.setItem(SELECTED_NOTEBOOK_KEY, (id ?? '').trim())
  } catch {
    // ignore
  }
}

export function App() {
  const [notebookSessions, setNotebookSessions] = useState<Record<string, string>>(() => loadNotebookSessions())
  const [notebookId, setNotebookId] = useState<string>(() => loadSelectedNotebookId())
  const [sessionId, setSessionId] = useState<string>('')
  const [providerName, setProviderName] = useState<string>('')
  const [input, setInput] = useState<string>('')
  const [messages, setMessages] = useState<UiMessage[]>([])
  const [debugEvents, setDebugEvents] = useState<DebugEvent[]>([])
  const [isConnected, setIsConnected] = useState(false)
  const [progress, setProgress] = useState<any>(null)
  const [rightTab, setRightTab] = useState<'sources' | 'reports' | 'encyclopedia' | 'cards' | 'quiz' | 'skills' | 'debug'>(
    'sources',
  )

  const connRef = useRef<AgUiConnection | null>(null)

  useEffect(() => {
    saveNotebookSessions(notebookSessions)
  }, [notebookSessions])

  useEffect(() => {
    saveSelectedNotebookId(notebookId)
  }, [notebookId])

  useEffect(() => {
    if (!notebookId) return

    // Load notebook detail + progress summary
    void (async () => {
      try {
        const resp = await fetch(`/api/notebooks/${notebookId}`)
        const data = await resp.json()
        if (!resp.ok) throw new Error(data?.error ?? 'failed to load notebook')
        setProgress(data?.progressSummary ?? null)
      } catch (e: any) {
        setProgress({ error: String(e?.message ?? e ?? 'failed') })
      }
    })()
  }, [notebookId])

  useEffect(() => {
    if (!notebookId) {
      setSessionId('')
      return
    }

    const existing = notebookSessions[notebookId]
    if (existing) {
      setSessionId(existing)
      return
    }

    // Create and bind session for this notebook
    void (async () => {
      try {
        const resp = await fetch('/api/sessions', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ providerName: providerName.trim() || undefined }),
        })
        const data = await resp.json()
        if (!resp.ok) throw new Error(data?.error ?? 'failed to create session')
        const id = String(data.sessionId || '')
        if (!id) throw new Error('missing sessionId')

        setNotebookSessions((prev) => ({ ...prev, [notebookId]: id }))
        setSessionId(id)
      } catch (e) {
        setSessionId('')
      }
    })()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [notebookId])

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

  async function resetSession() {
    if (!notebookId) return
    const resp = await fetch('/api/sessions', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ providerName: providerName.trim() || undefined }),
    })
    const data = await resp.json()
    if (!resp.ok) throw new Error(data?.error ?? 'failed to create session')
    const id = String(data.sessionId || '')
    if (!id) throw new Error('missing sessionId')

    setNotebookSessions((prev) => ({ ...prev, [notebookId]: id }))
    setSessionId(id)
  }

  async function sendInput() {
    const msg = input.trim()
    if (!msg || !sessionId || !notebookId) return
    setInput('')

    await fetch(`/api/sessions/${sessionId}/input`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: msg, providerName: providerName.trim() || undefined, notebookId }),
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

      <div style={{ padding: 12, borderBottom: '1px solid #e5e5e5', display: 'flex', flexDirection: 'column', gap: 10 }}>
        <NotebookPicker value={notebookId} onChange={(id) => setNotebookId(id)} />

        <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
          <div style={{ fontSize: 12, color: '#444' }}>
            <strong>session</strong>: <span style={{ color: '#666' }}>{sessionId || '(none)'}</span>
          </div>
          <button onClick={() => void resetSession()} disabled={!notebookId} style={{ padding: '6px 10px' }}>
            New Session for Notebook
          </button>
          <div style={{ marginLeft: 'auto', fontSize: 12, color: '#444' }}>
            <strong>progress</strong>:{' '}
            {progress?.error ? (
              <span style={{ color: '#b91c1c' }}>{String(progress.error)}</span>
            ) : progress ? (
              <span style={{ color: '#666' }}>
                due {Number(progress.dueCards ?? 0)} · new {Number(progress.newCards ?? 0)} · sources{' '}
                {Number(progress.totalSources ?? 0)} · reports {Number(progress.totalReports ?? 0)} · quizzes{' '}
                {Number(progress.quizzesTaken ?? 0)}
              </span>
            ) : (
              <span style={{ color: '#999' }}>(not loaded)</span>
            )}
          </div>
        </div>
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
          <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 10 }}>
            <button
              onClick={() => setRightTab('sources')}
              style={{
                padding: '6px 10px',
                border: '1px solid #ddd',
                background: rightTab === 'sources' ? '#111827' : '#fff',
                color: rightTab === 'sources' ? '#fff' : '#111',
              }}
            >
              Sources
            </button>
            <button
              onClick={() => setRightTab('reports')}
              style={{
                padding: '6px 10px',
                border: '1px solid #ddd',
                background: rightTab === 'reports' ? '#111827' : '#fff',
                color: rightTab === 'reports' ? '#fff' : '#111',
              }}
            >
              Reports
            </button>
            <button
              onClick={() => setRightTab('encyclopedia')}
              style={{
                padding: '6px 10px',
                border: '1px solid #ddd',
                background: rightTab === 'encyclopedia' ? '#111827' : '#fff',
                color: rightTab === 'encyclopedia' ? '#fff' : '#111',
              }}
            >
              Encyclopedia
            </button>
            <button
              onClick={() => setRightTab('cards')}
              style={{
                padding: '6px 10px',
                border: '1px solid #ddd',
                background: rightTab === 'cards' ? '#111827' : '#fff',
                color: rightTab === 'cards' ? '#fff' : '#111',
              }}
            >
              Cards
            </button>
            <button
              onClick={() => setRightTab('quiz')}
              style={{
                padding: '6px 10px',
                border: '1px solid #ddd',
                background: rightTab === 'quiz' ? '#111827' : '#fff',
                color: rightTab === 'quiz' ? '#fff' : '#111',
              }}
            >
              Quiz
            </button>
            <button
              onClick={() => setRightTab('skills')}
              style={{
                padding: '6px 10px',
                border: '1px solid #ddd',
                background: rightTab === 'skills' ? '#111827' : '#fff',
                color: rightTab === 'skills' ? '#fff' : '#111',
              }}
            >
              Skills
            </button>
            <button
              onClick={() => setRightTab('debug')}
              style={{
                padding: '6px 10px',
                border: '1px solid #ddd',
                background: rightTab === 'debug' ? '#111827' : '#fff',
                color: rightTab === 'debug' ? '#fff' : '#111',
              }}
            >
              Debug
            </button>
            <span style={{ marginLeft: 'auto', fontSize: 12, color: '#666' }}>
              {rightTab === 'debug' ? 'last 40 events' : notebookId ? 'notebook-scoped' : 'select notebook'}
            </span>
          </div>

          {rightTab === 'sources' ? (
            notebookId ? (
              <SourcesPanel notebookId={notebookId} />
            ) : (
              <div style={{ fontSize: 12, color: '#999' }}>(select a notebook first)</div>
            )
          ) : rightTab === 'reports' ? (
            notebookId ? (
              <ReportsPanel notebookId={notebookId} />
            ) : (
              <div style={{ fontSize: 12, color: '#999' }}>(select a notebook first)</div>
            )
          ) : rightTab === 'encyclopedia' ? (
            notebookId ? (
              <EncyclopediaPanel notebookId={notebookId} />
            ) : (
              <div style={{ fontSize: 12, color: '#999' }}>(select a notebook first)</div>
            )
          ) : rightTab === 'cards' ? (
            notebookId ? (
              <CardsPanel notebookId={notebookId} />
            ) : (
              <div style={{ fontSize: 12, color: '#999' }}>(select a notebook first)</div>
            )
          ) : rightTab === 'quiz' ? (
            notebookId ? (
              <QuizPanel notebookId={notebookId} />
            ) : (
              <div style={{ fontSize: 12, color: '#999' }}>(select a notebook first)</div>
            )
          ) : rightTab === 'skills' ? (
            notebookId ? (
              <SkillsPanel notebookId={notebookId} />
            ) : (
              <div style={{ fontSize: 12, color: '#999' }}>(select a notebook first)</div>
            )
          ) : (
            <pre style={{ fontSize: 12, lineHeight: 1.35, background: '#fafafa', padding: 10, border: '1px solid #eee' }}>
              {JSON.stringify(debugEvents.slice(-40), null, 2)}
            </pre>
          )}
        </aside>
      </div>
    </div>
  )
}


