import { useMemo, useState } from 'react'

type RecommendedItem = {
  name: string
  location?: string
  effects?: string
}

type QueryResult = {
  query?: string
  providerName?: string
  recommendedItems?: RecommendedItem[]
  whyItWorks?: string
  massageTechniques?: string[]
  relatedTheory?: string
  cautions?: string[]
  relatedConcepts?: string[]
  sourceIds?: string[]
  rawPreview?: string
}

export function EncyclopediaPanel(props: { notebookId: string }) {
  const { notebookId } = props

  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const [buildInfo, setBuildInfo] = useState<any>(null)
  const [query, setQuery] = useState('')
  const [result, setResult] = useState<QueryResult | null>(null)

  const canQuery = useMemo(() => query.trim().length > 0 && !loading, [query, loading])

  async function build() {
    if (!notebookId) return
    setLoading(true)
    setError('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/encyclopedia:build`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({}),
      })
      const data = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to build encyclopedia')
      setBuildInfo(data?.meta ?? data)
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
    } finally {
      setLoading(false)
    }
  }

  async function runQuery() {
    if (!canQuery) return
    setLoading(true)
    setError('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/encyclopedia:query`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query: query.trim() }),
      })
      const data = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to query encyclopedia')
      setResult((data?.result ?? null) as QueryResult | null)
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
      setResult(null)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <strong>Encyclopedia</strong>
        <span style={{ color: '#666', fontSize: 12 }}>{loading ? 'loading...' : ''}</span>
        <button onClick={() => void build()} disabled={loading} style={{ marginLeft: 'auto', padding: '6px 10px' }}>
          Build / Update
        </button>
      </div>

      {buildInfo ? (
        <div style={{ fontSize: 12, color: '#666', background: '#fafafa', border: '1px solid #eee', padding: 10 }}>
          <strong>Build meta</strong>
          <pre style={{ marginTop: 6, whiteSpace: 'pre-wrap' }}>{JSON.stringify(buildInfo, null, 2)}</pre>
        </div>
      ) : null}

      <div style={{ border: '1px solid #eee', padding: 10, background: '#fafafa' }}>
        <div style={{ fontSize: 12, color: '#666', marginBottom: 8 }}>
          <strong>Query</strong> <span style={{ color: '#999' }}>(symptoms / description)</span>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder='e.g. "frequent headaches, fear of cold"'
            style={{ flex: 1, padding: '6px 8px' }}
            disabled={loading}
          />
          <button onClick={() => void runQuery()} disabled={!canQuery} style={{ padding: '6px 10px' }}>
            Ask
          </button>
        </div>
      </div>

      {result ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          <div style={{ fontSize: 12, color: '#666' }}>
            <strong>Recommended</strong>
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {(result.recommendedItems ?? []).slice(0, 12).map((x, idx) => (
              <div key={idx} style={{ border: '1px solid #eee', padding: 10, background: '#fff' }}>
                <div style={{ fontSize: 12, color: '#111' }}>
                  <strong>{x.name || '(item)'}</strong>
                </div>
                {x.location ? <div style={{ fontSize: 12, color: '#666' }}>location: {x.location}</div> : null}
                {x.effects ? <div style={{ fontSize: 12, color: '#666' }}>effects: {x.effects}</div> : null}
              </div>
            ))}
            {(!result.recommendedItems || result.recommendedItems.length === 0) && (
              <div style={{ fontSize: 12, color: '#999' }}>(no recommended items)</div>
            )}
          </div>

          <div style={{ fontSize: 12, color: '#666' }}>
            <strong>Why it works</strong>
          </div>
          <div style={{ fontSize: 12, lineHeight: 1.45, whiteSpace: 'pre-wrap' }}>{result.whyItWorks || '(empty)'}</div>

          <div style={{ fontSize: 12, color: '#666' }}>
            <strong>Massage techniques</strong>
          </div>
          <ul style={{ margin: 0, paddingLeft: 18, fontSize: 12 }}>
            {(result.massageTechniques ?? []).slice(0, 12).map((t, i) => (
              <li key={i}>{t}</li>
            ))}
          </ul>

          <div style={{ fontSize: 12, color: '#666' }}>
            <strong>Related theory</strong>
          </div>
          <div style={{ fontSize: 12, lineHeight: 1.45, whiteSpace: 'pre-wrap' }}>{result.relatedTheory || '(empty)'}</div>

          {(result.cautions ?? []).length ? (
            <>
              <div style={{ fontSize: 12, color: '#666' }}>
                <strong>Cautions</strong>
              </div>
              <ul style={{ margin: 0, paddingLeft: 18, fontSize: 12 }}>
                {(result.cautions ?? []).slice(0, 12).map((t, i) => (
                  <li key={i}>{t}</li>
                ))}
              </ul>
            </>
          ) : null}

          {(result.relatedConcepts ?? []).length ? (
            <>
              <div style={{ fontSize: 12, color: '#666' }}>
                <strong>Related concepts</strong>
              </div>
              <ul style={{ margin: 0, paddingLeft: 18, fontSize: 12 }}>
                {(result.relatedConcepts ?? []).slice(0, 20).map((t, i) => (
                  <li key={i}>{t}</li>
                ))}
              </ul>
            </>
          ) : null}

          {result.rawPreview ? (
            <details>
              <summary style={{ fontSize: 12, color: '#666', cursor: 'pointer' }}>Raw preview</summary>
              <pre style={{ fontSize: 12, whiteSpace: 'pre-wrap', background: '#fafafa', padding: 10, border: '1px solid #eee' }}>
                {result.rawPreview}
              </pre>
            </details>
          ) : null}
        </div>
      ) : (
        <div style={{ fontSize: 12, color: '#999' }}>(run a query to see structured results)</div>
      )}

      {error ? <div style={{ color: '#b91c1c', fontSize: 12 }}>{error}</div> : null}
    </div>
  )
}


