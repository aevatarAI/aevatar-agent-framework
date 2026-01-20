// ============================================================
//  Radial Force Layout - D3 Force + Radial Constraint
//  Center: Active Milestone, Radial: BFS Level Distribution
// ============================================================

import {
  forceSimulation,
  forceLink,
  forceManyBody,
  forceCollide,
  forceRadial,
  forceCenter,
  type Simulation,
  type SimulationNodeDatum,
  type SimulationLinkDatum,
} from 'd3-force'

// ─────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────

export interface LayoutNode extends SimulationNodeDatum {
  id: string
  label: string
  kind?: 'Plan' | 'Knowledge'
  planStatus?: 'Pending' | 'Active' | 'Completed'
  isOtherSession?: boolean
  level: number  // BFS level from center node
  fx?: number | null  // Fixed x (for center node)
  fy?: number | null  // Fixed y (for center node)
}

export interface LayoutEdge extends SimulationLinkDatum<LayoutNode> {
  id: string
  source: string | LayoutNode
  target: string | LayoutNode
  type?: string
}

export interface LayoutResult {
  nodes: LayoutNode[]
  edges: LayoutEdge[]
}

export interface LayoutConfig {
  width: number
  height: number
  centerNodeId?: string | null
  nodeRadius?: number
  levelRadius?: number  // Radius increment per level
  existingPositions?: Map<string, { x: number; y: number }>  // Preserve existing positions
}

// ─────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────

const DEFAULT_NODE_RADIUS = 36
const DEFAULT_LEVEL_RADIUS = 140
const SIMULATION_ITERATIONS = 300

// ─────────────────────────────────────────────────────────────
// BFS Level Calculation
// ─────────────────────────────────────────────────────────────

function calculateBfsLevels(
  nodes: LayoutNode[],
  edges: LayoutEdge[],
  centerNodeId: string | null
): Map<string, number> {
  const levels = new Map<string, number>()
  
  if (!centerNodeId || !nodes.find(n => n.id === centerNodeId)) {
    // No center node, assign level 0 to all
    nodes.forEach(n => levels.set(n.id, 0))
    return levels
  }

  // Build adjacency list (bidirectional for BFS)
  const adjacency = new Map<string, Set<string>>()
  nodes.forEach(n => adjacency.set(n.id, new Set()))
  
  edges.forEach(e => {
    const sourceId = typeof e.source === 'string' ? e.source : e.source.id
    const targetId = typeof e.target === 'string' ? e.target : e.target.id
    adjacency.get(sourceId)?.add(targetId)
    adjacency.get(targetId)?.add(sourceId)
  })

  // BFS from center
  const queue: string[] = [centerNodeId]
  levels.set(centerNodeId, 0)
  
  while (queue.length > 0) {
    const current = queue.shift()!
    const currentLevel = levels.get(current)!
    
    adjacency.get(current)?.forEach(neighbor => {
      if (!levels.has(neighbor)) {
        levels.set(neighbor, currentLevel + 1)
        queue.push(neighbor)
      }
    })
  }

  // Assign max level + 1 to disconnected nodes
  const maxLevel = Math.max(0, ...Array.from(levels.values()))
  nodes.forEach(n => {
    if (!levels.has(n.id)) {
      levels.set(n.id, maxLevel + 1)
    }
  })

  return levels
}

// ─────────────────────────────────────────────────────────────
// Main Layout Function
// ─────────────────────────────────────────────────────────────

export function createRadialForceLayout(
  inputNodes: LayoutNode[],
  inputEdges: LayoutEdge[],
  config: LayoutConfig
): LayoutResult {
  const {
    width,
    height,
    centerNodeId,
    nodeRadius = DEFAULT_NODE_RADIUS,
    levelRadius = DEFAULT_LEVEL_RADIUS,
  } = config

  const centerX = width / 2
  const centerY = height / 2

  // Calculate BFS levels
  const levels = calculateBfsLevels(inputNodes, inputEdges, centerNodeId ?? null)

  // Group nodes by level for deterministic positioning
  const nodesByLevel = new Map<number, LayoutNode[]>()
  inputNodes.forEach(n => {
    const level = levels.get(n.id) ?? 0
    if (!nodesByLevel.has(level)) nodesByLevel.set(level, [])
    nodesByLevel.get(level)!.push(n)
  })

  // Deep clone nodes with deterministic initial positions based on level and index
  const nodes: LayoutNode[] = inputNodes.map(n => {
    const level = levels.get(n.id) ?? 0
    const nodesAtLevel = nodesByLevel.get(level) || []
    const indexAtLevel = nodesAtLevel.findIndex(node => node.id === n.id)
    const countAtLevel = nodesAtLevel.length
    
    // Calculate deterministic angle based on index within level
    const angleOffset = level * 0.5 // Slight rotation per level for visual variety
    const angle = (2 * Math.PI * indexAtLevel / countAtLevel) + angleOffset
    const radius = level * levelRadius * 0.5 // Initial radius (will be adjusted by force)
    
    return {
      ...n,
      level,
      x: centerX + Math.cos(angle) * radius,
      y: centerY + Math.sin(angle) * radius,
    }
  })

  // Fix center node position
  if (centerNodeId) {
    const centerNode = nodes.find(n => n.id === centerNodeId)
    if (centerNode) {
      centerNode.fx = centerX
      centerNode.fy = centerY
      centerNode.x = centerX
      centerNode.y = centerY
    }
  }

  // Deep clone edges
  const edges: LayoutEdge[] = inputEdges.map(e => ({ ...e }))

  // Create simulation
  const simulation: Simulation<LayoutNode, LayoutEdge> = forceSimulation(nodes)
    // Center force (weak, for initial positioning)
    .force('center', forceCenter(centerX, centerY).strength(0.05))
    // Radial force: push nodes to their BFS level radius
    .force('radial', forceRadial<LayoutNode>(
      d => d.level * levelRadius,
      centerX,
      centerY
    ).strength(0.8))
    // Repulsion between nodes
    .force('charge', forceManyBody<LayoutNode>().strength(-400).distanceMax(400))
    // Collision avoidance
    .force('collide', forceCollide<LayoutNode>(nodeRadius + 15).strength(0.9))
    // Link force (weak, to keep connected nodes somewhat close)
    .force('link', forceLink<LayoutNode, LayoutEdge>(edges)
      .id(d => d.id)
      .distance(levelRadius * 0.8)
      .strength(0.3)
    )

  // Run simulation synchronously
  simulation.stop()
  for (let i = 0; i < SIMULATION_ITERATIONS; i++) {
    simulation.tick()
  }

  return { nodes, edges }
}

