// ============================================================
//  Canvas Renderer - Node/Edge/Text Drawing Engine
//  Renders topology graph with radial gradient nodes and arrows
// ============================================================

import { NODE_STYLES, STATUS_OPACITY } from './dag-node-styles'
import type { LayoutNode, LayoutEdge } from './radial-force-layout'

// ─────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────

export interface RenderConfig {
  nodeRadius: number
  edgeColor: string
  edgeColorMotivated: string
  arrowSize: number
  showEdgeLabels: boolean
  selectedNodeId?: string | null
  hoveredNodeId?: string | null
  highlightedNodeIds?: string[]
  dimmedMode?: boolean
}

export interface Transform {
  x: number
  y: number
  scale: number
}

// ─────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────

const DEFAULT_CONFIG: RenderConfig = {
  nodeRadius: 36,
  edgeColor: '#00f0ff',
  edgeColorMotivated: '#f59e0b',
  arrowSize: 10,
  showEdgeLabels: true,
}

// ─────────────────────────────────────────────────────────────
// Canvas Renderer Class
// ─────────────────────────────────────────────────────────────

export class CanvasRenderer {
  private canvas: HTMLCanvasElement
  private ctx: CanvasRenderingContext2D
  private config: RenderConfig
  private transform: Transform = { x: 0, y: 0, scale: 1 }
  private dpr: number = 1

