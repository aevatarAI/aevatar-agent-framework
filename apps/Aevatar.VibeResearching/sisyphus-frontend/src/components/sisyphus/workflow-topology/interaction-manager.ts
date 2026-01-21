// ============================================================
//  Interaction Manager - Pan/Zoom/Click/Hover/Drag for Canvas
//  Handles all user interactions with the topology canvas
// ============================================================

import type { LayoutNode } from './radial-force-layout'
import type { Transform } from './canvas-renderer'

// ─────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────

export interface InteractionCallbacks {
  onTransformChange: (transform: Transform) => void
  onNodeClick: (node: LayoutNode | null) => void
  onNodeHover: (node: LayoutNode | null, x: number, y: number) => void
  onNodeDrag: (node: LayoutNode, x: number, y: number) => void
  onNodeDragEnd: (node: LayoutNode) => void
  onBackgroundClick: () => void
}

export interface InteractionConfig {
  minZoom: number
  maxZoom: number
  zoomSensitivity: number
  enableNodeDrag: boolean
}

// ─────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────

const DEFAULT_CONFIG: InteractionConfig = {
  minZoom: 0.1,
  maxZoom: 3,
  zoomSensitivity: 0.001,
  enableNodeDrag: true,
}

// ─────────────────────────────────────────────────────────────
// Interaction Manager Class
// ─────────────────────────────────────────────────────────────

export class InteractionManager {
  private canvas: HTMLCanvasElement
  private config: InteractionConfig
  private callbacks: InteractionCallbacks
  private transform: Transform = { x: 0, y: 0, scale: 1 }
  private nodes: LayoutNode[] = []

  // Pan state
  private isPanning = false
  private lastPanX = 0
  private lastPanY = 0

  // Node drag state
  private isDraggingNode = false
  private draggedNode: LayoutNode | null = null
  private dragStarted = false

  // Hit test function from renderer
  private hitTest: (x: number, y: number, nodes: LayoutNode[]) => LayoutNode | null

  // Bound event handlers (for cleanup)
  private boundHandlers: {
    mousedown: (e: MouseEvent) => void
    mousemove: (e: MouseEvent) => void
    mouseup: (e: MouseEvent) => void
    mouseleave: (e: MouseEvent) => void
    wheel: (e: WheelEvent) => void
  }

  constructor(
    canvas: HTMLCanvasElement,
    hitTest: (x: number, y: number, nodes: LayoutNode[]) => LayoutNode | null,
    callbacks: InteractionCallbacks,
    config: Partial<InteractionConfig> = {}
  ) {
    this.canvas = canvas
    this.hitTest = hitTest
    this.callbacks = callbacks
    this.config = { ...DEFAULT_CONFIG, ...config }

    // Bind handlers
    this.boundHandlers = {
      mousedown: this.handleMouseDown.bind(this),
      mousemove: this.handleMouseMove.bind(this),
      mouseup: this.handleMouseUp.bind(this),
      mouseleave: this.handleMouseLeave.bind(this),
      wheel: this.handleWheel.bind(this),
    }

    this.attachListeners()
  }

  // ── Public API ──

  setTransform(transform: Transform): void {
    this.transform = { ...transform }
  }

  getTransform(): Transform {
    return { ...this.transform }
  }

  setNodes(nodes: LayoutNode[]): void {
    this.nodes = nodes
  }

  focusOnNode(node: LayoutNode, canvasWidth: number, canvasHeight: number): void {
    if (node.x === undefined || node.y === undefined) return

    const newTransform: Transform = {
      x: canvasWidth / 2 - node.x * this.transform.scale,
      y: canvasHeight / 2 - node.y * this.transform.scale,
      scale: this.transform.scale,
    }

    this.transform = newTransform
    this.callbacks.onTransformChange(newTransform)
  }

