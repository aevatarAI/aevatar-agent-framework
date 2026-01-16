// ============================================================
//  NodeLegend - Clickable Filter for DAG Nodes
// ============================================================

import { cn } from '@/lib/utils'
import { NODE_STYLES, type NodeFilterMode } from './dag-node-styles'

interface NodeLegendProps {
  filterMode: NodeFilterMode
  onFilterChange: (mode: NodeFilterMode) => void
}

export function NodeLegend({ filterMode, onFilterChange }: NodeLegendProps) {
  const handleFilterClick = (mode: NodeFilterMode) => {
    onFilterChange(filterMode === mode ? 'all' : mode)
  }

  const isActive = (mode: NodeFilterMode) => filterMode === mode

  return (
    <div className="flex-shrink-0 px-4 py-2.5 border-t border-border-subtle bg-bg-elevated/50">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-[10px] font-mono">
        <span className="text-text-dimmed select-none">Filter:</span>

        {/* Plan Active */}
        <button
          onClick={() => handleFilterClick('PlanActive')}
          className={cn(
            "flex items-center gap-1.5 px-2 py-1 rounded-md border transition-all cursor-pointer",
            isActive('PlanActive')
              ? "border-orange-400/60 bg-orange-400/10"
              : "border-transparent hover:border-border-subtle hover:bg-bg-elevated"
          )}
        >
          <span
            className="inline-block w-3 h-3 rounded-full animate-glow-pulse"
            style={{
              background: `radial-gradient(circle, ${NODE_STYLES.PlanActive.bg} 0%, ${NODE_STYLES.PlanActive.border} 100%)`,
              boxShadow: `0 0 8px ${NODE_STYLES.PlanActive.glow}`,
            }}
          />
          <span className={cn("text-orange-400", isActive('PlanActive') && "font-semibold")}>
            Active Milestone
          </span>
        </button>

        {/* Plan Node */}
        <button
          onClick={() => handleFilterClick('Plan')}
          className={cn(
            "flex items-center gap-1.5 px-2 py-1 rounded-md border transition-all cursor-pointer",
            isActive('Plan')
              ? "border-blue-400/60 bg-blue-400/10"
              : "border-transparent hover:border-border-subtle hover:bg-bg-elevated"
          )}
        >
          <span
            className="inline-block w-3 h-3 rounded-full"
            style={{
              background: `radial-gradient(circle, ${NODE_STYLES.Plan.bg} 0%, ${NODE_STYLES.Plan.border} 100%)`,
              boxShadow: `0 0 6px ${NODE_STYLES.Plan.glow}`,
            }}
          />
          <span className={cn("text-blue-400", isActive('Plan') && "font-semibold")}>
            Plan Node
          </span>
        </button>

        {/* Knowledge Node */}
        <button
          onClick={() => handleFilterClick('Knowledge')}
          className={cn(
            "flex items-center gap-1.5 px-2 py-1 rounded-md border transition-all cursor-pointer",
            isActive('Knowledge')
              ? "border-green-400/60 bg-green-400/10"
              : "border-transparent hover:border-border-subtle hover:bg-bg-elevated"
          )}
        >
          <span
            className="inline-block w-3 h-3 rounded-full"
            style={{
              background: `radial-gradient(circle, ${NODE_STYLES.Knowledge.bg} 0%, ${NODE_STYLES.Knowledge.border} 100%)`,
              boxShadow: `0 0 6px ${NODE_STYLES.Knowledge.glow}`,
            }}
          />
          <span className={cn("text-green-400", isActive('Knowledge') && "font-semibold")}>
            Knowledge Node
          </span>
        </button>

        {/* Other Session */}
        <button
          onClick={() => handleFilterClick('OtherSession')}
          className={cn(
            "flex items-center gap-1.5 px-2 py-1 rounded-md border transition-all cursor-pointer",
            isActive('OtherSession')
              ? "border-gray-400/60 bg-gray-400/10"
              : "border-transparent hover:border-border-subtle hover:bg-bg-elevated"
          )}
        >
          <span
            className="inline-block w-3 h-3 rounded-full opacity-60"
            style={{
              background: `radial-gradient(circle, ${NODE_STYLES.KnowledgeOther.bg} 0%, ${NODE_STYLES.KnowledgeOther.border} 100%)`,
            }}
          />
          <span className={cn("text-gray-400", isActive('OtherSession') && "font-semibold")}>
            Other Session
          </span>
        </button>

        {/* Clear Filter */}
        {filterMode !== 'all' && (
          <button
            onClick={() => onFilterChange('all')}
            className="px-2 py-1 text-[9px] text-text-muted hover:text-neon-rose border border-border-subtle hover:border-neon-rose/40 rounded-md transition-all"
          >
            Clear
          </button>
        )}
      </div>
    </div>
  )
}
