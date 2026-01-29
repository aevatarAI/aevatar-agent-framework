// ============================================================
//  Animated Edge - Edge with particle flow animation
// ============================================================

import React, { memo } from 'react'
import { getBezierPath, EdgeLabelRenderer } from '@xyflow/react'
import type { EdgeProps } from '@xyflow/react'
import { motion } from 'framer-motion'

interface AnimatedEdgeData {
  color?: string
  isVote?: boolean
  messageCount?: number
}

const AnimatedEdge: React.FC<EdgeProps> = ({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  style = {},
  markerEnd,
  data,
}) => {
  const edgeData = data as AnimatedEdgeData | undefined
  const color = edgeData?.color || '#00f0ff'
  const isVote = edgeData?.isVote || false

  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
  })

  return (
    <>
      {/* Base edge path */}
      <path
        id={id}
        style={{
          ...style,
          stroke: color,
          strokeWidth: isVote ? 2 : style.strokeWidth || 2,
          strokeDasharray: isVote ? '5 5' : style.strokeDasharray,
          fill: 'none',
        }}
        className="react-flow__edge-path"
        d={edgePath}
        markerEnd={markerEnd}
      />

      {/* Animated particle */}
      <motion.circle
        r={isVote ? 3 : 4}
        fill={color}
        filter="url(#glow-cyan)"
        style={{ offsetPath: `path('${edgePath}')` }}
        animate={{ offsetDistance: ['0%', '100%'] }}
        transition={{
          repeat: Infinity,
          duration: isVote ? 1 : 1.5,
          ease: 'linear',
          repeatDelay: 0.5,
        }}
      />

      {/* Second particle for busy edges */}
      {!isVote && (
        <motion.circle
          r={3}
          fill={color}
          opacity={0.6}
          filter="url(#glow-cyan)"
          style={{ offsetPath: `path('${edgePath}')` }}
          animate={{ offsetDistance: ['0%', '100%'] }}
          transition={{
            repeat: Infinity,
            duration: 1.5,
            ease: 'linear',
            delay: 0.75,
            repeatDelay: 0.5,
          }}
        />
      )}

      {/* Message count label */}
      {edgeData?.messageCount && edgeData.messageCount > 0 && (
        <EdgeLabelRenderer>
          <div
            style={{
              position: 'absolute',
              transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
              pointerEvents: 'all',
            }}
            className="px-1.5 py-0.5 rounded bg-bg-surface/90 border border-border-subtle text-[9px] font-mono text-text-muted"
          >
            {edgeData.messageCount}
          </div>
        </EdgeLabelRenderer>
      )}
    </>
  )
}

export default memo(AnimatedEdge)
