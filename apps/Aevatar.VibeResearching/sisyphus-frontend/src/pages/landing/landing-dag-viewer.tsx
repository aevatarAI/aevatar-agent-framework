// ============================================================
//  Landing DAG Viewer - Canvas-based Interactive DAG Display
//  Data source: Global DAG API (real data from backend)
// ============================================================

import { useCallback, useMemo, useState, useRef, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { RotateCcw, Sparkles, Loader2, GitBranch, RefreshCw, X } from 'lucide-react'
import { cn } from '@/lib/utils'
import { getGlobalDagSnapshot, type DagSnapshot, type DagNode as ApiDagNode } from '@/lib/axiom-client'
import type { DAGGraph, DAGNode } from '@/types'
import {
  createPersistentSimulation,
  findCenterNode,
  type LayoutNode,
  type LayoutEdge,
  type SimulationManager,
} from '@/components/sisyphus/workflow-topology/radial-force-layout'
import { CanvasRenderer, type Transform } from '@/components/sisyphus/workflow-topology/canvas-renderer'
import { InteractionManager } from '@/components/sisyphus/workflow-topology/interaction-manager'

// ─── Transform API data to frontend format ───
function transformApiData(snapshot: DagSnapshot): DAGGraph {
  const nodes: DAGNode[] = (snapshot.nodes || []).map((node: ApiDagNode) => ({
    id: node.id,
    label: node.label || node.id,
    kind: (node.kind as 'Plan' | 'Knowledge') || 'Knowledge',
    status: 'completed',
    type: node.type || 'Knowledge',
    proof: node.proof,
    attestationsCount: node.attestationsCount,
    planStatus: node.planStatus as 'Pending' | 'Active' | 'Completed' | undefined,
    sessionId: node.sessionId,
  }))

  const edges = (snapshot.edges || []).map((edge) => ({
    source: edge.fromId,
    target: edge.toId,
    type: edge.type,
  }))

  return { nodes, edges }
}

// ─── Calculate Stats ───
function calculateStats(data: DAGGraph | null) {
  if (!data) return { totalNodes: 0, totalEdges: 0, planCount: 0, knowledgeCount: 0 }
  
  const planNodes = data.nodes.filter(n => n.kind === 'Plan')
  const knowledgeNodes = data.nodes.filter(n => n.kind === 'Knowledge')
  
  return {
    totalNodes: data.nodes.length,
    totalEdges: data.edges.length,
    planCount: planNodes.length,
    knowledgeCount: knowledgeNodes.length,
  }
}

// ─── Tooltip Component ───
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
        <div className="flex flex-wrap items-center gap-1.5 mb-2">
          {node.kind && (
            <span className={cn(
              "text-[9px] px-1.5 py-0.5 rounded shrink-0",
              node.kind === 'Plan' ? "bg-blue-500/20 text-blue-400" : "bg-green-500/20 text-green-400"
            )}>
              {node.kind}
            </span>
          )}
          {node.planStatus && (
            <span className={cn(
              "text-[9px] px-1.5 py-0.5 rounded shrink-0",
              node.planStatus === 'Active' && "bg-blue-500/20 text-blue-400",
              node.planStatus === 'Completed' && "bg-green-500/20 text-green-400"
            )}>
              {node.planStatus}
            </span>
          )}
        </div>
        <div className="font-bold truncate mb-2 pb-2 border-b border-slate-600/50" style={{ color: style.bg }}>
          {node.id.length > 24 ? node.id.slice(0, 22) + '..' : node.id}
        </div>
        <div className="text-slate-200 leading-relaxed text-[11px] line-clamp-2">
          {node.label}
        </div>
      </div>
    </div>
  )
}

