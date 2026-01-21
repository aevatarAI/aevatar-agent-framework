// ============================================================
//  Review Graph Canvas Renderer
//  Renders knowledge nodes with review status colors
// ============================================================

import { REVIEW_NODE_STYLES, REVIEW_STATUS_OPACITY, type ReviewNodeStatus } from './review-node-styles'

// ─────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────

export interface ReviewLayoutNode {
  id: string
  label: string
  reviewStatus: ReviewNodeStatus
  x?: number
  y?: number
  // For d3 force simulation
  fx?: number | null
  fy?: number | null
  vx?: number
  vy?: number
  index?: number
}

export interface ReviewLayoutEdge {
  id: string
  source: string | ReviewLayoutNode
  target: string | ReviewLayoutNode
}

export interface ReviewRenderConfig {
  nodeRadius: number
  edgeColor: string
  selectedNodeId?: string | null
  hoveredNodeId?: string | null
  pulsePhase?: number // For blinking animation on 'reviewing' nodes
}

export interface Transform {
  x: number
  y: number
  scale: number
}

// ─────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────

const DEFAULT_CONFIG: ReviewRenderConfig = {
  nodeRadius: 32,
  edgeColor: 'rgba(0, 255, 255, 0.4)',
}

// ─────────────────────────────────────────────────────────────
// Review Graph Canvas Renderer Class
// ─────────────────────────────────────────────────────────────

export class ReviewGraphRenderer {
  private canvas: HTMLCanvasElement
  private ctx: CanvasRenderingContext2D
  private config: ReviewRenderConfig
  private transform: Transform = { x: 0, y: 0, scale: 1 }
  private dpr: number = 1

  constructor(canvas: HTMLCanvasElement, config: Partial<ReviewRenderConfig> = {}) {
    this.canvas = canvas
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new Error('Failed to get 2d context')
    this.ctx = ctx
    this.config = { ...DEFAULT_CONFIG, ...config }
    this.dpr = window.devicePixelRatio || 1
  }

  // ── Public API ──

  setTransform(transform: Transform): void {
    this.transform = transform
  }

  setConfig(config: Partial<ReviewRenderConfig>): void {
    this.config = { ...this.config, ...config }
  }

