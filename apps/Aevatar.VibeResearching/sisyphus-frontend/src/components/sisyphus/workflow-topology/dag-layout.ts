// ============================================================
//  Dagre Layout Algorithm for DAG Visualization
// ============================================================

import dagre from 'dagre'
import { Position, type Node, type Edge } from '@xyflow/react'

const NODE_WIDTH = 72
const NODE_HEIGHT = 72

/**
 * Apply Dagre layout algorithm to position nodes in a DAG
 * @param nodes - ReactFlow nodes
 * @param edges - ReactFlow edges
 * @param direction - Layout direction: 'TB' (top-bottom) or 'LR' (left-right)
 */
export function getLayoutedElements(
  nodes: Node[],
  edges: Edge[],
  direction: 'TB' | 'LR' = 'TB'
): { nodes: Node[]; edges: Edge[] } {
  const dagreGraph = new dagre.graphlib.Graph()
  dagreGraph.setDefaultEdgeLabel(() => ({}))
  dagreGraph.setGraph({ rankdir: direction, nodesep: 70, ranksep: 90 })

  // Add nodes to dagre graph
  nodes.forEach((node) => {
    dagreGraph.setNode(node.id, { width: NODE_WIDTH, height: NODE_HEIGHT })
  })

  // Add edges to dagre graph
  edges.forEach((edge) => {
    dagreGraph.setEdge(edge.source, edge.target)
  })

  // Run layout algorithm
  dagre.layout(dagreGraph)

  // Map calculated positions back to ReactFlow nodes
  const layoutedNodes = nodes.map((node) => {
    const nodeWithPosition = dagreGraph.node(node.id)
    return {
      ...node,
      position: {
        x: nodeWithPosition.x - NODE_WIDTH / 2,
        y: nodeWithPosition.y - NODE_HEIGHT / 2,
      },
      targetPosition: direction === 'TB' ? Position.Top : Position.Left,
      sourcePosition: direction === 'TB' ? Position.Bottom : Position.Right,
    }
  })

  return { nodes: layoutedNodes, edges }
}
