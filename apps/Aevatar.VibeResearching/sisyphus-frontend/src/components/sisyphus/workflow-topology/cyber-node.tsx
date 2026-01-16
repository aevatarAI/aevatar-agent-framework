// ============================================================
//  CyberNode - Custom DAG Node Component
// ============================================================

import { useState } from 'react'
import { Handle, Position } from '@xyflow/react'
import { cn } from '@/lib/utils'
import { NODE_STYLES, STATUS_OPACITY, type CyberNodeData } from './dag-node-styles'

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
          "relative flex items-center justify-center rounded-full transition-all duration-300 hover:scale-110 cursor-pointer",
          data.selected && "ring-2 ring-neon-gold ring-offset-2 ring-offset-bg-base",
          data.highlighted && !data.selected && "ring-2 ring-white/50 ring-offset-1 ring-offset-bg-base",
          isPulsing && "animate-glow-pulse"
        )}
        style={{
          width: 48,
          height: 48,
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
        {/* Kind indicator icon */}
        {data.kind === 'Plan' && (
          <span className="text-[10px]" style={{ color: '#0a0f19' }}>📋</span>
        )}
        {data.kind === 'Knowledge' && (
          <span className="text-[10px]" style={{ color: '#0a0f19' }}>💡</span>
        )}
        {!data.kind && (
          <span
            className="font-mono font-extrabold text-[10px] tracking-wider"
            style={{ color: '#0a0f19', textShadow: `0 0 2px ${nodeStyle.bg}` }}
          >
            {data.id.slice(0, 4)}
          </span>
        )}
      </div>

      {/* Hover Tooltip */}
      {showTooltip && (
        <div
          className="absolute z-50 pointer-events-none"
          style={{ left: '50%', bottom: '100%', transform: 'translateX(-50%)', marginBottom: 10 }}
        >
          <div
            className="px-4 py-3 rounded-lg text-xs font-mono whitespace-normal break-words"
            style={{
              width: '280px',
              background: 'rgba(10, 15, 25, 0.98)',
              border: `2px solid ${nodeStyle.border}`,
              boxShadow: `0 0 30px ${nodeStyle.glow}, 0 4px 20px rgba(0,0,0,0.5)`,
            }}
          >
            <div className="flex items-center gap-2 mb-2 pb-2 border-b border-slate-600/50">
              <span className="font-bold" style={{ color: nodeStyle.bg }}>{data.id}</span>
              {data.kind && (
                <span className={cn(
                  "text-[9px] px-1.5 py-0.5 rounded",
                  data.kind === 'Plan' ? "bg-blue-500/20 text-blue-400" : "bg-green-500/20 text-green-400"
                )}>
                  {data.kind}
                </span>
              )}
              {data.isOtherSession && (
                <span className="text-[9px] px-1.5 py-0.5 rounded bg-gray-500/20 text-gray-400">
                  Other Session
                </span>
              )}
              {data.planStatus && (
                <span className={cn(
                  "text-[9px] px-1.5 py-0.5 rounded",
                  data.planStatus === 'Pending' && "bg-yellow-500/20 text-yellow-400",
                  data.planStatus === 'Active' && "bg-blue-500/20 text-blue-400",
                  data.planStatus === 'Completed' && "bg-green-500/20 text-green-400"
                )}>
                  {data.planStatus}
                </span>
              )}
            </div>
            <div className="text-slate-200 leading-relaxed text-[11px]">{data.label}</div>
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