  resize(width: number, height: number): void {
    this.canvas.width = width * this.dpr
    this.canvas.height = height * this.dpr
    this.canvas.style.width = `${width}px`
    this.canvas.style.height = `${height}px`
    this.ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0)
  }

  render(nodes: ReviewLayoutNode[], edges: ReviewLayoutEdge[]): void {
    const { ctx, transform } = this
    const width = this.canvas.width / this.dpr
    const height = this.canvas.height / this.dpr

    // Clear canvas
    ctx.save()
    ctx.setTransform(1, 0, 0, 1, 0, 0)
    ctx.clearRect(0, 0, this.canvas.width, this.canvas.height)
    ctx.restore()

    // Apply transform
    ctx.save()
    ctx.translate(transform.x, transform.y)
    ctx.scale(transform.scale, transform.scale)

    // Draw background grid
    this.drawGrid(width, height)

    // Draw edges first (below nodes)
    edges.forEach(edge => this.drawEdge(edge, nodes))

    // Draw nodes
    nodes.forEach(node => this.drawNode(node))

    ctx.restore()
  }

  // ── Hit Testing ──

  hitTest(x: number, y: number, nodes: ReviewLayoutNode[]): ReviewLayoutNode | null {
    const { transform, config } = this
    // Transform screen coords to world coords
    const worldX = (x - transform.x) / transform.scale
    const worldY = (y - transform.y) / transform.scale

    // Check nodes in reverse order (top-most first)
    for (let i = nodes.length - 1; i >= 0; i--) {
      const node = nodes[i]
      if (node.x === undefined || node.y === undefined) continue
      const dx = worldX - node.x
      const dy = worldY - node.y
      if (dx * dx + dy * dy <= config.nodeRadius * config.nodeRadius) {
        return node
      }
    }
    return null
  }

  // ── Private Drawing Methods ──

  private drawGrid(width: number, height: number): void {
    const { ctx, transform } = this
    const gap = 20
    const dotRadius = 1
    const color = 'rgba(100, 150, 200, 0.1)'

    ctx.fillStyle = color

    // Calculate visible area in world coords
    const startX = -transform.x / transform.scale
    const startY = -transform.y / transform.scale
    const endX = startX + width / transform.scale
    const endY = startY + height / transform.scale

    // Snap to grid
    const gridStartX = Math.floor(startX / gap) * gap
    const gridStartY = Math.floor(startY / gap) * gap

    for (let x = gridStartX; x < endX; x += gap) {
      for (let y = gridStartY; y < endY; y += gap) {
        ctx.beginPath()
        ctx.arc(x, y, dotRadius, 0, Math.PI * 2)
        ctx.fill()
      }
    }
  }

  private drawNode(node: ReviewLayoutNode): void {
    const { ctx, config } = this
    if (node.x === undefined || node.y === undefined) return

    const x = node.x
    const y = node.y
    const r = config.nodeRadius

    // Get node style based on review status
    const style = REVIEW_NODE_STYLES[node.reviewStatus]
    const opacity = REVIEW_STATUS_OPACITY[node.reviewStatus]

    ctx.save()
    ctx.globalAlpha = opacity

    // Calculate pulse effect for 'reviewing' nodes
    const isReviewing = node.reviewStatus === 'reviewing'
    const pulseIntensity = isReviewing && config.pulsePhase !== undefined
      ? 0.5 + 0.5 * Math.sin(config.pulsePhase)
      : 1

    // Draw glow
    const isSelected = node.id === config.selectedNodeId
    const isHovered = node.id === config.hoveredNodeId

    if (isReviewing) {
      ctx.shadowColor = style.glow
      ctx.shadowBlur = 30 + 15 * pulseIntensity
    } else if (isSelected) {
      ctx.shadowColor = 'rgba(255, 215, 0, 0.8)'
      ctx.shadowBlur = 24
    } else {
      ctx.shadowColor = style.glow
      ctx.shadowBlur = 12
    }

    // Draw circle with gradient
    const gradient = ctx.createRadialGradient(x, y, 0, x, y, r)
    gradient.addColorStop(0, style.bg)
    gradient.addColorStop(1, style.border)

    ctx.beginPath()
    ctx.arc(x, y, r, 0, Math.PI * 2)
    ctx.fillStyle = gradient
    ctx.fill()

    // Draw border
    ctx.shadowBlur = 0
    ctx.strokeStyle = isSelected ? '#ffd700' : style.border
    ctx.lineWidth = isSelected ? 3 : 2
    ctx.stroke()

    // Draw selection ring
    if (isSelected) {
      ctx.strokeStyle = 'rgba(255, 215, 0, 0.5)'
      ctx.lineWidth = 2
      ctx.beginPath()
      ctx.arc(x, y, r + 6, 0, Math.PI * 2)
      ctx.stroke()
    }

    // Draw hover ring
    if (isHovered && !isSelected) {
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.4)'
      ctx.lineWidth = 2
      ctx.beginPath()
      ctx.arc(x, y, r + 4, 0, Math.PI * 2)
      ctx.stroke()
    }

    // Draw pulse ring for reviewing nodes
    if (isReviewing) {
      ctx.strokeStyle = `rgba(249, 115, 22, ${0.3 + 0.3 * pulseIntensity})`
      ctx.lineWidth = 3
      ctx.beginPath()
      ctx.arc(x, y, r + 8 + 4 * pulseIntensity, 0, Math.PI * 2)
      ctx.stroke()
    }

    ctx.restore()

    // Draw node label (inside node)
    this.drawNodeLabel(node, x, y, style.textColor)
  }

  private drawNodeLabel(node: ReviewLayoutNode, x: number, y: number, textColor: string): void {
    const { ctx } = this

    ctx.save()
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'

    // Status badge
    const statusEmoji = this.getStatusEmoji(node.reviewStatus)
    ctx.font = '10px system-ui'
    ctx.fillStyle = textColor
    ctx.fillText(statusEmoji, x, y - 8)

    // Draw label (truncated)
    const displayText = node.label.length > 10
      ? node.label.slice(0, 9) + '..'
      : node.label
    ctx.font = '9px system-ui'
    ctx.globalAlpha = 0.9
    ctx.fillText(displayText, x, y + 8)

    ctx.restore()
  }

  private getStatusEmoji(status: ReviewNodeStatus): string {
    switch (status) {
      case 'reviewed': return '✓'
      case 'pending': return '⏳'
      case 'deactivated': return '✗'
      case 'removed': return '🗑'
      case 'reviewing': return '⚡'
      default: return '?'
    }
  }

  private drawEdge(edge: ReviewLayoutEdge, nodes: ReviewLayoutNode[]): void {
    const { ctx, config } = this

    // Resolve source/target nodes
    const sourceNode = typeof edge.source === 'string'
      ? nodes.find(n => n.id === edge.source)
      : edge.source
    const targetNode = typeof edge.target === 'string'
      ? nodes.find(n => n.id === edge.target)
      : edge.target

    if (!sourceNode || !targetNode) return
    if (sourceNode.x === undefined || sourceNode.y === undefined) return
    if (targetNode.x === undefined || targetNode.y === undefined) return

    // Calculate edge endpoints (from node border, not center)
    const dx = targetNode.x - sourceNode.x
    const dy = targetNode.y - sourceNode.y
    const dist = Math.sqrt(dx * dx + dy * dy)
    if (dist === 0) return

    const nx = dx / dist
    const ny = dy / dist

    const startX = sourceNode.x + nx * config.nodeRadius
    const startY = sourceNode.y + ny * config.nodeRadius
    const endX = targetNode.x - nx * (config.nodeRadius + 8)
    const endY = targetNode.y - ny * (config.nodeRadius + 8)

    ctx.save()

    // Draw line
    ctx.strokeStyle = config.edgeColor
    ctx.lineWidth = 1.5

    ctx.beginPath()
    ctx.moveTo(startX, startY)
    ctx.lineTo(endX, endY)
    ctx.stroke()

    // Draw arrow
    this.drawArrow(endX, endY, nx, ny, config.edgeColor, 8)

    ctx.restore()
  }

  private drawArrow(x: number, y: number, nx: number, ny: number, color: string, size: number): void {
    const { ctx } = this
    const angle = Math.atan2(ny, nx)
    const arrowAngle = Math.PI / 6

    ctx.fillStyle = color
    ctx.beginPath()
    ctx.moveTo(x + nx * size, y + ny * size)
    ctx.lineTo(
      x - size * Math.cos(angle - arrowAngle),
      y - size * Math.sin(angle - arrowAngle)
    )
    ctx.lineTo(
      x - size * Math.cos(angle + arrowAngle),
      y - size * Math.sin(angle + arrowAngle)
    )
    ctx.closePath()
    ctx.fill()
  }
}
