import { useEffect, useMemo, useState } from 'react'

export type NotebookListItem = {
  notebookId: string
  displayName: string
  createdAt?: string
  updatedAt?: string
}

type ListResponse = {
  count?: number
  notebooks?: Array<{
    notebookId: string
    displayName: string
    createdAt?: string
    updatedAt?: string
  }>
}

type CreateResponse = {
  ok?: boolean
  notebook?: {
    notebookId: string
    displayName: string
    createdAt?: string
    updatedAt?: string
  }
  error?: string
}

export function NotebookPicker(props: {
  value: string
  onChange: (notebookId: string) => void
}) {
  const { value, onChange } = props

  const [items, setItems] = useState<NotebookListItem[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string>('')

  const [newName, setNewName] = useState('')
  const canCreate = useMemo(() => newName.trim().length > 0 && !loading, [newName, loading])

  async function refresh() {
    setLoading(true)
    setError('')
    try {
      const resp = await fetch('/api/notebooks')
      const data: ListResponse = await resp.json()
      if (!resp.ok) throw new Error((data as any)?.error ?? 'failed to load notebooks')
      const list = Array.isArray(data.notebooks) ? data.notebooks : []
      setItems(
        list
          .filter((x) => x && typeof x.notebookId === 'string')
          .map((x) => ({
            notebookId: String(x.notebookId),
            displayName: String(x.displayName ?? x.notebookId),
            createdAt: x.createdAt,
            updatedAt: x.updatedAt,
          })),
      )
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
    } finally {
      setLoading(false)
    }
  }

  async function createNotebook() {
    if (!canCreate) return
    setLoading(true)
    setError('')
    try {
      const resp = await fetch('/api/notebooks', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ displayName: newName.trim() }),
      })
      const data: CreateResponse = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to create notebook')

      const id = String(data?.notebook?.notebookId ?? '')
      if (!id) throw new Error('missing notebookId')

      setNewName('')
      await refresh()
      onChange(id)
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void refresh()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <label style={{ fontSize: 12, color: '#444', minWidth: 70 }}>notebook</label>
        <select
          value={value}
          onChange={(e) => onChange(e.target.value)}
          style={{ padding: '6px 8px', minWidth: 320 }}
          disabled={loading}
        >
          <option value="" disabled>
            Select notebook...
          </option>
          {items.map((x) => (
            <option key={x.notebookId} value={x.notebookId}>
              {x.displayName} ({x.notebookId})
            </option>
          ))}
        </select>
        <button onClick={() => refresh()} disabled={loading} style={{ padding: '6px 10px' }}>
          Refresh
        </button>
      </div>

      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <label style={{ fontSize: 12, color: '#444', minWidth: 70 }}>create</label>
        <input
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          placeholder="e.g. World War II History"
          style={{ padding: '6px 8px', minWidth: 320 }}
          disabled={loading}
        />
        <button onClick={() => createNotebook()} disabled={!canCreate} style={{ padding: '6px 10px' }}>
          Create
        </button>
      </div>

      {error ? <div style={{ color: '#b91c1c', fontSize: 12 }}>{error}</div> : null}
    </div>
  )
}


