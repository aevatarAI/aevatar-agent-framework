// ============================================================
//  Consensus Node - Displays voting cluster results
//  Shows the actual Maker voting mechanism:
//  - Proposals are clustered by hash/semantic similarity
//  - Each cluster's size = vote count
//  - Winner needs K votes ahead of runner-up
// ============================================================

import React, { memo } from 'react'
import { Handle, Position } from '@xyflow/react'
import type { NodeProps } from '@xyflow/react'
import { motion } from 'framer-motion'
import { Vote, Check, Layers, Trophy } from 'lucide-react'
import { cn } from '@/lib/utils'

interface ConsensusNodeData {
  clusterCount: number
  winnerVotes: number
  runnerUpVotes: number
  kValue: number
  consensusReached: boolean
  mode: 'semantic' | 'hash'
  detailMode?: boolean
}

const ConsensusNode: React.FC<NodeProps> = ({ data, selected }) => {
  const nodeData = data as unknown as ConsensusNodeData
  const { 
    clusterCount, 
    winnerVotes, 
    runnerUpVotes, 
    kValue, 
    consensusReached,
    mode 
  } = nodeData
  const detailMode = nodeData.detailMode !== false

  // Calculate margin (how far ahead is the leader)
  const margin = winnerVotes - runnerUpVotes
  const isWinning = margin >= kValue

  return (
    <div className="relative">
      {/* Success glow when consensus reached */}
      {consensusReached && detailMode && (
        <motion.div
          className="absolute -inset-3 rounded-2xl bg-neon-green/20 blur-lg"
          animate={{ opacity: [0.3, 0.6, 0.3] }}
          transition={{ repeat: Infinity, duration: 2 }}
        />
      )}

      {/* Main node */}
      <motion.div
        className={cn(
          "relative rounded-2xl border-2 flex flex-col items-center justify-center",
          "bg-gradient-to-br from-bg-surface to-bg-elevated",
          detailMode ? "w-48 px-4 py-3" : "w-12 h-12 rounded-full",
          selected && "shadow-[0_0_20px_rgba(0,240,255,0.5)]",
          consensusReached
            ? "border-neon-green shadow-[0_0_15px_rgba(34,197,94,0.4)]"
            : "border-neon-cyan/50"
        )}
        whileHover={{ scale: 1.02 }}
        transition={{ duration: 0.2 }}
      >
        {detailMode ? (
          <>
            {/* Header */}
            <div className="flex items-center gap-2 mb-2">
              <Layers className="w-4 h-4 text-neon-cyan" />
              <span className="text-[11px] font-mono font-semibold text-neon-cyan tracking-wider">
                CONSENSUS
              </span>
              {consensusReached && (
                <Check className="w-4 h-4 text-neon-green" />
              )}
            </div>

            {/* Cluster info */}
            <div className="w-full space-y-1.5">
              {/* Mode */}
              <div className="flex items-center justify-between text-[10px] font-mono">
                <span className="text-text-muted">Mode</span>
                <span className={cn(
                  "px-1.5 py-0.5 rounded",
                  mode === 'semantic' 
                    ? "bg-neon-cyan/20 text-neon-cyan" 
                    : "bg-text-muted/20 text-text-muted"
                )}>
                  {mode === 'semantic' ? 'Semantic' : 'Hash'}
                </span>
              </div>

              {/* Clusters */}
              <div className="flex items-center justify-between text-[10px] font-mono">
                <span className="text-text-muted">Clusters</span>
                <span className="text-text-primary">{clusterCount}</span>
              </div>

              {/* Voting result */}
              <div className="pt-1 border-t border-border-subtle">
                <div className="flex items-center justify-between text-[10px] font-mono mb-1">
                  <span className="text-text-muted flex items-center gap-1">
                    <Trophy className="w-3 h-3 text-neon-green" />
                    Leader
                  </span>
                  <span className="text-neon-green font-bold">{winnerVotes} votes</span>
                </div>
                <div className="flex items-center justify-between text-[10px] font-mono">
                  <span className="text-text-muted">Runner-up</span>
                  <span className="text-text-muted">{runnerUpVotes} votes</span>
                </div>
              </div>

              {/* Margin indicator */}
              <div className="pt-1 border-t border-border-subtle">
                <div className="flex items-center justify-between text-[10px] font-mono">
                  <span className="text-text-muted">Margin (K={kValue})</span>
                  <span className={cn(
                    "font-bold",
                    isWinning ? "text-neon-green" : "text-amber-500"
                  )}>
                    {margin >= 0 ? '+' : ''}{margin}
                  </span>
                </div>
                {/* Progress bar showing margin vs K */}
                <div className="mt-1 h-1.5 bg-bg-elevated rounded-full overflow-hidden">
                  <motion.div
                    className={cn(
                      "h-full rounded-full",
                      isWinning ? "bg-neon-green" : "bg-amber-500"
                    )}
                    initial={{ width: 0 }}
                    animate={{ width: `${Math.min(100, (margin / kValue) * 100)}%` }}
                    transition={{ duration: 0.5 }}
                  />
                </div>
              </div>
            </div>
          </>
        ) : (
          // Compact mode
          <div className="flex items-center justify-center">
            {consensusReached ? (
              <Check className="w-5 h-5 text-neon-green" />
            ) : (
              <Vote className="w-5 h-5 text-neon-cyan" />
            )}
          </div>
        )}
      </motion.div>

      {/* Status badge */}
      {detailMode && (
        <motion.div
          className={cn(
            "absolute -top-2 -right-2 px-2 py-0.5 rounded-full text-[9px] font-mono font-bold",
            "border-2 border-bg-base",
            consensusReached
              ? "bg-neon-green text-bg-base"
              : "bg-amber-500 text-bg-base"
          )}
          initial={{ scale: 0 }}
          animate={{ scale: 1 }}
          transition={{ type: 'spring', stiffness: 300 }}
        >
          {consensusReached ? 'DONE' : 'VOTING'}
        </motion.div>
      )}

      {/* Handles */}
      <Handle
        type="target"
        position={Position.Top}
        className={cn(
          "!border-2 !border-bg-base !bg-neon-cyan",
          detailMode ? "!w-3 !h-3" : "!w-2 !h-2"
        )}
      />
    </div>
  )
}

export default memo(ConsensusNode)
