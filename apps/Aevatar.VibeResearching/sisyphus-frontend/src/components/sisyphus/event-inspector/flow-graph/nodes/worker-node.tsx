// ============================================================
//  Worker Node - Maker worker with vote badge
// ============================================================

import React, { memo } from 'react'
import { Handle, Position } from '@xyflow/react'
import type { NodeProps } from '@xyflow/react'
import { motion } from 'framer-motion'
import { Vote, Star } from 'lucide-react'
import { cn } from '@/lib/utils'

interface WorkerNodeData {
  label: string
  votes: number
  isLeader: boolean
  color: string
  status: 'idle' | 'running' | 'completed'
}

const WorkerNode: React.FC<NodeProps> = ({ data, selected }) => {
  const nodeData = data as unknown as WorkerNodeData
  const { label, votes, isLeader, color, status } = nodeData

  return (
    <div className="relative">
      {/* Leader rotating ring */}
      {isLeader && (
        <motion.div
          className="absolute -inset-3 rounded-full border-2 border-neon-green/30"
          animate={{ rotate: 360 }}
          transition={{ repeat: Infinity, duration: 8, ease: 'linear' }}
        />
      )}

      {/* Glow effect for leader */}
      {isLeader && (
        <motion.div
          className="absolute -inset-2 rounded-xl bg-neon-green/20 blur-lg"
          animate={{ opacity: [0.4, 0.7, 0.4] }}
          transition={{ repeat: Infinity, duration: 2 }}
        />
      )}

      {/* Main node */}
      <motion.div
        className={cn(
          "relative w-28 h-14 rounded-xl border-2 flex flex-col items-center justify-center",
          "bg-gradient-to-br from-bg-surface to-bg-elevated",
          selected
            ? "shadow-[0_0_20px_rgba(0,240,255,0.5)]"
            : "",
          isLeader
            ? "border-neon-green shadow-[0_0_15px_rgba(34,197,94,0.4)]"
            : "border-opacity-50"
        )}
        style={{ borderColor: isLeader ? '#22c55e' : color }}
        whileHover={{
          scale: 1.05,
          boxShadow: `0 0 20px ${color}80`,
        }}
        transition={{ duration: 0.2 }}
      >
        {/* Label */}
        <span
          className="text-[10px] font-mono font-semibold tracking-wider"
          style={{ color }}
        >
          {label.toUpperCase()}
        </span>

        {/* Status */}
        <span className="text-[9px] text-text-muted font-mono">
          {status}
        </span>
      </motion.div>

      {/* Vote Badge */}
      <motion.div
        className={cn(
          "absolute -top-2 -right-2 min-w-[24px] h-6 px-1.5 rounded-full flex items-center justify-center gap-0.5",
          "border-2 border-bg-base text-[10px] font-mono font-bold",
          isLeader
            ? "bg-neon-green text-bg-base"
            : "bg-bg-elevated text-text-primary"
        )}
        style={!isLeader ? { borderColor: color, color } : undefined}
        initial={{ scale: 0 }}
        animate={{ scale: 1 }}
        transition={{ type: 'spring', stiffness: 300 }}
      >
        {isLeader ? (
          <Star className="w-3 h-3" fill="currentColor" />
        ) : (
          <Vote className="w-3 h-3" />
        )}
        <span>{votes}</span>
      </motion.div>

      {/* Handles */}
      <Handle
        type="target"
        position={Position.Top}
        className="!w-2.5 !h-2.5 !border-2 !border-bg-base"
        style={{ backgroundColor: color }}
      />
      <Handle
        type="source"
        position={Position.Bottom}
        className="!w-2.5 !h-2.5 !border-2 !border-bg-base"
        style={{ backgroundColor: color }}
      />
      <Handle
        type="source"
        position={Position.Left}
        id="vote-out"
        className="!w-2 !h-2 !bg-neon-green !border-2 !border-bg-base"
      />
      <Handle
        type="target"
        position={Position.Right}
        id="vote-in"
        className="!w-2 !h-2 !bg-neon-green !border-2 !border-bg-base"
      />
    </div>
  )
}

export default memo(WorkerNode)
