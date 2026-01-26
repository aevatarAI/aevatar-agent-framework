// ============================================================
//  NodeDetailsPanel - Node Details with Markdown Explanation
// ============================================================

import { useEffect, memo } from 'react'
import { useSisyphusStore } from '@/store/sisyphus-store'
import { getNodeExplanation } from '@/lib/axiom-client'
import { MarkdownPreview } from '@/components/ui/markdown-preview'
import { cn } from '@/lib/utils'

interface NodeDetailsPanelProps {
  sessionId: string
}

export const NodeDetailsPanel = memo(function NodeDetailsPanel({ sessionId }: NodeDetailsPanelProps) {
  // ── Fine-grained subscriptions to minimize re-renders ──
  const selectedNodeId = useSisyphusStore((s) => s.selectedNodeId)
  const nodeExplanation = useSisyphusStore((s) => s.nodeExplanation)
  const dagLoading = useSisyphusStore((s) => s.dagLoading)
  const dagError = useSisyphusStore((s) => s.dagError)
  const setNodeExplanation = useSisyphusStore((s) => s.setNodeExplanation)
  const setDagLoading = useSisyphusStore((s) => s.setDagLoading)
  const setDagError = useSisyphusStore((s) => s.setDagError)
  
  // Only subscribe to dag.nodes for finding selected node
  const selectedNode = useSisyphusStore((s) => {
    if (!selectedNodeId || !s.dag?.nodes) return null
    return s.dag.nodes.find(n => n.id === selectedNodeId) ?? null
  })

  // Load node explanation when node selected
  useEffect(() => {
    if (!selectedNodeId || !sessionId) return

    let cancelled = false
    setDagLoading(true)
    setDagError("")

    getNodeExplanation(sessionId, selectedNodeId)
      .then((explain) => {
        if (cancelled) return
        setNodeExplanation(explain)
      })
      .catch((e) => {
        if (cancelled) return
        setDagError(e?.message || "Failed to load node details")
      })
      .finally(() => {
        if (!cancelled) setDagLoading(false)
      })

    return () => { cancelled = true }
  }, [selectedNodeId, sessionId, setNodeExplanation, setDagLoading, setDagError])

  if (!selectedNode) {
    return (
      <div className="text-xs text-text-muted text-center py-4">
        Select a node to view details
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {/* Node Header */}
      <div>
        <div className="text-xs font-mono text-neon-cyan break-all">{selectedNode.id}</div>
        <div className="mt-1.5 flex items-center gap-2 flex-wrap">
          {(nodeExplanation?.kind || selectedNode.kind) && (
            <span className={cn(
              "text-[10px] px-2 py-0.5 rounded-full font-medium",
              (nodeExplanation?.kind || selectedNode.kind) === 'Plan'
                ? "bg-blue-500/20 border border-blue-500/30 text-blue-400"
                : "bg-green-500/20 border border-green-500/30 text-green-400"
            )}>
              {nodeExplanation?.kind || selectedNode.kind}
            </span>
          )}
          {nodeExplanation?.title && (
            <span className="text-[11px] text-text-secondary truncate max-w-[200px]">
              {nodeExplanation.title}
            </span>
          )}
        </div>
      </div>

      {/* Loading State */}
      {dagLoading && (
        <div className="text-[11px] text-text-muted animate-pulse py-4 text-center">
          Loading explanation...
        </div>
      )}

      {/* Error State */}
      {dagError && (
        <div className="text-[11px] text-neon-rose py-2">{dagError}</div>
      )}

      {/* Markdown Explanation Content */}
      {nodeExplanation?.markdownContent && (
        <MarkdownPreview
          content={nodeExplanation.markdownContent}
          title={nodeExplanation.title || selectedNode.id}
          maxHeight="max-h-[50vh]"
        />
      )}

      {/* Dependencies Summary */}
      {nodeExplanation && (nodeExplanation.directDependencies.length > 0 || nodeExplanation.dependents.length > 0) && (
        <div className="space-y-2 pt-2 border-t border-border-subtle">
          {nodeExplanation.directDependencies.length > 0 && (
            <div className="text-[11px]">
              <span className="text-text-muted">Dependencies:</span>
              <div className="mt-1 flex flex-wrap gap-1">
                {nodeExplanation.directDependencies.slice(0, 8).map((dep) => (
                  <span key={dep} className="px-1.5 py-0.5 rounded bg-bg-elevated border border-border-subtle text-[10px] font-mono text-text-secondary">
                    {dep.length > 16 ? `${dep.slice(0, 8)}…${dep.slice(-6)}` : dep}
                  </span>
                ))}
                {nodeExplanation.directDependencies.length > 8 && (
                  <span className="text-[10px] text-text-dimmed">+{nodeExplanation.directDependencies.length - 8} more</span>
                )}
              </div>
            </div>
          )}
          {nodeExplanation.dependents.length > 0 && (
            <div className="text-[11px]">
              <span className="text-text-muted">Dependents:</span>
              <div className="mt-1 flex flex-wrap gap-1">
                {nodeExplanation.dependents.slice(0, 8).map((dep) => (
                  <span key={dep} className="px-1.5 py-0.5 rounded bg-bg-elevated border border-border-subtle text-[10px] font-mono text-text-secondary">
                    {dep.length > 16 ? `${dep.slice(0, 8)}…${dep.slice(-6)}` : dep}
                  </span>
                ))}
                {nodeExplanation.dependents.length > 8 && (
                  <span className="text-[10px] text-text-dimmed">+{nodeExplanation.dependents.length - 8} more</span>
                )}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Fallback */}
      {!dagLoading && !nodeExplanation && selectedNode.label && (
        <div className="text-xs text-text-secondary leading-relaxed p-3 rounded-lg border border-border-subtle bg-bg-elevated/50">
          {selectedNode.label}
        </div>
      )}
    </div>
  )
})
