// ============================================================
//  Voting Status Panel - Enhanced voting information display
// ============================================================

import React from 'react'
import { motion } from 'framer-motion'
import { Check, Flag, Layers, Users } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { VotingStatus } from '@/types'

interface VotingStatusPanelProps {
  status: VotingStatus
  className?: string
}

const VotingStatusPanel: React.FC<VotingStatusPanelProps> = ({ status, className }) => {
  const maxVotes = Math.max(...status.workers.map(w => w.votes), 1)

  return (
    <motion.div
      initial={{ opacity: 0, x: -20 }}
      animate={{ opacity: 1, x: 0 }}
      className={cn(
        "w-64 bg-bg-surface/95 backdrop-blur-sm border border-border-subtle rounded-lg shadow-lg overflow-hidden",
        className
      )}
    >
      {/* Header */}
      <div className="px-4 py-2 border-b border-border-subtle bg-neon-cyan/5">
        <h3 className="text-xs font-mono font-semibold text-neon-cyan tracking-wider flex items-center gap-2">
          <Users className="w-3.5 h-3.5" />
          VOTING STATUS
        </h3>
      </div>

      {/* Stats */}
      <div className="px-4 py-3 space-y-2 text-xs font-mono">
        {/* Round */}
        <div className="flex items-center justify-between">
          <span className="text-text-muted">Round</span>
          <span className="text-text-primary">
            {status.round} / {status.maxRounds}
          </span>
        </div>

        {/* K Value */}
        <div className="flex items-center justify-between">
          <span className="text-text-muted">K Value</span>
          <span className="text-text-primary">{status.k}</span>
        </div>

        {/* Mode */}
        <div className="flex items-center justify-between">
          <span className="text-text-muted">Mode</span>
          <span className={cn(
            "px-1.5 py-0.5 rounded text-[10px]",
            status.mode === 'semantic'
              ? "bg-neon-cyan/20 text-neon-cyan"
              : "bg-text-muted/20 text-text-muted"
          )}>
            {status.mode === 'semantic'
              ? `Semantic ${status.similarityThreshold ? `${Math.round(status.similarityThreshold * 100)}%` : ''}`
              : 'Hash'}
          </span>
        </div>

        {/* Red Flags */}
        {status.redFlagCount > 0 && (
          <div className="flex items-center justify-between">
            <span className="text-text-muted flex items-center gap-1">
              <Flag className="w-3 h-3 text-red-500" />
              Red Flags
            </span>
            <span className="text-red-500">{status.redFlagCount}</span>
          </div>
        )}

        {/* Clusters */}
        {status.clusterCount > 0 && (
          <div className="flex items-center justify-between">
            <span className="text-text-muted flex items-center gap-1">
              <Layers className="w-3 h-3" />
              Clusters
            </span>
            <span className="text-text-primary">{status.clusterCount}</span>
          </div>
        )}
      </div>

      {/* Divider */}
      <div className="h-px bg-border-subtle mx-4" />

      {/* Workers */}
      <div className="px-4 py-3 space-y-2">
        {status.workers.map((worker, index) => (
          <div key={worker.id} className="flex items-center gap-2">
            {/* Color dot */}
            <div
              className="w-2 h-2 rounded-full flex-shrink-0"
              style={{ backgroundColor: worker.color }}
            />

            {/* Name */}
            <span className="text-[10px] font-mono text-text-muted flex-1 truncate">
              {worker.name}
            </span>

            {/* Progress bar */}
            <div className="w-16 h-1.5 bg-bg-elevated rounded-full overflow-hidden">
              <motion.div
                className="h-full rounded-full"
                style={{ backgroundColor: worker.isLeader ? '#22c55e' : worker.color }}
                initial={{ width: 0 }}
                animate={{ width: `${(worker.votes / maxVotes) * 100}%` }}
                transition={{ duration: 0.5, delay: index * 0.1 }}
              />
            </div>

            {/* Vote count */}
            <span className={cn(
              "text-[10px] font-mono w-4 text-right",
              worker.isLeader ? "text-neon-green font-bold" : "text-text-muted"
            )}>
              {worker.votes}
            </span>
          </div>
        ))}
      </div>

      {/* Divider */}
      <div className="h-px bg-border-subtle mx-4" />

      {/* Consensus Status */}
      <div className="px-4 py-2">
        <div className={cn(
          "flex items-center gap-2 text-xs font-mono",
          status.consensusReached ? "text-neon-green" : "text-text-muted"
        )}>
          {status.consensusReached ? (
            <>
              <Check className="w-3.5 h-3.5" />
              <span>Consensus Reached</span>
              {status.clusterCount > 0 && (
                <span className="text-text-muted">({status.clusterCount} clusters)</span>
              )}
            </>
          ) : (
            <span>Voting in progress...</span>
          )}
        </div>
      </div>

      {/* Winner Info */}
      {status.winner && (
        <div className="px-4 py-2 border-t border-border-subtle bg-neon-green/5">
          <div className="text-[10px] font-mono space-y-1">
            <div className="flex items-center justify-between">
              <span className="text-text-muted">Winner</span>
              <span className="text-neon-green truncate max-w-[120px]">
                {status.winner.proposalId}
              </span>
            </div>
            <div className="flex items-center justify-between">
              <span className="text-text-muted">Votes</span>
              <span className="text-text-primary">
                {status.winner.votes} vs {status.winner.runnerUpVotes}
              </span>
            </div>
          </div>
        </div>
      )}
    </motion.div>
  )
}

export default VotingStatusPanel
