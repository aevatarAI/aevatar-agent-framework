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
const SIMULATION_ITERATIONS = 200  // Balanced: enough for initial layout, not too slow

// ─────────────────────────────────────────────────────────────
// Connected Component Detection (Union-Find)
// ─────────────────────────────────────────────────────────────

interface ComponentInfo {
  id: number
  nodeIds: Set<string>
  centerNodeId: string | null
  cx: number  // Component center X
  cy: number  // Component center Y
  radius: number  // Allocated radius for this component
}

/**
 * Detect connected components using Union-Find algorithm
 * Returns: nodeId -> componentId mapping
 */
function detectConnectedComponents(
  nodes: LayoutNode[],
  edges: LayoutEdge[]
): Map<string, number> {
  if (nodes.length === 0) return new Map()

  // Union-Find data structures
  const parent = new Map<string, string>()
  const rank = new Map<string, number>()

  // Initialize: each node is its own parent
  nodes.forEach(n => {
    parent.set(n.id, n.id)
    rank.set(n.id, 0)
  })

  // Find with path compression
  function find(x: string): string {
    if (parent.get(x) !== x) {
      parent.set(x, find(parent.get(x)!))
    }
    return parent.get(x)!
  }

  // Union by rank
  function union(x: string, y: string): void {
    const px = find(x)
    const py = find(y)
    if (px === py) return

    const rankX = rank.get(px) ?? 0
    const rankY = rank.get(py) ?? 0

    if (rankX < rankY) {
      parent.set(px, py)
    } else if (rankX > rankY) {
      parent.set(py, px)
    } else {
      parent.set(py, px)
      rank.set(px, rankX + 1)
    }
  }

  // Union all connected nodes via edges
  edges.forEach(e => {
    const sourceId = typeof e.source === 'string' ? e.source : e.source.id
    const targetId = typeof e.target === 'string' ? e.target : e.target.id
    if (parent.has(sourceId) && parent.has(targetId)) {
      union(sourceId, targetId)
    }
  })

  // Assign component IDs (root -> componentId)
  const rootToComponent = new Map<string, number>()
  let componentId = 0

  const nodeToComponent = new Map<string, number>()
  nodes.forEach(n => {
    const root = find(n.id)
    if (!rootToComponent.has(root)) {
      rootToComponent.set(root, componentId++)
    }
    nodeToComponent.set(n.id, rootToComponent.get(root)!)
  })

  return nodeToComponent
}

/**
 * Group nodes by their component ID
 */
function groupNodesByComponent(
  nodes: LayoutNode[],
  componentMap: Map<string, number>
): Map<number, LayoutNode[]> {
  const groups = new Map<number, LayoutNode[]>()

  nodes.forEach(n => {
    const compId = componentMap.get(n.id) ?? 0
    if (!groups.has(compId)) {
      groups.set(compId, [])
    }
    groups.get(compId)!.push(n)
  })

  return groups
}

/**
 * Find center node for a component
 * Priority: Active Plan > Plan with most connections > Most connected node
 */
