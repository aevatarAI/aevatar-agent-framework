// ============================================================
//  Dagre Layout Algorithm for DAG Visualization
//  NOTE: This file is currently unused. The project uses radial-force-layout instead.
//  To re-enable, install: npm install dagre @types/dagre
// ============================================================

// import dagre from 'dagre'
import { Position, type Node, type Edge } from '@xyflow/react'

const NODE_WIDTH = 72
const NODE_HEIGHT = 72

/**
 * Apply Dagre layout algorithm to position nodes in a DAG
 * @param nodes - ReactFlow nodes
 * @param edges - ReactFlow edges
 * @param direction - Layout direction: 'TB' (top-bottom) or 'LR' (left-right)
 *
 * NOTE: Currently disabled - dagre package not installed.
 * This layout is reserved for potential future use with tree-like DAG structures.
 */
export function getLayoutedElements(
  nodes: Node[],
  edges: Edge[],
  _direction: 'TB' | 'LR' = 'TB'
): { nodes: Node[]; edges: Edge[] } {
  // Dagre layout disabled - return nodes with default positions
  // To enable, uncomment dagre import and install the package

  const layoutedNodes = nodes.map((node, index) => {
    return {
      ...node,
      position: node.position || {
        x: (index % 5) * (NODE_WIDTH + 20),
        y: Math.floor(index / 5) * (NODE_HEIGHT + 20),
      },
      targetPosition: Position.Top,
      sourcePosition: Position.Bottom,
    }
  })

  return { nodes: layoutedNodes, edges }
}
