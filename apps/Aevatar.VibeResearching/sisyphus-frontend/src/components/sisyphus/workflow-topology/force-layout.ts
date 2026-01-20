// ============================================================
//  Force-Directed Layout Algorithm for Dense Clustering
//  Uses d3-force to create radial aggregation around center
// ============================================================

import {
  forceSimulation,
  forceLink,
  forceManyBody,
  forceCollide,
  forceRadial,
  type Simulation,
  type SimulationNodeDatum,
  type SimulationLinkDatum,
} from 'd3-force'
import { Position, type Node, type Edge } from '@xyflow/react'

// ─────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────

interface ForceNode extends SimulationNodeDatum {
  id: string
  isCenter: boolean
}

interface ForceLink extends SimulationLinkDatum<ForceNode> {
  source: string | ForceNode
  target: string | ForceNode
}

// ─────────────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────────────

const FORCE_CONFIG = {
  // Node repulsion - weak to allow tight packing
  chargeStrength: -80,
  // Link distance - short for tight connections
  linkDistance: 90,
  // Link strength - strong to keep connected nodes together
  linkStrength: 0.8,
  // Collision radius - prevents overlap (node radius 36 + 9px gap)
  collisionRadius: 45,
  // Simulation iterations
  iterations: 400,
  // Radial force - STRONG to force circular distribution
  radialStrength: 0.8,
  // Base radius for first ring
  baseRadius: 120,
  // Radius increment per ring
  radiusIncrement: 100,
}

const NODE_WIDTH = 72
const NODE_HEIGHT = 72

// ─────────────────────────────────────────────────────────────
// Force Layout Algorithm
// ─────────────────────────────────────────────────────────────

/**
 * Apply force-directed layout for dense clustering visualization
 * @param nodes - ReactFlow nodes
 * @param edges - ReactFlow edges
 * @param centerNodeId - Node ID to place at center (e.g., active milestone)
 * @param width - Canvas width for centering
 * @param height - Canvas height for centering
 * @param existingPositions - Map of node ID to existing position (for incremental updates)
 */