function findComponentCenter(
  componentNodes: LayoutNode[],
  edges: LayoutEdge[]
): string | null {
  if (componentNodes.length === 0) return null

  const nodeIds = new Set(componentNodes.map(n => n.id))

  // Filter edges within this component
  const componentEdges = edges.filter(e => {
    const sourceId = typeof e.source === 'string' ? e.source : e.source.id
    const targetId = typeof e.target === 'string' ? e.target : e.target.id
    return nodeIds.has(sourceId) && nodeIds.has(targetId)
  })

  // Priority 1: Active Plan
  const activePlan = componentNodes.find(
    n => n.kind === 'Plan' && n.planStatus === 'Active'
  )
  if (activePlan) return activePlan.id

  // Priority 2: Plan with most connections
  const planNodes = componentNodes.filter(n => n.kind === 'Plan')
  if (planNodes.length > 0 && componentEdges.length > 0) {
    const connectionCount = new Map<string, number>()
    planNodes.forEach(n => connectionCount.set(n.id, 0))

    componentEdges.forEach(e => {
      const sourceId = typeof e.source === 'string' ? e.source : e.source.id
      const targetId = typeof e.target === 'string' ? e.target : e.target.id
      if (connectionCount.has(sourceId)) {
        connectionCount.set(sourceId, (connectionCount.get(sourceId) || 0) + 1)
      }
      if (connectionCount.has(targetId)) {
        connectionCount.set(targetId, (connectionCount.get(targetId) || 0) + 1)
      }
    })

    let maxCount = 0
    let bestPlanId: string | null = null
    connectionCount.forEach((count, id) => {
      if (count > maxCount) {
        maxCount = count
        bestPlanId = id
      }
    })
    if (bestPlanId) return bestPlanId
  }

  // Priority 3: Most connected node overall
  if (componentEdges.length > 0) {
    const connectionCount = new Map<string, number>()
    componentNodes.forEach(n => connectionCount.set(n.id, 0))

    componentEdges.forEach(e => {
      const sourceId = typeof e.source === 'string' ? e.source : e.source.id
      const targetId = typeof e.target === 'string' ? e.target : e.target.id
      if (connectionCount.has(sourceId)) {
        connectionCount.set(sourceId, (connectionCount.get(sourceId) || 0) + 1)
      }
      if (connectionCount.has(targetId)) {
        connectionCount.set(targetId, (connectionCount.get(targetId) || 0) + 1)
      }
    })

    let maxCount = 0
    let mostConnected: string | null = null
    connectionCount.forEach((count, id) => {
      if (count > maxCount) {
        maxCount = count
        mostConnected = id
      }
    })
    if (mostConnected) return mostConnected
  }

  // Fallback: first node
  return componentNodes[0]?.id ?? null
}

// ─────────────────────────────────────────────────────────────
// Component Region Assignment
// ─────────────────────────────────────────────────────────────

/**
 * Assign regions to components using circle packing algorithm
 * Larger components get more space, arranged to minimize overlap
 */
function assignComponentRegions(
  componentGroups: Map<number, LayoutNode[]>,
  width: number,
  height: number,
  levelRadius: number
): Map<number, ComponentInfo> {
  const regions = new Map<number, ComponentInfo>()
  const componentCount = componentGroups.size

  if (componentCount === 0) return regions

  // Single component: center it
  if (componentCount === 1) {
    const [compId, nodes] = Array.from(componentGroups.entries())[0]
    regions.set(compId, {
      id: compId,
      nodeIds: new Set(nodes.map(n => n.id)),
      centerNodeId: null,  // Will be set later
      cx: width / 2,
      cy: height / 2,
      radius: Math.min(width, height) / 2 - 50,
    })
    return regions
  }

  // Multiple components: arrange in a circular pattern
  // Sort by size (largest first) for better packing
  const sortedComponents = Array.from(componentGroups.entries())
    .map(([id, nodes]) => ({
      id,
      nodes,
      size: nodes.length,
      // Estimate radius based on node count and levels
      estimatedRadius: Math.sqrt(nodes.length) * levelRadius * 0.6,
    }))
    .sort((a, b) => b.size - a.size)

  const canvasCenterX = width / 2
  const canvasCenterY = height / 2
  const maxDimension = Math.min(width, height)

  // Arrange components in concentric rings
  // Large component(s) in center, smaller ones around
  if (sortedComponents.length >= 1) {
    const largestComponent = sortedComponents[0]

    // If largest component is significantly bigger (>50% of nodes), center it
    const totalNodes = sortedComponents.reduce((sum, c) => sum + c.size, 0)
    const largestRatio = largestComponent.size / totalNodes

    if (largestRatio > 0.4 && sortedComponents.length > 1) {
      // Center the largest component
      regions.set(largestComponent.id, {
        id: largestComponent.id,
        nodeIds: new Set(largestComponent.nodes.map(n => n.id)),
        centerNodeId: null,
        cx: canvasCenterX,
        cy: canvasCenterY,
        radius: largestComponent.estimatedRadius,
      })

      // Arrange remaining components in a ring around it
      const remainingComponents = sortedComponents.slice(1)
      const ringRadius = largestComponent.estimatedRadius + levelRadius * 1.5
      const angleStep = (2 * Math.PI) / remainingComponents.length

      remainingComponents.forEach((comp, index) => {
        const angle = angleStep * index - Math.PI / 2  // Start from top
        regions.set(comp.id, {
          id: comp.id,
          nodeIds: new Set(comp.nodes.map(n => n.id)),
          centerNodeId: null,
          cx: canvasCenterX + Math.cos(angle) * ringRadius,
          cy: canvasCenterY + Math.sin(angle) * ringRadius,
          radius: comp.estimatedRadius,
        })
      })
    } else {
      // Distribute all components evenly in a ring
      const ringRadius = maxDimension * 0.25
      const angleStep = (2 * Math.PI) / sortedComponents.length

      sortedComponents.forEach((comp, index) => {
        const angle = angleStep * index - Math.PI / 2
        regions.set(comp.id, {
          id: comp.id,
          nodeIds: new Set(comp.nodes.map(n => n.id)),
          centerNodeId: null,
          cx: canvasCenterX + Math.cos(angle) * ringRadius,
          cy: canvasCenterY + Math.sin(angle) * ringRadius,
          radius: comp.estimatedRadius,
        })
      })
    }
  }

  return regions
}

