// ============================================================
//  Static Edge - Clean edge without continuous animation
//  Event-driven animation triggers when events occur
// ============================================================

import React, { memo, useState, useEffect, useRef } from 'react'
import { getBezierPath, EdgeLabelRenderer } from '@xyflow/react'
import type { EdgeProps } from '@xyflow/react'

interface AnimatedEdgeData {
  color?: string
  isVote?: boolean
  messageCount?: number
  detailMode?: boolean
  // Event-driven animation
  eventTrigger?: number  // Increment to trigger animation
  eventLabel?: string    // Label to show in event box
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
  const detailMode = edgeData?.detailMode !== false
  const eventTrigger = edgeData?.eventTrigger || 0
  // Reserved for future event label display
  const _eventLabel = edgeData?.eventLabel || ''
  void _eventLabel

  // Track event animations
  const [activeEvents, setActiveEvents] = useState<{ id: number; progress: number }[]>([])
  const prevTriggerRef = useRef(eventTrigger)
  const eventIdRef = useRef(0)

  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
  })

  // Trigger event animation when eventTrigger changes
  useEffect(() => {
    if (eventTrigger > prevTriggerRef.current) {
      const newEventId = ++eventIdRef.current
      setActiveEvents(prev => [...prev, { id: newEventId, progress: 0 }])
      
      // Remove event after animation completes
      setTimeout(() => {
        setActiveEvents(prev => prev.filter(e => e.id !== newEventId))
      }, 1500)
    }
    prevTriggerRef.current = eventTrigger
  }, [eventTrigger])

  return (
    <>
      {/* Base edge path - static, no animation */}
      <path
        id={id}
        style={{
          ...style,
          stroke: color,
          strokeWidth: detailMode ? 1.5 : 1,
          strokeDasharray: isVote ? '5 5' : (style.strokeDasharray as string),
          strokeOpacity: 0.6,
          fill: 'none',
        }}
        className="react-flow__edge-path"
        d={edgePath}
        markerEnd={markerEnd}
      />

      {/* Subtle glow for better visibility */}
      {detailMode && (
        <path
          style={{
            stroke: color,
            strokeWidth: 4,
            strokeOpacity: 0.1,
            fill: 'none',
          }}
          d={edgePath}
        />
      )}

      {/* Event box animations - triggered by events */}
      {activeEvents.map(event => (
        <g key={event.id}>
          {/* Animated event box traveling along the path - same style as Agent Flow Graph */}
          <rect
            x="-16"
            y="-10"
            width={detailMode ? 32 : 24}
            height={detailMode ? 20 : 14}
            rx={4}
            fill="rgba(34, 211, 238, 0.95)"
            stroke="#fff"
            strokeWidth={detailMode ? 1.5 : 1}
            filter={detailMode ? "url(#glow-cyan)" : undefined}
          >
            <animateMotion
              dur="1.2s"
              fill="freeze"
              path={edgePath}
            >
              <mpath href={`#${id}`} />
            </animateMotion>
            <animate
              attributeName="opacity"
              values="0;1;1;0"
              dur="1.2s"
              fill="freeze"
            />
          </rect>
          {/* 📦 Emoji centered in the box */}
          <text
            fontSize={detailMode ? 11 : 8}
            fill="#0A0F1C"
            textAnchor="middle"
            dominantBaseline="central"
            fontFamily="monospace"
            fontWeight="600"
          >
            <animateMotion
              dur="1.2s"
              fill="freeze"
              path={edgePath}
            >
              <mpath href={`#${id}`} />
            </animateMotion>
            <animate
              attributeName="opacity"
              values="0;1;1;0"
              dur="1.2s"
              fill="freeze"
            />
            📦
          </text>
        </g>
      ))}

      {/* Message count label - only in detail mode */}
      {detailMode && edgeData?.messageCount && edgeData.messageCount > 0 && (
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