// ─────────────────────────────────────────────────────────────
// Persistent Simulation Manager (for drag interaction)
// ─────────────────────────────────────────────────────────────

export interface SimulationManager {
  simulation: Simulation<LayoutNode, LayoutEdge>
  nodes: LayoutNode[]
  edges: LayoutEdge[]
  reheat: () => void
  setDraggedNode: (nodeId: string | null) => void
  updateNodePosition: (nodeId: string, x: number, y: number) => void
  stop: () => void
  destroy: () => void
}

export function createPersistentSimulation(
  inputNodes: LayoutNode[],
  inputEdges: LayoutEdge[],
  config: LayoutConfig,
  onTick: (nodes: LayoutNode[]) => void
): SimulationManager {
  const {
    width,
    height,
    centerNodeId,
    nodeRadius = DEFAULT_NODE_RADIUS,
    levelRadius = DEFAULT_LEVEL_RADIUS,
    existingPositions,
  } = config

  const centerX = width / 2
  const centerY = height / 2

  // Calculate BFS levels
  const levels = calculateBfsLevels(inputNodes, inputEdges, centerNodeId ?? null)

  // Group nodes by level for deterministic positioning
  const nodesByLevel = new Map<number, LayoutNode[]>()
  inputNodes.forEach(n => {
    const level = levels.get(n.id) ?? 0
    if (!nodesByLevel.has(level)) nodesByLevel.set(level, [])
    nodesByLevel.get(level)!.push(n)
  })

  // Track which nodes are new (need layout calculation)
  const newNodeIds = new Set<string>()

  // Clone nodes - preserve existing positions, calculate for new nodes only
  const nodes: LayoutNode[] = inputNodes.map(n => {
    const level = levels.get(n.id) ?? 0
    const existingPos = existingPositions?.get(n.id)
    
    if (existingPos) {
      // Existing node - preserve position
      return {
        ...n,
        level,
        x: existingPos.x,
        y: existingPos.y,
      }
    } else {
      // New node - calculate initial position
      newNodeIds.add(n.id)
      const nodesAtLevel = nodesByLevel.get(level) || []
      const indexAtLevel = nodesAtLevel.findIndex(node => node.id === n.id)
      const countAtLevel = nodesAtLevel.length
      
      const angleOffset = level * 0.5
      const angle = (2 * Math.PI * indexAtLevel / countAtLevel) + angleOffset
      const radius = level * levelRadius * 0.5
      
      return {
        ...n,
        level,
        x: centerX + Math.cos(angle) * radius,
        y: centerY + Math.sin(angle) * radius,
      }
    }
  })

  // Fix center node position
  if (centerNodeId) {
    const centerNode = nodes.find(n => n.id === centerNodeId)
    if (centerNode) {
      centerNode.fx = centerX
      centerNode.fy = centerY
      centerNode.x = centerX
      centerNode.y = centerY
    }
  }

  const edges: LayoutEdge[] = inputEdges.map(e => ({ ...e }))

  // Create simulation - optimized for stability
  const simulation = forceSimulation(nodes)
    .force('center', forceCenter(centerX, centerY).strength(0.01))
    .force('radial', forceRadial<LayoutNode>(
      d => d.level * levelRadius,
      centerX,
      centerY
    ).strength(0.6))
    .force('charge', forceManyBody<LayoutNode>().strength(-200).distanceMax(300))
    .force('collide', forceCollide<LayoutNode>(nodeRadius + 20).strength(1).iterations(2))
    .force('link', forceLink<LayoutNode, LayoutEdge>(edges)
      .id(d => d.id)
      .distance(levelRadius * 0.8)
      .strength(0.15)
    )
    .alphaDecay(0.05)
    .alphaMin(0.001)
    .velocityDecay(0.6)
    .on('tick', () => onTick(nodes))

  // Only run simulation if there are new nodes or no existing positions
  const hasExistingPositions = existingPositions && existingPositions.size > 0
  const iterations = hasExistingPositions
    ? (newNodeIds.size > 0 ? 50 : 0)  // Few iterations for new nodes only
    : SIMULATION_ITERATIONS            // Full iterations for initial layout

  if (iterations > 0) {
    simulation.alpha(hasExistingPositions ? 0.3 : 1)
    for (let i = 0; i < iterations; i++) {
      simulation.tick()
    }
  }
  simulation.stop()
  onTick(nodes)

  // Track currently dragged node
  let draggedNodeId: string | null = null

  return {
    simulation,
    nodes,
    edges,
    reheat: () => {
      // Only reheat briefly for settling after drag
      simulation.alpha(0.15).restart()
    },
    setDraggedNode: (nodeId: string | null) => {
      // Unfix previous dragged node (except center)
      if (draggedNodeId && draggedNodeId !== centerNodeId) {
        const prevNode = nodes.find(n => n.id === draggedNodeId)
        if (prevNode) {
          prevNode.fx = null
          prevNode.fy = null
        }
      }
      draggedNodeId = nodeId
    },
    updateNodePosition: (nodeId: string, x: number, y: number) => {
      const node = nodes.find(n => n.id === nodeId)
      if (node) {
        node.fx = x
        node.fy = y
        node.x = x
        node.y = y
        // Low alpha for gentle collision response
        simulation.alpha(0.1).restart()
      }
    },
    stop: () => {
      simulation.stop()
    },
    destroy: () => {
      simulation.stop()
      simulation.on('tick', null)
    }
  }
}

