import { useEffect, useMemo, useState } from 'react'

type SkillMeta = {
  notebookId?: string
  version: string
  providerName?: string
  theme?: string
  createdAt?: string
  sourceIds?: string[]
  parseError?: string
}

type SkillBundle = {
  name?: string
  description?: string
  systemPrompt?: string
  tools?: string[]
  usageExamples?: string[]
  knowledgeSummary?: string
  glossary?: Array<{ term: string; definition: string }>
  limitations?: string[]
  parseError?: string
}

type ListResponse = { ok?: boolean; count?: number; versions?: SkillMeta[]; error?: string }
type GetResponse = { ok?: boolean; meta?: SkillMeta; bundle?: SkillBundle; markdown?: string; error?: string }

export function SkillsPanel(props: { notebookId: string }) {
  const { notebookId } = props

  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [info, setInfo] = useState<string>('')

  const [theme, setTheme] = useState('')
  const canGenerate = useMemo(() => !loading, [loading])

  const [items, setItems] = useState<SkillMeta[]>([])
  const [selectedVersion, setSelectedVersion] = useState<string>('')
  const [selected, setSelected] = useState<GetResponse | null>(null)

  async function refreshList() {
    if (!notebookId) return
    setLoading(true)
    setError('')
    setInfo('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/skills`)
      const data: ListResponse = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to list skills')
      setItems(Array.isArray(data.versions) ? data.versions : [])
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
      setItems([])
    } finally {
      setLoading(false)
    }
  }

  async function load(version: string) {
    if (!notebookId) return
    if (!version) return
    setLoading(true)
    setError('')
    setInfo('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/skills/${encodeURIComponent(version)}`)
      const data: GetResponse = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to load skill bundle')
      setSelected(data)
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
      setSelected(null)
    } finally {
      setLoading(false)
    }
  }

  async function generate() {
    if (!canGenerate) return
    setLoading(true)
    setError('')
    setInfo('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/skills:generate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ theme: theme.trim() || undefined }),
      })
      const data = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to generate skills')

      const version = String(data?.meta?.version ?? '')
      await refreshList()
      if (version) {
        setSelectedVersion(version)
        await load(version)
      }

      setInfo('generated')
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
    } finally {
      setLoading(false)
    }
  }

  async function copy(text: string) {
    text = text ?? ''
    if (!text.trim()) return

    try {
      await navigator.clipboard.writeText(text)
      setInfo('copied to clipboard')
      setTimeout(() => setInfo(''), 1500)
    } catch {
      // Fallback: show in UI, user can copy manually
      setInfo('copy failed (clipboard not available)')
      setTimeout(() => setInfo(''), 2500)
    }
  }

  useEffect(() => {
    setItems([])
    setSelectedVersion('')
    setSelected(null)
    setError('')
    setInfo('')
    void refreshList()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [notebookId])

  useEffect(() => {
    if (!selectedVersion) {
      setSelected(null)
      return
    }
    void load(selectedVersion)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedVersion])

  const mdPreview = useMemo(() => {
    const raw = String(selected?.markdown ?? '')
    const max = 60_000
    if (raw.length <= max) return raw
    return raw.slice(0, max) + '\n\n...(truncated)'
  }, [selected])

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <strong>Skills</strong>
        <span style={{ color: '#666', fontSize: 12 }}>{loading ? 'loading...' : `${items.length} versions`}</span>
        <button onClick={() => void refreshList()} disabled={loading} style={{ marginLeft: 'auto', padding: '6px 10px' }}>
          Refresh
        </button>
      </div>

      <div style={{ border: '1px solid #eee', padding: 10, background: '#fafafa' }}>
        <div style={{ fontSize: 12, color: '#666', marginBottom: 8 }}>
          <strong>Generate</strong>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <input
            value={theme}
            onChange={(e) => setTheme(e.target.value)}
            placeholder="(optional) theme"
            style={{ flex: 1, padding: '6px 8px' }}
            disabled={loading}
          />
          <button onClick={() => void generate()} disabled={!canGenerate} style={{ padding: '6px 10px' }}>
            Generate
          </button>
        </div>
        <div style={{ fontSize: 12, color: '#999', marginTop: 6 }}>
          Tip: this generates a reusable agent skill bundle (JSON + Markdown) versioned under the notebook directory.
        </div>
      </div>

      <div style={{ display: 'flex', gap: 12, minHeight: 0 }}>
        <div style={{ flex: 1, minWidth: 220 }}>
          <div style={{ fontSize: 12, color: '#666', marginBottom: 6 }}>
            <strong>Versions</strong>
          </div>
          <select
            value={selectedVersion}
            onChange={(e) => setSelectedVersion(e.target.value)}
            style={{ width: '100%', padding: '6px 8px' }}
            disabled={loading || items.length === 0}
          >
            <option value="">Select version...</option>
            {items.map((m) => (
              <option key={m.version} value={m.version}>
                {m.version} {m.theme ? `· ${m.theme}` : ''}
              </option>
            ))}
          </select>
          <div style={{ fontSize: 12, color: '#999', marginTop: 6 }}>Selecting a version loads the bundle + markdown.</div>
        </div>

        <div style={{ flex: 2, minWidth: 0 }}>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 6 }}>
            <div style={{ fontSize: 12, color: '#666' }}>
              <strong>View</strong>
            </div>
            <button
              onClick={() => void copy(mdPreview)}
              disabled={!mdPreview.trim()}
              style={{ marginLeft: 'auto', padding: '6px 10px' }}
            >
              Copy Markdown
            </button>
            <button
              onClick={() => void copy(JSON.stringify(selected?.bundle ?? {}, null, 2))}
              disabled={!selected?.bundle}
              style={{ padding: '6px 10px' }}
            >
              Copy JSON
            </button>
          </div>

          {selected?.meta ? (
            <div style={{ fontSize: 12, color: '#666', marginBottom: 8 }}>
              <strong>{selected.bundle?.name || selected.meta.theme || '(bundle)'}</strong>{' '}
              <span style={{ color: '#999' }}>· {selected.meta.createdAt || ''}</span>
              {selected.meta.parseError ? <span style={{ color: '#b91c1c' }}> · parseError</span> : null}
            </div>
          ) : null}

          {selectedVersion ? (
            <pre
              style={{
                whiteSpace: 'pre-wrap',
                lineHeight: 1.35,
                fontSize: 12,
                background: '#fff',
                border: '1px solid #eee',
                padding: 10,
                maxHeight: 420,
                overflow: 'auto',
              }}
            >
              {mdPreview || '(empty)'}
            </pre>
          ) : (
            <div style={{ fontSize: 12, color: '#999' }}>(select a version to view)</div>
          )}
        </div>
      </div>

      {info ? <div style={{ color: '#065f46', fontSize: 12 }}>{info}</div> : null}
      {error ? <div style={{ color: '#b91c1c', fontSize: 12 }}>{error}</div> : null}
    </div>
  )
}


