// ============================================================
//  Animated Edge - Custom React Flow Edge
//  Displays flowing line with subtle glow effects
//  Optimized for readability and reduced visual noise
// ============================================================

import { memo, useMemo } from 'react'
import { BaseEdge, getBezierPath, type Position } from '@xyflow/react'
import type { AgentStatus } from '@/store/agent-topology-store'

// ------------------------------------------------------------
//  Types
// ------------------------------------------------------------

export interface AnimatedEdgeData extends Record<string, unknown> {
  sourceStatus?: AgentStatus
  targetStatus?: AgentStatus
  edgeIndex?: number       // Index among edges from same source
  totalFromSource?: number // Total edges from same source
  messageCount?: number    // Number of messages through this edge
}

// ------------------------------------------------------------
//  Color Config by Status
// ------------------------------------------------------------

const getEdgeColor = (sourceStatus?: AgentStatus, targetStatus?: AgentStatus) => {
  if (sourceStatus === 'running') return '#22D3EE'    // cyan-400
  if (sourceStatus === 'completed') return '#34D399'  // emerald-400 (brighter)
  if (targetStatus === 'running') return '#22D3EE'
  return '#94A3B8'  // slate-400 (brighter for visibility)
}

// ------------------------------------------------------------
//  Component
// ------------------------------------------------------------

interface AnimatedEdgeProps {
  id: string
  sourceX: number
  sourceY: number
  targetX: number
  targetY: number
  sourcePosition: Position
  targetPosition: Position
  data?: AnimatedEdgeData
}

const AnimatedEdge = memo(({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  data,
}: AnimatedEdgeProps) => {
  const sourceStatus = data?.sourceStatus
  const targetStatus = data?.targetStatus
  const edgeIndex = data?.edgeIndex ?? 0
  const totalFromSource = data?.totalFromSource ?? 1
  const messageCount = data?.messageCount ?? 0
  
  // Calculate offset to separate edges from same source
  // Spread edges horizontally based on their index
  const { offsetX, curvature } = useMemo(() => {
    // Base offset: spread edges left/right from center
    const spreadWidth = 40 // Total spread width
    let offsetX = 0
    let baseCurvature = 0.25
    
    if (totalFromSource > 1) {
      // Calculate position in spread: -0.5 to 0.5 for 2 edges, etc.
      const normalizedPos = (edgeIndex - (totalFromSource - 1) / 2) / Math.max(1, totalFromSource - 1)
      offsetX = normalizedPos * spreadWidth
      // More curvature for offset edges
      baseCurvature = 0.3 + Math.abs(normalizedPos) * 0.2
    }
    
    // Additional curvature for horizontal edges
    const deltaX = targetX - sourceX
    if (Math.abs(deltaX) > 50) {
      baseCurvature += 0.15
    }
    
    return { offsetX, curvature: baseCurvature }
  }, [sourceX, targetX, edgeIndex, totalFromSource])
  
  // Apply horizontal offset to source position
  const adjustedSourceX = sourceX + offsetX
  
  const [edgePath] = getBezierPath({
    sourceX: adjustedSourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
    curvature,
  })
  
  const color = getEdgeColor(sourceStatus, targetStatus)
  const isActive = sourceStatus === 'running'
  const isCompleted = sourceStatus === 'completed'
  
  // Slower animation: 3s active, 8s idle (reduced from 1.5s/4s)
  const flowDuration = isActive ? 3 : 8
  
  // Unique filter ID to avoid conflicts between edges
  const filterId = `glow-${id}`
  
  // Calculate label position (midpoint of edge with offset)
  const labelX = (adjustedSourceX + targetX) / 2 + 15
  const labelY = (sourceY + targetY) / 2
  
  return (
    <>
      {/* Base edge line - more visible */}
      <BaseEdge
        id={id}
        path={edgePath}
        style={{
          stroke: color,
          strokeWidth: isActive ? 3 : 2,
          opacity: isCompleted ? 0.85 : isActive ? 0.9 : 0.7,
          transition: 'stroke 0.5s ease, opacity 0.5s ease',
        }}
      />
      
      {/* Single flowing particle */}
      <circle 
        r={isActive ? 5 : 3} 
        fill={color} 
        opacity={isActive ? 1 : 0.8}
        filter={`url(#${filterId})`}
      >
        <animateMotion
          dur={`${flowDuration}s`}
          repeatCount="indefinite"
          path={edgePath}
          calcMode="linear"
        />
      </circle>
      
      {/* Message count label (only show if > 0) */}
      {messageCount > 0 && (
        <g transform={`translate(${labelX}, ${labelY})`}>
          <rect
            x={-12}
            y={-8}
            width={24}
            height={16}
            rx={4}
            fill="rgba(15, 23, 42, 0.9)"
            stroke={color}
            strokeWidth={0.5}
            opacity={0.9}
          />
          <text
            x={0}
            y={4}
            textAnchor="middle"
            fontSize={9}
            fontFamily="monospace"
            fill={color}
          >
            {messageCount}
          </text>
        </g>
      )}
      
      {/* Glow filter */}
      <defs>
        <filter id={filterId} x="-100%" y="-100%" width="300%" height="300%">
          <feGaussianBlur stdDeviation={isActive ? 3 : 1.5} result="coloredBlur" />
          <feMerge>
            <feMergeNode in="coloredBlur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>
    </>
  )
})

AnimatedEdge.displayName = 'AnimatedEdge'

export default AnimatedEdge