// Legacy: Incremental Update (for animation)
export function createSimulation(
  nodes: LayoutNode[],
  edges: LayoutEdge[],
  config: LayoutConfig,
  onTick: (nodes: LayoutNode[]) => void
): Simulation<LayoutNode, LayoutEdge> {
  const {
    width,
    height,
    centerNodeId,
    nodeRadius = DEFAULT_NODE_RADIUS,
    levelRadius = DEFAULT_LEVEL_RADIUS,
  } = config

  const centerX = width / 2
  const centerY = height / 2

  if (centerNodeId) {
    const centerNode = nodes.find(n => n.id === centerNodeId)
    if (centerNode) {
      centerNode.fx = centerX
      centerNode.fy = centerY
    }
  }

  const simulation = forceSimulation(nodes)
    .force('center', forceCenter(centerX, centerY).strength(0.05))
    .force('radial', forceRadial<LayoutNode>(
      d => d.level * levelRadius,
      centerX,
      centerY
    ).strength(0.8))
    .force('charge', forceManyBody<LayoutNode>().strength(-400).distanceMax(400))
    .force('collide', forceCollide<LayoutNode>(nodeRadius + 15).strength(0.9))
    .force('link', forceLink<LayoutNode, LayoutEdge>(edges)
      .id(d => d.id)
      .distance(levelRadius * 0.8)
      .strength(0.3)
    )
    .on('tick', () => onTick(nodes))

  return simulation
}

// ─────────────────────────────────────────────────────────────
// Utility: Find Center Node
// Priority 1: Active milestone
// Priority 2: Node with most connections
// ─────────────────────────────────────────────────────────────

export function findCenterNode(nodes: LayoutNode[], edges?: LayoutEdge[]): string | null {
  if (nodes.length === 0) return null

  // Priority 1: Active milestone (Plan with Active status)
  const activeMilestone = nodes.find(
    n => n.kind === 'Plan' && n.planStatus === 'Active'
  )
  if (activeMilestone) return activeMilestone.id

  // Priority 2: Node with most connections
  if (edges && edges.length > 0) {
    const connectionCount = new Map<string, number>()
    
    // Initialize all nodes with 0 connections
    nodes.forEach(n => connectionCount.set(n.id, 0))
    
    // Count connections for each node
    edges.forEach(edge => {
      const sourceId = typeof edge.source === 'string' ? edge.source : edge.source.id
      const targetId = typeof edge.target === 'string' ? edge.target : edge.target.id
      
      if (connectionCount.has(sourceId)) {
        connectionCount.set(sourceId, (connectionCount.get(sourceId) || 0) + 1)
      }
      if (connectionCount.has(targetId)) {
        connectionCount.set(targetId, (connectionCount.get(targetId) || 0) + 1)
      }
    })
    
    // Find node with max connections
    let maxConnections = 0
    let mostConnectedNodeId: string | null = null
    
    connectionCount.forEach((count, nodeId) => {
      if (count > maxConnections) {
        maxConnections = count
        mostConnectedNodeId = nodeId
      }
    })
    
    if (mostConnectedNodeId) return mostConnectedNodeId
  }

  // Fallback: first node
  return nodes[0]?.id ?? null
}
