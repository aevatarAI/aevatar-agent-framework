import { useEffect, useMemo, useState } from 'react'

type ReportMeta = {
  reportId: string
  topic: string
  currentVersion?: number
  createdAt?: string
  updatedAt?: string
  sourceIds?: string[]
}

type ListResponse = { count?: number; reports?: ReportMeta[]; error?: string }
type GetResponse = { meta?: ReportMeta; version?: number; content?: string; error?: string }

export function ReportsPanel(props: { notebookId: string }) {
  const { notebookId } = props

  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const [topic, setTopic] = useState('')
  const canGenerate = useMemo(() => topic.trim().length > 0 && !loading, [topic, loading])

  const [items, setItems] = useState<ReportMeta[]>([])
  const [selectedId, setSelectedId] = useState<string>('')
  const [selected, setSelected] = useState<{ meta: ReportMeta; version: number; content: string } | null>(null)

  async function refreshList() {
    if (!notebookId) return
    setLoading(true)
    setError('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/reports`)
      const data: ListResponse = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to list reports')
      setItems(Array.isArray(data.reports) ? data.reports : [])
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
      setItems([])
    } finally {
      setLoading(false)
    }
  }

  async function loadReport(reportId: string) {
    if (!notebookId) return
    if (!reportId) return
    setLoading(true)
    setError('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/reports/${reportId}`)
      const data: GetResponse = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to load report')
      if (!data.meta) throw new Error('missing meta')
      setSelected({ meta: data.meta, version: Number(data.version ?? 0), content: String(data.content ?? '') })
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
      setSelected(null)
    } finally {
      setLoading(false)
    }
  }

  async function generateReport() {
    if (!canGenerate) return
    setLoading(true)
    setError('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/reports:generate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ topic: topic.trim() }),
      })
      const data = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to generate report')

      setTopic('')
      await refreshList()

      const id = String(data?.report?.reportId ?? '')
      if (id) {
        setSelectedId(id)
        await loadReport(id)
      }
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

  useEffect(() => {
    if (!selectedId) {
      setSelected(null)
      return
    }
    void loadReport(selectedId)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId])

  const contentPreview = useMemo(() => {
    const raw = selected?.content ?? ''
    const max = 30_000
    if (raw.length <= max) return raw
    return raw.slice(0, max) + '\n\n...(truncated)'
  }, [selected])

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <strong>Reports</strong>
        <span style={{ color: '#666', fontSize: 12 }}>{loading ? 'loading...' : `${items.length} items`}</span>
        <button onClick={() => void refreshList()} disabled={loading} style={{ marginLeft: 'auto', padding: '6px 10px' }}>
          Refresh
        </button>
      </div>

      <div style={{ border: '1px solid #eee', padding: 10, background: '#fafafa' }}>
        <div style={{ fontSize: 12, color: '#666', marginBottom: 8 }}>
          <strong>Generate</strong>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <input
            value={topic}
            onChange={(e) => setTopic(e.target.value)}
            placeholder="topic (e.g. summarize key concepts)"
            style={{ flex: 1, padding: '6px 8px' }}
            disabled={loading}
          />
          <button onClick={() => void generateReport()} disabled={!canGenerate} style={{ padding: '6px 10px' }}>
            Generate
          </button>
        </div>
        <div style={{ fontSize: 12, color: '#999', marginTop: 6 }}>
          Tip: report content is versioned under the notebook directory.
        </div>
      </div>

      <div style={{ display: 'flex', gap: 12, minHeight: 0 }}>
        <div style={{ flex: 1, minWidth: 220 }}>
          <div style={{ fontSize: 12, color: '#666', marginBottom: 6 }}>
            <strong>History</strong>
          </div>
          <select
            value={selectedId}
            onChange={(e) => setSelectedId(e.target.value)}
            style={{ width: '100%', padding: '6px 8px' }}
            disabled={loading || items.length === 0}
          >
            <option value="">Select report...</option>
            {items.map((r) => (
              <option key={r.reportId} value={r.reportId}>
                {r.topic} ({r.reportId})
              </option>
            ))}
          </select>
          <div style={{ fontSize: 12, color: '#999', marginTop: 6 }}>
            Selecting a report loads the latest version.
          </div>
        </div>

        <div style={{ flex: 2, minWidth: 0 }}>
          <div style={{ fontSize: 12, color: '#666', marginBottom: 6 }}>
            <strong>View</strong>
            {selected ? (
              <span style={{ color: '#999' }}>
                {' '}
                · v{selected.version || selected.meta.currentVersion || 0} · {selected.meta.updatedAt || selected.meta.createdAt || ''}
              </span>
            ) : null}
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
                maxHeight: 360,
                overflow: 'auto',
              }}
            >
              {contentPreview}
            </pre>
          ) : (
            <div style={{ fontSize: 12, color: '#999' }}>(select a report to view)</div>
          )}
        </div>
      </div>

      {error ? <div style={{ color: '#b91c1c', fontSize: 12 }}>{error}</div> : null}
    </div>
  )
}


