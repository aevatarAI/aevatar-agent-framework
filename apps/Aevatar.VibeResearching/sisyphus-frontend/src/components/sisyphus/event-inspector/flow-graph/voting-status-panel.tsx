// ============================================================
//  Voting Status Panel - Maker Consensus Visualization
//
//  Maker Voting Mechanism:
//  1. Each Worker generates a Proposal independently
//  2. Proposals are clustered by hash/semantic similarity
//  3. Each cluster's size = "votes" for that answer
//  4. Winner needs K votes ahead of runner-up for consensus
//
//  Backend provides: round, maxRounds, k, mode, clusterCount,
//                    consensusReached, winner
//  Backend does NOT provide: workers[]
// ============================================================

import React from 'react'
import { motion } from 'framer-motion'
import { Check, Flag, Layers, AlertTriangle, FileText, Trophy } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { VotingStatus } from '@/types'

interface VotingStatusPanelProps {
  status: VotingStatus
  isInferred?: boolean
  className?: string
}

const VotingStatusPanel: React.FC<VotingStatusPanelProps> = ({ 
  status, 
  isInferred = false,
  className 
}) => {
  const hasWorkers = status.workers.length > 0

  // Calculate margin for K-ahead consensus
  const winnerVotes = status.winner?.votes ?? 0
  const runnerUpVotes = status.winner?.runnerUpVotes ?? 0
  const margin = winnerVotes - runnerUpVotes
  const hasMarginData = winnerVotes > 0

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
          <Layers className="w-3.5 h-3.5" />
          MAKER CONSENSUS
          {isInferred && (
            <span className="ml-auto flex items-center gap-1 text-[9px] text-amber-500/80 font-normal">
              <AlertTriangle className="w-3 h-3" />
              Inferred
            </span>
          )}
        </h3>
      </div>

      {/* Voting Parameters */}
      <div className="px-4 py-3 space-y-2 text-xs font-mono">
        {/* Round */}
        <div className="flex items-center justify-between">
          <span className="text-text-muted">Round</span>
          <span className="text-text-primary">
            {status.round} / {status.maxRounds}
          </span>
        </div>

        {/* Mode */}
        <div className="flex items-center justify-between">
          <span className="text-text-muted">Clustering</span>
          <span className={cn(
            "px-1.5 py-0.5 rounded text-[10px]",
            status.mode === 'semantic'
              ? "bg-neon-cyan/20 text-neon-cyan"
              : "bg-text-muted/20 text-text-muted"
          )}>
            {status.mode === 'semantic' ? 'Semantic' : 'Hash'}
          </span>
        </div>

        {/* K Value - explained */}
        <div className="flex items-center justify-between">
          <span className="text-text-muted">K (margin needed)</span>
          <span className="text-text-primary">{status.k}</span>
        </div>

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

        {/* Red Flags */}
        {(status.redFlagCount ?? 0) > 0 && (
          <div className="flex items-center justify-between">
            <span className="text-text-muted flex items-center gap-1">
              <Flag className="w-3 h-3 text-red-500" />
              Red Flags
            </span>
            <span className="text-red-500">{status.redFlagCount}</span>
          </div>
        )}
      </div>

      {/* Cluster Voting Results - The REAL voting mechanism */}
      {hasMarginData && (
        <>
          <div className="h-px bg-border-subtle mx-4" />
          <div className="px-4 py-3 space-y-2">
            <div className="flex items-center gap-1.5 text-[9px] text-text-muted mb-2">
              <Trophy className="w-3 h-3 text-neon-green" />
              <span>CLUSTER VOTES</span>
              <span className="opacity-50">(proposals grouped)</span>
            </div>

            {/* Winner cluster */}
            <div className="flex items-center gap-2">
              <div className="w-2 h-2 rounded-full bg-neon-green flex-shrink-0" />
              <span className="text-[10px] font-mono text-text-muted flex-1">Leader</span>
              <div className="w-20 h-1.5 bg-bg-elevated rounded-full overflow-hidden">
                <motion.div
                  className="h-full rounded-full bg-neon-green"
                  initial={{ width: 0 }}
                  animate={{ width: `${(winnerVotes / Math.max(winnerVotes, runnerUpVotes)) * 100}%` }}
                  transition={{ duration: 0.5 }}
                />
              </div>
              <span className="text-[10px] font-mono text-neon-green font-bold w-6 text-right">
                {winnerVotes}
              </span>
            </div>

            {/* Runner-up cluster */}
            <div className="flex items-center gap-2">
              <div className="w-2 h-2 rounded-full bg-text-muted/50 flex-shrink-0" />
              <span className="text-[10px] font-mono text-text-muted flex-1">Runner-up</span>
              <div className="w-20 h-1.5 bg-bg-elevated rounded-full overflow-hidden">
                <motion.div
                  className="h-full rounded-full bg-text-muted/50"
                  initial={{ width: 0 }}
                  animate={{ width: `${(runnerUpVotes / Math.max(winnerVotes, runnerUpVotes)) * 100}%` }}
                  transition={{ duration: 0.5, delay: 0.1 }}
                />
              </div>
              <span className="text-[10px] font-mono text-text-muted w-6 text-right">
                {runnerUpVotes}
              </span>
            </div>

            {/* Margin indicator */}
            <div className="pt-1 mt-1 border-t border-border-subtle/50">
              <div className="flex items-center justify-between text-[10px] font-mono">
                <span className="text-text-muted">Margin vs K={status.k}</span>
                <span className={cn(
                  "font-bold",
                  margin >= status.k ? "text-neon-green" : "text-amber-500"
                )}>
                  {margin >= 0 ? '+' : ''}{margin} {margin >= status.k ? '✓' : ''}
                </span>
              </div>
            </div>
          </div>
        </>
      )}

      {/* Workers - shown as "who submitted proposals" */}
      {hasWorkers && (
        <>
          <div className="h-px bg-border-subtle mx-4" />
          <div className="px-4 py-3 space-y-2">
            <div className="flex items-center gap-1.5 text-[9px] text-text-muted mb-2">
              <FileText className="w-3 h-3" />
              <span>PROPOSALS</span>
              <span className="opacity-50">(by worker)</span>
            </div>

            {status.workers.map((worker, _index) => (
              <div key={worker.id} className="flex items-center gap-2">
                <div
                  className="w-2 h-2 rounded-full flex-shrink-0"
                  style={{ backgroundColor: worker.color }}
                />
                <span className="text-[10px] font-mono text-text-muted flex-1 truncate">
                  {worker.name}
                </span>
                <span className={cn(
                  "text-[10px] font-mono",
                  worker.isLeader ? "text-neon-green font-bold" : "text-text-muted"
                )}>
                  {worker.isLeader ? '★' : ''}
                </span>
              </div>
            ))}
          </div>
        </>
      )}

      {/* No workers fallback */}
      {!hasWorkers && !hasMarginData && (
        <>
          <div className="h-px bg-border-subtle mx-4" />
          <div className="px-4 py-3 text-[10px] text-text-muted font-mono text-center">
            Waiting for proposals...
          </div>
        </>
      )}

      {/* Consensus Status */}
      <div className="h-px bg-border-subtle mx-4" />
      <div className="px-4 py-2">
        <div className={cn(
          "flex items-center gap-2 text-xs font-mono",
          status.consensusReached ? "text-neon-green" : "text-text-muted"
        )}>
          {status.consensusReached ? (
            <>
              <Check className="w-3.5 h-3.5" />
              <span>Consensus Reached</span>
            </>
          ) : (
            <span>Voting in progress...</span>
          )}
        </div>
      </div>

      {/* Winner Info */}
      {status.winner && status.consensusReached && (
        <div className="px-4 py-2 border-t border-border-subtle bg-neon-green/5">
          <div className="text-[10px] font-mono">
            <div className="flex items-center justify-between">
              <span className="text-text-muted">Winner Proposal</span>
              <span className="text-neon-green truncate max-w-[100px]" title={status.winner.proposalId}>
                {status.winner.proposalId?.split(':').pop() || status.winner.proposalId}
              </span>
            </div>
          </div>
        </div>
      )}
    </motion.div>
  )
}

export default VotingStatusPanel
