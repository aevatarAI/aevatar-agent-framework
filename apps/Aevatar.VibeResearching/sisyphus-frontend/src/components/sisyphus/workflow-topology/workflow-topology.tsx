// ============================================================
//  WorkflowTopology - Canvas-based DAG Visualization
//  Radial Force Layout with Active Milestone as Center
// ============================================================

import { useCallback, useMemo, useEffect, useRef, useState } from 'react'
import { GitBranch, ArrowUpCircle, ArrowDownCircle, Link2, X } from 'lucide-react'
import { useSisyphusStore } from '@/store/sisyphus-store'
import { useDagInteractions } from '@/hooks/use-dag-interactions'
import type { DAGNode, NodeKind } from '@/types'
import { getDagSnapshot } from '@/lib/axiom-client'
import { cn } from '@/lib/utils'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogCloseButton } from '@/components/ui/dialog'
import { SummaryModal } from '../summary-modal'
import { SubGraphViewer } from '../sub-graph-viewer'

import { type NodeFilterMode } from './dag-node-styles'
import {
  createRadialForceLayout,
  createPersistentSimulation,
  findCenterNode,
  type LayoutNode,
  type LayoutEdge,
  type SimulationManager,
} from './radial-force-layout'
import { CanvasRenderer, type Transform } from './canvas-renderer'
import { InteractionManager } from './interaction-manager'
import { NodeLegend } from './node-legend'
import { NodeDetailsPanel } from './node-details-panel'
import { TopologyHeader } from './topology-header'

// ─────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────

export interface WorkflowTopologyProps {
  sessionId: string
  fullHeight?: boolean
  onCollapse?: () => void
}

// ─────────────────────────────────────────────────────────────
// Tooltip Component
// ─────────────────────────────────────────────────────────────

interface TooltipProps {
  node: LayoutNode
  x: number
  y: number
}

function NodeTooltip({ node, x, y }: TooltipProps) {
  const style = node.kind === 'Plan'
    ? { bg: '#3b82f6', border: '#60a5fa' }
    : { bg: '#22c55e', border: '#4ade80' }

  return (
    <div
      className="fixed z-[100] pointer-events-none"
      style={{ left: x + 15, top: y - 10 }}
    >
      <div
        className="px-3 py-2.5 rounded-lg text-xs font-mono"
        style={{
          minWidth: '180px',
          maxWidth: '320px',
          background: 'rgba(10, 15, 25, 0.98)',
          border: `2px solid ${style.border}`,
          boxShadow: `0 0 30px ${style.bg}40, 0 4px 20px rgba(0,0,0,0.5)`,
        }}
      >
        {/* Badges */}
        <div className="flex flex-wrap items-center gap-1.5 mb-2">
          {node.kind && (
            <span className={cn(
              "text-[9px] px-1.5 py-0.5 rounded shrink-0",
              node.kind === 'Plan' ? "bg-blue-500/20 text-blue-400" : "bg-green-500/20 text-green-400"
            )}>
              {node.kind}
            </span>
          )}
          {node.isOtherSession && (
            <span className="text-[9px] px-1.5 py-0.5 rounded bg-gray-500/20 text-gray-400 shrink-0">
              Other Session
            </span>
          )}
          {node.planStatus && (
            <span className={cn(
              "text-[9px] px-1.5 py-0.5 rounded shrink-0",
              node.planStatus === 'Pending' && "bg-yellow-500/20 text-yellow-400",
              node.planStatus === 'Active' && "bg-blue-500/20 text-blue-400",
              node.planStatus === 'Completed' && "bg-green-500/20 text-green-400"
            )}>
              {node.planStatus}
            </span>
          )}
        </div>
        {/* ID */}
        <div
          className="font-bold truncate mb-2 pb-2 border-b border-slate-600/50"
          style={{ color: style.bg }}
          title={node.id}
        >
          {node.id.length > 24 ? node.id.slice(0, 22) + '..' : node.id}
        </div>
        {/* Label */}
        <div className="text-slate-200 leading-relaxed text-[11px] line-clamp-2" title={node.label}>
          {node.label}
        </div>
      </div>
    </div>
  )
}

