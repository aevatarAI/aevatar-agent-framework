// ============================================================
//  Worker Node - Maker worker (generates Proposals)
//  Shows leader status with star badge (no vote counts)
//  Includes summary preview and click-to-detail
// ============================================================

import React, { memo } from 'react'
import { Handle, Position } from '@xyflow/react'
import type { NodeProps } from '@xyflow/react'
import { motion } from 'framer-motion'
import { Star, User, FileText, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'

interface WorkerNodeData {
  label: string
  votes: number
  isLeader: boolean
  color: string
  status: 'idle' | 'running' | 'completed'
  detailMode?: boolean
  isInferred?: boolean
  // New fields for content preview
  summary?: string
  hasContent?: boolean
  onViewDetail?: () => void
}

const WorkerNode: React.FC<NodeProps> = ({ data, selected }) => {
  const nodeData = data as unknown as WorkerNodeData
  const { label, isLeader, color, status, summary, hasContent, onViewDetail } = nodeData
  const detailMode = nodeData.detailMode !== false

  return (
    <div className="relative">
      {/* Leader rotating ring - only in detail mode */}
      {isLeader && detailMode && (
        <motion.div
          className="absolute -inset-3 rounded-full border-2 border-neon-green/30"
          animate={{ rotate: 360 }}
          transition={{ repeat: Infinity, duration: 8, ease: 'linear' }}
        />
      )}

      {/* Glow effect for leader */}
      {isLeader && detailMode && (
        <motion.div
          className="absolute -inset-2 rounded-xl bg-neon-green/20 blur-lg"
          animate={{ opacity: [0.4, 0.7, 0.4] }}
          transition={{ repeat: Infinity, duration: 2 }}
        />
      )}

      {/* Main node */}
      <motion.div
        className={cn(
          "relative rounded-xl border-2 flex flex-col",
          "bg-gradient-to-br from-bg-surface to-bg-elevated",
          detailMode ? "w-36 min-h-[60px] p-2" : "w-8 h-8 rounded-full items-center justify-center",
          selected && "shadow-[0_0_20px_rgba(0,240,255,0.5)]",
          isLeader
            ? "border-neon-green shadow-[0_0_15px_rgba(34,197,94,0.4)]"
            : "border-opacity-50",
          hasContent && "cursor-pointer"
        )}
        style={{ borderColor: isLeader ? '#22c55e' : color }}
        whileHover={{
          scale: 1.03,
          boxShadow: `0 0 20px ${color}80`,
        }}
        transition={{ duration: 0.2 }}
        onClick={() => hasContent && onViewDetail?.()}
      >
        {detailMode ? (
          <>
            {/* Header: Label + Status */}
            <div className="flex items-center justify-between w-full">
              <span
                className="text-[10px] font-mono font-semibold tracking-wider"
                style={{ color }}
              >
                {label.toUpperCase()}
              </span>
              <span className={cn(
                "text-[8px] px-1 py-0.5 rounded font-mono",
                status === 'completed' ? "bg-neon-green/20 text-neon-green" :
                status === 'running' ? "bg-neon-cyan/20 text-neon-cyan" :
                "bg-text-muted/20 text-text-muted"
              )}>
                {status}
              </span>
            </div>

            {/* Summary preview */}
            {summary && (
              <p className="text-[8px] text-text-muted font-mono mt-1 line-clamp-2 leading-tight">
                {summary}
              </p>
            )}

            {/* View detail hint */}
            {hasContent && (
              <div className="flex items-center justify-end mt-1 text-[8px] text-text-muted opacity-60 hover:opacity-100 transition-opacity">
                <span>detail</span>
                <ChevronRight className="w-2.5 h-2.5" />
              </div>
            )}
          </>
        ) : (
          <User className="w-4 h-4" style={{ color }} />
        )}
      </motion.div>

      {/* Leader badge only - no vote counts (shown in Consensus node) */}
      {isLeader && (
        <motion.div
          className={cn(
            "absolute flex items-center justify-center",
            "border-2 border-bg-base font-mono font-bold",
            "bg-neon-green text-bg-base",
            detailMode 
              ? "-top-2 -right-2 w-6 h-6 rounded-full"
              : "-top-1 -right-1 w-4 h-4 rounded-full"
          )}
          initial={{ scale: 0 }}
          animate={{ scale: 1 }}
          transition={{ type: 'spring', stiffness: 300 }}
        >
          <Star className={cn(detailMode ? "w-3 h-3" : "w-2 h-2")} fill="currentColor" />
        </motion.div>
      )}

      {/* Proposal indicator for non-leaders - solid color background */}
      {!isLeader && detailMode && (
        <motion.div
          className="absolute -top-1.5 -right-1.5 w-5 h-5 rounded-full flex items-center justify-center
                     border-2 border-bg-base"
          style={{ backgroundColor: color }}
          initial={{ scale: 0 }}
          animate={{ scale: 1 }}
          transition={{ type: 'spring', stiffness: 300, delay: 0.1 }}
        >
          <FileText className="w-2.5 h-2.5 text-bg-base" />
        </motion.div>
      )}

      {/* Handles */}
      <Handle
        type="target"
        position={Position.Top}
        className={cn(
          "!border-2 !border-bg-base",
          detailMode ? "!w-2.5 !h-2.5" : "!w-1.5 !h-1.5"
        )}
        style={{ backgroundColor: color }}
      />
      <Handle
        type="source"
        position={Position.Bottom}
        className={cn(
          "!border-2 !border-bg-base",
          detailMode ? "!w-2.5 !h-2.5" : "!w-1.5 !h-1.5"
        )}
        style={{ backgroundColor: color }}
      />
      <Handle
        type="source"
        position={Position.Left}
        id="tool-out"
        className={cn(
          "!bg-neon-rose !border-2 !border-bg-base",
          detailMode ? "!w-2 !h-2" : "!w-1.5 !h-1.5"
        )}
      />
    </div>
  )
}

export default memo(WorkerNode)
