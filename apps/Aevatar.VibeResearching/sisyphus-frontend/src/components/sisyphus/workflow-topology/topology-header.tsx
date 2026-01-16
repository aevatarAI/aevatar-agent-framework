// ============================================================
//  TopologyHeader - DAG Visualization Controls
// ============================================================

import { Network, RefreshCw, FileText, Crosshair, Maximize2, Minimize2 } from 'lucide-react'
import { cn } from '@/lib/utils'

export interface TopologyHeaderProps {
  onLayout: (direction: 'TB' | 'LR') => void
  onRefresh: () => void
  onCollapse?: () => void
  onSummary?: () => void
  onFocusActive?: () => void
  onFullscreenToggle?: () => void
  refreshing: boolean
  nodeCount: number
  edgeCount: number
  planCount?: number
  knowledgeCount?: number
  activeMilestone?: string | null
  isFullscreen?: boolean
}

export function TopologyHeader({
  onLayout,
  onRefresh,
  onCollapse,
  onSummary,
  onFocusActive,
  onFullscreenToggle,
  refreshing,
  nodeCount,
  edgeCount,
  planCount = 0,
  knowledgeCount = 0,
  activeMilestone,
  isFullscreen = false,
}: TopologyHeaderProps) {
  return (
    <div className="flex-shrink-0 flex items-center justify-between p-4 border-b border-accent-emerald/30 bg-gradient-to-r from-accent-emerald/10 to-transparent">
      <div className="flex items-center gap-3">
        <div className="relative">
          <div className="absolute inset-0 rounded-xl bg-accent-emerald blur-lg opacity-40 animate-pulse" />
          <div className="relative flex h-11 w-11 items-center justify-center rounded-xl bg-gradient-to-br from-accent-emerald/20 to-accent-emerald/5 border-2 border-accent-emerald/50">
            <Network className="h-5 w-5 text-accent-emerald" />
          </div>
        </div>
        <div>
          <h3 className="font-mono text-sm font-bold tracking-wider text-accent-emerald text-balance">
            TOPOLOGY MAP
          </h3>
          <p className="text-[10px] text-text-muted font-mono tracking-wide text-pretty">
            {nodeCount > 0 ? (
              <span>
                n={nodeCount} e={edgeCount}
                {(planCount > 0 || knowledgeCount > 0) && (
                  <span className="ml-2">
                    (<span className="text-blue-400">P:{planCount}</span>{' '}
                    <span className="text-green-400">K:{knowledgeCount}</span>)
                  </span>
                )}
              </span>
            ) : 'Workflow Dependency Graph'}
          </p>
        </div>
      </div>

      <div className="flex items-center gap-2">
        {/* Focus Active Node */}
        {activeMilestone && onFocusActive && (
          <button
            onClick={onFocusActive}
            aria-label="Focus on active node"
            className="flex items-center gap-1.5 px-2.5 py-1.5 text-[10px] font-mono rounded-md border border-orange-400/40 bg-orange-400/10 text-orange-400 hover:bg-orange-400/20 hover:border-orange-400/60 transition-all"
            title="Focus on active milestone"
          >
            <Crosshair className="size-3" />
            <span>Focus</span>
          </button>
        )}

        {/* Summary */}
        {onSummary && nodeCount > 0 && (
          <button
            onClick={onSummary}
            aria-label="Generate Summary"
            className="p-1.5 rounded-md border border-border-subtle text-text-muted hover:text-neon-green hover:border-neon-green/40 transition-all"
            title="Generate Summary"
          >
            <FileText className="size-3.5" />
          </button>
        )}

        {/* Fullscreen Toggle */}
        {onFullscreenToggle && (
          <button
            onClick={onFullscreenToggle}
            aria-label={isFullscreen ? "Exit fullscreen" : "Enter fullscreen"}
            className={cn(
              "p-1.5 rounded-md border transition-all",
              isFullscreen
                ? "border-neon-cyan/40 bg-neon-cyan/10 text-neon-cyan hover:bg-neon-cyan/20"
                : "border-border-subtle text-text-muted hover:text-neon-cyan hover:border-neon-cyan/40"
            )}
            title={isFullscreen ? "Exit fullscreen" : "Fullscreen"}
          >
            {isFullscreen ? <Minimize2 className="size-3.5" /> : <Maximize2 className="size-3.5" />}
          </button>
        )}

        {/* Collapse */}
        {!isFullscreen && onCollapse && (
          <button
            onClick={onCollapse}
            aria-label="Collapse DAG panel"
            className="p-1.5 rounded-md border border-border-subtle text-text-muted hover:text-neon-gold hover:border-neon-gold/40 transition-all"
          >
            <svg className="size-3.5" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
            </svg>
          </button>
        )}

        {/* Refresh */}
        <button
          onClick={onRefresh}
          disabled={refreshing}
          aria-label="Refresh DAG"
          className={cn(
            "p-1.5 rounded-md border border-border-subtle text-text-muted hover:text-neon-cyan hover:border-neon-cyan/40 transition-all disabled:opacity-50",
            refreshing && "animate-spin"
          )}
        >
          <RefreshCw className="size-3.5" />
        </button>

        {/* Layout Buttons */}
        <div className="flex items-center gap-1 p-1 rounded-lg bg-bg-elevated border border-border-subtle">
          <button
            onClick={() => onLayout('TB')}
            aria-label="Layout graph vertically"
            className="group px-3 py-2 text-xs font-mono font-semibold tracking-wide rounded-md bg-neon-cyan/10 border border-neon-cyan/40 text-neon-cyan hover:bg-neon-cyan/20 hover:border-neon-cyan/60 active:scale-95 transition-all duration-200 cursor-pointer flex items-center gap-2 shadow-[0_0_12px_rgba(0,240,255,0.15)]"
          >
            <svg className="size-4" fill="none" stroke="currentColor" strokeWidth={2.5} viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m0 0l-4-4m4 4l4-4" />
            </svg>
            <span>Vertical</span>
          </button>
          <button
            onClick={() => onLayout('LR')}
            aria-label="Layout graph horizontally"
            className="group px-3 py-2 text-xs font-mono font-semibold tracking-wide rounded-md bg-neon-gold/10 border border-neon-gold/40 text-neon-gold hover:bg-neon-gold/20 hover:border-neon-gold/60 active:scale-95 transition-all duration-200 cursor-pointer flex items-center gap-2 shadow-[0_0_12px_rgba(255,215,0,0.15)]"
          >
            <svg className="size-4" fill="none" stroke="currentColor" strokeWidth={2.5} viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" d="M4 12h16m0 0l-4-4m4 4l-4 4" />
            </svg>
            <span>Horizontal</span>
          </button>
        </div>
      </div>
    </div>
  )
}
