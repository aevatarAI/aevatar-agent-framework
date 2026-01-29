// ============================================================
//  Review Progress Popup
//  Popup dialog showing knowledge graph with review status
// ============================================================

import { useCallback, useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { X, Loader2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import { getReviewAgentGraph } from '@/lib/axiom-client'
import type { ReviewGraphResponse } from '@/types/review-agent'
import ReviewProgressGraph, { type ReviewGraphNode } from './review-progress-graph'

// Review Log Entry type (from review-agent-dashboard)
interface ReviewLogEntry {
  nodeId: string
  nodeLabel: string
  explainContent?: string | null
  result: 'Passed' | 'Failed' | 'Skipped' | null
  timestamp: number
  deactivatedReason?: string | null
  verificationContent?: string | null
}

interface ReviewProgressPopupProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  currentNodeId?: string | null
  reviewLog?: ReviewLogEntry[]
  // Unified counters from SSE (ensures consistency with Progress Counters)
  nodesReviewed?: number
  nodesPending?: number
  nodesDeactivated?: number
  nodesRemoved?: number
}

// ─────────────────────────────────────────────────────────────
// Legend Component
// ─────────────────────────────────────────────────────────────

function GraphLegend() {
  const items = [
    { status: 'validated', label: 'Validated', color: '#22c55e' },
    { status: 'pending', label: 'Pending', color: '#eab308' },
    { status: 'deactivated', label: 'Deactivated', color: '#ef4444' },
    { status: 'removed', label: 'Removed', color: '#a855f7' },
    { status: 'reviewing', label: 'Reviewing', color: '#f97316' },
  ]

  return (
    <div className="flex flex-wrap gap-3 items-center">
      {items.map(item => (
        <div key={item.status} className="flex items-center gap-1.5">
          <div
            className="w-3 h-3 rounded-full"
            style={{ backgroundColor: item.color }}
          />
          <span className="text-xs text-text-muted font-mono">{item.label}</span>
        </div>
      ))}
    </div>
  )
}

// ─────────────────────────────────────────────────────────────
// Node Details Panel
// ─────────────────────────────────────────────────────────────

interface NodeDetailsPanelProps {
  node: ReviewGraphNode | null
  onClose: () => void
}

function NodeDetailsPanel({ node, onClose }: NodeDetailsPanelProps) {
  if (!node) return null

  const statusColors: Record<string, string> = {
    validated: 'text-green-400',
    reviewed: 'text-green-400',  // backwards compatibility
    pending: 'text-yellow-400',
    deactivated: 'text-red-400',
    removed: 'text-purple-400',
    reviewing: 'text-orange-400',
  }

  return (
    <div className="absolute top-14 right-3 w-72 bg-bg-surface/95 backdrop-blur-md border border-border-subtle rounded-lg shadow-xl z-50">
      <div className="flex items-center justify-between px-3 py-2 border-b border-border-subtle">
        <span className="text-xs font-semibold text-text-secondary">Node Details</span>
        <button
          onClick={onClose}
          className="p-1 rounded hover:bg-white/10 text-text-muted hover:text-text-primary transition-colors"
        >
          <X className="size-3" />
        </button>
      </div>
      <div className="p-3 space-y-2 text-xs">
        <div>
          <span className="text-text-dimmed">ID:</span>
          <div className="font-mono text-text-primary truncate" title={node.nodeId}>
            {node.nodeId}
          </div>
        </div>
        <div>
          <span className="text-text-dimmed">Label:</span>
          <div className="text-text-secondary line-clamp-2">{node.label}</div>
        </div>
        <div>
          <span className="text-text-dimmed">Status:</span>
          <span className={cn("ml-1 font-semibold uppercase", statusColors[node.reviewStatus])}>
            {node.reviewStatus}
          </span>
        </div>
        {node.lastReviewedAt && (
          <div>
            <span className="text-text-dimmed">Last Reviewed:</span>
            <div className="text-text-secondary">
              {new Date(node.lastReviewedAt).toLocaleString()}
            </div>
          </div>
        )}
        {node.deactivatedAt && (
          <div>
            <span className="text-text-dimmed">Deactivated At:</span>
            <div className="text-text-secondary">
              {new Date(node.deactivatedAt).toLocaleString()}
            </div>
          </div>
        )}
        {node.deactivatedReason && (
          <div>
            <span className="text-text-dimmed">Reason:</span>
            <div className="text-red-400 text-[10px] line-clamp-3">{node.deactivatedReason}</div>
          </div>
        )}
        {node.dependsOn.length > 0 && (
          <div>
            <span className="text-text-dimmed">Dependencies ({node.dependsOn.length}):</span>
            <div className="mt-1 max-h-20 overflow-y-auto">
              {node.dependsOn.slice(0, 5).map(dep => (
                <div key={dep} className="font-mono text-text-muted text-[10px] truncate">
                  {dep}
                </div>
              ))}
              {node.dependsOn.length > 5 && (
                <div className="text-text-dimmed text-[10px]">
                  +{node.dependsOn.length - 5} more...
                </div>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

// ─────────────────────────────────────────────────────────────
// Helper Functions
// ─────────────────────────────────────────────────────────────

// Helper to extract failure reason from LLM JSON output
function extractFailureReason(verificationContent: string | null | undefined): string | null {
  if (!verificationContent) return null;

  try {
    // Try to find and parse JSON objects in the content
    const jsonMatches = verificationContent.match(/\{[^{}]*"reason"\s*:\s*"[^"]*"[^{}]*\}/g);
    if (jsonMatches) {
      for (const match of jsonMatches) {
        try {
          const parsed = JSON.parse(match);
          if (parsed.reason && typeof parsed.reason === 'string') {
            return parsed.reason;
          }
        } catch {
          // Continue to next match
        }
      }
    }

    // Try to extract reason from a larger JSON structure
    const reasonMatch = verificationContent.match(/"reason"\s*:\s*"([^"]+)"/);
    if (reasonMatch && reasonMatch[1]) {
      return reasonMatch[1];
    }
  } catch {
    // Fall through to return truncated content
  }

  // If we can't parse, return first meaningful text (skip json markers)
  const cleanedContent = verificationContent
    .replace(/^[\s`]*json[\s`]*/i, '')
    .replace(/```/g, '')
    .trim();

  if (cleanedContent.length > 150) {
    return cleanedContent.slice(0, 150) + '...';
  }
  return cleanedContent || null;
}

// ─────────────────────────────────────────────────────────────
// Review Detail Panel (for nodes with review log entries)
// ─────────────────────────────────────────────────────────────

interface ReviewDetailPanelProps {
  entry: ReviewLogEntry
  onClose: () => void
}

function ReviewDetailPanel({ entry, onClose }: ReviewDetailPanelProps) {
  return (
    <div className="w-full h-full flex flex-col overflow-hidden bg-bg-surface/95">
      {/* Detail Header */}
      <div className="flex-shrink-0 flex items-center justify-between p-3 border-b border-border-subtle">
        <h4 className="font-semibold text-text-primary text-sm">Review Detail</h4>
        <button
          onClick={onClose}
          className="p-1 rounded hover:bg-white/10 transition-colors"
        >
          <X className="w-4 h-4 text-text-muted" />
        </button>
      </div>

      {/* Detail Content - scrollable */}
      <div className="flex-1 overflow-y-auto p-3 space-y-3">
        {/* Result Badge & Timestamp */}
        <div className="flex items-center gap-3">
          <span className={cn(
            "inline-flex items-center gap-2 px-3 py-1.5 rounded-full text-sm font-semibold",
            entry.result === 'Passed'
              ? "bg-neon-green/20 text-neon-green"
              : entry.result === 'Failed'
              ? "bg-neon-red/20 text-neon-red"
              : "bg-neon-orange/20 text-neon-orange"
          )}>
            {entry.result === 'Passed' ? '✓ Passed' : entry.result === 'Failed' ? '✗ Failed' : '○ Skipped'}
          </span>
          <span className="text-text-muted text-xs font-mono">
            {new Date(entry.timestamp).toLocaleString()}
          </span>
        </div>

        {/* Node ID */}
        <div>
          <label className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">Node ID</label>
          <div className="h-[36px] p-2 rounded bg-surface-elevated border border-border-subtle overflow-x-auto">
            <code className="text-text-primary text-xs font-mono whitespace-nowrap">{entry.nodeId}</code>
          </div>
        </div>

        {/* Knowledge Node */}
        <div>
          <label className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">Knowledge Node</label>
          <div className="h-[60px] p-2 rounded bg-surface-elevated border border-border-subtle overflow-y-auto">
            <p className="text-text-primary text-sm whitespace-pre-wrap break-words">{entry.nodeLabel || '-'}</p>
          </div>
        </div>

        {/* Explain Content */}
        <div>
          <label className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">Explain Content</label>
          <div className="h-[100px] p-2 rounded bg-surface-elevated border border-border-subtle overflow-y-auto">
            <p className="text-text-muted text-xs whitespace-pre-wrap break-words font-mono">
              {entry.explainContent || '-'}
            </p>
          </div>
        </div>

        {/* Failure Reason (if failed) */}
        {entry.result === 'Failed' && (
          <div>
            <label className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">Failure Reason</label>
            <div className="h-[70px] p-2 rounded bg-neon-red/10 border border-neon-red/30 overflow-y-auto">
              <p className="text-neon-red text-sm whitespace-pre-wrap break-words">
                {entry.deactivatedReason || extractFailureReason(entry.verificationContent) || '-'}
              </p>
            </div>
          </div>
        )}

        {/* Verification Content (LLM Response) */}
        {entry.verificationContent && (
          <div>
            <label className="text-text-dimmed text-xs uppercase tracking-wider block mb-1">LLM Response</label>
            <div className="h-[120px] p-2 rounded bg-surface-elevated border border-border-subtle overflow-y-auto">
              <pre className="text-text-primary text-xs font-mono whitespace-pre-wrap break-words">
                {entry.verificationContent}
              </pre>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

// ─────────────────────────────────────────────────────────────
// Main Component
// ─────────────────────────────────────────────────────────────

export function ReviewProgressPopup({
  open,
  onOpenChange,
  currentNodeId,
  reviewLog,
  nodesReviewed,
  nodesPending,
  nodesDeactivated,
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  nodesRemoved: _nodesRemoved,
}: ReviewProgressPopupProps) {
  const [graphData, setGraphData] = useState<ReviewGraphResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [selectedNode, setSelectedNode] = useState<ReviewGraphNode | null>(null)
  const [selectedReviewEntry, setSelectedReviewEntry] = useState<ReviewLogEntry | null>(null)

  // Fetch graph data when popup opens
  const fetchGraphData = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await getReviewAgentGraph()
      setGraphData(data)
    } catch (err) {
      console.error('[ReviewProgressPopup] Error fetching graph:', err)
      setError(err instanceof Error ? err.message : 'Failed to load graph')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    if (open) {
      fetchGraphData()
      setSelectedNode(null)
      setSelectedReviewEntry(null)
    }
  }, [open, fetchGraphData])

  // All nodes and edges (no filtering)
  const allNodes = graphData?.nodes ?? []
  const allEdges = graphData?.edges ?? []

  const handleNodeClick = useCallback((node: ReviewGraphNode) => {
    setSelectedNode(node)
    // Find matching review log entry by nodeId
    const matchedEntry = reviewLog?.find(entry => entry.nodeId === node.nodeId)
    setSelectedReviewEntry(matchedEntry ?? null)
  }, [reviewLog])

  // Handle ESC key
  useEffect(() => {
    if (!open) return
    const handleEsc = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onOpenChange(false)
    }
    document.addEventListener('keydown', handleEsc)
    return () => document.removeEventListener('keydown', handleEsc)
  }, [open, onOpenChange])

  // Prevent body scroll when open
  useEffect(() => {
    if (open) {
      document.body.style.overflow = 'hidden'
    } else {
      document.body.style.overflow = ''
    }
    return () => { document.body.style.overflow = '' }
  }, [open])

  if (!open) return null

  // Use portal to render at document body level for proper centering
  return createPortal(
    <div className="fixed inset-0 z-[20000]">
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black/70 backdrop-blur-sm"
        onClick={() => onOpenChange(false)}
      />

      {/* Centered content */}
      <div className="fixed inset-0 flex items-center justify-center p-4">
        <div className="relative flex flex-col w-[80vw] max-w-5xl h-[75vh] bg-bg-surface/95 backdrop-blur-md border border-border-default rounded-xl shadow-2xl text-text-primary overflow-hidden">
          {/* Header */}
          <div className="flex-shrink-0 p-4 border-b border-border-subtle">
            <div className="flex items-center justify-between">
              <div>
                <h2 className="text-lg font-semibold text-text-primary">
                  Review Progress Graph
                </h2>
                <p className="text-text-muted text-sm mt-1">
                  {/* Use SSE counters if provided (unified with Progress Counters), fallback to graphData */}
                  {(() => {
                    const hasSSECounters = nodesReviewed !== undefined && nodesPending !== undefined
                    const totalNodes = hasSSECounters
                      ? (nodesReviewed ?? 0) + (nodesPending ?? 0)
                      : graphData?.totalNodes ?? 0
                    const validatedCount = hasSSECounters
                      ? (nodesReviewed ?? 0) - (nodesDeactivated ?? 0)
                      : graphData?.validatedCount ?? 0
                    const pendingCount = hasSSECounters ? (nodesPending ?? 0) : (graphData?.pendingCount ?? 0)
                    const deactivatedCount = hasSSECounters ? (nodesDeactivated ?? 0) : (graphData?.deactivatedCount ?? 0)
                    const reviewingCount = graphData?.reviewingCount ?? 0

                    return (
                      <>
                        {totalNodes} nodes total
                        {' • '}
                        <span className="text-green-400">{validatedCount} validated</span>
                        {' • '}
                        <span className="text-yellow-400">{pendingCount} pending</span>
                        {' • '}
                        <span className="text-red-400">{deactivatedCount} deactivated</span>
                        {reviewingCount > 0 && (
                          <>
                            {' • '}
                            <span className="text-orange-400">{reviewingCount} reviewing</span>
                          </>
                        )}
                      </>
                    )
                  })()}
                </p>
              </div>
              <button
                onClick={() => onOpenChange(false)}
                className="p-2 rounded-lg text-text-muted hover:text-text-primary hover:bg-surface-elevated transition-colors"
              >
                <X className="size-5" />
              </button>
            </div>
            {/* Legend */}
            <div className="pt-3">
              <GraphLegend />
            </div>
          </div>

          {/* Main content area - flex row when detail panel is shown */}
          <div className={cn(
            "flex-1 flex min-h-0 m-3 gap-3",
            selectedReviewEntry ? "flex-row" : "flex-col"
          )}>
            {/* Graph container */}
            <div className={cn(
              "relative bg-[#0c0f14] rounded-lg border border-border-subtle overflow-hidden transition-all duration-300",
              selectedReviewEntry ? "w-[60%]" : "w-full h-full"
            )}>
              {loading ? (
                <div className="absolute inset-0 flex items-center justify-center">
                  <div className="flex flex-col items-center gap-2">
                    <Loader2 className="size-8 animate-spin text-neon-cyan" />
                    <span className="text-sm text-text-muted font-mono">Loading graph...</span>
                  </div>
                </div>
              ) : error ? (
                <div className="absolute inset-0 flex items-center justify-center">
                  <div className="text-center">
                    <div className="text-4xl mb-2">❌</div>
                    <div className="text-sm text-red-400 font-mono">{error}</div>
                    <button
                      onClick={fetchGraphData}
                      className="mt-3 px-3 py-1.5 text-xs font-mono rounded border border-neon-cyan/40 text-neon-cyan hover:bg-neon-cyan/10 transition-colors"
                    >
                      Retry
                    </button>
                  </div>
                </div>
              ) : (
                <ReviewProgressGraph
                  nodes={allNodes}
                  edges={allEdges}
                  currentNodeId={currentNodeId}
                  onNodeClick={handleNodeClick}
                  className="w-full h-full"
                />
              )}

              {/* Simple node details panel - only show when no review entry found */}
              {selectedNode && !selectedReviewEntry && (
                <NodeDetailsPanel
                  node={selectedNode}
                  onClose={() => setSelectedNode(null)}
                />
              )}
            </div>

            {/* Review Detail Panel - shown when a node with review log entry is selected */}
            {selectedReviewEntry && (
              <div className="w-[40%] rounded-lg border border-border-subtle overflow-hidden">
                <ReviewDetailPanel
                  entry={selectedReviewEntry}
                  onClose={() => {
                    setSelectedNode(null)
                    setSelectedReviewEntry(null)
                  }}
                />
              </div>
            )}
          </div>
        </div>
      </div>
    </div>,
    document.body
  )
}

export default ReviewProgressPopup
