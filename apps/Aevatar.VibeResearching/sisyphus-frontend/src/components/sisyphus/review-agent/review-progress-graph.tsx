// ============================================================
//  Review Progress Graph
//  Canvas-based visualization of knowledge nodes with review status
// ============================================================

import { useCallback, useEffect, useRef, useState, useMemo } from 'react'
import * as d3 from 'd3-force'
import { cn } from '@/lib/utils'
import { ReviewGraphRenderer, type ReviewLayoutNode, type ReviewLayoutEdge, type Transform } from './review-graph-renderer'
import type { ReviewNodeStatus } from './review-node-styles'

// ─────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────

export interface ReviewGraphNode {
  nodeId: string
  label: string
  sessionId?: string | null
  reviewStatus: string
  lastReviewedAt?: string | null
  deactivatedAt?: string | null
  deactivatedReason?: string | null
  dependsOn: string[]
}

export interface ReviewGraphEdge {
  source: string
  target: string
  type: string
}

export interface ReviewProgressGraphProps {
  nodes: ReviewGraphNode[]
  edges: ReviewGraphEdge[]
  currentNodeId?: string | null
  onNodeClick?: (node: ReviewGraphNode) => void
  className?: string
}

// ─────────────────────────────────────────────────────────────
// Tooltip Component
// ─────────────────────────────────────────────────────────────

interface TooltipProps {
  node: ReviewLayoutNode & { originalNode: ReviewGraphNode }
  x: number
  y: number
}

function NodeTooltip({ node, x, y }: TooltipProps) {
  const statusColors: Record<string, { bg: string; border: string }> = {
    reviewed: { bg: '#3b82f6', border: '#60a5fa' },
    pending: { bg: '#eab308', border: '#facc15' },
    deactivated: { bg: '#ef4444', border: '#f87171' },
    removed: { bg: '#a855f7', border: '#c084fc' },
    reviewing: { bg: '#f97316', border: '#fb923c' },
  }

  const style = statusColors[node.reviewStatus] || statusColors.pending

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
        {/* Status badge */}
        <div className="flex items-center gap-2 mb-2">
          <span
            className="text-[9px] px-1.5 py-0.5 rounded uppercase font-semibold"
            style={{ background: `${style.bg}30`, color: style.border }}
          >
            {node.reviewStatus}
          </span>
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
        {/* Deactivation reason if applicable */}
        {node.originalNode.deactivatedReason && (
          <div className="mt-2 pt-2 border-t border-slate-600/50 text-red-400 text-[10px]">
            Reason: {node.originalNode.deactivatedReason}
          </div>
        )}
      </div>
    </div>
  )
}

// ─────────────────────────────────────────────────────────────
// Main Component
// ─────────────────────────────────────────────────────────────

