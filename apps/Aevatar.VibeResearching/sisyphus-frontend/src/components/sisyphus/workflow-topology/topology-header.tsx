// ============================================================
//  TopologyHeader - DAG Visualization Controls
// ============================================================

import { Network, RefreshCw, FileText, Maximize2, Minimize2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Tooltip, TooltipTrigger, TooltipContent, TooltipProvider } from '@/components/ui/tooltip'

export interface TopologyHeaderProps {
  onLayout: (direction: 'TB' | 'LR') => void
  onRefresh: () => void
  onCollapse?: () => void
  onSummary?: () => void
  onFullscreenToggle?: () => void
  refreshing: boolean
  nodeCount: number
  edgeCount: number
  planCount?: number
  knowledgeCount?: number
  isFullscreen?: boolean
}

export function TopologyHeader({
  onLayout,
  onRefresh,
  onCollapse,
  onSummary,
  onFullscreenToggle,
  refreshing,
  nodeCount,
  edgeCount,
  planCount = 0,
  knowledgeCount = 0,
  isFullscreen = false,
}: TopologyHeaderProps) {
  return (
    <TooltipProvider delayDuration={200}>
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
          {/* Summary */}
          {onSummary && nodeCount > 0 && (
            <Tooltip>
              <TooltipTrigger asChild>
                <button
                  onClick={onSummary}
                  aria-label="Generate Summary"
                  className="p-1.5 rounded-md border border-border-subtle text-text-muted hover:text-neon-green hover:border-neon-green/40 transition-all"
                >
                  <FileText className="size-3.5" />
                </button>
              </TooltipTrigger>
              <TooltipContent>Generate Summary</TooltipContent>
            </Tooltip>
          )}

          {/* Fullscreen Toggle */}
          {onFullscreenToggle && (
            <Tooltip>
              <TooltipTrigger asChild>
                <button
                  onClick={onFullscreenToggle}
                  aria-label={isFullscreen ? "Exit fullscreen" : "Enter fullscreen"}
                  className={cn(
                    "p-1.5 rounded-md border transition-all",
                    isFullscreen
                      ? "border-neon-cyan/40 bg-neon-cyan/10 text-neon-cyan hover:bg-neon-cyan/20"
                      : "border-border-subtle text-text-muted hover:text-neon-cyan hover:border-neon-cyan/40"
                  )}
                >
                  {isFullscreen ? <Minimize2 className="size-3.5" /> : <Maximize2 className="size-3.5" />}
                </button>
              </TooltipTrigger>
              <TooltipContent>{isFullscreen ? "Exit fullscreen" : "Fullscreen"}</TooltipContent>
            </Tooltip>
          )}

          {/* Collapse */}
          {!isFullscreen && onCollapse && (
            <Tooltip>
              <TooltipTrigger asChild>
                <button
                  onClick={onCollapse}
                  aria-label="Collapse panel"
                  className="p-1.5 rounded-md border border-border-subtle text-text-muted hover:text-neon-gold hover:border-neon-gold/40 transition-all"
                >
                  <svg className="size-3.5" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
                  </svg>
                </button>
              </TooltipTrigger>
              <TooltipContent>Collapse panel</TooltipContent>
            </Tooltip>
          )}

          {/* Refresh */}
          <Tooltip>
            <TooltipTrigger asChild>
              <button
                onClick={onRefresh}
                disabled={refreshing}
                aria-label="Refresh"
                className={cn(
                  "p-1.5 rounded-md border border-border-subtle text-text-muted hover:text-neon-cyan hover:border-neon-cyan/40 transition-all disabled:opacity-50",
                  refreshing && "animate-spin"
                )}
              >
                <RefreshCw className="size-3.5" />
              </button>
            </TooltipTrigger>
            <TooltipContent>Refresh</TooltipContent>
          </Tooltip>

          {/* Layout Buttons */}
          <div className="flex items-center gap-1 p-1 rounded-lg bg-bg-elevated border border-border-subtle">
            <Tooltip>
              <TooltipTrigger asChild>
                <button
                  onClick={() => onLayout('TB')}
                  aria-label="Vertical layout"
                  className="p-1.5 rounded-md bg-neon-cyan/10 border border-neon-cyan/40 text-neon-cyan hover:bg-neon-cyan/20 hover:border-neon-cyan/60 active:scale-95 transition-all"
                >
                  <svg className="size-3.5" fill="none" stroke="currentColor" strokeWidth={2.5} viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m0 0l-4-4m4 4l4-4" />
                  </svg>
                </button>
              </TooltipTrigger>
              <TooltipContent>Vertical layout</TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger asChild>
                <button
                  onClick={() => onLayout('LR')}
                  aria-label="Horizontal layout"
                  className="p-1.5 rounded-md bg-neon-gold/10 border border-neon-gold/40 text-neon-gold hover:bg-neon-gold/20 hover:border-neon-gold/60 active:scale-95 transition-all"
                >
                  <svg className="size-3.5" fill="none" stroke="currentColor" strokeWidth={2.5} viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M4 12h16m0 0l-4-4m4 4l-4 4" />
                  </svg>
                </button>
              </TooltipTrigger>
              <TooltipContent>Horizontal layout</TooltipContent>
            </Tooltip>
          </div>
        </div>
      </div>
    </TooltipProvider>
  )
}
