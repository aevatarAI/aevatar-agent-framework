// ============================================================
//  Animated Edge - Custom React Flow Edge
//  Displays flowing line with arrows and EventBox animations
//  Features: arrow markers, message transfer animation
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
  lastEventPreview?: string // Preview of last event transferred
  isTransferring?: boolean  // True when event is being transferred
  isSourceSSEActive?: boolean // True only when source is running via SSE (not API)
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
  const isSourceSSEActive = data?.isSourceSSEActive ?? false
  
  // Show EventBox animation ONLY when:
  // 1. Source node is running AND
  // 2. Source node was triggered via SSE (not from API/history)
  // This prevents animation when loading historical streaming state
  const showEventBox = sourceStatus === 'running' && isSourceSSEActive
  
  // Calculate offset to separate edges from same source
  const { offsetX, curvature } = useMemo(() => {
    const spreadWidth = 40
    let offsetX = 0
    let baseCurvature = 0.25
    
    if (totalFromSource > 1) {
      const normalizedPos = (edgeIndex - (totalFromSource - 1) / 2) / Math.max(1, totalFromSource - 1)
      offsetX = normalizedPos * spreadWidth
      baseCurvature = 0.3 + Math.abs(normalizedPos) * 0.2
    }
    
    const deltaX = targetX - sourceX
    if (Math.abs(deltaX) > 50) {
      baseCurvature += 0.15
    }
    
    return { offsetX, curvature: baseCurvature }
  }, [sourceX, targetX, edgeIndex, totalFromSource])
  
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
  
  // Unique IDs for SVG elements
  const filterId = `glow-${id}`
  const markerId = `arrow-${id}`
  
  // Label position
  const labelX = (adjustedSourceX + targetX) / 2 + 15
  const labelY = (sourceY + targetY) / 2
  
  return (
    <>
      {/* SVG Definitions: Arrow marker + Glow filter */}
      <defs>
        {/* Arrow marker */}
        <marker
          id={markerId}
          viewBox="0 0 10 10"
          refX="8"
          refY="5"
          markerWidth="6"
          markerHeight="6"
          orient="auto-start-reverse"
        >
          <path
            d="M 0 0 L 10 5 L 0 10 z"
            fill={color}
            opacity={isActive ? 1 : 0.7}
          />
        </marker>
        
        {/* Glow filter */}
        <filter id={filterId} x="-100%" y="-100%" width="300%" height="300%">
          <feGaussianBlur stdDeviation={isActive ? 3 : 1.5} result="coloredBlur" />
          <feMerge>
            <feMergeNode in="coloredBlur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>
      
      {/* Base edge line with arrow - static, no continuous animation */}
      <BaseEdge
        id={id}
        path={edgePath}
        style={{
          stroke: color,
          strokeWidth: isActive ? 3 : 2,
          opacity: isCompleted ? 0.85 : isActive ? 0.9 : 0.7,
          transition: 'stroke 0.5s ease, opacity 0.5s ease',
          markerEnd: `url(#${markerId})`,
        }}
      />
      
      {/* EventBox Animation - Shows when upstream node is running/outputting */}
      {showEventBox && (
        <g>
          {/* Animated rect - centered on path point */}
          <rect
            x="-16"
            y="-10"
            width="32"
            height="20"
            rx="4"
            fill="rgba(34, 211, 238, 0.95)"
            stroke="#fff"
            strokeWidth="1.5"
            filter={`url(#${filterId})`}
          >
            <animateMotion
              dur="3s"
              repeatCount="indefinite"
              path={edgePath}
              calcMode="linear"
            />
          </rect>
          {/* Animated text - centered in rect */}
          <text
            fontSize="11"
            fill="#0A0F1C"
            textAnchor="middle"
            dominantBaseline="central"
            fontFamily="monospace"
            fontWeight="600"
          >
            <animateMotion
              dur="3s"
              repeatCount="indefinite"
              path={edgePath}
              calcMode="linear"
            />
            📦
          </text>
        </g>
      )}
      
      {/* Message count badge */}
      {messageCount > 0 && (
        <g transform={`translate(${labelX}, ${labelY})`}>
          <rect
            x={-14}
            y={-9}
            width={28}
            height={18}
            rx={4}
            fill="rgba(15, 23, 42, 0.95)"
            stroke={color}
            strokeWidth={1}
          />
          <text
            x={0}
            y={4}
            textAnchor="middle"
            fontSize={10}
            fontFamily="JetBrains Mono, monospace"
            fontWeight="500"
            fill={color}
          >
            {messageCount}
          </text>
        </g>
      )}
    </>
  )
})

AnimatedEdge.displayName = 'AnimatedEdge'

export default AnimatedEdge
