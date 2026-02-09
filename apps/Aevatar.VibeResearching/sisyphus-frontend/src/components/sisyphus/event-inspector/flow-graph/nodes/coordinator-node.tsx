// ============================================================
//  Coordinator Node - Central orchestration node with pulse
//  Supports detailMode for simplified/detailed display
// ============================================================

import React, { memo } from 'react'
import { Handle, Position } from '@xyflow/react'
import type { NodeProps } from '@xyflow/react'
import { motion } from 'framer-motion'
import { Cpu } from 'lucide-react'
import { cn } from '@/lib/utils'

interface CoordinatorNodeData {
  label: string
  status: 'idle' | 'running' | 'completed'
  messageCount?: number      // Coordinator events count
  totalEventCount?: number   // Total events count
  detailMode?: boolean
  onClick?: () => void
}

const CoordinatorNode: React.FC<NodeProps> = ({ data, selected }) => {
  const nodeData = data as unknown as CoordinatorNodeData
  const isRunning = nodeData.status === 'running'
  const detailMode = nodeData.detailMode !== false // Default to true
  const hasEvents = (nodeData.messageCount ?? 0) > 0

  return (
    <div className="relative">
      {/* Pulse glow effect - only in detail mode */}
      {detailMode && (
        <motion.div
          className="absolute -inset-3 rounded-2xl bg-neon-cyan/20 blur-xl"
          animate={{
            opacity: isRunning ? [0.3, 0.6, 0.3] : 0.2,
            scale: isRunning ? [1, 1.05, 1] : 1,
          }}
          transition={{
            repeat: Infinity,
            duration: 2,
            ease: 'easeInOut',
          }}
        />
      )}

      {/* Main node */}
      <motion.div
        className={cn(
          "relative rounded-xl border-2 flex flex-col items-center justify-center",
          "bg-gradient-to-br from-bg-surface to-bg-elevated",
          // Size based on detail mode
          detailMode ? "w-32 h-16" : "w-10 h-10 rounded-full",
          selected
            ? "border-neon-cyan shadow-[0_0_20px_rgba(0,240,255,0.5)]"
            : "border-neon-cyan/50",
          isRunning && "shadow-[0_0_15px_rgba(0,240,255,0.3)]",
          // Clickable cursor when has events
          hasEvents && "cursor-pointer"
        )}
        whileHover={{
          scale: 1.05,
          boxShadow: '0 0 25px rgba(0,240,255,0.5)',
        }}
        transition={{ duration: 0.2 }}
      >
        {detailMode ? (
          <>
            {/* Icon + Label */}
            <div className="flex items-center gap-2 mb-1">
              <Cpu className="w-4 h-4 text-neon-cyan" />
              <span className="text-xs font-mono font-semibold text-neon-cyan tracking-wider">
                {nodeData.label.toUpperCase()}
              </span>
            </div>

            {/* Event count - show coordinator events sent */}
            {nodeData.messageCount !== undefined && nodeData.messageCount > 0 && (
              <div className="text-[9px] font-mono text-text-muted">
                {nodeData.messageCount} events
              </div>
            )}
          </>
        ) : (
          // Compact: just icon
          <Cpu className="w-5 h-5 text-neon-cyan" />
        )}

        {/* Status indicator */}
        <motion.div
          className={cn(
            "absolute w-3 h-3 rounded-full",
            detailMode ? "-top-1 -right-1" : "-top-0.5 -right-0.5 w-2 h-2",
            nodeData.status === 'running' && "bg-neon-cyan",
            nodeData.status === 'completed' && "bg-neon-green",
            nodeData.status === 'idle' && "bg-text-muted"
          )}
          animate={isRunning ? {
            scale: [1, 1.2, 1],
            opacity: [1, 0.7, 1],
          } : {}}
          transition={{ repeat: Infinity, duration: 1 }}
        />
      </motion.div>

      {/* Handles */}
      <Handle
        type="source"
        position={Position.Bottom}
        className={cn(
          "!border-2 !border-bg-base !bg-neon-cyan",
          detailMode ? "!w-3 !h-3" : "!w-2 !h-2"
        )}
      />
    </div>
  )
}

export default memo(CoordinatorNode)
