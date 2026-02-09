// ============================================================
//  View Options Bar - Layer toggle controls
// ============================================================

import React from 'react'
import { Users, Wrench, Eye, RotateCcw } from 'lucide-react'
import { cn } from '@/lib/utils'

interface ViewOptionsBarProps {
  visibleLayers: {
    maker: boolean
    tool: boolean
  }
  detailMode: boolean
  onToggleLayer: (layer: 'maker' | 'tool') => void
  onToggleDetail: () => void
  onReset: () => void
}

const ViewOptionsBar: React.FC<ViewOptionsBarProps> = ({
  visibleLayers,
  detailMode,
  onToggleLayer,
  onToggleDetail,
  onReset,
}) => {
  return (
    <div className="absolute top-4 right-4 flex items-center gap-2 bg-bg-surface/95 backdrop-blur-sm border border-border-subtle rounded-lg p-2 shadow-lg">
      {/* VIEW label */}
      <span className="text-[10px] font-mono text-text-muted px-2">VIEW</span>

      {/* Layer toggles */}
      <div className="flex items-center gap-1 border-l border-border-subtle pl-2">
        <LayerToggle
          active={visibleLayers.maker}
          onClick={() => onToggleLayer('maker')}
          icon={<Users className="w-3.5 h-3.5" />}
          label="Maker"
          color="cyan"
        />
        <LayerToggle
          active={visibleLayers.tool}
          onClick={() => onToggleLayer('tool')}
          icon={<Wrench className="w-3.5 h-3.5" />}
          label="Tool"
          color="rose"
        />
      </div>

      {/* Divider */}
      <div className="w-px h-6 bg-border-subtle" />

      {/* Detail toggle */}
      <button
        onClick={onToggleDetail}
        className={cn(
          "flex items-center gap-1.5 px-2 py-1 text-[10px] font-mono rounded transition-colors",
          detailMode
            ? "bg-neon-cyan/20 text-neon-cyan"
            : "text-text-muted hover:text-text-primary"
        )}
      >
        <Eye className="w-3.5 h-3.5" />
        <span>Detail</span>
        <div className={cn(
          "w-6 h-3 rounded-full transition-colors relative",
          detailMode ? "bg-neon-cyan" : "bg-bg-elevated"
        )}>
          <div className={cn(
            "absolute top-0.5 w-2 h-2 rounded-full bg-white transition-transform",
            detailMode ? "left-3.5" : "left-0.5"
          )} />
        </div>
      </button>

      {/* Divider */}
      <div className="w-px h-6 bg-border-subtle" />

      {/* Reset */}
      <button
        onClick={onReset}
        className="p-1.5 text-text-muted hover:text-neon-cyan transition-colors rounded hover:bg-bg-elevated"
        title="Reset view"
      >
        <RotateCcw className="w-3.5 h-3.5" />
      </button>
    </div>
  )
}

// Layer Toggle Button
interface LayerToggleProps {
  active: boolean
  onClick: () => void
  icon: React.ReactNode
  label: string
  color: 'cyan' | 'rose'
}

const LayerToggle: React.FC<LayerToggleProps> = ({ active, onClick, icon, label, color }) => {
  const colorClasses = {
    cyan: active ? 'bg-neon-cyan/20 text-neon-cyan border-neon-cyan/40' : 'text-text-muted border-border-subtle',
    rose: active ? 'bg-neon-rose/20 text-neon-rose border-neon-rose/40' : 'text-text-muted border-border-subtle',
  }

  return (
    <button
      onClick={onClick}
      className={cn(
        "flex items-center gap-1 px-2 py-1 text-[10px] font-mono rounded border transition-colors",
        colorClasses[color],
        !active && "hover:text-text-primary hover:border-text-muted"
      )}
    >
      {icon}
      <span>{label}</span>
    </button>
  )
}

export default ViewOptionsBar
