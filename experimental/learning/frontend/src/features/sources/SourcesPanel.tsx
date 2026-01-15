import { useEffect, useMemo, useState } from 'react'

type SourceMeta = {
  sourceId: string
  title: string
  mimeType?: string
  sizeChars?: number
  createdAt?: string
  updatedAt?: string
}

type ListResponse = { count?: number; sources?: SourceMeta[]; error?: string }
type GetResponse = { meta?: SourceMeta; content?: string; error?: string }

export function SourcesPanel(props: { notebookId: string }) {
  const { notebookId } = props

  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const [items, setItems] = useState<SourceMeta[]>([])
  const [selectedId, setSelectedId] = useState<string>('')
  const [selected, setSelected] = useState<{ meta: SourceMeta; content: string } | null>(null)

  // Import (text)
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const canImportText = useMemo(() => title.trim().length > 0 && content.trim().length > 0 && !loading, [title, content, loading])

  // Import (file)
  const [file, setFile] = useState<File | null>(null)
  const [fileTitle, setFileTitle] = useState('')
  const canImportFile = useMemo(() => !!file && !loading, [file, loading])

  async function refreshList() {
    if (!notebookId) return
    setLoading(true)
    setError('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/sources`)
      const data: ListResponse = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to list sources')
      setItems(Array.isArray(data.sources) ? data.sources : [])
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
      setItems([])
    } finally {
      setLoading(false)
    }
  }

  async function loadSource(sourceId: string) {
    if (!notebookId) return
    if (!sourceId) return
    setLoading(true)
    setError('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/sources/${sourceId}`)
      const data: GetResponse = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to load source')
      if (!data.meta) throw new Error('missing meta')
      setSelected({ meta: data.meta, content: String(data.content ?? '') })
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
      setSelected(null)
    } finally {
      setLoading(false)
    }
  }

  async function importText() {
    if (!canImportText) return
    setLoading(true)
    setError('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/sources/text`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          title: title.trim(),
          content: content,
          mimeType: 'text/plain',
        }),
      })
      const data = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to import source')
      setTitle('')
      setContent('')
      await refreshList()
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
    } finally {
      setLoading(false)
    }
  }

  async function importFile() {
    if (!canImportFile || !file) return
    setLoading(true)
    setError('')
    try {
      const form = new FormData()
      form.append('file', file)
      if (fileTitle.trim()) form.append('title', fileTitle.trim())

      const resp = await fetch(`/api/notebooks/${notebookId}/sources/file`, {
        method: 'POST',
        body: form,
      })
      const data = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to import file')
      setFile(null)
      setFileTitle('')
      await refreshList()
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    setItems([])
    setSelectedId('')
    setSelected(null)
    void refreshList()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [notebookId])

  // Load selected preview
  useEffect(() => {
    if (!selectedId) {
      setSelected(null)
      return
    }
    void loadSource(selectedId)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId])

  const preview = useMemo(() => {
    const raw = selected?.content ?? ''
    const max = 20_000
    if (raw.length <= max) return { text: raw, truncated: false }
    return { text: raw.slice(0, max) + '\n\n...(truncated)', truncated: true }
  }, [selected])

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <strong>Sources</strong>
        <span style={{ color: '#666', fontSize: 12 }}>{loading ? 'loading...' : `${items.length} items`}</span>
        <button onClick={() => void refreshList()} disabled={loading} style={{ marginLeft: 'auto', padding: '6px 10px' }}>
          Refresh
        </button>
      </div>

      <div style={{ border: '1px solid #eee', padding: 10, background: '#fafafa' }}>
        <div style={{ fontSize: 12, color: '#666', marginBottom: 8 }}>
          <strong>Import text</strong>
        </div>
        <div style={{ display: 'flex', gap: 8, marginBottom: 8 }}>
          <input
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="title"
            style={{ flex: 1, padding: '6px 8px' }}
            disabled={loading}
          />
          <button onClick={() => void importText()} disabled={!canImportText} style={{ padding: '6px 10px' }}>
            Import
          </button>
        </div>
        <textarea
          value={content}
          onChange={(e) => setContent(e.target.value)}
          placeholder="paste text here..."
          style={{ width: '100%', minHeight: 120, padding: 8, fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' }}
          disabled={loading}
        />
      </div>

      <div style={{ border: '1px solid #eee', padding: 10, background: '#fafafa' }}>
        <div style={{ fontSize: 12, color: '#666', marginBottom: 8 }}>
          <strong>Import file</strong> <span style={{ color: '#999' }}>(.txt/.md only)</span>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <input
            type="file"
            accept=".txt,.md,.markdown,text/plain,text/markdown"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            disabled={loading}
          />
          <input
            value={fileTitle}
            onChange={(e) => setFileTitle(e.target.value)}
            placeholder="(optional) title"
            style={{ flex: 1, padding: '6px 8px' }}
            disabled={loading}
          />
          <button onClick={() => void importFile()} disabled={!canImportFile} style={{ padding: '6px 10px' }}>
            Upload
          </button>
        </div>
      </div>

      <div style={{ display: 'flex', gap: 12, minHeight: 0 }}>
        <div style={{ flex: 1, minWidth: 220 }}>
          <div style={{ fontSize: 12, color: '#666', marginBottom: 6 }}>
            <strong>List</strong>
          </div>
          <select
            value={selectedId}
            onChange={(e) => setSelectedId(e.target.value)}
            style={{ width: '100%', padding: '6px 8px' }}
            disabled={loading || items.length === 0}
          >
            <option value="">Select source...</option>
            {items.map((s) => (
              <option key={s.sourceId} value={s.sourceId}>
                {s.title} ({s.sourceId})
              </option>
            ))}
          </select>
          <div style={{ fontSize: 12, color: '#999', marginTop: 6 }}>
            Tip: keep sources small; preview is truncated for large texts.
          </div>
        </div>

        <div style={{ flex: 2, minWidth: 0 }}>
          <div style={{ fontSize: 12, color: '#666', marginBottom: 6 }}>
            <strong>Preview</strong>
            {selected ? <span style={{ color: '#999' }}> · {selected.meta.mimeType || 'text'}</span> : null}
          </div>
          {selected ? (
            <pre
              style={{
                whiteSpace: 'pre-wrap',
                lineHeight: 1.35,
                fontSize: 12,
                background: '#fff',
                border: '1px solid #eee',
                padding: 10,
                maxHeight: 320,
                overflow: 'auto',
              }}
            >
              {preview.text}
            </pre>
          ) : (
            <div style={{ fontSize: 12, color: '#999' }}>(select a source to preview)</div>
          )}
        </div>
      </div>

      {error ? <div style={{ color: '#b91c1c', fontSize: 12 }}>{error}</div> : null}
    </div>
  )
}