export function getForceLayoutedElements(
  nodes: Node[],
  edges: Edge[],
  centerNodeId?: string | null,
  width: number = 800,
  height: number = 600,
  existingPositions?: Map<string, { x: number; y: number }>
): { nodes: Node[]; edges: Edge[] } {
  if (nodes.length === 0) return { nodes, edges }

  const centerX = width / 2
  const centerY = height / 2

  // Check if we have existing positions for most nodes (incremental update)
  const hasExistingLayout = existingPositions && existingPositions.size > 0
  const newNodeIds = hasExistingLayout 
    ? nodes.filter(n => !existingPositions.has(n.id)).map(n => n.id)
    : []
  
  // Check if center node has changed - if so, need full re-layout
  const centerNodeCached = centerNodeId ? existingPositions?.get(centerNodeId) : null
  const centerNodeAtCenter = centerNodeCached 
    ? Math.abs(centerNodeCached.x - (centerX - NODE_WIDTH / 2)) < 10 &&
      Math.abs(centerNodeCached.y - (centerY - NODE_HEIGHT / 2)) < 10
    : false

  // Skip layout ONLY if: all nodes cached AND center node is already at center (or no center)
  if (hasExistingLayout && newNodeIds.length === 0 && (!centerNodeId || centerNodeAtCenter)) {
    const layoutedNodes: Node[] = nodes.map((node) => {
      const existing = existingPositions.get(node.id)
      return {
        ...node,
        position: existing 
          ? { x: existing.x, y: existing.y }
          : node.position,
        targetPosition: Position.Top,
        sourcePosition: Position.Bottom,
      }
    })
    return { nodes: layoutedNodes, edges }
  }

  // Build adjacency map to calculate node depth from center
  const adjacencyMap = new Map<string, Set<string>>()
  edges.forEach(edge => {
    if (!adjacencyMap.has(edge.source)) adjacencyMap.set(edge.source, new Set())
    if (!adjacencyMap.has(edge.target)) adjacencyMap.set(edge.target, new Set())
    adjacencyMap.get(edge.source)!.add(edge.target)
    adjacencyMap.get(edge.target)!.add(edge.source)
  })

  // ─── Determine actual center node ───
  // Priority: 1) Active Milestone (provided) -> 2) Most connected node
  let actualCenterNodeId = centerNodeId
  if (!actualCenterNodeId && nodes.length > 0) {
    // Find node with most connections
    let maxConnections = -1
    nodes.forEach(node => {
      const connections = adjacencyMap.get(node.id)?.size || 0
      if (connections > maxConnections) {
        maxConnections = connections
        actualCenterNodeId = node.id
      }
    })
  }

  // BFS to calculate depth (distance from center node)
  const depthMap = new Map<string, number>()
  if (actualCenterNodeId) {
    const queue: [string, number][] = [[actualCenterNodeId, 0]]
    depthMap.set(actualCenterNodeId, 0)
    while (queue.length > 0) {
      const [nodeId, depth] = queue.shift()!
      const neighbors = adjacencyMap.get(nodeId) || new Set()
      neighbors.forEach(neighbor => {
        if (!depthMap.has(neighbor)) {
          depthMap.set(neighbor, depth + 1)
          queue.push([neighbor, depth + 1])
        }
      })
    }
  }
  // Assign depth to unconnected nodes
  nodes.forEach(node => {
    if (!depthMap.has(node.id)) {
      depthMap.set(node.id, 3) // Default ring for disconnected nodes
    }
  })

  // Check if there are new nodes - if so, allow all nodes to adjust for collision avoidance
  const hasNewNodes = newNodeIds.length > 0

  // Convert to force simulation nodes
  const forceNodes: ForceNode[] = nodes.map((node) => {
    const existing = existingPositions?.get(node.id)
    const isCenter = node.id === actualCenterNodeId
    const depth = depthMap.get(node.id) || 1
    
    // Calculate target radius based on depth
    const targetRadius = isCenter ? 0 : FORCE_CONFIG.baseRadius + (depth - 1) * FORCE_CONFIG.radiusIncrement
    
    // Initial position: center node ALWAYS at center, others on ring
    let x: number, y: number
    if (isCenter) {
      // Center node MUST be at center, ignore cache
      x = centerX
      y = centerY
    } else if (existing) {
      // Non-center nodes use cached position
      x = existing.x + NODE_WIDTH / 2
      y = existing.y + NODE_HEIGHT / 2
    } else {
      // Distribute evenly on the ring
      const nodesAtDepth = nodes.filter(n => depthMap.get(n.id) === depth)
      const indexAtDepth = nodesAtDepth.findIndex(n => n.id === node.id)
      const angle = (indexAtDepth / nodesAtDepth.length) * 2 * Math.PI - Math.PI / 2
      x = centerX + Math.cos(angle) * targetRadius
      y = centerY + Math.sin(angle) * targetRadius
    }
    
    return {
      id: node.id,
      isCenter,
      x,
      y,
      depth,
      targetRadius,
      // Fix position for existing nodes ONLY when no new nodes
      // When new nodes exist, allow all nodes to adjust for collision avoidance
      fx: existing && !isCenter && !hasNewNodes ? x : undefined,
      fy: existing && !isCenter && !hasNewNodes ? y : undefined,
    } as ForceNode & { depth: number; targetRadius: number }
  })

  // Convert edges to force links
  const forceLinks: ForceLink[] = edges.map((edge) => ({
    source: edge.source,
    target: edge.target,
  }))

  // Create force simulation with RADIAL distribution
  const simulation: Simulation<ForceNode, ForceLink> = forceSimulation(forceNodes)
    // Link force - connects nodes with edges
    .force(
      'link',
      forceLink<ForceNode, ForceLink>(forceLinks)
        .id((d) => d.id)
        .distance(FORCE_CONFIG.linkDistance)
        .strength(FORCE_CONFIG.linkStrength)
    )
    // Charge force - weak repulsion
    .force('charge', forceManyBody().strength(FORCE_CONFIG.chargeStrength))
    // Collision force - prevents overlap (3 iterations for better precision)
    .force('collide', forceCollide(FORCE_CONFIG.collisionRadius).iterations(3))
    // CRITICAL: Radial force - forces nodes to their ring based on depth
    .force(
      'radial',
      forceRadial<ForceNode & { targetRadius: number }>(
        (d) => d.targetRadius || FORCE_CONFIG.baseRadius,
        centerX,
        centerY
      ).strength(FORCE_CONFIG.radialStrength)
    )
    .stop()

  // Fix center node position
  if (actualCenterNodeId) {
    const centerNode = forceNodes.find((n) => n.id === actualCenterNodeId)
    if (centerNode) {
      centerNode.fx = centerX
      centerNode.fy = centerY
    }
  }

  // Run simulation synchronously
  for (let i = 0; i < FORCE_CONFIG.iterations; i++) {
    simulation.tick()
  }

  // Map positions back to ReactFlow nodes
  const nodeMap = new Map(forceNodes.map((n) => [n.id, n]))
  const layoutedNodes: Node[] = nodes.map((node) => {
    const forceNode = nodeMap.get(node.id)
    return {
      ...node,
      position: {
        x: (forceNode?.x ?? 0) - NODE_WIDTH / 2,
        y: (forceNode?.y ?? 0) - NODE_HEIGHT / 2,
      },
      // Force layout uses radial positions, so handles point outward
      targetPosition: Position.Top,
      sourcePosition: Position.Bottom,
    }
  })

  return { nodes: layoutedNodes, edges }
}

// ─────────────────────────────────────────────────────────────
// Layout Mode Type
// ─────────────────────────────────────────────────────────────

export type LayoutMode = 'dagre-tb' | 'dagre-lr' | 'force'

export const LAYOUT_MODES: { id: LayoutMode; label: string; icon: string }[] = [
  { id: 'dagre-tb', label: 'Vertical', icon: 'vertical' },
  { id: 'dagre-lr', label: 'Horizontal', icon: 'horizontal' },
  { id: 'force', label: 'Cluster', icon: 'cluster' },
]
