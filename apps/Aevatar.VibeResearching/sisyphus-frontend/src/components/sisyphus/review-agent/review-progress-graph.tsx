// ============================================================
//  Review Progress Graph
//  Canvas-based visualization of knowledge nodes with review status
// ============================================================

import { useCallback, useEffect, useRef, useState, useMemo } from 'react'
import { createPortal } from 'react-dom'
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
    validated: { bg: '#22c55e', border: '#4ade80' },
    reviewed: { bg: '#22c55e', border: '#4ade80' },  // backwards compatibility
    pending: { bg: '#eab308', border: '#facc15' },
    deactivated: { bg: '#ef4444', border: '#f87171' },
    removed: { bg: '#a855f7', border: '#c084fc' },
    reviewing: { bg: '#f97316', border: '#fb923c' },
  }

  const style = statusColors[node.reviewStatus] || statusColors.pending

  // Use portal to render tooltip at document body level to avoid CSS interference
  return createPortal(
    <div
      className="fixed pointer-events-none"
      style={{ left: x + 15, top: y - 10, zIndex: 30000 }}
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
    </div>,
    document.body
  )
}

// ─────────────────────────────────────────────────────────────
// Layout Node Type with position
// ─────────────────────────────────────────────────────────────

type LayoutNode = ReviewLayoutNode & {
  originalNode: ReviewGraphNode
  fx?: number | null
  fy?: number | null
  vx?: number
  vy?: number
}

// ─────────────────────────────────────────────────────────────
// Helper: Screen to World coordinate conversion
// ─────────────────────────────────────────────────────────────

