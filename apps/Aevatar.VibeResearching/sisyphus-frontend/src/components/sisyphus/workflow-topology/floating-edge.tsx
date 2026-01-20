// ============================================================
//  FloatingEdge - Edge that connects to node edges, not centers
//  Creates radial effect like the reference image
// ============================================================

import { useCallback } from 'react'
import { useStore, type EdgeProps, type Node } from '@xyflow/react'

// Node dimensions (must match CyberNode)
const NODE_RADIUS = 36 // 72/2

// Calculate the point on the edge of a circle given center and target
function getEdgePoint(
  center: { x: number; y: number },
  target: { x: number; y: number },
  radius: number
): { x: number; y: number } {
  const dx = target.x - center.x
  const dy = target.y - center.y
  const distance = Math.sqrt(dx * dx + dy * dy)
  
  if (distance === 0) return center
  
  // Normalize and scale to radius
  return {
    x: center.x + (dx / distance) * radius,
    y: center.y + (dy / distance) * radius,
  }
}

// Get node center position
function getNodeCenter(node: Node): { x: number; y: number } {
  return {
    x: node.position.x + NODE_RADIUS,
    y: node.position.y + NODE_RADIUS,
  }
}

export function FloatingEdge({
  id,
  source,
  target,
  style,
  markerEnd,
}: EdgeProps) {
  // Get source and target nodes from store
  const sourceNode = useStore(
    useCallback((store) => store.nodeLookup.get(source), [source])
  )
  const targetNode = useStore(
    useCallback((store) => store.nodeLookup.get(target), [target])
  )

  if (!sourceNode || !targetNode) {
    return null
  }

  // Calculate node centers
  const sourceCenter = getNodeCenter(sourceNode)
  const targetCenter = getNodeCenter(targetNode)

  // Calculate edge points on the circumference of each node
  const sourcePoint = getEdgePoint(sourceCenter, targetCenter, NODE_RADIUS)
  const targetPoint = getEdgePoint(targetCenter, sourceCenter, NODE_RADIUS)

  // Simple straight line path
  const path = `M ${sourcePoint.x} ${sourcePoint.y} L ${targetPoint.x} ${targetPoint.y}`

  return (
    <path
      id={id}
      className="react-flow__edge-path"
      d={path}
      style={style}
      markerEnd={markerEnd as string}
    />
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export const edgeTypes = { floating: FloatingEdge }