  constructor(canvas: HTMLCanvasElement, config: Partial<RenderConfig> = {}) {
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

  setConfig(config: Partial<RenderConfig>): void {
    this.config = { ...this.config, ...config }
  }

  resize(width: number, height: number): void {
    this.canvas.width = width * this.dpr
    this.canvas.height = height * this.dpr
    this.canvas.style.width = `${width}px`
    this.canvas.style.height = `${height}px`
    this.ctx.scale(this.dpr, this.dpr)
  }

  render(nodes: LayoutNode[], edges: LayoutEdge[]): void {
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

  hitTest(x: number, y: number, nodes: LayoutNode[]): LayoutNode | null {
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
    const color = 'rgba(0, 255, 136, 0.15)'

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

  private drawNode(node: LayoutNode): void {
    const { ctx, config } = this
    if (node.x === undefined || node.y === undefined) return

    const x = node.x
    const y = node.y
    const r = config.nodeRadius

    // Get node style based on kind and status
    const style = this.getNodeStyle(node)

    // Calculate opacity
    let opacity = STATUS_OPACITY[node.planStatus?.toLowerCase() ?? 'completed'] ?? 1
    if (config.dimmedMode && config.highlightedNodeIds) {
      const isHighlighted = config.highlightedNodeIds.includes(node.id)
      const isSelected = node.id === config.selectedNodeId
      if (!isHighlighted && !isSelected) opacity = 0.3
    }

    ctx.save()
    ctx.globalAlpha = opacity

    // Draw glow
    const isSelected = node.id === config.selectedNodeId
    const isHovered = node.id === config.hoveredNodeId
    const isPulsing = node.kind === 'Plan' && node.planStatus === 'Active'

    if (isSelected || isPulsing) {
      ctx.shadowColor = isSelected ? 'rgba(255, 215, 0, 0.8)' : style.glow
      ctx.shadowBlur = isPulsing ? 40 : 24
    } else {
      ctx.shadowColor = style.glow
      ctx.shadowBlur = 15
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

    ctx.restore()

    // Draw node label (inside node)
    this.drawNodeLabel(node, x, y)
  }

  private drawNodeLabel(node: LayoutNode, x: number, y: number): void {
    const { ctx } = this
    const textColor = '#0a0f19'

    ctx.save()
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'

    // Kind emoji + label
    const emoji = node.kind === 'Plan' ? '📋' : node.kind === 'Knowledge' ? '💡' : '⚡'
    const kindLabel = node.planStatus === 'Active' ? 'Active' :
      node.kind === 'Plan' ? 'Plan' :
        node.kind === 'Knowledge' ? 'Know' : 'Node'

    // Draw emoji + kind
    ctx.font = '9px system-ui'
    ctx.fillStyle = textColor
    ctx.fillText(`${emoji} ${kindLabel}`, x, y - 8)

    // Draw ID (truncated)
    const idCore = this.extractIdCore(node.id)
    ctx.font = '10px monospace'
    ctx.globalAlpha = 0.85
    ctx.fillText(idCore, x, y + 8)

    ctx.restore()
  }

  private drawEdge(edge: LayoutEdge, nodes: LayoutNode[]): void {
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

    const isMotivatedBy = edge.type === 'motivated_by'
    const color = isMotivatedBy ? config.edgeColorMotivated : config.edgeColor

    // Calculate edge endpoints (from node border, not center)
    const dx = targetNode.x - sourceNode.x
    const dy = targetNode.y - sourceNode.y
    const dist = Math.sqrt(dx * dx + dy * dy)
    if (dist === 0) return

    const nx = dx / dist
    const ny = dy / dist

    const startX = sourceNode.x + nx * config.nodeRadius
    const startY = sourceNode.y + ny * config.nodeRadius
    const endX = targetNode.x - nx * (config.nodeRadius + config.arrowSize)
    const endY = targetNode.y - ny * (config.nodeRadius + config.arrowSize)

    ctx.save()

    // Draw line
    ctx.strokeStyle = color
    ctx.lineWidth = isMotivatedBy ? 1.5 : 2
    if (isMotivatedBy) {
      ctx.setLineDash([5, 3])
    }

    ctx.beginPath()
    ctx.moveTo(startX, startY)
    ctx.lineTo(endX, endY)
    ctx.stroke()

    ctx.setLineDash([])

    // Draw arrow
    this.drawArrow(endX, endY, nx, ny, color, isMotivatedBy ? 8 : 10)

    // Draw edge label
    if (config.showEdgeLabels) {
      const midX = (startX + endX) / 2
      const midY = (startY + endY) / 2
      const label = isMotivatedBy ? '✨' : 'DEPENDS_ON'

      ctx.font = isMotivatedBy ? '10px system-ui' : '8px monospace'
      ctx.fillStyle = color
      ctx.textAlign = 'center'
      ctx.textBaseline = 'middle'

      // Draw background for text
      if (!isMotivatedBy) {
        const metrics = ctx.measureText(label)
        const padding = 4
        ctx.fillStyle = 'rgba(10, 15, 25, 0.9)'
        ctx.fillRect(
          midX - metrics.width / 2 - padding,
          midY - 6 - padding,
          metrics.width + padding * 2,
          12 + padding * 2
        )
        ctx.fillStyle = color
      }

      ctx.fillText(label, midX, midY)
    }

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

  private getNodeStyle(node: LayoutNode) {
    if (node.isOtherSession) {
      return node.kind === 'Plan' ? NODE_STYLES.PlanOther : NODE_STYLES.KnowledgeOther
    }
    if (node.kind === 'Plan' && node.planStatus === 'Active') {
      return NODE_STYLES.PlanActive
    }
    if (node.kind === 'Plan') {
      return NODE_STYLES.Plan
    }
    if (node.kind === 'Knowledge') {
      return NODE_STYLES.Knowledge
    }
    return NODE_STYLES.Default
  }

  private extractIdCore(id: string): string {
    const prefixes = [
      'axiom_', 'theorem_', 'lemma_', 'plan_', 'knowledge_',
      'analysis_', 'hypothesis_', 'verification_', 'final_',
      'proof_', 'definition_', 'corollary_', 'proposition_',
      'thm_', 'ax_', 'lem_', 'def_', 'prop_', 'cor_',
    ]
    let core = id
    for (const prefix of prefixes) {
      if (core.startsWith(prefix)) {
        core = core.slice(prefix.length)
        break
      }
    }
    core = core.replace(/_v\d+$/, '')
    if (core.length > 10) {
      core = core.slice(0, 9) + '..'
    }
    return core
  }
}
