import { useEffect, useMemo, useState } from 'react'

type LearningCard = {
  cardId: string
  kind?: string
  front: string
  back: string
  mnemonic?: string
  dueAt?: string
  repetitions?: number
  intervalDays?: number
  easeFactor?: number
  lastReviewedAt?: string
}

type Queue = {
  asOf?: string
  dueTotal?: number
  newTotal?: number
  due?: LearningCard[]
  new?: LearningCard[]
}

type Stats = {
  asOf?: string
  totalCards?: number
  newCards?: number
  dueCards?: number
  reviewedCardsToday?: number
  totalReviews?: number
}

export function CardsPanel(props: { notebookId: string }) {
  const { notebookId } = props

  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const [queue, setQueue] = useState<Queue | null>(null)
  const [stats, setStats] = useState<Stats | null>(null)

  const [selectedId, setSelectedId] = useState('')
  const [showBack, setShowBack] = useState(false)

  const [genTopic, setGenTopic] = useState('')
  const [genCount, setGenCount] = useState(10)
  const canGenerate = useMemo(() => genTopic.trim().length > 0 && !loading, [genTopic, loading])

  const selectedCard = useMemo(() => {
    const all = [...(queue?.due ?? []), ...(queue?.new ?? [])]
    return all.find((c) => c.cardId === selectedId) ?? null
  }, [queue, selectedId])

  async function refresh() {
    if (!notebookId) return
    setLoading(true)
    setError('')
    try {
      const [qResp, sResp] = await Promise.all([
        fetch(`/api/notebooks/${notebookId}/cards:daily`),
        fetch(`/api/notebooks/${notebookId}/cards:stats`),
      ])
      const qData = await qResp.json()
      const sData = await sResp.json()
      if (!qResp.ok) throw new Error(qData?.error ?? 'failed to load queue')
      if (!sResp.ok) throw new Error(sData?.error ?? 'failed to load stats')

      setQueue(qData?.queue ?? null)
      setStats(sData?.stats ?? null)

      // auto-select first item if none selected
      const first = (qData?.queue?.due?.[0]?.cardId as string) || (qData?.queue?.new?.[0]?.cardId as string) || ''
      setSelectedId((prev) => prev || first)
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
      setQueue(null)
      setStats(null)
    } finally {
      setLoading(false)
    }
  }

  async function review(grade: number) {
    if (!selectedId) return
    setLoading(true)
    setError('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/cards:review`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ cardId: selectedId, grade, generateMnemonic: grade <= 2 }),
      })
      const data = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to submit review')

      setShowBack(false)
      await refresh()
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
    } finally {
      setLoading(false)
    }
  }

  async function generateCards() {
    if (!canGenerate) return
    setLoading(true)
    setError('')
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/cards:generate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ topic: genTopic.trim(), count: genCount }),
      })
      const data = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to generate cards')
      setGenTopic('')
      await refresh()
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    setQueue(null)
    setStats(null)
    setSelectedId('')
    setShowBack(false)
    void refresh()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [notebookId])

  useEffect(() => {
    setShowBack(false)
  }, [selectedId])

  const dueCount = Number(queue?.dueTotal ?? (queue?.due ?? []).length ?? 0)
  const newCount = Number(queue?.newTotal ?? (queue?.new ?? []).length ?? 0)

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <strong>Cards</strong>
        <span style={{ color: '#666', fontSize: 12 }}>
          {loading ? 'loading...' : `due ${dueCount} · new ${newCount}`}
        </span>
        <button onClick={() => void refresh()} disabled={loading} style={{ marginLeft: 'auto', padding: '6px 10px' }}>
          Refresh
        </button>
      </div>

      <div style={{ border: '1px solid #eee', padding: 10, background: '#fafafa' }}>
        <div style={{ fontSize: 12, color: '#666', marginBottom: 8 }}>
          <strong>Generate (optional)</strong> <span style={{ color: '#999' }}>(creates new cards)</span>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <input
            value={genTopic}
            onChange={(e) => setGenTopic(e.target.value)}
            placeholder="topic"
            style={{ flex: 1, padding: '6px 8px' }}
            disabled={loading}
          />
          <input
            value={genCount}
            type="number"
            min={1}
            max={30}
            onChange={(e) => setGenCount(Number(e.target.value || 10))}
            style={{ width: 90, padding: '6px 8px' }}
            disabled={loading}
          />
          <button onClick={() => void generateCards()} disabled={!canGenerate} style={{ padding: '6px 10px' }}>
            Generate
          </button>
        </div>
      </div>

      {stats ? (
        <div style={{ fontSize: 12, color: '#666' }}>
          <strong>stats</strong>: total {Number(stats.totalCards ?? 0)} · due {Number(stats.dueCards ?? 0)} · new{' '}
          {Number(stats.newCards ?? 0)} · reviewedToday {Number(stats.reviewedCardsToday ?? 0)} · totalReviews{' '}
          {Number(stats.totalReviews ?? 0)}
        </div>
      ) : null}

      <div style={{ display: 'flex', gap: 12, minHeight: 0 }}>
        <div style={{ flex: 1, minWidth: 220 }}>
          <div style={{ fontSize: 12, color: '#666', marginBottom: 6 }}>
            <strong>Queue</strong>
          </div>
          <select
            value={selectedId}
            onChange={(e) => setSelectedId(e.target.value)}
            style={{ width: '100%', padding: '6px 8px' }}
            disabled={loading || (!queue?.due?.length && !queue?.new?.length)}
          >
            <option value="">Select card...</option>
            {(queue?.due ?? []).map((c) => (
              <option key={c.cardId} value={c.cardId}>
                [due] {c.front.slice(0, 40)} ({c.cardId})
              </option>
            ))}
            {(queue?.new ?? []).map((c) => (
              <option key={c.cardId} value={c.cardId}>
                [new] {c.front.slice(0, 40)} ({c.cardId})
              </option>
            ))}
          </select>
          <div style={{ fontSize: 12, color: '#999', marginTop: 6 }}>
            Tip: grade 5 = easy, 3 = hard-but-correct, 0 = complete fail.
          </div>
        </div>

        <div style={{ flex: 2, minWidth: 0 }}>
          <div style={{ fontSize: 12, color: '#666', marginBottom: 6 }}>
            <strong>Card</strong>
          </div>
          {selectedCard ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              <div style={{ border: '1px solid #eee', padding: 10, background: '#fff' }}>
                <div style={{ fontSize: 12, color: '#666' }}>
                  <strong>front</strong>
                </div>
                <div style={{ whiteSpace: 'pre-wrap', lineHeight: 1.45 }}>{selectedCard.front}</div>
              </div>

              {showBack ? (
                <div style={{ border: '1px solid #eee', padding: 10, background: '#fff' }}>
                  <div style={{ fontSize: 12, color: '#666' }}>
                    <strong>back</strong>
                  </div>
                  <div style={{ whiteSpace: 'pre-wrap', lineHeight: 1.45 }}>{selectedCard.back}</div>
                </div>
              ) : (
                <button onClick={() => setShowBack(true)} style={{ padding: '8px 10px' }}>
                  Show answer
                </button>
              )}

              {selectedCard.mnemonic ? (
                <div style={{ border: '1px solid #eee', padding: 10, background: '#fafafa' }}>
                  <div style={{ fontSize: 12, color: '#666' }}>
                    <strong>mnemonic</strong>
                  </div>
                  <div style={{ whiteSpace: 'pre-wrap', lineHeight: 1.45 }}>{selectedCard.mnemonic}</div>
                </div>
              ) : null}

              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
                {[0, 1, 2, 3, 4, 5].map((g) => (
                  <button key={g} onClick={() => void review(g)} disabled={loading} style={{ padding: '8px 10px' }}>
                    Grade {g}
                  </button>
                ))}
              </div>
            </div>
          ) : (
            <div style={{ fontSize: 12, color: '#999' }}>(select a card to review)</div>
          )}
        </div>
      </div>

      {error ? <div style={{ color: '#b91c1c', fontSize: 12 }}>{error}</div> : null}
    </div>
  )
}