// ─────────────────────────────────────────────────────────────
// Main Component
// ─────────────────────────────────────────────────────────────

export function WorkflowTopology({ sessionId, fullHeight = false, onCollapse }: WorkflowTopologyProps) {
  const { dag, selectedNodeId, setDag, setSelectedNode, isConnected, activeMilestoneNodeId, setActiveMilestoneNodeId, nodeExplanation } = useSisyphusStore()
  const { setHighlight, clearHighlight, highlightMode, highlightedNodeIds, dagStats } = useDagInteractions()

  // Refs
  const containerRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const rendererRef = useRef<CanvasRenderer | null>(null)
  const interactionRef = useRef<InteractionManager | null>(null)
  const simulationRef = useRef<SimulationManager | null>(null)
  const positionCacheRef = useRef<Map<string, { x: number; y: number }>>(new Map())
  const lastSessionIdRef = useRef<string | null>(null)
  const animationRef = useRef<number>(0)

  // State
  const [refreshing, setRefreshing] = useState(false)
  const [detailsOpen, setDetailsOpen] = useState(false)
  const [summaryOpen, setSummaryOpen] = useState(false)
  const [filterMode, setFilterMode] = useState<NodeFilterMode>('all')
  const [isFullscreen, setIsFullscreen] = useState(false)
  const [transform, setTransform] = useState<Transform>({ x: 0, y: 0, scale: 1 })
  const [hoveredNode, setHoveredNode] = useState<{ node: LayoutNode; x: number; y: number } | null>(null)
  const [canvasSize, setCanvasSize] = useState({ width: 800, height: 600 })

  // Layout nodes/edges
  const [layoutNodes, setLayoutNodes] = useState<LayoutNode[]>([])
  const [layoutEdges, setLayoutEdges] = useState<LayoutEdge[]>([])

  const selectedNodeForDialog = useMemo(() => {
    if (!selectedNodeId || !dag?.nodes) return null
    return dag.nodes.find(n => n.id === selectedNodeId) || null
  }, [dag, selectedNodeId])

  // ── Convert DAG to layout format ──
  const { filteredNodes, filteredEdges, centerNodeId } = useMemo(() => {
    if (!dag?.nodes || dag.nodes.length === 0) {
      return { filteredNodes: [], filteredEdges: [], centerNodeId: null }
    }

    const isHighlighting = highlightMode !== 'none'

    // Filter nodes
    const filteredDagNodes = dag.nodes.slice(0, 200).filter((node) => {
      if (filterMode === 'all') return true
      const isOtherSession = node.sessionId ? node.sessionId !== sessionId : false
      const isCurrentSession = !isOtherSession
      const isActiveMilestone = node.id === activeMilestoneNodeId || node.planStatus === 'Active'
      if (filterMode === 'PlanActive') return isActiveMilestone && node.kind === 'Plan'
      if (filterMode === 'Plan') return node.kind === 'Plan' && isCurrentSession
      if (filterMode === 'Knowledge') return node.kind === 'Knowledge' && isCurrentSession
      if (filterMode === 'OtherSession') return isOtherSession
      return true
    })

    const visibleNodeIds = new Set(filteredDagNodes.map(n => n.id))

    // Convert to LayoutNode
    const nodes: LayoutNode[] = filteredDagNodes.map((node) => {
      const isOtherSession = node.sessionId ? node.sessionId !== sessionId : false
      const isActiveMilestone = node.id === activeMilestoneNodeId
      return {
        id: node.id,
        label: node.label || node.id,
        kind: node.kind as 'Plan' | 'Knowledge' | undefined,
        planStatus: isActiveMilestone ? 'Active' : (node.planStatus as 'Pending' | 'Active' | 'Completed' | undefined),
        isOtherSession,
        level: 0, // Will be calculated by layout
      }
    })

    // Filter edges
    const edges: LayoutEdge[] = (dag.edges || [])
      .slice(0, 400)
      .filter(e => visibleNodeIds.has(e.source) && visibleNodeIds.has(e.target))
      .map((e, i) => ({
        id: `e-${e.source}-${e.target}-${i}`,
        source: e.source,
        target: e.target,
        type: e.type,
      }))

    // Find center node (Active milestone first, then most connected)
    const center = activeMilestoneNodeId && visibleNodeIds.has(activeMilestoneNodeId)
      ? activeMilestoneNodeId
      : findCenterNode(nodes, edges)

    return { filteredNodes: nodes, filteredEdges: edges, centerNodeId: center }
  }, [dag, selectedNodeId, highlightMode, highlightedNodeIds, sessionId, activeMilestoneNodeId, filterMode])

  // ── Calculate layout with persistent simulation ──
  useEffect(() => {
    // Cleanup previous simulation
    simulationRef.current?.destroy()
    simulationRef.current = null

    if (filteredNodes.length === 0) {
      setLayoutNodes([])
      setLayoutEdges([])
      return
    }

    // Skip if canvas size is invalid
    if (canvasSize.width <= 0 || canvasSize.height <= 0) {
      return
    }

    // Check if session changed - clear cache and reset transform
    const sessionChanged = lastSessionIdRef.current !== sessionId
    if (sessionChanged) {
      positionCacheRef.current.clear()
      setTransform({ x: canvasSize.width / 2, y: canvasSize.height / 2, scale: 1 })
      lastSessionIdRef.current = sessionId
    }

    // Create persistent simulation with position cache
    const manager = createPersistentSimulation(
      filteredNodes,
      filteredEdges,
      {
        width: canvasSize.width,
        height: canvasSize.height,
        centerNodeId,
        existingPositions: sessionChanged ? undefined : positionCacheRef.current,
      },
      (nodes) => {
        // Update layout and cache positions
        setLayoutNodes([...nodes])
        // Update position cache
        nodes.forEach(n => {
          if (n.x !== undefined && n.y !== undefined) {
            positionCacheRef.current.set(n.id, { x: n.x, y: n.y })
          }
        })
      }
    )

    simulationRef.current = manager
    setLayoutNodes([...manager.nodes])
    setLayoutEdges([...manager.edges])

    // Initial cache update
    manager.nodes.forEach(n => {
      if (n.x !== undefined && n.y !== undefined) {
        positionCacheRef.current.set(n.id, { x: n.x, y: n.y })
      }
    })

    return () => {
      manager.destroy()
    }
  }, [filteredNodes, filteredEdges, centerNodeId, canvasSize, sessionId])

  // ── Initialize renderer and interaction manager ──
  useEffect(() => {
    const canvas = canvasRef.current
    const container = containerRef.current
    if (!canvas || !container) return

    // Get actual container size for initial resize
    const rect = container.getBoundingClientRect()
    const actualWidth = rect.width
    const actualHeight = Math.max(300, rect.height - 120)

    // Create renderer with actual dimensions
    const renderer = new CanvasRenderer(canvas, {
      selectedNodeId,
      highlightedNodeIds,
      dimmedMode: highlightMode !== 'none',
    })
    renderer.resize(actualWidth, actualHeight)
    rendererRef.current = renderer

    // Update canvasSize state to match actual dimensions
    if (canvasSize.width !== actualWidth || canvasSize.height !== actualHeight) {
      setCanvasSize({ width: actualWidth, height: actualHeight })
    }

    // Create interaction manager
    const interaction = new InteractionManager(
      canvas,
      (x, y, nodes) => renderer.hitTest(x, y, nodes),
      {
        onTransformChange: setTransform,
        onNodeClick: (node) => {
          if (node) {
            setSelectedNode(node.id)
            clearHighlight()
            setDetailsOpen(true)
          }
        },
        onNodeHover: (node, x, y) => {
          setHoveredNode(node ? { node, x, y } : null)
        },
        onNodeDrag: (node, x, y) => {
          // Update position in simulation - this will push other nodes away
          simulationRef.current?.updateNodePosition(node.id, x, y)
        },
        onNodeDragEnd: (node) => {
          // Release the fixed position and let simulation settle
          simulationRef.current?.setDraggedNode(null)
          simulationRef.current?.reheat()
        },
        onBackgroundClick: () => {
          // Optional: clear selection on background click
        },
      }
    )
    interactionRef.current = interaction

    // Cleanup
    return () => {
      interaction.destroy()
      cancelAnimationFrame(animationRef.current)
    }
  }, [canvasSize.width, canvasSize.height])

  // ── Update renderer config when selection changes ──
  useEffect(() => {
    rendererRef.current?.setConfig({
      selectedNodeId,
      highlightedNodeIds,
      dimmedMode: highlightMode !== 'none',
    })
  }, [selectedNodeId, highlightedNodeIds, highlightMode])

  // ── Ensure renderer is resized when session changes ──
  useEffect(() => {
    // Delayed resize to ensure DOM is updated
    const timeoutId = setTimeout(() => {
      const container = containerRef.current
      if (!rendererRef.current || !container) return

      // Get actual container dimensions
      const rect = container.getBoundingClientRect()
      const actualWidth = rect.width
      const actualHeight = Math.max(300, rect.height - 120)

      if (actualWidth > 0 && actualHeight > 0) {
        rendererRef.current.resize(actualWidth, actualHeight)
        setCanvasSize({ width: actualWidth, height: actualHeight })
      }
    }, 50)
    return () => clearTimeout(timeoutId)
  }, [sessionId])

  // ── Render loop ──
  useEffect(() => {
    const render = () => {
      const renderer = rendererRef.current
      if (renderer && layoutNodes.length > 0) {
        renderer.setTransform(transform)
        renderer.render(layoutNodes, layoutEdges)
      }
      animationRef.current = requestAnimationFrame(render)
    }

    render()
    return () => cancelAnimationFrame(animationRef.current)
  }, [layoutNodes, layoutEdges, transform])

  // ── Update interaction manager nodes ──
  useEffect(() => {
    interactionRef.current?.setNodes(layoutNodes)
    interactionRef.current?.setTransform(transform)
  }, [layoutNodes, transform])

  // ── Resize handling ──
  useEffect(() => {
    const container = containerRef.current
    if (!container) return

    const resizeObserver = new ResizeObserver((entries) => {
      const entry = entries[0]
      if (entry) {
        const { width, height } = entry.contentRect
        // Subtract header and footer heights
        const canvasHeight = Math.max(300, height - 120)
        setCanvasSize({ width, height: canvasHeight })
        rendererRef.current?.resize(width, canvasHeight)
      }
    })

    resizeObserver.observe(container)
    return () => resizeObserver.disconnect()
  }, [])

  // ── Auto-fit on initial load or session change ──
  useEffect(() => {
    if (layoutNodes.length > 0 && interactionRef.current && canvasSize.width > 0) {
      // Use requestAnimationFrame to ensure DOM is ready, then fit view
      const rafId = requestAnimationFrame(() => {
        setTimeout(() => {
          interactionRef.current?.fitView(layoutNodes, canvasSize.width, canvasSize.height)
        }, 50)
      })
      return () => cancelAnimationFrame(rafId)
    }
  }, [sessionId, layoutNodes.length, canvasSize.width, canvasSize.height])

  // ── Focus on active milestone ──
  const focusOnActiveMilestone = useCallback(() => {
    if (!activeMilestoneNodeId || !interactionRef.current) return
    const node = layoutNodes.find(n => n.id === activeMilestoneNodeId)
    if (node) {
      interactionRef.current.focusOnNode(node, canvasSize.width, canvasSize.height)
    }
  }, [activeMilestoneNodeId, layoutNodes, canvasSize])

  // ── Refresh DAG ──
  const silentRefresh = useCallback(async () => {
    if (!sessionId || !isConnected) return
    try {
      const snapshot = await getDagSnapshot(sessionId)
      if (snapshot) {
        const nodes: DAGNode[] = (snapshot.nodes || []).map(n => ({
          id: n.id, label: n.label || n.id, status: 'completed', type: n.type || 'node',
          kind: n.kind as NodeKind | undefined, owner: n.owner, proof: n.proof,
          attestations: n.attestations, attestationsCount: n.attestationsCount, sessionId: n.sessionId,
          planStatus: n.planStatus as 'Pending' | 'Active' | 'Completed' | undefined,
        }))
        const edges = (snapshot.edges || []).map(e => ({ source: e.fromId, target: e.toId, type: e.type }))
        setDag({ nodes, edges })

        const activeNode = nodes.find(n => n.planStatus === 'Active')
        if (activeNode && activeNode.id !== activeMilestoneNodeId) {
          setActiveMilestoneNodeId(activeNode.id, sessionId)
        } else if (!activeNode && activeMilestoneNodeId) {
          setActiveMilestoneNodeId(null, sessionId)
        }
      }
    } catch (e) {
      if ((e as Error)?.name !== 'AbortError') console.error("Failed to refresh DAG:", e)
    }
  }, [sessionId, isConnected, setDag, activeMilestoneNodeId, setActiveMilestoneNodeId])

  const handleRefresh = useCallback(async () => {
    setRefreshing(true)
    await silentRefresh()
    setRefreshing(false)
  }, [silentRefresh])

  // Auto-refresh
  useEffect(() => {
    if (!sessionId || !isConnected) return
    silentRefresh()
    const intervalId = setInterval(silentRefresh, 10000)
    return () => clearInterval(intervalId)
  }, [sessionId, isConnected, silentRefresh])

  // ── Fullscreen handling ──
  const toggleFullscreen = useCallback(async () => {
    if (!isFullscreen) {
      try {
        if (containerRef.current) {
          await containerRef.current.requestFullscreen()
          setIsFullscreen(true)
        }
      } catch {
        setIsFullscreen(true)
      }
    } else {
      try {
        if (document.fullscreenElement) {
          await document.exitFullscreen()
        }
        setIsFullscreen(false)
      } catch {
        setIsFullscreen(false)
      }
    }
  }, [isFullscreen])

  useEffect(() => {
    const handleFullscreenChange = () => {
      setIsFullscreen(!!document.fullscreenElement)
    }
    document.addEventListener('fullscreenchange', handleFullscreenChange)
    return () => document.removeEventListener('fullscreenchange', handleFullscreenChange)
  }, [])

  const nodeCount = dag?.nodes?.length ?? 0
  const edgeCount = dag?.edges?.length ?? 0

  // ── Empty state ──
  if (!dag?.nodes || dag.nodes.length === 0) {
    return (
      <div
        ref={containerRef}
        className={cn(
          "flex flex-col overflow-hidden transition-all duration-300",
          isFullscreen ? "fixed inset-0 z-[9999] bg-[#0a0c10] border-4 border-neon-cyan/50" : "card",
          fullHeight && !isFullscreen && "h-full"
        )}
      >
        <TopologyHeader
          onRefresh={handleRefresh} refreshing={refreshing}
          nodeCount={0} edgeCount={0} activeMilestone={activeMilestoneNodeId}
          onFullscreenToggle={toggleFullscreen} isFullscreen={isFullscreen}
          onFocusActive={focusOnActiveMilestone}
        />
        <div className="flex-1 flex flex-col items-center justify-center py-8 text-center min-h-[300px]">
          <div className="relative">
            <div className="absolute inset-0 rounded-full bg-accent-emerald blur-2xl opacity-20 animate-pulse" />
            <div className="relative flex h-20 w-20 items-center justify-center rounded-full border-2 border-border bg-surface">
              <GitBranch className="h-10 w-10 text-text-muted" />
            </div>
          </div>
          <p className="mt-6 text-base text-text-muted font-mono">AWAITING TOPOLOGY DATA</p>
          <p className="mt-2 text-xs text-text-dimmed font-mono max-w-xs">Graph will materialize as reasoning progresses</p>
          {isConnected && (
            <button onClick={handleRefresh} disabled={refreshing} className="mt-4 px-4 py-2 text-xs font-mono rounded-lg border border-neon-cyan/40 text-neon-cyan hover:bg-neon-cyan/10 transition-colors disabled:opacity-50">
              {refreshing ? "Loading..." : "Load DAG"}
            </button>
          )}
        </div>
      </div>
    )
  }

  return (
    <div
      ref={containerRef}
      className={cn(
        "flex flex-col overflow-hidden transition-all duration-300",
        isFullscreen ? "fixed inset-0 z-[9999] bg-[#0a0c10] border-4 border-neon-cyan/50" : "card",
        fullHeight && !isFullscreen && "h-full"
      )}
    >
      {/* Header */}
      <div className="relative z-30 flex-shrink-0 bg-[#0c0f14]">
        <TopologyHeader
          onRefresh={handleRefresh} onCollapse={isFullscreen ? undefined : onCollapse}
          onSummary={() => setSummaryOpen(true)}
          onFullscreenToggle={toggleFullscreen} isFullscreen={isFullscreen}
          onFocusActive={focusOnActiveMilestone}
          refreshing={refreshing} nodeCount={nodeCount} edgeCount={edgeCount}
          planCount={dagStats.planCount} knowledgeCount={dagStats.knowledgeCount}
          activeMilestone={activeMilestoneNodeId}
        />
      </div>

      {/* Canvas container */}
      <div className={cn("flex-1 relative z-10", isFullscreen ? "min-h-0" : "min-h-[350px]")}>
        <div className="absolute inset-0 bg-[#0c0f14]">
          <canvas
            ref={canvasRef}
            className="w-full h-full"
            style={{ cursor: 'grab' }}
          />
          {/* Fullscreen exit button */}
          {isFullscreen && (
            <button
              onClick={toggleFullscreen}
              className="absolute top-4 left-4 z-50 flex items-center gap-2 px-3 py-2 rounded-lg bg-bg-surface/90 backdrop-blur-md border border-neon-cyan/30 text-neon-cyan hover:bg-neon-cyan/20 transition-all font-mono text-xs"
            >
              <X className="size-4" />
              <span>EXIT FULLSCREEN</span>
            </button>
          )}
          {/* Zoom controls */}
          <div className="absolute bottom-4 right-4 z-40 flex flex-col gap-1 p-1 rounded-lg bg-bg-surface/90 backdrop-blur-md border border-neon-cyan/30">
            <button
              onClick={() => interactionRef.current?.setZoom(transform.scale * 1.2, canvasSize.width / 2, canvasSize.height / 2)}
              className="p-1.5 rounded hover:bg-neon-cyan/20 text-neon-cyan transition-colors"
              title="Zoom in"
            >
              <svg className="size-4" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
              </svg>
            </button>
            <button
              onClick={() => interactionRef.current?.setZoom(transform.scale / 1.2, canvasSize.width / 2, canvasSize.height / 2)}
              className="p-1.5 rounded hover:bg-neon-cyan/20 text-neon-cyan transition-colors"
              title="Zoom out"
            >
              <svg className="size-4" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" d="M20 12H4" />
              </svg>
            </button>
            <button
              onClick={() => interactionRef.current?.fitView(layoutNodes, canvasSize.width, canvasSize.height)}
              className="p-1.5 rounded hover:bg-neon-cyan/20 text-neon-cyan transition-colors"
              title="Fit view"
            >
              <svg className="size-4" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5v-4m0 4h-4m4 0l-5-5" />
              </svg>
            </button>
          </div>
        </div>
      </div>

      {/* Footer */}
      <div className={cn("relative z-30 flex-shrink-0", isFullscreen && "bg-[#0c0f14]")}>
        <NodeLegend filterMode={filterMode} onFilterChange={setFilterMode} />
      </div>

      {/* Tooltip */}
      {hoveredNode && <NodeTooltip node={hoveredNode.node} x={hoveredNode.x} y={hoveredNode.y} />}

      {/* Node details modal */}
      <Dialog open={detailsOpen} onOpenChange={(open) => { setDetailsOpen(open); if (!open) { setSelectedNode(null); clearHighlight() } }}>
        <DialogContent className="bg-bg-surface/95 backdrop-blur-md border border-border-default rounded-xl shadow-lg text-text-primary overflow-hidden max-w-7xl w-[90vw]">
          <DialogHeader>
            <div className="min-w-0">
              <DialogTitle>Node details</DialogTitle>
              <DialogDescription>
                {selectedNodeForDialog?.id ? <span className="font-mono tabular-nums">{selectedNodeForDialog.id}</span> : <span className="font-mono">—</span>}
              </DialogDescription>
            </div>
            <DialogCloseButton />
          </DialogHeader>

          {/* Split Panel Layout */}
          <div className="flex min-h-[450px] max-h-[70vh]">
            {/* Left Panel - Sub-graph Visualization */}
            <div className="flex-1 min-w-[300px] max-w-[400px] border-r border-border-subtle">
              {selectedNodeId && dag && (
                <SubGraphViewer
                  dag={dag}
                  selectedNodeId={selectedNodeId}
                  selectedNodeKind={(selectedNodeForDialog?.kind as 'Plan' | 'Knowledge') || 'Knowledge'}
                  nodeExplanation={nodeExplanation}
                  onNodeSelect={setSelectedNode}
                />
              )}
            </div>

            {/* Right Panel - Details & Highlight Controls */}
            <div className="flex-[1.5] min-w-[350px] overflow-y-auto">
              <div className="p-4">
                <NodeDetailsPanel sessionId={sessionId} />
                {selectedNodeId && (
                  <div className="mt-4 pt-4 border-t border-border-subtle">
                    <div className="text-xs text-text-muted mb-2">Highlight related nodes:</div>
                    <div className="flex flex-wrap gap-2">
                      <button onClick={() => setHighlight(selectedNodeId, 'upstream')} className={cn("flex items-center gap-1.5 px-3 py-1.5 text-xs font-mono rounded-md border transition-all", highlightMode === 'upstream' ? "bg-neon-cyan/20 border-neon-cyan/40 text-neon-cyan" : "border-border-subtle text-text-muted hover:text-text-secondary hover:border-border-default")}>
                        <ArrowUpCircle className="size-3" />Upstream
                      </button>
                      <button onClick={() => setHighlight(selectedNodeId, 'downstream')} className={cn("flex items-center gap-1.5 px-3 py-1.5 text-xs font-mono rounded-md border transition-all", highlightMode === 'downstream' ? "bg-neon-gold/20 border-neon-gold/40 text-neon-gold" : "border-border-subtle text-text-muted hover:text-text-secondary hover:border-border-default")}>
                        <ArrowDownCircle className="size-3" />Downstream
                      </button>
                      <button onClick={() => setHighlight(selectedNodeId, 'chain')} className={cn("flex items-center gap-1.5 px-3 py-1.5 text-xs font-mono rounded-md border transition-all", highlightMode === 'chain' ? "bg-neon-green/20 border-neon-green/40 text-neon-green" : "border-border-subtle text-text-muted hover:text-text-secondary hover:border-border-default")}>
                        <Link2 className="size-3" />Full Chain
                      </button>
                      {highlightMode !== 'none' && (
                        <button onClick={clearHighlight} className="px-3 py-1.5 text-xs font-mono rounded-md border border-border-subtle text-text-muted hover:text-neon-rose hover:border-neon-rose/40 transition-all">Clear</button>
                      )}
                    </div>
                    {highlightMode !== 'none' && (
                      <div className="mt-2 text-[10px] text-text-dimmed">Highlighting {highlightedNodeIds.length} {highlightMode} node(s)</div>
                    )}
                  </div>
                )}
              </div>
            </div>
          </div>
        </DialogContent>
      </Dialog>

      <SummaryModal open={summaryOpen} onOpenChange={setSummaryOpen} sessionId={sessionId} />
    </div>
  )
}

export default WorkflowTopology
