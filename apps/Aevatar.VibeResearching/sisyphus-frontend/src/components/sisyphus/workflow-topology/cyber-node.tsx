// ============================================================
//  CyberNode - Custom DAG Node Component
// ============================================================

import { useState } from 'react'
import { Handle, Position } from '@xyflow/react'
import { cn } from '@/lib/utils'
import { NODE_STYLES, STATUS_OPACITY, type CyberNodeData } from './dag-node-styles'

// ─── Extract meaningful core from node ID ───
// "axiom_entropy_nonneg_v1" -> "entropy_nonneg"
function extractIdCore(id: string): string {
  const prefixes = [
    // Full prefixes
    'axiom_', 'theorem_', 'lemma_', 'plan_', 'knowledge_',
    'analysis_', 'hypothesis_', 'verification_', 'final_',
    'proof_', 'definition_', 'corollary_', 'proposition_',
    // Short prefixes (thm_, ax_, etc.)
    'thm_', 'ax_', 'lem_', 'def_', 'prop_', 'cor_',
  ]
  let core = id
  for (const prefix of prefixes) {
    if (core.startsWith(prefix)) {
      core = core.slice(prefix.length)
      break
    }
  }
  // Remove version suffix (_v1, _v2, etc.)
  core = core.replace(/_v\d+$/, '')
  // Truncate if too long (max 10 chars for display)
  if (core.length > 10) {
    core = core.slice(0, 9) + '..'
  }
  return core
}

export function CyberNode({ data }: { data: CyberNodeData }) {
  const [showTooltip, setShowTooltip] = useState(false)

  // Check if this is an active plan node (pulsing)
  const isPulsing = data.kind === 'Plan' && data.planStatus === 'Active'

  // Get style based on node kind, session ownership, and active state
  const nodeStyle = data.isOtherSession
    ? (data.kind === 'Plan' ? NODE_STYLES.PlanOther : NODE_STYLES.KnowledgeOther)
    : isPulsing
      ? NODE_STYLES.PlanActive
      : data.kind === 'Plan'
        ? NODE_STYLES.Plan
        : data.kind === 'Knowledge'
          ? NODE_STYLES.Knowledge
          : NODE_STYLES.Default

  // Calculate opacity based on various states
  let opacity = STATUS_OPACITY[data.status] || 1
  if (data.dimmed) opacity = 0.3
  if (data.highlighted || data.selected) opacity = 1

  return (
    <>
      <Handle
        type="target"
        position={Position.Top}
        className="!w-2 !h-2 !bg-transparent !border-0"
      />

      <div
        className={cn(
          "relative flex items-center justify-center rounded-full transition-all duration-300 hover:scale-105 cursor-pointer",
          data.selected && "ring-2 ring-neon-gold ring-offset-2 ring-offset-bg-base",
          data.highlighted && !data.selected && "ring-2 ring-white/50 ring-offset-1 ring-offset-bg-base",
          isPulsing && "animate-glow-pulse"
        )}
        style={{
          width: 72,
          height: 72,
          background: `radial-gradient(circle, ${nodeStyle.bg} 0%, ${nodeStyle.border} 100%)`,
          border: `2px solid ${data.selected ? '#ffd700' : nodeStyle.border}`,
          boxShadow: data.selected
            ? `0 0 24px rgba(255,215,0,0.5), inset 0 0 10px rgba(255,255,255,0.2)`
            : isPulsing
              ? `0 0 30px ${nodeStyle.glow}, 0 0 60px ${nodeStyle.glow}, inset 0 0 10px rgba(255,255,255,0.2)`
              : `0 0 20px ${nodeStyle.glow}, inset 0 0 10px rgba(255,255,255,0.2)`,
          opacity,
        }}
        onMouseEnter={() => setShowTooltip(true)}
        onMouseLeave={() => setShowTooltip(false)}
      >
        {/* ─── Inner Label: Kind + Short ID ─── */}
        <div className="flex flex-col items-center justify-center gap-0.5 select-none">
          {/* Kind row: emoji + type label */}
          <div className="flex items-center gap-0.5">
            <span className="text-xs leading-none">
              {data.kind === 'Plan' ? '📋' : data.kind === 'Knowledge' ? '💡' : '⚡'}
            </span>
            <span
              className="text-[9px] font-bold tracking-wide leading-none"
              style={{ color: '#0a0f19', textShadow: '0 0 2px rgba(255,255,255,0.3)' }}
            >
              {isPulsing ? 'Active' : data.kind === 'Plan' ? 'Plan' : data.kind === 'Knowledge' ? 'Know' : 'Node'}
            </span>
          </div>
          {/* ID row: extracted core content */}
          <span
            className="text-[10px] font-mono leading-none"
            style={{ color: '#0a0f19', opacity: 0.85 }}
          >
            {extractIdCore(data.id)}
          </span>
        </div>
      </div>

      {/* Hover Tooltip */}
      {showTooltip && (
        <div
          className="absolute z-50 pointer-events-none"
          style={{ left: '50%', bottom: '100%', transform: 'translateX(-50%)', marginBottom: 10 }}
        >
          <div
            className="px-3 py-2.5 rounded-lg text-xs font-mono"
            style={{
              minWidth: '180px',
              maxWidth: '320px',
              background: 'rgba(10, 15, 25, 0.98)',
              border: `2px solid ${nodeStyle.border}`,
              boxShadow: `0 0 30px ${nodeStyle.glow}, 0 4px 20px rgba(0,0,0,0.5)`,
            }}
          >
            {/* Row 1: Badges */}
            <div className="flex flex-wrap items-center gap-1.5 mb-2">
              {data.kind && (
                <span className={cn(
                  "text-[9px] px-1.5 py-0.5 rounded shrink-0",
                  data.kind === 'Plan' ? "bg-blue-500/20 text-blue-400" : "bg-green-500/20 text-green-400"
                )}>
                  {data.kind}
                </span>
              )}
              {data.isOtherSession && (
                <span className="text-[9px] px-1.5 py-0.5 rounded bg-gray-500/20 text-gray-400 shrink-0">
                  Other Session
                </span>
              )}
              {data.planStatus && (
                <span className={cn(
                  "text-[9px] px-1.5 py-0.5 rounded shrink-0",
                  data.planStatus === 'Pending' && "bg-yellow-500/20 text-yellow-400",
                  data.planStatus === 'Active' && "bg-blue-500/20 text-blue-400",
                  data.planStatus === 'Completed' && "bg-green-500/20 text-green-400"
                )}>
                  {data.planStatus}
                </span>
              )}
            </div>
            {/* Row 2: ID (truncated) */}
            <div
              className="font-bold truncate mb-2 pb-2 border-b border-slate-600/50"
              style={{ color: nodeStyle.bg }}
              title={data.id}
            >
              {data.id.length > 24 ? data.id.slice(0, 22) + '..' : data.id}
            </div>
            {/* Row 3: Label (max 2 lines) */}
            <div
              className="text-slate-200 leading-relaxed text-[11px] line-clamp-2"
              title={data.label}
            >
              {data.label}
            </div>
          </div>
          <div
            className="absolute left-1/2 -translate-x-1/2"
            style={{
              bottom: -8,
              width: 0,
              height: 0,
              borderLeft: '8px solid transparent',
              borderRight: '8px solid transparent',
              borderTop: `8px solid ${nodeStyle.border}`,
            }}
          />
        </div>
      )}

      <Handle
        type="source"
        position={Position.Bottom}
        className="!w-2 !h-2 !bg-transparent !border-0"
      />
    </>
  )
}

export const nodeTypes = { cyber: CyberNode }