// ─────────────────────────────────────────────────────────────
// BFS Level Calculation
// ─────────────────────────────────────────────────────────────

/**
 * Calculate BFS levels from a center node (original single-center version)
 */
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

/**
 * Calculate BFS levels per component, each with its own center
 * Returns combined levels map and component info with centers
 */
function calculateBfsLevelsPerComponent(
  nodes: LayoutNode[],
  edges: LayoutEdge[],
  componentMap: Map<string, number>,
  componentGroups: Map<number, LayoutNode[]>,
  componentRegions: Map<number, ComponentInfo>
): { levels: Map<string, number>; componentCenters: Map<number, string> } {
  const levels = new Map<string, number>()
  const componentCenters = new Map<number, string>()

  // Build global adjacency list
  const adjacency = new Map<string, Set<string>>()
  nodes.forEach(n => adjacency.set(n.id, new Set()))
  
  edges.forEach(e => {
    const sourceId = typeof e.source === 'string' ? e.source : e.source.id
    const targetId = typeof e.target === 'string' ? e.target : e.target.id
    adjacency.get(sourceId)?.add(targetId)
    adjacency.get(targetId)?.add(sourceId)
  })

  // Process each component independently
  componentGroups.forEach((compNodes, compId) => {
    // Find center for this component
    const centerId = findComponentCenter(compNodes, edges)
    if (centerId) {
      componentCenters.set(compId, centerId)
      
      // Update region info
      const region = componentRegions.get(compId)
      if (region) {
        region.centerNodeId = centerId
      }
    }

    // BFS within this component
    if (centerId) {
      const queue: string[] = [centerId]
      levels.set(centerId, 0)
      
      while (queue.length > 0) {
        const current = queue.shift()!
        const currentLevel = levels.get(current)!
        
        adjacency.get(current)?.forEach(neighbor => {
          // Only process nodes in the same component
          if (!levels.has(neighbor) && componentMap.get(neighbor) === compId) {
            levels.set(neighbor, currentLevel + 1)
            queue.push(neighbor)
          }
        })
      }
    }

    // Assign level 0 to any remaining nodes in this component
    compNodes.forEach(n => {
      if (!levels.has(n.id)) {
        levels.set(n.id, 0)
      }
    })
  })

  return { levels, componentCenters }
}

// ─────────────────────────────────────────────────────────────
// Component Separation Force
// ─────────────────────────────────────────────────────────────

interface ExtendedLayoutNode extends LayoutNode {
  componentId?: number
  componentCx?: number
  componentCy?: number
}

/**
 * Custom D3 force that pushes nodes toward their component center
 * and adds extra repulsion between nodes in different components
 */