  fitView(nodes: LayoutNode[], canvasWidth: number, canvasHeight: number, padding = 50): void {
    if (nodes.length === 0) return

    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity
    nodes.forEach(node => {
      if (node.x !== undefined && node.y !== undefined) {
        minX = Math.min(minX, node.x)
        minY = Math.min(minY, node.y)
        maxX = Math.max(maxX, node.x)
        maxY = Math.max(maxY, node.y)
      }
    })

    if (minX === Infinity) return

    const nodeRadius = 36
    minX -= nodeRadius + padding
    minY -= nodeRadius + padding
    maxX += nodeRadius + padding
    maxY += nodeRadius + padding

    const graphWidth = maxX - minX
    const graphHeight = maxY - minY

    const scaleX = canvasWidth / graphWidth
    const scaleY = canvasHeight / graphHeight
    const scale = Math.min(scaleX, scaleY, this.config.maxZoom)

    const centerX = (minX + maxX) / 2
    const centerY = (minY + maxY) / 2

    const newTransform: Transform = {
      x: canvasWidth / 2 - centerX * scale,
      y: canvasHeight / 2 - centerY * scale,
      scale: Math.max(scale, this.config.minZoom),
    }

    this.transform = newTransform
    this.callbacks.onTransformChange(newTransform)
  }

  setZoom(scale: number, centerX: number, centerY: number): void {
    const clampedScale = Math.max(this.config.minZoom, Math.min(this.config.maxZoom, scale))
    const scaleRatio = clampedScale / this.transform.scale

    const newTransform: Transform = {
      x: centerX - (centerX - this.transform.x) * scaleRatio,
      y: centerY - (centerY - this.transform.y) * scaleRatio,
      scale: clampedScale,
    }

    this.transform = newTransform
    this.callbacks.onTransformChange(newTransform)
  }

  resetView(canvasWidth: number, canvasHeight: number): void {
    const newTransform: Transform = {
      x: canvasWidth / 2,
      y: canvasHeight / 2,
      scale: 1,
    }

    this.transform = newTransform
    this.callbacks.onTransformChange(newTransform)
  }

  destroy(): void {
    this.detachListeners()
  }

  // ── Private: Screen to World coordinate conversion ──

  private screenToWorld(screenX: number, screenY: number): { x: number; y: number } {
    return {
      x: (screenX - this.transform.x) / this.transform.scale,
      y: (screenY - this.transform.y) / this.transform.scale,
    }
  }

  // ── Event Handlers ──

  private handleMouseDown(e: MouseEvent): void {
    if (e.button !== 0) return // Left button only

    const rect = this.canvas.getBoundingClientRect()
    const screenX = e.clientX - rect.left
    const screenY = e.clientY - rect.top

    // Check if clicking on a node
    const clickedNode = this.hitTest(screenX, screenY, this.nodes)

    if (clickedNode && this.config.enableNodeDrag) {
      // Start node dragging
      this.isDraggingNode = true
      this.draggedNode = clickedNode
      this.dragStarted = false
      this.canvas.style.cursor = 'grabbing'
    } else {
      // Start canvas panning
      this.isPanning = true
      this.lastPanX = e.clientX
      this.lastPanY = e.clientY
      this.canvas.style.cursor = 'grabbing'
    }
  }

  private handleMouseMove(e: MouseEvent): void {
    const rect = this.canvas.getBoundingClientRect()
    const screenX = e.clientX - rect.left
    const screenY = e.clientY - rect.top

    if (this.isDraggingNode && this.draggedNode) {
      // Node dragging mode
      this.dragStarted = true
      const worldPos = this.screenToWorld(screenX, screenY)
      
      // Update node position
      this.draggedNode.x = worldPos.x
      this.draggedNode.y = worldPos.y
      
      // Clear fixed position if it was set
      this.draggedNode.fx = worldPos.x
      this.draggedNode.fy = worldPos.y
      
      this.callbacks.onNodeDrag(this.draggedNode, worldPos.x, worldPos.y)
      this.canvas.style.cursor = 'grabbing'
    } else if (this.isPanning) {
      // Canvas panning mode
      const dx = e.clientX - this.lastPanX
      const dy = e.clientY - this.lastPanY
      this.lastPanX = e.clientX
      this.lastPanY = e.clientY

      const newTransform: Transform = {
        x: this.transform.x + dx,
        y: this.transform.y + dy,
        scale: this.transform.scale,
      }

      this.transform = newTransform
      this.callbacks.onTransformChange(newTransform)
    } else {
      // Hover detection
      const hoveredNode = this.hitTest(screenX, screenY, this.nodes)
      this.callbacks.onNodeHover(hoveredNode, e.clientX, e.clientY)
      this.canvas.style.cursor = hoveredNode ? 'pointer' : 'grab'
    }
  }

