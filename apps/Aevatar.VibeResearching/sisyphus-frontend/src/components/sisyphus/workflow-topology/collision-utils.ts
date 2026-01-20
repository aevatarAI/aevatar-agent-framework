// ============================================================
//  Collision Utils - Node Mutual Exclusion During Drag
//  Implements real-time collision detection and resolution
// ============================================================

import type { Node } from '@xyflow/react'

// ─────────────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────────────

export const COLLISION_CONFIG = {
  // Node visual radius (72px diameter / 2)
  nodeRadius: 36,
  // Minimum gap between nodes
  gap: 18,
  // Total safe distance = nodeRadius * 2 + gap = 90px
  get minDistance() {
    return this.nodeRadius * 2 + this.gap
  },
  // Maximum iterations (reduced for performance - 3 is usually enough)
  maxIterations: 3,
  // Smoothing factor for push distance (>1.0 = extra buffer)
  pushBuffer: 1.15,
  // Only check nodes within this radius of dragged node (performance optimization)
  proximityRadius: 300,
}

// ─────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────

export interface NodePositionUpdate {
  id: string
  position: { x: number; y: number }
}

interface Point {
  x: number
  y: number
}

// ─────────────────────────────────────────────────────────────
// Core Functions
// ─────────────────────────────────────────────────────────────

/**
 * Get center point of a node (position is top-left corner)
 */
function getNodeCenter(node: Node): Point {
  return {
    x: node.position.x + COLLISION_CONFIG.nodeRadius,
    y: node.position.y + COLLISION_CONFIG.nodeRadius,
  }
}

/**
 * Calculate distance between two points
 */
function distance(a: Point, b: Point): number {
  const dx = b.x - a.x
  const dy = b.y - a.y
  return Math.sqrt(dx * dx + dy * dy)
}

/**
 * Normalize a vector (make it unit length)
 */
function normalize(dx: number, dy: number): Point {
  const len = Math.sqrt(dx * dx + dy * dy)
  if (len === 0) return { x: 1, y: 0 } // Default direction if overlapping exactly
  return { x: dx / len, y: dy / len }
}

/**
 * Resolve collisions between nodes iteratively (optimized for performance)
 * Only checks nodes within proximity radius of the dragged node
 *
 * @param draggedNode - The node being dragged (with updated position)
 * @param allNodes - All nodes in the graph
 * @param minDistance - Minimum distance between node centers (default: 90px)
 */
export function resolveCollisions(
  draggedNode: Node,
  allNodes: Node[],
  minDistance: number = COLLISION_CONFIG.minDistance
): NodePositionUpdate[] {
  if (allNodes.length < 2) return []

  const draggedCenter = {
    x: draggedNode.position.x + COLLISION_CONFIG.nodeRadius,
    y: draggedNode.position.y + COLLISION_CONFIG.nodeRadius,
  }

  // Filter to only nearby nodes for performance (O(n) instead of checking all pairs)
  const proximityRadius = COLLISION_CONFIG.proximityRadius
  const nearbyNodes = allNodes.filter(node => {
    if (node.id === draggedNode.id) return true
    const nodeCenter = {
      x: node.position.x + COLLISION_CONFIG.nodeRadius,
      y: node.position.y + COLLISION_CONFIG.nodeRadius,
    }
    return distance(draggedCenter, nodeCenter) < proximityRadius
  })

  if (nearbyNodes.length < 2) return []

  // Store positions (mutable during collision resolution)
  const positions = new Map<string, Point>()
  for (const node of nearbyNodes) {
    positions.set(node.id, { ...node.position })
  }

  const nodeIds = Array.from(positions.keys())
  const draggedNodeId = draggedNode.id

  // Iteratively resolve collisions (few iterations for smooth performance)
  for (let iteration = 0; iteration < COLLISION_CONFIG.maxIterations; iteration++) {
    let hasCollision = false

    for (let i = 0; i < nodeIds.length; i++) {
      for (let j = i + 1; j < nodeIds.length; j++) {
        const idA = nodeIds[i]
        const idB = nodeIds[j]

        const posA = positions.get(idA)!
        const posB = positions.get(idB)!

        const centerA = {
          x: posA.x + COLLISION_CONFIG.nodeRadius,
          y: posA.y + COLLISION_CONFIG.nodeRadius,
        }
        const centerB = {
          x: posB.x + COLLISION_CONFIG.nodeRadius,
          y: posB.y + COLLISION_CONFIG.nodeRadius,
        }

        const dist = distance(centerA, centerB)

        if (dist < minDistance) {
          hasCollision = true

          const dx = centerB.x - centerA.x
          const dy = centerB.y - centerA.y
          const dir = normalize(dx, dy)
          const overlap = (minDistance - dist) * COLLISION_CONFIG.pushBuffer

          if (idA === draggedNodeId) {
            positions.set(idB, {
              x: posB.x + dir.x * overlap,
              y: posB.y + dir.y * overlap,
            })
          } else if (idB === draggedNodeId) {
            positions.set(idA, {
              x: posA.x - dir.x * overlap,
              y: posA.y - dir.y * overlap,
            })
          } else {
            const halfOverlap = overlap / 2
            positions.set(idA, {
              x: posA.x - dir.x * halfOverlap,
              y: posA.y - dir.y * halfOverlap,
            })
            positions.set(idB, {
              x: posB.x + dir.x * halfOverlap,
              y: posB.y + dir.y * halfOverlap,
            })
          }
        }
      }
    }

    if (!hasCollision) break
  }

  // Build updates array (only changed positions)
  const updates: NodePositionUpdate[] = []
  for (const node of nearbyNodes) {
    if (node.id === draggedNodeId) continue

    const newPos = positions.get(node.id)!
    const oldPos = node.position

    if (Math.abs(newPos.x - oldPos.x) > 0.5 || Math.abs(newPos.y - oldPos.y) > 0.5) {
      updates.push({ id: node.id, position: newPos })
    }
  }

  return updates
}

/**
 * Check if two nodes are colliding
 */
export function areNodesColliding(
  nodeA: Node,
  nodeB: Node,
  minDistance: number = COLLISION_CONFIG.minDistance
): boolean {
  const centerA = getNodeCenter(nodeA)
  const centerB = getNodeCenter(nodeB)
  return distance(centerA, centerB) < minDistance
}