function forceComponentSeparation(
  componentMap: Map<string, number>,
  componentRegions: Map<number, ComponentInfo>,
  strength: number = 0.3
) {
  let nodes: ExtendedLayoutNode[] = []

  function force(alpha: number) {
    nodes.forEach(node => {
      const compId = node.componentId
      if (compId === undefined) return

      const region = componentRegions.get(compId)
      if (!region) return

      // Pull toward component center
      const dx = region.cx - (node.x ?? 0)
      const dy = region.cy - (node.y ?? 0)
      const distance = Math.sqrt(dx * dx + dy * dy)

      if (distance > 0) {
        // Stronger pull when far from component center
        const pullStrength = strength * alpha * Math.min(1, distance / 200)
        node.vx = (node.vx ?? 0) + dx * pullStrength / distance
        node.vy = (node.vy ?? 0) + dy * pullStrength / distance
      }
    })

    // Extra repulsion between nodes in different components
    for (let i = 0; i < nodes.length; i++) {
      for (let j = i + 1; j < nodes.length; j++) {
        const nodeA = nodes[i]
        const nodeB = nodes[j]

        // Only apply extra repulsion to nodes in different components
        if (nodeA.componentId === nodeB.componentId) continue

        const dx = (nodeB.x ?? 0) - (nodeA.x ?? 0)
        const dy = (nodeB.y ?? 0) - (nodeA.y ?? 0)
        const distance = Math.sqrt(dx * dx + dy * dy)

        if (distance > 0 && distance < 300) {
          // Strong repulsion for nodes in different components
          const repulsion = alpha * 200 / (distance * distance)
          const fx = dx * repulsion / distance
          const fy = dy * repulsion / distance

          nodeA.vx = (nodeA.vx ?? 0) - fx
          nodeA.vy = (nodeA.vy ?? 0) - fy
          nodeB.vx = (nodeB.vx ?? 0) + fx
          nodeB.vy = (nodeB.vy ?? 0) + fy
        }
      }
    }
  }

  force.initialize = function(inputNodes: ExtendedLayoutNode[]) {
    nodes = inputNodes
    // Attach component info to each node for faster lookup
    nodes.forEach(node => {
      node.componentId = componentMap.get(node.id)
      const region = componentRegions.get(node.componentId ?? 0)
      if (region) {
        node.componentCx = region.cx
        node.componentCy = region.cy
      }
    })
  }

  force.strength = function(s: number) {
    strength = s
    return force
  }

  return force
}

/**
 * Custom force to apply radial layout per component
 * Each component has its own center and radial rings
 */
