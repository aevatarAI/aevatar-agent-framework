import { useEffect, useMemo, useState } from 'react'

type QuizQuestion = {
  questionId: string
  type: 'mcq' | 'tf' | 'short' | string
  prompt: string
  options?: string[]
}

type Quiz = {
  quizId: string
  topic: string
  createdAt?: string
  strictGrading?: boolean
  sourceIds?: string[]
  questions: QuizQuestion[]
}

type GenerateResponse = { ok?: boolean; quiz?: Quiz; error?: string }

type AttemptAnswer = {
  questionId: string
  type?: string
  prompt?: string
  options?: string[]
  userAnswer?: string
  correctAnswer?: string
  isCorrect?: boolean | null
  explanation?: string
}

type Attempt = {
  attemptId?: string
  quizId?: string
  submittedAt?: string
  strictGrading?: boolean
  totalQuestions?: number
  correctCount?: number
  answers?: AttemptAnswer[]
}

type SubmitResponse = { ok?: boolean; quizId?: string; attempt?: Attempt; error?: string }

export function QuizPanel(props: { notebookId: string }) {
  const { notebookId } = props

  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const [topic, setTopic] = useState('')
  const [count, setCount] = useState(8)
  const [strict, setStrict] = useState(false)

  const [quiz, setQuiz] = useState<Quiz | null>(null)
  const [answers, setAnswers] = useState<Record<string, string>>({})
  const [attempt, setAttempt] = useState<Attempt | null>(null)

  const canGenerate = useMemo(() => topic.trim().length > 0 && !loading, [topic, loading])
  const canSubmit = useMemo(() => !!quiz && !loading, [quiz, loading])

  useEffect(() => {
    // reset when notebook changes
    setQuiz(null)
    setAnswers({})
    setAttempt(null)
    setError('')
  }, [notebookId])

  async function generate() {
    if (!canGenerate) return
    setLoading(true)
    setError('')
    setAttempt(null)
    try {
      const resp = await fetch(`/api/notebooks/${notebookId}/quiz:generate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ topic: topic.trim(), count, strictGrading: strict }),
      })
      const data: GenerateResponse = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to generate quiz')
      if (!data.quiz?.quizId) throw new Error('missing quizId')

      setQuiz(data.quiz)
      setAnswers({})
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
      setQuiz(null)
      setAnswers({})
    } finally {
      setLoading(false)
    }
  }

  function setAnswer(questionId: string, value: string) {
    setAnswers((prev) => ({ ...prev, [questionId]: value }))
  }

  async function submit() {
    if (!quiz?.quizId) return
    setLoading(true)
    setError('')
    try {
      const payload = {
        quizId: quiz.quizId,
        strictGrading: strict,
        answers: quiz.questions.map((q) => ({
          questionId: q.questionId,
          answer: String(answers[q.questionId] ?? ''),
        })),
      }

      const resp = await fetch(`/api/notebooks/${notebookId}/quiz:submit`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      const data: SubmitResponse = await resp.json()
      if (!resp.ok) throw new Error(data?.error ?? 'failed to submit')
      setAttempt(data.attempt ?? null)
    } catch (e: any) {
      setError(String(e?.message ?? e ?? 'failed'))
      setAttempt(null)
    } finally {
      setLoading(false)
    }
  }

  const scoreText = useMemo(() => {
    if (!attempt) return ''
    const c = Number(attempt.correctCount ?? 0)
    const t = Number(attempt.totalQuestions ?? 0)
    if (t <= 0) return ''
    return `${c}/${t}`
  }, [attempt])

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <strong>Quiz</strong>
        {quiz ? <span style={{ fontSize: 12, color: '#666' }}>quizId: {quiz.quizId}</span> : null}
        {attempt ? (
          <span style={{ fontSize: 12, color: '#111' }}>
            <strong>score</strong>: {scoreText}
          </span>
        ) : null}
        <button onClick={() => void generate()} disabled={!canGenerate} style={{ marginLeft: 'auto', padding: '6px 10px' }}>
          Generate
        </button>
      </div>

      <div style={{ border: '1px solid #eee', padding: 10, background: '#fafafa' }}>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <input
            value={topic}
            onChange={(e) => setTopic(e.target.value)}
            placeholder="topic (e.g. review key points)"
            style={{ flex: 1, padding: '6px 8px' }}
            disabled={loading}
          />
          <input
            value={count}
            type="number"
            min={1}
            max={20}
            onChange={(e) => setCount(Number(e.target.value || 8))}
            style={{ width: 80, padding: '6px 8px' }}
            disabled={loading}
          />
          <label style={{ fontSize: 12, color: '#444', display: 'flex', gap: 6, alignItems: 'center' }}>
            <input type="checkbox" checked={strict} onChange={(e) => setStrict(e.target.checked)} disabled={loading} />
            strict
          </label>
        </div>
        <div style={{ fontSize: 12, color: '#999', marginTop: 6 }}>
          strict = short answers are AI-graded (best-effort). Otherwise they are shown with reference answers.
        </div>
      </div>

      {quiz ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {quiz.questions.map((q, idx) => {
            const user = String(answers[q.questionId] ?? '')
            const graded = attempt?.answers?.find((a) => a.questionId === q.questionId)
            const isCorrect = graded?.isCorrect
            const status =
              isCorrect === true ? '✅ correct' : isCorrect === false ? '❌ incorrect' : attempt ? '—' : ''

            return (
              <div key={q.questionId} style={{ border: '1px solid #eee', background: '#fff', padding: 10 }}>
                <div style={{ display: 'flex', gap: 8, alignItems: 'baseline' }}>
                  <div style={{ fontSize: 12, color: '#666' }}>
                    <strong>Q{idx + 1}</strong> <span style={{ color: '#999' }}>{q.type}</span>
                  </div>
                  <div style={{ marginLeft: 'auto', fontSize: 12, color: isCorrect === false ? '#b91c1c' : isCorrect === true ? '#065f46' : '#666' }}>
                    {status}
                  </div>
                </div>
                <div style={{ marginTop: 6, whiteSpace: 'pre-wrap', lineHeight: 1.45 }}>{q.prompt}</div>

                <div style={{ marginTop: 10 }}>
                  {q.type === 'mcq' ? (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                      {(q.options ?? []).map((opt, i) => (
                        <label key={i} style={{ display: 'flex', gap: 8, alignItems: 'center', fontSize: 12 }}>
                          <input
                            type="radio"
                            name={q.questionId}
                            value={String(i)}
                            checked={user === String(i)}
                            onChange={(e) => setAnswer(q.questionId, e.target.value)}
                            disabled={loading}
                          />
                          <span>
                            <strong>{String.fromCharCode(65 + i)}.</strong> {opt}
                          </span>
                        </label>
                      ))}
                      {(q.options ?? []).length === 0 ? (
                        <div style={{ fontSize: 12, color: '#999' }}>(no options)</div>
                      ) : null}
                    </div>
                  ) : q.type === 'tf' ? (
                    <div style={{ display: 'flex', gap: 12 }}>
                      {['true', 'false'].map((v) => (
                        <label key={v} style={{ display: 'flex', gap: 8, alignItems: 'center', fontSize: 12 }}>
                          <input
                            type="radio"
                            name={q.questionId}
                            value={v}
                            checked={user === v}
                            onChange={(e) => setAnswer(q.questionId, e.target.value)}
                            disabled={loading}
                          />
                          <span>{v}</span>
                        </label>
                      ))}
                    </div>
                  ) : (
                    <textarea
                      value={user}
                      onChange={(e) => setAnswer(q.questionId, e.target.value)}
                      placeholder="your answer..."
                      style={{ width: '100%', minHeight: 80, padding: 8, fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' }}
                      disabled={loading}
                    />
                  )}
                </div>

                {attempt && graded ? (
                  <div style={{ marginTop: 10, background: '#fafafa', border: '1px solid #eee', padding: 10 }}>
                    <div style={{ fontSize: 12, color: '#666' }}>
                      <strong>Explanation</strong>
                    </div>
                    <div style={{ fontSize: 12, whiteSpace: 'pre-wrap', lineHeight: 1.45 }}>
                      {graded.explanation || '(none)'}
                    </div>
                    <div style={{ marginTop: 8, fontSize: 12, color: '#666' }}>
                      <strong>Correct answer</strong>:{' '}
                      <span style={{ color: '#111' }}>{graded.correctAnswer || '(none)'}</span>
                    </div>
                  </div>
                ) : null}
              </div>
            )
          })}

          <div style={{ display: 'flex', gap: 8 }}>
            <button onClick={() => void submit()} disabled={!canSubmit} style={{ padding: '8px 10px' }}>
              Submit
            </button>
            <button
              onClick={() => {
                setAttempt(null)
                setError('')
              }}
              disabled={loading}
              style={{ padding: '8px 10px' }}
            >
              Clear result
            </button>
          </div>
        </div>
      ) : (
        <div style={{ fontSize: 12, color: '#999' }}>(generate a quiz to start)</div>
      )}

      {error ? <div style={{ color: '#b91c1c', fontSize: 12 }}>{error}</div> : null}
    </div>
  )
}


