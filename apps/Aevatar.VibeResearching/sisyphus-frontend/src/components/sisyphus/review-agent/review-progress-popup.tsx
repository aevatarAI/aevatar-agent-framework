// ============================================================
//  Review Progress Popup
//  Popup dialog showing knowledge graph with review status
// ============================================================

import { useCallback, useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { X, Loader2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import ReviewProgressGraph, { type ReviewGraphNode, type ReviewGraphEdge } from './review-progress-graph'

// ─────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────

interface ReviewGraphResponse {
  nodes: ReviewGraphNode[]
  edges: ReviewGraphEdge[]
  totalNodes: number
  reviewedCount: number
  pendingCount: number
  deactivatedCount: number
  reviewingCount: number
}

interface ReviewProgressPopupProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  currentNodeId?: string | null
}

// ─────────────────────────────────────────────────────────────
// Legend Component
// ─────────────────────────────────────────────────────────────

function GraphLegend() {
  const items = [
    { status: 'reviewed', label: 'Reviewed', color: '#3b82f6' },
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
    reviewed: 'text-blue-400',
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
// Main Component
// ─────────────────────────────────────────────────────────────

export function ReviewProgressPopup({
  open,
  onOpenChange,
  currentNodeId,
}: ReviewProgressPopupProps) {
  const [graphData, setGraphData] = useState<ReviewGraphResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [selectedNode, setSelectedNode] = useState<ReviewGraphNode | null>(null)

  // Fetch graph data when popup opens
  const fetchGraphData = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const response = await fetch('/api/review-agent/graph')
      if (!response.ok) {
        throw new Error(`Failed to fetch graph: ${response.status}`)
      }
      const data: ReviewGraphResponse = await response.json()
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
    }
  }, [open, fetchGraphData])

  // All nodes and edges (no filtering)
  const allNodes = graphData?.nodes ?? []
  const allEdges = graphData?.edges ?? []

  const handleNodeClick = useCallback((node: ReviewGraphNode) => {
    setSelectedNode(node)
  }, [])

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
                  {graphData && (
                    <>
                      {graphData.totalNodes} nodes total
                      {' • '}
                      <span className="text-blue-400">{graphData.reviewedCount} reviewed</span>
                      {' • '}
                      <span className="text-yellow-400">{graphData.pendingCount} pending</span>
                      {' • '}
                      <span className="text-red-400">{graphData.deactivatedCount} deactivated</span>
                      {graphData.reviewingCount > 0 && (
                        <>
                          {' • '}
                          <span className="text-orange-400">{graphData.reviewingCount} reviewing</span>
                        </>
                      )}
                    </>
                  )}
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

          {/* Graph container */}
          <div className="flex-1 relative min-h-0 bg-[#0c0f14] m-3 rounded-lg border border-border-subtle overflow-hidden">
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

            {/* Node details panel */}
            <NodeDetailsPanel
              node={selectedNode}
              onClose={() => setSelectedNode(null)}
            />
          </div>
        </div>
      </div>
    </div>,
    document.body
  )
}

export default ReviewProgressPopup