const screenToWorld = (screenX: number, screenY: number, t: Transform) => ({
  x: (screenX - t.x) / t.scale,
  y: (screenY - t.y) / t.scale,
})

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
  const simulationRef = useRef<d3.Simulation<LayoutNode, ReviewLayoutEdge> | null>(null)
  const animationRef = useRef<number>(0)
  const pulseRef = useRef<number>(0)
  const nodePositionsRef = useRef<Map<string, { x: number; y: number }>>(new Map())
  const hasInitialFitView = useRef(false)

  // State
  const [transform, setTransform] = useState<Transform>({ x: 0, y: 0, scale: 1 })
  const [hoveredNode, setHoveredNode] = useState<{
    node: LayoutNode
    x: number
    y: number
  } | null>(null)
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [canvasSize, setCanvasSize] = useState({ width: 600, height: 400 })
  const [layoutNodes, setLayoutNodes] = useState<LayoutNode[]>([])
  const [layoutEdges, setLayoutEdges] = useState<ReviewLayoutEdge[]>([])

  // Drag state
  const [draggedNode, setDraggedNode] = useState<LayoutNode | null>(null)
  const isDraggingNode = useRef(false)
  const hasDraggedSignificantly = useRef(false)  // Track if mouse actually moved during drag
  const dragStartPos = useRef<{ x: number; y: number } | null>(null)  // Initial mouse position
  const justFinishedDragging = useRef(false)  // Prevent click after drag
  const [cursorStyle, setCursorStyle] = useState<'grab' | 'grabbing' | 'pointer' | 'default'>('default')

  // Base structure: only depends on nodes and edges (NOT currentNodeId)
  const { baseNodes, baseEdges } = useMemo(() => {
    const bnodes = nodes.map(n => ({
      id: n.nodeId,
      label: n.label,
      originalNode: n,
      // Map backend 'reviewed' to 'validated' for backwards compatibility
      // (new backend already returns 'validated')
      reviewStatus: (n.reviewStatus === 'reviewed' ? 'validated' : n.reviewStatus) as ReviewNodeStatus,
    }))
    const bedges: ReviewLayoutEdge[] = edges.map((e, i) => ({
      id: `e-${e.source}-${e.target}-${i}`,
      source: e.source,
      target: e.target,
    }))
    return { baseNodes: bnodes, baseEdges: bedges }
  }, [nodes, edges])  // ← NOT depends on currentNodeId

  // Update review status when currentNodeId changes (without restarting layout)
  // Note: Only the currentNodeId node should be 'reviewing', all others should use their
  // original status (but 'reviewing' from backend should be treated as 'pending' if not current)
  useEffect(() => {
    if (layoutNodes.length === 0) return

    let needsUpdate = false
    layoutNodes.forEach(node => {
      let newStatus: ReviewNodeStatus
      if (currentNodeId === node.id) {
        // Current node being reviewed
        newStatus = 'reviewing'
      } else {
        // For non-current nodes, use original status but:
        // - 'reviewing' -> 'pending' (only one node shows as 'reviewing')
        // - 'reviewed' -> 'validated' (rename for frontend)
        const originalStatus = node.originalNode.reviewStatus
        if (originalStatus === 'reviewing') {
          newStatus = 'pending'
        } else if (originalStatus === 'reviewed') {
          newStatus = 'validated'
        } else {
          newStatus = originalStatus as ReviewNodeStatus
        }
      }

      if (node.reviewStatus !== newStatus) {
        node.reviewStatus = newStatus
        needsUpdate = true
      }
    })

    if (needsUpdate) {
      setLayoutNodes([...layoutNodes])  // Trigger re-render without restarting simulation
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- intentionally omit layoutNodes to prevent restart loop
  }, [currentNodeId])

  // hitTest: detect which node is under the mouse
  const hitTest = useCallback((screenX: number, screenY: number): LayoutNode | null => {
    const worldPos = screenToWorld(screenX, screenY, transform)
    const nodeRadius = 24

    // Check in reverse order (top nodes first)
    for (let i = layoutNodes.length - 1; i >= 0; i--) {
      const node = layoutNodes[i]
      if (node.x === undefined || node.y === undefined) continue
      const dx = worldPos.x - node.x
      const dy = worldPos.y - node.y
      if (dx * dx + dy * dy <= nodeRadius * nodeRadius) {
        return node
      }
    }
    return null
  }, [layoutNodes, transform])

  // Initialize force simulation with optimized config (like Main Graph)
  useEffect(() => {
    if (baseNodes.length === 0) {
      setLayoutNodes([])
      setLayoutEdges([])
      return
    }

    // Cleanup previous simulation
    simulationRef.current?.stop()

    // Clean up positions for nodes that no longer exist
    const currentNodeIds = new Set(baseNodes.map(n => n.id))
    for (const nodeId of nodePositionsRef.current.keys()) {
      if (!currentNodeIds.has(nodeId)) {
        nodePositionsRef.current.delete(nodeId)
      }
    }

    // Create layout nodes, restore known positions
    // Note: Only currentNodeId should be 'reviewing', others with 'reviewing' status from backend
    // should be treated as 'pending' to ensure only one node shows as reviewing
    const newLayoutNodes: LayoutNode[] = baseNodes.map(n => {
      const savedPos = nodePositionsRef.current.get(n.id)
      let reviewStatus: ReviewNodeStatus
      if (currentNodeId === n.id) {
        reviewStatus = 'reviewing'
      } else {
        // For non-current nodes:
        // - 'reviewing' -> 'pending' (only one node shows as reviewing)
        // - baseNodes already have 'reviewed' mapped to 'validated'
        reviewStatus = (n.reviewStatus === 'reviewing' ? 'pending' : n.reviewStatus) as ReviewNodeStatus
      }
      return {
        ...n,
        reviewStatus,
        x: savedPos?.x,
        y: savedPos?.y,
        vx: 0,
        vy: 0,
      }
    })

    // Create edge links with proper references
    const newLayoutEdges = baseEdges.map(e => ({ ...e }))

    // Create new simulation with Main Graph-like config
    const simulation = d3.forceSimulation<LayoutNode, ReviewLayoutEdge>(newLayoutNodes)
      .force('link', d3.forceLink<LayoutNode, ReviewLayoutEdge>(newLayoutEdges)
        .id(d => d.id)
        .distance(100)
        .strength(0.3))  // Weaker link force
      .force('charge', d3.forceManyBody()
        .strength(-200)     // Moderate repulsion
        .distanceMax(250))  // Limit range
      .force('center', d3.forceCenter(canvasSize.width / 2, canvasSize.height / 2)
        .strength(0.05))    // Weak center force
      .force('collision', d3.forceCollide()
        .radius(35)
        .strength(0.9))     // Strong collision avoidance
      .alphaDecay(0.05)     // ⭐ Fast cooling (Main Graph config)
      .velocityDecay(0.6)   // ⭐ Smooth animation (Main Graph config)

    simulation.on('tick', () => {
      // Save positions for persistence
      newLayoutNodes.forEach(n => {
        if (n.x !== undefined && n.y !== undefined) {
          nodePositionsRef.current.set(n.id, { x: n.x, y: n.y })
        }
      })
      setLayoutNodes([...newLayoutNodes])
    })

    // First load uses high alpha, subsequent uses low alpha
    const hasExistingPositions = newLayoutNodes.some(n => n.x !== undefined)
    simulation.alpha(hasExistingPositions ? 0.1 : 0.8).restart()

    simulationRef.current = simulation
    setLayoutEdges(newLayoutEdges)

    return () => {
      simulation.stop()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- currentNodeId changes are handled by separate effect
  }, [baseNodes, baseEdges, canvasSize])

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

  // Pan handling
  const isDragging = useRef(false)
  const lastMousePos = useRef({ x: 0, y: 0 })

  // Mouse down: start node drag or canvas pan
  const handleMouseDown = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    const rect = canvasRef.current?.getBoundingClientRect()
    if (!rect) return

    const screenX = e.clientX - rect.left
    const screenY = e.clientY - rect.top
    const node = hitTest(screenX, screenY)

    if (node) {
      // Start node drag
      isDraggingNode.current = true
      hasDraggedSignificantly.current = false  // Reset drag tracking
      dragStartPos.current = { x: screenX, y: screenY }  // Record start position
      setDraggedNode(node)
      // Fix node position
      node.fx = node.x
      node.fy = node.y
      // Reheat simulation
      simulationRef.current?.alpha(0.3).restart()
      setCursorStyle('grabbing')
    } else {
      // Start canvas pan
      isDragging.current = true
      lastMousePos.current = { x: e.clientX, y: e.clientY }
    }
  }, [hitTest])

  // Mouse move: handle node drag, canvas pan, and hover detection
  const handleMouseMove = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    const rect = canvasRef.current?.getBoundingClientRect()
    if (!rect) return

    const screenX = e.clientX - rect.left
    const screenY = e.clientY - rect.top

    if (isDraggingNode.current && draggedNode) {
      // Check if mouse has moved significantly (more than 5 pixels)
      if (dragStartPos.current) {
        const dx = screenX - dragStartPos.current.x
        const dy = screenY - dragStartPos.current.y
        if (dx * dx + dy * dy > 25) {  // 5px threshold squared
          hasDraggedSignificantly.current = true
        }
      }

      // Node dragging
      const worldPos = screenToWorld(screenX, screenY, transform)

      // Update node fixed position
      draggedNode.fx = worldPos.x
      draggedNode.fy = worldPos.y
      draggedNode.x = worldPos.x
      draggedNode.y = worldPos.y

      // Keep simulation active
      simulationRef.current?.alpha(0.3).restart()
      setCursorStyle('grabbing')
    } else if (isDragging.current) {
      // Canvas panning
      const dx = e.clientX - lastMousePos.current.x
      const dy = e.clientY - lastMousePos.current.y
      lastMousePos.current = { x: e.clientX, y: e.clientY }
      setTransform(prev => ({
        ...prev,
        x: prev.x + dx,
        y: prev.y + dy,
      }))
    } else {
      // Hover detection
      const node = hitTest(screenX, screenY)
      if (node) {
        setHoveredNode({
          node,
          x: e.clientX,
          y: e.clientY,
        })
        setCursorStyle('grab')
      } else {
        setHoveredNode(null)
        setCursorStyle('default')
      }
    }
  }, [hitTest, draggedNode, transform])

  // Mouse up: release node drag or canvas pan
  const handleMouseUp = useCallback(() => {
    if (isDraggingNode.current && draggedNode) {
      // Release fixed position
      draggedNode.fx = null
      draggedNode.fy = null
      setDraggedNode(null)
      isDraggingNode.current = false
      // Only mark as "just finished dragging" if there was significant movement
      // This allows simple clicks to work properly
      if (hasDraggedSignificantly.current) {
        justFinishedDragging.current = true
        setTimeout(() => { justFinishedDragging.current = false }, 50)
      }
      hasDraggedSignificantly.current = false
      dragStartPos.current = null
      // Let simulation cool down slowly
      simulationRef.current?.alpha(0.1).restart()
    }
    isDragging.current = false
    setCursorStyle('default')
  }, [draggedNode])

  // Click: select node
  const handleClick = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    // Ignore click if we just finished dragging
    if (isDraggingNode.current || justFinishedDragging.current) return

    const rect = canvasRef.current?.getBoundingClientRect()
    if (!rect) return

    const screenX = e.clientX - rect.left
    const screenY = e.clientY - rect.top
    const node = hitTest(screenX, screenY)

    if (node) {
      setSelectedNodeId(node.id)
      onNodeClick?.(node.originalNode)
    } else {
      setSelectedNodeId(null)
    }
  }, [hitTest, onNodeClick])

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

  // Auto fit on initial load (only once)
  useEffect(() => {
    if (layoutNodes.length > 0 && !hasInitialFitView.current) {
      hasInitialFitView.current = true
      const timer = setTimeout(fitView, 500)
      return () => clearTimeout(timer)
    }
  }, [layoutNodes.length, fitView])

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
        style={{ cursor: cursorStyle }}
        onMouseMove={handleMouseMove}
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