function forceComponentRadial(
  componentMap: Map<string, number>,
  componentRegions: Map<number, ComponentInfo>,
  levelRadius: number,
  strength: number = 0.6
) {
  let nodes: ExtendedLayoutNode[] = []

  function force(alpha: number) {
    nodes.forEach(node => {
      const compId = node.componentId
      if (compId === undefined) return

      const region = componentRegions.get(compId)
      if (!region) return

      // Target radius based on BFS level
      const targetRadius = (node.level || 0) * levelRadius

      // Current distance from component center
      const dx = (node.x ?? 0) - region.cx
      const dy = (node.y ?? 0) - region.cy
      const currentRadius = Math.sqrt(dx * dx + dy * dy)

      if (currentRadius > 0) {
        // Push toward target radius
        const radiusDiff = targetRadius - currentRadius
        const pushStrength = strength * alpha * radiusDiff / currentRadius

        node.vx = (node.vx ?? 0) + dx * pushStrength
        node.vy = (node.vy ?? 0) + dy * pushStrength
      } else if (targetRadius > 0) {
        // Node at center but should be on a ring - give random direction
        const angle = Math.random() * 2 * Math.PI
        node.vx = (node.vx ?? 0) + Math.cos(angle) * strength * alpha * 10
        node.vy = (node.vy ?? 0) + Math.sin(angle) * strength * alpha * 10
      }
    })
  }

  force.initialize = function(inputNodes: ExtendedLayoutNode[]) {
    nodes = inputNodes
    nodes.forEach(node => {
      node.componentId = componentMap.get(node.id)
    })
  }

  force.strength = function(s: number) {
    strength = s
    return force
  }

  return force
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
    // Give level 0 nodes a minimum spread radius to avoid all nodes at center
    const minRadius = levelRadius * 0.3
    const radius = Math.max(minRadius, level * levelRadius * 0.5)
    
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

  // Create simulation - optimized for large graphs
  // Barnes-Hut theta: higher = faster but less accurate (default 0.9)
  // Note: Initial layout needs stronger force to spread nodes properly
  const chargeForce = forceManyBody<LayoutNode>()
    .strength(-300)      // Stronger for initial layout (persistent sim uses -150)
    .distanceMax(300)    // Wider range for initial spread
    .theta(1.0)          // More accurate for initial layout

  const simulation: Simulation<LayoutNode, LayoutEdge> = forceSimulation(nodes)
    // Center force (weak, for initial positioning)
    .force('center', forceCenter(centerX, centerY).strength(0.05))
    // Radial force: push nodes to their BFS level radius
    .force('radial', forceRadial<LayoutNode>(
      d => d.level * levelRadius,
      centerX,
      centerY
    ).strength(0.8))
    // Repulsion between nodes (optimized)
    .force('charge', chargeForce)
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
    nodeRadius = DEFAULT_NODE_RADIUS,
    levelRadius = DEFAULT_LEVEL_RADIUS,
    existingPositions,
  } = config

  // ─────────────────────────────────────────────────────────────
  // Step 1: Detect connected components
  // ─────────────────────────────────────────────────────────────
  const componentMap = detectConnectedComponents(inputNodes, inputEdges)
  const componentGroups = groupNodesByComponent(inputNodes, componentMap)
  const numComponents = componentGroups.size

  // ─────────────────────────────────────────────────────────────
  // Step 2: Assign regions to components
  // ─────────────────────────────────────────────────────────────
  const componentRegions = assignComponentRegions(
    componentGroups,
    width,
    height,
    levelRadius
  )

  // ─────────────────────────────────────────────────────────────
  // Step 3: Calculate BFS levels per component
  // ─────────────────────────────────────────────────────────────
  const { levels, componentCenters } = calculateBfsLevelsPerComponent(
    inputNodes,
    inputEdges,
    componentMap,
    componentGroups,
    componentRegions
  )

  // Track which nodes are new (need layout calculation)
  const newNodeIds = new Set<string>()

  // ─────────────────────────────────────────────────────────────
  // Step 4: Clone nodes with initial positions
  // ─────────────────────────────────────────────────────────────
  const nodes: ExtendedLayoutNode[] = inputNodes.map(n => {
    const level = levels.get(n.id) ?? 0
    const compId = componentMap.get(n.id) ?? 0
    const region = componentRegions.get(compId)
    const existingPos = existingPositions?.get(n.id)

    // Component center coordinates
    const compCx = region?.cx ?? width / 2
    const compCy = region?.cy ?? height / 2

    if (existingPos) {
      // Existing node - preserve position
      return {
        ...n,
        level,
        componentId: compId,
        componentCx: compCx,
        componentCy: compCy,
        x: existingPos.x,
        y: existingPos.y,
      }
    } else {
      // New node - calculate initial position based on component region
      newNodeIds.add(n.id)

      // Get nodes at same level within same component
      const compNodes = componentGroups.get(compId) || []
      const nodesAtLevel = compNodes.filter(cn => levels.get(cn.id) === level)
      const indexAtLevel = nodesAtLevel.findIndex(cn => cn.id === n.id)
      const countAtLevel = Math.max(1, nodesAtLevel.length)

      // Calculate position around component center
      const angleOffset = level * 0.5
      const angle = (2 * Math.PI * indexAtLevel / countAtLevel) + angleOffset
      const minRadius = levelRadius * 0.3
      const radius = Math.max(minRadius, level * levelRadius * 0.5)

      return {
        ...n,
        level,
        componentId: compId,
        componentCx: compCx,
        componentCy: compCy,
        x: compCx + Math.cos(angle) * radius,
        y: compCy + Math.sin(angle) * radius,
      }
    }
  })

  // ─────────────────────────────────────────────────────────────
  // Step 5: Fix component center nodes
  // ─────────────────────────────────────────────────────────────
  componentCenters.forEach((centerId, compId) => {
    const region = componentRegions.get(compId)
    if (!region) return

    const centerNode = nodes.find(n => n.id === centerId)
    if (centerNode) {
      centerNode.fx = region.cx
      centerNode.fy = region.cy
      centerNode.x = region.cx
      centerNode.y = region.cy
    }
  })

  const edges: LayoutEdge[] = inputEdges.map(e => ({ ...e }))

  // ─────────────────────────────────────────────────────────────
  // Step 6: Create simulation with component-aware forces
  // ─────────────────────────────────────────────────────────────
  const chargeForce = forceManyBody<ExtendedLayoutNode>()
    .strength(-150)
    .distanceMax(200)
    .theta(1.2)

  const simulation = forceSimulation(nodes)
    // Collision avoidance
    .force('collide', forceCollide<ExtendedLayoutNode>(nodeRadius + 20).strength(1).iterations(1))
    // Standard charge repulsion
    .force('charge', chargeForce)
    // Link force
    .force('link', forceLink<ExtendedLayoutNode, LayoutEdge>(edges)
      .id(d => d.id)
      .distance(levelRadius * 0.8)
      .strength(0.2)
    )
    .alphaDecay(0.05)
    .alphaMin(0.001)
    .velocityDecay(0.6)
    .on('tick', () => onTick(nodes))

  // Add component-aware forces only if multiple components
  if (numComponents > 1) {
    simulation
      .force('componentSeparation', forceComponentSeparation(componentMap, componentRegions, 0.4))
      .force('componentRadial', forceComponentRadial(componentMap, componentRegions, levelRadius, 0.5))
  } else {
    // Single component: use standard radial force
    const singleRegion = componentRegions.values().next().value
    const cx = singleRegion?.cx ?? width / 2
    const cy = singleRegion?.cy ?? height / 2

    simulation
      .force('center', forceCenter(cx, cy).strength(0.01))
      .force('radial', forceRadial<ExtendedLayoutNode>(
        d => (d.level || 0) * levelRadius,
        cx,
        cy
      ).strength(0.6))
  }

  // ─────────────────────────────────────────────────────────────
  // Step 7: Run initial iterations
  // ─────────────────────────────────────────────────────────────
  const hasExistingPositions = existingPositions && existingPositions.size > 0
  const iterations = hasExistingPositions
    ? (newNodeIds.size > 0 ? 80 : 0)  // More iterations for multi-component layout
    : SIMULATION_ITERATIONS

  if (iterations > 0) {
    simulation.alpha(hasExistingPositions ? 0.5 : 1)
    for (let i = 0; i < iterations; i++) {
      simulation.tick()
    }
  }
  simulation.stop()
  onTick(nodes)

  // Track currently dragged node and all component centers
  let draggedNodeId: string | null = null
  const allCenterIds = new Set(componentCenters.values())

  return {
    simulation,
    nodes,
    edges,
    reheat: () => {
      simulation.alpha(0.15).restart()
    },
    setDraggedNode: (nodeId: string | null) => {
      // Unfix previous dragged node (except component centers)
      if (draggedNodeId && !allCenterIds.has(draggedNodeId)) {
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

  // Optimized charge force for large graphs
  const chargeForce = forceManyBody<LayoutNode>()
    .strength(-200)
    .distanceMax(250)
    .theta(1.2)

  const simulation = forceSimulation(nodes)
    .force('center', forceCenter(centerX, centerY).strength(0.05))
    .force('radial', forceRadial<LayoutNode>(
      d => d.level * levelRadius,
      centerX,
      centerY
    ).strength(0.8))
    .force('charge', chargeForce)
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
