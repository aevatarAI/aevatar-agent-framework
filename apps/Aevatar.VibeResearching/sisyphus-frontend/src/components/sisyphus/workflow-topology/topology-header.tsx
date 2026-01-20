// ============================================================
//  TopologyHeader - DAG Visualization Controls
// ============================================================

import { Network, RefreshCw, FileText, Maximize2, Minimize2, Crosshair } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Tooltip, TooltipTrigger, TooltipContent, TooltipProvider } from '@/components/ui/tooltip'
import { type LayoutMode } from './force-layout'

export interface TopologyHeaderProps {
  onLayout: (direction: 'TB' | 'LR') => void
  onLayoutModeChange?: (mode: LayoutMode) => void
  layoutMode?: LayoutMode
  onRefresh: () => void
  onCollapse?: () => void
  onSummary?: () => void
  onFullscreenToggle?: () => void
  onFocusActive?: () => void
  refreshing: boolean
  nodeCount: number
  edgeCount: number
  planCount?: number
  knowledgeCount?: number
  isFullscreen?: boolean
  activeMilestone?: string | null
}

export function TopologyHeader({
  onLayout,
  onLayoutModeChange,
  layoutMode = 'force',
  onRefresh,
  onCollapse,
  onSummary,
  onFullscreenToggle,
  onFocusActive,
  refreshing,
  nodeCount,
  edgeCount,
  planCount = 0,
  knowledgeCount = 0,
  isFullscreen = false,
  activeMilestone,
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
          {/* Focus Active Node */}
          {onFocusActive && (
            <Tooltip>
              <TooltipTrigger asChild>
                <button
                  onClick={onFocusActive}
                  aria-label="Focus on active node"
                  className={cn(
                    "flex items-center gap-1.5 px-2.5 py-1.5 text-[10px] font-mono rounded-md border transition-all",
                    activeMilestone
                      ? "border-orange-400/40 bg-orange-400/10 text-orange-400 hover:bg-orange-400/20 hover:border-orange-400/60"
                      : "border-border-subtle text-text-muted hover:text-neon-cyan hover:border-neon-cyan/40"
                  )}
                >
                  <Crosshair className="size-3" />
                  <span>Focus</span>
                </button>
              </TooltipTrigger>
              <TooltipContent>{activeMilestone ? "Focus on active milestone" : "Smart focus (plan nodes)"}</TooltipContent>
            </Tooltip>
          )}

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

          {/* Layout Mode Toggle - cycles through: force ↔ dagre-tb */}
          <Tooltip>
            <TooltipTrigger asChild>
              <button
                onClick={() => {
                  const nextMode = layoutMode === 'force' ? 'dagre-tb' : 'force'
                  if (nextMode === 'dagre-tb') onLayout('TB')
                  onLayoutModeChange?.(nextMode)
                }}
                aria-label="Toggle layout mode"
                className={cn(
                  "p-1.5 rounded-md border active:scale-95 transition-all",
                  layoutMode === 'force' && "bg-neon-green/20 border-neon-green/60 text-neon-green shadow-[0_0_8px_rgba(0,255,136,0.25)]",
                  (layoutMode === 'dagre-tb' || layoutMode === 'dagre-lr') && "bg-neon-cyan/20 border-neon-cyan/60 text-neon-cyan shadow-[0_0_8px_rgba(0,240,255,0.25)]"
                )}
              >
                {layoutMode === 'force' ? (
                  <svg className="size-3.5" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24">
                    <circle cx="12" cy="12" r="3" />
                    <circle cx="12" cy="5" r="2" />
                    <circle cx="19" cy="12" r="2" />
                    <circle cx="12" cy="19" r="2" />
                    <circle cx="5" cy="12" r="2" />
                  </svg>
                ) : (
                  <svg className="size-3.5" fill="none" stroke="currentColor" strokeWidth={2.5} viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m0 0l-4-4m4 4l4-4" />
                  </svg>
                )}
              </button>
            </TooltipTrigger>
            <TooltipContent>
              {layoutMode === 'force' ? 'Cluster (click for Tree)' : 'Tree (click for Cluster)'}
            </TooltipContent>
          </Tooltip>
        </div>
      </div>
    </TooltipProvider>
  )
}