export function LandingDagViewer() {
  const navigate = useNavigate()
  const containerRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const rendererRef = useRef<CanvasRenderer | null>(null)
  const interactionRef = useRef<InteractionManager | null>(null)
  const simulationRef = useRef<SimulationManager | null>(null)
  const positionCacheRef = useRef<Map<string, { x: number; y: number }>>(new Map())
  const animationRef = useRef<number>(0)

  const [selectedNode, setSelectedNode] = useState<DAGNode | null>(null)
  const [hoveredNode, setHoveredNode] = useState<{ node: LayoutNode; x: number; y: number } | null>(null)
  // Initialize transform to center (will be updated by fitView)
  const [transform, setTransform] = useState<Transform>({ x: 300, y: 250, scale: 1 })
  const [canvasSize, setCanvasSize] = useState({ width: 600, height: 500 })
  const [isCanvasReady, setIsCanvasReady] = useState(false)

  // Layout state
  const [layoutNodes, setLayoutNodes] = useState<LayoutNode[]>([])
  const [layoutEdges, setLayoutEdges] = useState<LayoutEdge[]>([])

  // Data Loading State
  const [dagData, setDagData] = useState<DAGGraph | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // ─── Fetch Global DAG Data ───
  const fetchDagData = useCallback(async () => {
    setIsLoading(true)
    setError(null)
    try {
      const snapshot = await getGlobalDagSnapshot()
      if (snapshot && snapshot.nodes && snapshot.nodes.length > 0) {
        const transformed = transformApiData(snapshot)
        setDagData(transformed)
      } else {
        setDagData(null)
      }
    } catch (err) {
      console.error('[LandingDagViewer] Failed to fetch DAG:', err)
      setError((err as Error)?.message || 'Failed to load knowledge graph')
      setDagData(null)
    } finally {
      setIsLoading(false)
    }
  }, [])

  // Fetch on mount
  useEffect(() => {
    fetchDagData()
  }, [fetchDagData])

  const stats = useMemo(() => calculateStats(dagData), [dagData])

  // ─── Convert DAG to Layout format ───
  const { filteredNodes, filteredEdges, centerNodeId } = useMemo(() => {
    if (!dagData) return { filteredNodes: [], filteredEdges: [], centerNodeId: null }

    const nodes: LayoutNode[] = dagData.nodes.map((node) => ({
      id: node.id,
      label: node.label || node.id,
      kind: node.kind as 'Plan' | 'Knowledge' | undefined,
      planStatus: node.planStatus as 'Pending' | 'Active' | 'Completed' | undefined,
      isOtherSession: false,
      level: 0,
    }))

    // Build node ID set for edge filtering (prevent "node not found" errors)
    const nodeIds = new Set(nodes.map(n => n.id))

    // Filter edges: only include edges where both source and target exist
    const edges: LayoutEdge[] = dagData.edges
      .filter(e => nodeIds.has(e.source) && nodeIds.has(e.target))
      .map((e, i) => ({
        id: `e-${e.source}-${e.target}-${i}`,
        source: e.source,
        target: e.target,
        type: e.type,
      }))

    const center = findCenterNode(nodes, edges)
    return { filteredNodes: nodes, filteredEdges: edges, centerNodeId: center }
  }, [dagData])

  // ─── Calculate layout with persistent simulation ───
  useEffect(() => {
    simulationRef.current?.destroy()
    simulationRef.current = null

    if (filteredNodes.length === 0) {
      setLayoutNodes([])
      setLayoutEdges([])
      return
    }

    // Skip if canvas size is not ready
    if (canvasSize.width <= 0 || canvasSize.height <= 0) {
      return
    }

    const manager = createPersistentSimulation(
      filteredNodes,
      filteredEdges,
      {
        width: canvasSize.width,
        height: canvasSize.height,
        centerNodeId,
        existingPositions: positionCacheRef.current,
      },
      (nodes) => {
        setLayoutNodes([...nodes])
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

    manager.nodes.forEach(n => {
      if (n.x !== undefined && n.y !== undefined) {
        positionCacheRef.current.set(n.id, { x: n.x, y: n.y })
      }
    })

    // Auto fitView after simulation completes (initial load only)
    // Delay allows React state to update and interaction manager to be ready
    const fitTimer = setTimeout(() => {
      if (interactionRef.current && !hasInitialFitRef.current) {
        interactionRef.current.fitView(manager.nodes, canvasSize.width, canvasSize.height)
        hasInitialFitRef.current = true
      }
    }, 100)

    return () => {
      clearTimeout(fitTimer)
      manager.destroy()
    }
  }, [filteredNodes, filteredEdges, centerNodeId, canvasSize])

  // ─── Initialize renderer and interaction ───
  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return

    const renderer = new CanvasRenderer(canvas, {
      selectedNodeId: selectedNode?.id,
    })
    renderer.resize(canvasSize.width, canvasSize.height)
    // Override renderer's fixed pixel CSS with responsive sizing
    // This ensures canvas fills its container regardless of canvasSize values
    canvas.style.width = '100%'
    canvas.style.height = '100%'
    rendererRef.current = renderer

    const interaction = new InteractionManager(
      canvas,
      (x, y, nodes) => renderer.hitTest(x, y, nodes),
      {
        onTransformChange: setTransform,
        onNodeClick: (node) => {
          if (node && dagData) {
            const dagNode = dagData.nodes.find(n => n.id === node.id)
            setSelectedNode(dagNode || null)
          }
        },
        onNodeHover: (node, x, y) => {
          setHoveredNode(node ? { node, x, y } : null)
        },
        onNodeDrag: (node, x, y) => {
          simulationRef.current?.updateNodePosition(node.id, x, y)
        },
        onNodeDragEnd: (_node) => {
          simulationRef.current?.setDraggedNode(null)
          simulationRef.current?.reheat()
        },
        onBackgroundClick: () => setSelectedNode(null),
      }
    )
    interactionRef.current = interaction

    return () => {
      interaction.destroy()
      cancelAnimationFrame(animationRef.current)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps -- selectedNode used in callback, not as dependency
  }, [canvasSize, dagData])

  // ─── Update renderer config ───
  useEffect(() => {
    rendererRef.current?.setConfig({ selectedNodeId: selectedNode?.id })
  }, [selectedNode])

  // ─── Render on change (not continuous loop) ───
  useEffect(() => {
    const renderer = rendererRef.current
    if (renderer && layoutNodes.length > 0) {
      renderer.setTransform(transform)
      renderer.render(layoutNodes, layoutEdges)
    }
  }, [layoutNodes, layoutEdges, transform])

  // ─── Update interaction nodes ───
  useEffect(() => {
    interactionRef.current?.setNodes(layoutNodes)
    interactionRef.current?.setTransform(transform)
  }, [layoutNodes, transform])

  // ─── Resize handling ───
  useEffect(() => {
    const container = containerRef.current
    const canvas = canvasRef.current
    if (!container || !canvas) return

    let resizeTimer: ReturnType<typeof setTimeout> | null = null

    // Update canvas size to match container
    const updateSize = () => {
      const rect = container.getBoundingClientRect()
      const width = Math.floor(rect.width)
      const height = Math.floor(Math.max(400, rect.height))
      if (width > 0 && height > 0) {
        setCanvasSize({ width, height })
        rendererRef.current?.resize(width, height)
        // Force canvas to fill container by overriding renderer's inline styles
        canvas.style.width = '100%'
        canvas.style.height = '100%'
        if (!isCanvasReady) {
          setTransform({ x: width / 2, y: height / 2, scale: 1 })
          setIsCanvasReady(true)
        }
      }
    }

    // Debounced resize handler
    const handleResize = () => {
      if (resizeTimer) clearTimeout(resizeTimer)
      resizeTimer = setTimeout(updateSize, 100)
    }

    // Initial size check
    const initialTimer = requestAnimationFrame(() => {
      updateSize()
    })

    const resizeObserver = new ResizeObserver(handleResize)

    resizeObserver.observe(container)
    return () => {
      cancelAnimationFrame(initialTimer)
      if (resizeTimer) clearTimeout(resizeTimer)
      resizeObserver.disconnect()
    }
  }, [isCanvasReady])

  // ─── Auto-fit on load ───
  // Track if initial fitView has been done
  const hasInitialFitRef = useRef(false)
  
  useEffect(() => {
    // Reset fit flag when nodes change significantly
    if (layoutNodes.length === 0) {
      hasInitialFitRef.current = false
    }
  }, [layoutNodes.length])
  
  useEffect(() => {
    if (layoutNodes.length > 0 && interactionRef.current && isCanvasReady && canvasSize.width > 0 && !hasInitialFitRef.current) {
      // Wait for force layout to stabilize (simulation runs ~300 iterations)
      // Then fit view to show all nodes
      const timeoutId = setTimeout(() => {
        interactionRef.current?.fitView(layoutNodes, canvasSize.width, canvasSize.height)
        hasInitialFitRef.current = true
      }, 800)
      return () => clearTimeout(timeoutId)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps -- fitView only on initial load
  }, [layoutNodes.length, isCanvasReady, canvasSize.width, canvasSize.height])

  // ─── Reset View ───
  const handleResetView = useCallback(() => {
    interactionRef.current?.fitView(layoutNodes, canvasSize.width, canvasSize.height)
  }, [layoutNodes, canvasSize])

  // ─── Empty State ───
  const EmptyState = () => (
    <div className="flex-1 flex items-center justify-center p-8">
      <div className="text-center max-w-md">
        <div className="relative mx-auto mb-8">
          <div className="size-24 rounded-2xl bg-gradient-to-br from-neon-cyan/20 to-neon-purple/20 flex items-center justify-center border border-neon-cyan/30">
            <GitBranch className="size-12 text-neon-cyan/60" />
          </div>
          <div className="absolute -top-2 -right-2 size-4 rounded-full bg-neon-gold/40 animate-pulse" />
          <div className="absolute -bottom-1 -left-1 size-3 rounded-full bg-neon-purple/40 animate-pulse" style={{ animationDelay: '0.5s' }} />
        </div>
        <h3 className="font-display text-xl font-bold text-text-primary mb-3">Knowledge Graph Awaits</h3>
        <p className="text-sm text-text-secondary leading-relaxed mb-6">
          No research sessions yet. Start your first research session to build your knowledge graph.
        </p>
        <div className="flex flex-col items-center gap-3">
          <button
            onClick={() => navigate('/app')}
            className="inline-flex items-center gap-2 px-6 py-3 rounded-xl bg-neon-cyan text-bg-base font-semibold transition-all hover:shadow-glow-cyan hover:-translate-y-0.5"
          >
            <Sparkles className="size-4" />
            <span>Start First Research</span>
          </button>
          <button
            onClick={fetchDagData}
            className="inline-flex items-center gap-2 px-4 py-2 text-sm text-text-muted hover:text-neon-cyan transition-colors"
          >
            <RefreshCw className="size-3" />
            <span>Refresh</span>
          </button>
        </div>
      </div>
    </div>
  )

  // ─── Loading State ───
  const LoadingState = () => (
    <div className="flex-1 flex items-center justify-center">
      <div className="text-center">
        <Loader2 className="size-12 text-neon-cyan animate-spin mx-auto mb-4" />
        <p className="text-sm text-text-muted font-mono">Loading Knowledge Graph...</p>
      </div>
    </div>
  )

  // ─── Error State ───
  const ErrorState = () => (
    <div className="flex-1 flex items-center justify-center p-8">
      <div className="text-center max-w-md">
        <div className="size-16 rounded-xl bg-red-500/20 flex items-center justify-center mx-auto mb-4 border border-red-500/30">
          <X className="size-8 text-red-400" />
        </div>
        <h3 className="font-display text-lg font-bold text-text-primary mb-2">Failed to Load</h3>
        <p className="text-sm text-text-secondary mb-4">{error}</p>
        <button
          onClick={fetchDagData}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-lg border border-neon-cyan/50 text-neon-cyan hover:bg-neon-cyan/10 transition-all"
        >
          <RefreshCw className="size-4" />
          <span>Try Again</span>
        </button>
      </div>
    </div>
  )

  // ─── Render Content ───
  const renderContent = () => {
    if (isLoading) return <LoadingState />
    if (error) return <ErrorState />
    if (!dagData || dagData.nodes.length === 0) return <EmptyState />

    return (
      <div ref={containerRef} className="w-full h-full relative bg-[#0c0f14]">
        {/* Use inline style with !important to override renderer's style.width/height */}
        <canvas 
          ref={canvasRef} 
          className="block" 
          style={{ 
            cursor: 'grab',
            width: '100%',
            height: '100%',
          }} 
        />
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
            onClick={handleResetView}
            className="p-1.5 rounded hover:bg-neon-cyan/20 text-neon-cyan transition-colors"
            title="Fit view"
          >
            <svg className="size-4" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5v-4m0 4h-4m4 0l-5-5" />
            </svg>
          </button>
        </div>
        {hoveredNode && <NodeTooltip node={hoveredNode.node} x={hoveredNode.x} y={hoveredNode.y} />}
      </div>
    )
  }

  return (
    <div className="flex h-[550px]">
      {/* Left: DAG Visualization */}
      <div className="flex-[1.5] h-full relative">
        {renderContent()}
        {dagData && dagData.nodes.length > 0 && (
          <div className="absolute top-4 right-4 z-30 flex items-center gap-2">
            <button
              onClick={fetchDagData}
              className="p-2 rounded-lg bg-bg-surface/90 backdrop-blur-md border border-border-subtle text-text-muted hover:text-neon-cyan hover:border-neon-cyan/30 transition-all"
              title="Refresh Data"
            >
              <RefreshCw className="size-4" />
            </button>
            <button
              onClick={handleResetView}
              className="p-2 rounded-lg bg-bg-surface/90 backdrop-blur-md border border-border-subtle text-text-muted hover:text-neon-cyan hover:border-neon-cyan/30 transition-all"
              title="Reset View"
            >
              <RotateCcw className="size-4" />
            </button>
          </div>
        )}
      </div>

      {/* Right: Sidebar */}
      <div className="w-80 border-l border-border-subtle bg-bg-surface/80 backdrop-blur-md overflow-y-auto flex flex-col">
        <div className="p-4 space-y-6 flex-1">
          <div className="space-y-3">
            <h3 className="font-display text-xs text-neon-cyan tracking-wider">KNOWLEDGE GRAPH</h3>
            <StatCard label="KNOWLEDGE NODES" value={stats.knowledgeCount} color="green" />
          </div>

          {selectedNode && (
            <div className="space-y-3">
              <h3 className="font-display text-xs text-neon-purple tracking-wider">SELECTED NODE</h3>
              <div className="p-4 rounded-lg border border-border-subtle bg-bg-base/50 space-y-3">
                <div className="flex items-center gap-2">
                  <span className={cn(
                    'px-2 py-0.5 text-xs font-mono rounded border',
                    selectedNode.kind === 'Plan'
                      ? 'bg-neon-purple/20 text-neon-purple border-neon-purple/30'
                      : 'bg-neon-green/20 text-neon-green border-neon-green/30'
                  )}>
                    {selectedNode.kind || 'Node'}
                  </span>
                  {selectedNode.kind === 'Plan' && selectedNode.planStatus && (
                    <span className={cn(
                      'px-2 py-0.5 text-xs font-mono rounded border',
                      selectedNode.planStatus === 'Active'
                        ? 'bg-neon-gold/20 text-neon-gold border-neon-gold/30'
                        : selectedNode.planStatus === 'Completed'
                        ? 'bg-neon-green/20 text-neon-green border-neon-green/30'
                        : 'bg-bg-accent text-text-muted border-border-subtle'
                    )}>
                      {selectedNode.planStatus}
                    </span>
                  )}
                </div>
                <p className="text-sm font-medium text-text-primary leading-snug">{selectedNode.label}</p>
                <p className="text-xs text-text-dimmed font-mono">{selectedNode.id}</p>
                {selectedNode.proof && (
                  <div className="pt-2 border-t border-border-subtle">
                    <span className="text-xs text-neon-cyan">Proof:</span>
                    <p className="text-xs text-text-secondary mt-1">{selectedNode.proof}</p>
                  </div>
                )}
                {selectedNode.attestationsCount && (
                  <div className="flex items-center gap-2 text-xs text-text-muted">
                    <span className="text-neon-gold">✓</span>
                    <span>{selectedNode.attestationsCount} attestations</span>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>

        <div className="p-4 border-t border-border-subtle">
          <button
            onClick={() => navigate('/app')}
            className="w-full flex items-center justify-center gap-2 px-4 py-3 rounded-xl bg-neon-cyan text-bg-base font-semibold transition-all hover:shadow-glow-cyan hover:-translate-y-0.5"
          >
            <Sparkles className="size-4" />
            <span>Start Your Research</span>
          </button>
          <p className="mt-2 text-xs text-text-dimmed text-center">Build your own knowledge graph</p>
        </div>
      </div>
    </div>
  )
}

// ─── Stat Card Component ───
function StatCard({ label, value, color, className }: { label: string; value: number; color: 'cyan' | 'gold' | 'purple' | 'green'; className?: string }) {
  const colorClasses = {
    cyan: 'border-neon-cyan/30 text-neon-cyan',
    gold: 'border-neon-gold/30 text-neon-gold',
    purple: 'border-neon-purple/30 text-neon-purple',
    green: 'border-neon-green/30 text-neon-green',
  }

  return (
    <div className={cn('p-3 rounded-lg border bg-bg-base/50', colorClasses[color], className)}>
      <div className="font-display text-2xl font-bold">{value}</div>
      <div className="text-[10px] font-mono text-text-dimmed tracking-wider">{label}</div>
    </div>
  )
}