  private handleMouseUp(e: MouseEvent): void {
    const rect = this.canvas.getBoundingClientRect()
    const screenX = e.clientX - rect.left
    const screenY = e.clientY - rect.top

    if (this.isDraggingNode && this.draggedNode) {
      if (this.dragStarted) {
        // Drag ended - notify callback
        this.callbacks.onNodeDragEnd(this.draggedNode)
      } else {
        // Was a click, not a drag
        this.callbacks.onNodeClick(this.draggedNode)
      }
      this.isDraggingNode = false
      this.draggedNode = null
      this.dragStarted = false
    } else if (this.isPanning) {
      this.isPanning = false
    } else {
      // Check for click on background or node
      const clickedNode = this.hitTest(screenX, screenY, this.nodes)
      if (clickedNode) {
        this.callbacks.onNodeClick(clickedNode)
      } else {
        this.callbacks.onBackgroundClick()
      }
    }

    this.canvas.style.cursor = 'grab'
  }

  private handleMouseLeave(_e: MouseEvent): void {
    if (this.isDraggingNode && this.draggedNode) {
      this.callbacks.onNodeDragEnd(this.draggedNode)
    }
    this.isPanning = false
    this.isDraggingNode = false
    this.draggedNode = null
    this.dragStarted = false
    this.canvas.style.cursor = 'default'
    this.callbacks.onNodeHover(null, 0, 0)
  }

  private handleWheel(e: WheelEvent): void {
    e.preventDefault()

    const rect = this.canvas.getBoundingClientRect()
    const mouseX = e.clientX - rect.left
    const mouseY = e.clientY - rect.top

    const delta = -e.deltaY * this.config.zoomSensitivity
    const newScale = this.transform.scale * (1 + delta)
    const clampedScale = Math.max(this.config.minZoom, Math.min(this.config.maxZoom, newScale))

    if (clampedScale === this.transform.scale) return

    const scaleRatio = clampedScale / this.transform.scale
    const newTransform: Transform = {
      x: mouseX - (mouseX - this.transform.x) * scaleRatio,
      y: mouseY - (mouseY - this.transform.y) * scaleRatio,
      scale: clampedScale,
    }

    this.transform = newTransform
    this.callbacks.onTransformChange(newTransform)
  }

  // ── Listener Management ──

  private attachListeners(): void {
    this.canvas.addEventListener('mousedown', this.boundHandlers.mousedown)
    this.canvas.addEventListener('mousemove', this.boundHandlers.mousemove)
    this.canvas.addEventListener('mouseup', this.boundHandlers.mouseup)
    this.canvas.addEventListener('mouseleave', this.boundHandlers.mouseleave)
    this.canvas.addEventListener('wheel', this.boundHandlers.wheel, { passive: false })
  }

  private detachListeners(): void {
    this.canvas.removeEventListener('mousedown', this.boundHandlers.mousedown)
    this.canvas.removeEventListener('mousemove', this.boundHandlers.mousemove)
    this.canvas.removeEventListener('mouseup', this.boundHandlers.mouseup)
    this.canvas.removeEventListener('mouseleave', this.boundHandlers.mouseleave)
    this.canvas.removeEventListener('wheel', this.boundHandlers.wheel)
  }
}
