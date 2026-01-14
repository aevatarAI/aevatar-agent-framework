import { useState, useCallback } from 'react'
import { FileText, Download, Copy, Check, Loader2 } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogCloseButton } from '@/components/ui/dialog'
import { getSessionSummary, getFullDagSummary, type SessionSummary, type DagSummary } from '@/lib/axiom-client'
import { cn } from '@/lib/utils'

// ============================================================
//  Summary Modal (US7)
//  Displays session summary or full DAG summary
// ============================================================

interface SummaryModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  sessionId: string | null
}

type SummaryType = 'session' | 'dag'

export function SummaryModal({ open, onOpenChange, sessionId }: SummaryModalProps) {
  const [summaryType, setSummaryType] = useState<SummaryType>('session')
  const [sessionSummary, setSessionSummary] = useState<SessionSummary | null>(null)
  const [dagSummary, setDagSummary] = useState<DagSummary | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const loadSummary = useCallback(async (type: SummaryType) => {
    if (!sessionId) {
      setError('No session selected')
      return
    }

    setLoading(true)
    setError(null)
    setSummaryType(type)

    try {
      if (type === 'session') {
        const result = await getSessionSummary(sessionId)
        setSessionSummary(result)
        setDagSummary(null)
      } else {
        const result = await getFullDagSummary(sessionId)
        setDagSummary(result)
        setSessionSummary(null)
      }
    } catch (e) {
      setError((e as Error)?.message || 'Failed to load summary')
    } finally {
      setLoading(false)
    }
  }, [sessionId])

  const currentMarkdown = summaryType === 'session'
    ? sessionSummary?.markdownContent
    : dagSummary?.markdownContent

  const handleCopy = useCallback(async () => {
    if (!currentMarkdown) return
    await navigator.clipboard.writeText(currentMarkdown)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }, [currentMarkdown])

  const handleDownload = useCallback(() => {
    if (!currentMarkdown) return
    const blob = new Blob([currentMarkdown], { type: 'text/markdown' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${summaryType}-summary-${sessionId}.md`
    a.click()
    URL.revokeObjectURL(url)
  }, [currentMarkdown, summaryType, sessionId])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-3xl max-h-[85vh] bg-bg-surface/95 backdrop-blur-md border border-border-default rounded-xl shadow-lg text-text-primary overflow-hidden flex flex-col">
        <DialogHeader className="flex-shrink-0">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-neon-cyan/20 border border-neon-cyan/30">
              <FileText className="h-5 w-5 text-neon-cyan" />
            </div>
            <div>
              <DialogTitle>Summary</DialogTitle>
              <DialogDescription>
                Generate and view session or DAG summaries
              </DialogDescription>
            </div>
          </div>
          <DialogCloseButton />
        </DialogHeader>

        <div className="flex-1 flex flex-col overflow-hidden">
          {/* Summary Type Selector */}
          <div className="flex-shrink-0 flex items-center gap-2 p-4 border-b border-border-subtle">
            <button
              onClick={() => loadSummary('session')}
              disabled={loading}
              className={cn(
                "px-4 py-2 text-xs font-mono rounded-lg border transition-all",
                summaryType === 'session' && !loading
                  ? "bg-neon-cyan/20 border-neon-cyan/40 text-neon-cyan"
                  : "border-border-subtle text-text-muted hover:text-text-secondary hover:border-border-default"
              )}
            >
              Session Summary
            </button>
            <button
              onClick={() => loadSummary('dag')}
              disabled={loading}
              className={cn(
                "px-4 py-2 text-xs font-mono rounded-lg border transition-all",
                summaryType === 'dag' && !loading
                  ? "bg-neon-gold/20 border-neon-gold/40 text-neon-gold"
                  : "border-border-subtle text-text-muted hover:text-text-secondary hover:border-border-default"
              )}
            >
              Full DAG Summary
            </button>

            {/* Copy/Download buttons */}
            {currentMarkdown && (
              <div className="ml-auto flex items-center gap-2">
                <button
                  onClick={handleCopy}
                  className="p-2 rounded-md border border-border-subtle text-text-muted hover:text-neon-cyan hover:border-neon-cyan/40 transition-all"
                  aria-label="Copy to clipboard"
                >
                  {copied ? <Check className="size-4 text-neon-green" /> : <Copy className="size-4" />}
                </button>
                <button
                  onClick={handleDownload}
                  className="p-2 rounded-md border border-border-subtle text-text-muted hover:text-neon-gold hover:border-neon-gold/40 transition-all"
                  aria-label="Download as Markdown"
                >
                  <Download className="size-4" />
                </button>
              </div>
            )}
          </div>

          {/* Content */}
          <div className="flex-1 overflow-y-auto p-4">
            {loading && (
              <div className="flex items-center justify-center py-12">
                <Loader2 className="size-8 text-neon-cyan animate-spin" />
                <span className="ml-3 text-sm text-text-muted">Generating summary...</span>
              </div>
            )}

            {error && (
              <div className="text-center py-8">
                <p className="text-neon-rose text-sm">{error}</p>
                <button
                  onClick={() => loadSummary(summaryType)}
                  className="mt-4 px-4 py-2 text-xs font-mono rounded-lg border border-neon-cyan/40 text-neon-cyan hover:bg-neon-cyan/10"
                >
                  Retry
                </button>
              </div>
            )}

            {!loading && !error && !currentMarkdown && (
              <div className="text-center py-12">
                <FileText className="size-12 text-text-dimmed mx-auto mb-4" />
                <p className="text-text-muted text-sm">Select a summary type to generate</p>
              </div>
            )}

            {!loading && !error && currentMarkdown && (
              <div className="prose prose-sm prose-invert max-w-none
                prose-headings:text-neon-cyan prose-headings:font-mono
                prose-h1:text-xl prose-h2:text-lg prose-h3:text-base
                prose-p:text-text-secondary prose-p:leading-relaxed
                prose-a:text-neon-cyan hover:prose-a:text-neon-cyan/80
                prose-code:text-neon-gold prose-code:text-xs prose-code:bg-bg-elevated prose-code:px-1 prose-code:py-0.5 prose-code:rounded
                prose-pre:bg-bg-elevated prose-pre:border prose-pre:border-border-subtle
                prose-table:text-xs
                prose-th:text-text-muted prose-th:font-mono prose-th:border-border-subtle
                prose-td:border-border-subtle
                prose-li:text-text-secondary
                prose-strong:text-text-primary
              ">
                <ReactMarkdown remarkPlugins={[remarkGfm]}>{currentMarkdown}</ReactMarkdown>
              </div>
            )}
          </div>

          {/* Stats Footer */}
          {!loading && !error && (sessionSummary || dagSummary) && (
            <div className="flex-shrink-0 px-4 py-3 border-t border-border-subtle bg-bg-elevated/50">
              {sessionSummary && (
                <div className="flex items-center gap-6 text-xs font-mono text-text-muted">
                  <span>Plans: <span className="text-neon-cyan">{sessionSummary.planNodeCount}</span></span>
                  <span>Knowledge: <span className="text-neon-green">{sessionSummary.knowledgeNodeCount}</span></span>
                  <span>Progress: <span className="text-neon-gold">{sessionSummary.progressPercentage.toFixed(1)}%</span></span>
                </div>
              )}
              {dagSummary && (
                <div className="flex items-center gap-6 text-xs font-mono text-text-muted">
                  <span>Nodes: <span className="text-neon-cyan">{dagSummary.totalNodes}</span></span>
                  <span>Edges: <span className="text-neon-green">{dagSummary.totalEdges}</span></span>
                  <span>Depth: <span className="text-neon-gold">{dagSummary.maxDepth}</span></span>
                </div>
              )}
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}