export function ReviewProgressGraph({
  nodes,
  edges,
  currentNodeId,
  onNodeClick,
  className,
}: ReviewProgressGraphProps) {
  // Refs
  const containerRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const rendererRef = useRef<ReviewGraphRenderer | null>(null)
  const simulationRef = useRef<d3.Simulation<ReviewLayoutNode, ReviewLayoutEdge> | null>(null)
  const animationRef = useRef<number>(0)
  const pulseRef = useRef<number>(0)

  // State
  const [transform, setTransform] = useState<Transform>({ x: 0, y: 0, scale: 1 })
  const [hoveredNode, setHoveredNode] = useState<{
    node: ReviewLayoutNode & { originalNode: ReviewGraphNode }
    x: number
    y: number
  } | null>(null)
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [canvasSize, setCanvasSize] = useState({ width: 600, height: 400 })
  const [layoutNodes, setLayoutNodes] = useState<(ReviewLayoutNode & { originalNode: ReviewGraphNode })[]>([])
  const [layoutEdges, setLayoutEdges] = useState<ReviewLayoutEdge[]>([])

  // Convert graph data to layout format
  const { convertedNodes, convertedEdges } = useMemo(() => {
    const cnodes: (ReviewLayoutNode & { originalNode: ReviewGraphNode })[] = nodes.map(n => ({
      id: n.nodeId,
      label: n.label,
      reviewStatus: (n.reviewStatus === 'reviewing' && currentNodeId === n.nodeId
        ? 'reviewing'
        : n.reviewStatus) as ReviewNodeStatus,
      originalNode: n,
    }))

    // Mark current reviewing node
    if (currentNodeId) {
      const currentNode = cnodes.find(n => n.id === currentNodeId)
      if (currentNode) {
        currentNode.reviewStatus = 'reviewing'
      }
    }

    const cedges: ReviewLayoutEdge[] = edges.map((e, i) => ({
      id: `e-${e.source}-${e.target}-${i}`,
      source: e.source,
      target: e.target,
    }))

    return { convertedNodes: cnodes, convertedEdges: cedges }
  }, [nodes, edges, currentNodeId])

  // Initialize force simulation
  useEffect(() => {
    if (convertedNodes.length === 0) {
      setLayoutNodes([])
      setLayoutEdges([])
      return
    }

    // Cleanup previous simulation
    simulationRef.current?.stop()

    // Create new simulation
    const simulation = d3.forceSimulation<ReviewLayoutNode, ReviewLayoutEdge>(convertedNodes)
      .force('link', d3.forceLink<ReviewLayoutNode, ReviewLayoutEdge>(convertedEdges)
        .id(d => d.id)
        .distance(100)
        .strength(0.5))
      .force('charge', d3.forceManyBody().strength(-300))
      .force('center', d3.forceCenter(canvasSize.width / 2, canvasSize.height / 2))
      .force('collision', d3.forceCollide().radius(40))

    simulation.on('tick', () => {
      setLayoutNodes([...convertedNodes] as (ReviewLayoutNode & { originalNode: ReviewGraphNode })[])
    })

    // Run simulation for a bit then slow down
    simulation.alpha(1).restart()

    simulationRef.current = simulation
    setLayoutEdges(convertedEdges)

    return () => {
      simulation.stop()
    }
  }, [convertedNodes, convertedEdges, canvasSize])

  // Initialize renderer
  useEffect(() => {
    const canvas = canvasRef.current
    const container = containerRef.current
    if (!canvas || !container) return

    // Get actual container size
    const rect = container.getBoundingClientRect()
    const width = rect.width > 0 ? rect.width : 600
    const height = rect.height > 0 ? rect.height : 400

    // Create renderer
    const renderer = new ReviewGraphRenderer(canvas, {
      selectedNodeId,
    })
    renderer.resize(width, height)
    rendererRef.current = renderer

    setCanvasSize({ width, height })
    setTransform({ x: 0, y: 0, scale: 1 })

    return () => {
      cancelAnimationFrame(animationRef.current)
      rendererRef.current = null
    }
  }, [])

  // Update renderer config
  useEffect(() => {
    rendererRef.current?.setConfig({
      selectedNodeId,
    })
  }, [selectedNodeId])

  // Render loop with pulse animation
  useEffect(() => {
    let lastTime = performance.now()

    const render = () => {
      const now = performance.now()
      const dt = (now - lastTime) / 1000
      lastTime = now

      // Update pulse phase for reviewing nodes
      pulseRef.current += dt * 4 // Speed of pulse

      const renderer = rendererRef.current
      if (renderer && layoutNodes.length > 0) {
        renderer.setTransform(transform)
        renderer.setConfig({ pulsePhase: pulseRef.current })
        renderer.render(layoutNodes, layoutEdges)
      }
      animationRef.current = requestAnimationFrame(render)
    }

    render()
    return () => cancelAnimationFrame(animationRef.current)
  }, [layoutNodes, layoutEdges, transform])

  // Resize handling
  useEffect(() => {
    const container = containerRef.current
    if (!container) return

    const resizeObserver = new ResizeObserver((entries) => {
      const entry = entries[0]
      if (entry) {
        const { width, height } = entry.contentRect
        setCanvasSize({ width, height })
        rendererRef.current?.resize(width, height)
      }
    })

    resizeObserver.observe(container)
    return () => resizeObserver.disconnect()
  }, [])

  // Mouse interaction handlers
  const handleMouseMove = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current
    if (!canvas || !rendererRef.current) return

    const rect = canvas.getBoundingClientRect()
    const x = e.clientX - rect.left
    const y = e.clientY - rect.top

    const node = rendererRef.current.hitTest(x, y, layoutNodes)
    if (node) {
      setHoveredNode({
        node: node as ReviewLayoutNode & { originalNode: ReviewGraphNode },
        x: e.clientX,
        y: e.clientY,
      })
      canvas.style.cursor = 'pointer'
    } else {
      setHoveredNode(null)
      canvas.style.cursor = 'grab'
    }
  }, [layoutNodes])

  const handleClick = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current
    if (!canvas || !rendererRef.current) return

    const rect = canvas.getBoundingClientRect()
    const x = e.clientX - rect.left
    const y = e.clientY - rect.top

    const node = rendererRef.current.hitTest(x, y, layoutNodes)
    if (node) {
      const layoutNode = node as ReviewLayoutNode & { originalNode: ReviewGraphNode }
      setSelectedNodeId(node.id)
      onNodeClick?.(layoutNode.originalNode)
    } else {
      setSelectedNodeId(null)
    }
  }, [layoutNodes, onNodeClick])

  // Pan handling
  const isDragging = useRef(false)
  const lastMousePos = useRef({ x: 0, y: 0 })

  const handleMouseDown = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    if (!rendererRef.current?.hitTest(
      e.clientX - (canvasRef.current?.getBoundingClientRect().left ?? 0),
      e.clientY - (canvasRef.current?.getBoundingClientRect().top ?? 0),
      layoutNodes
    )) {
      isDragging.current = true
      lastMousePos.current = { x: e.clientX, y: e.clientY }
    }
  }, [layoutNodes])

  const handleMouseUp = useCallback(() => {
    isDragging.current = false
  }, [])

  const handleMouseMoveForPan = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    if (isDragging.current) {
      const dx = e.clientX - lastMousePos.current.x
      const dy = e.clientY - lastMousePos.current.y
      lastMousePos.current = { x: e.clientX, y: e.clientY }
      setTransform(prev => ({
        ...prev,
        x: prev.x + dx,
        y: prev.y + dy,
      }))
    }
    handleMouseMove(e)
  }, [handleMouseMove])

  // Wheel zoom
  const handleWheel = useCallback((e: React.WheelEvent<HTMLCanvasElement>) => {
    e.preventDefault()
    const canvas = canvasRef.current
    if (!canvas) return

    const rect = canvas.getBoundingClientRect()
    const mouseX = e.clientX - rect.left
    const mouseY = e.clientY - rect.top

    const scaleFactor = e.deltaY > 0 ? 0.9 : 1.1
    const newScale = Math.max(0.1, Math.min(3, transform.scale * scaleFactor))

    // Zoom towards mouse position
    const worldX = (mouseX - transform.x) / transform.scale
    const worldY = (mouseY - transform.y) / transform.scale

    setTransform({
      x: mouseX - worldX * newScale,
      y: mouseY - worldY * newScale,
      scale: newScale,
    })
  }, [transform])

  // Fit view
  const fitView = useCallback(() => {
    if (layoutNodes.length === 0) return

    const xs = layoutNodes.filter(n => n.x !== undefined).map(n => n.x!)
    const ys = layoutNodes.filter(n => n.y !== undefined).map(n => n.y!)

    if (xs.length === 0) return

    const minX = Math.min(...xs) - 50
    const maxX = Math.max(...xs) + 50
    const minY = Math.min(...ys) - 50
    const maxY = Math.max(...ys) + 50

    const graphWidth = maxX - minX
    const graphHeight = maxY - minY

    const scale = Math.min(
      canvasSize.width / graphWidth,
      canvasSize.height / graphHeight,
      1.5
    ) * 0.9

    setTransform({
      x: (canvasSize.width - graphWidth * scale) / 2 - minX * scale,
      y: (canvasSize.height - graphHeight * scale) / 2 - minY * scale,
      scale,
    })
  }, [layoutNodes, canvasSize])

  // Auto fit on initial load
  useEffect(() => {
    if (layoutNodes.length > 0) {
      const timer = setTimeout(fitView, 500)
      return () => clearTimeout(timer)
    }
  }, [layoutNodes.length > 0])

  // Empty state
  if (nodes.length === 0) {
    return (
      <div className={cn("flex items-center justify-center h-full text-text-muted", className)}>
        <div className="text-center">
          <div className="text-4xl mb-2">📊</div>
          <div className="text-sm font-mono">No knowledge nodes to display</div>
        </div>
      </div>
    )
  }

  return (
    <div ref={containerRef} className={cn("relative w-full h-full", className)}>
      <canvas
        ref={canvasRef}
        className="w-full h-full"
        style={{ cursor: 'grab' }}
        onMouseMove={handleMouseMoveForPan}
        onMouseDown={handleMouseDown}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        onClick={handleClick}
        onWheel={handleWheel}
      />

      {/* Zoom controls */}
      <div className="absolute bottom-3 right-3 flex flex-col gap-1 p-1 rounded-lg bg-bg-surface/90 backdrop-blur-md border border-border-subtle">
        <button
          onClick={() => setTransform(prev => ({ ...prev, scale: prev.scale * 1.2 }))}
          className="p-1.5 rounded hover:bg-neon-cyan/20 text-text-muted hover:text-neon-cyan transition-colors"
          title="Zoom in"
        >
          <svg className="size-4" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
          </svg>
        </button>
        <button
          onClick={() => setTransform(prev => ({ ...prev, scale: prev.scale / 1.2 }))}
          className="p-1.5 rounded hover:bg-neon-cyan/20 text-text-muted hover:text-neon-cyan transition-colors"
          title="Zoom out"
        >
          <svg className="size-4" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" d="M20 12H4" />
          </svg>
        </button>
        <button
          onClick={fitView}
          className="p-1.5 rounded hover:bg-neon-cyan/20 text-text-muted hover:text-neon-cyan transition-colors"
          title="Fit view"
        >
          <svg className="size-4" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5v-4m0 4h-4m4 0l-5-5" />
          </svg>
        </button>
      </div>

      {/* Tooltip */}
      {hoveredNode && <NodeTooltip node={hoveredNode.node} x={hoveredNode.x} y={hoveredNode.y} />}
    </div>
  )
}

export default ReviewProgressGraph
