// ============================================================
//  Agent Node - Custom React Flow Node
//  Displays agent with status-based styling and statistics tooltip
// ============================================================

import { memo, useState } from 'react'
import { Handle, Position } from '@xyflow/react'
import { cn } from '@/lib/utils'
import type { AgentStatus, AgentStats } from '@/store/agent-topology-store'
import {
  Brain,
  BookOpen,
  Lightbulb,
  ShieldCheck,
  GitBranch,
  FileText,
  Clock,
  Zap,
  Inbox,
  ArrowDownLeft,
  type LucideIcon
} from 'lucide-react'

// ------------------------------------------------------------
//  Types
// ------------------------------------------------------------

// Event info from upstream agents
export interface UpstreamEvent {
  fromAgent: string
  eventType: string
  preview: string
  timestamp: number
}

export interface AgentNodeData extends Record<string, unknown> {
  label: string
  agentType: string
  status: AgentStatus
  layer: number  // Layer index for color selection
  stats?: AgentStats  // Statistics for tooltip
  outputPreview?: string  // First N chars of agent output for preview
  upstreamEvents?: UpstreamEvent[]  // Events received from upstream agents
  onSelect?: (agentType: string) => void  // Click handler for detail view
}

// ------------------------------------------------------------
//  Agent Icons (by type name)
// ------------------------------------------------------------

const AGENT_ICONS: Record<string, LucideIcon> = {
  planner: Brain,
  librarian: BookOpen,
  reasoner: Lightbulb,
  verifier: ShieldCheck,
  dag_builder: GitBranch,
  paper_editor: FileText,
}

// ------------------------------------------------------------
//  Layer-based Color System (auto-computed from topology)
// ------------------------------------------------------------

interface ColorScheme {
  color: string
  glowClass: string
  bgActive: string
  bgIdle: string
}

// Colors mapped by layer index
const LAYER_COLORS: ColorScheme[] = [
  // Layer 0: Entry (cyan-400)
  { color: '#22D3EE', glowClass: 'shadow-glow-cyan', bgActive: 'bg-neon-cyan', bgIdle: 'bg-neon-cyan/20' },
  // Layer 1: Process (cyan-500)
  { color: '#06B6D4', glowClass: 'shadow-glow-cyan', bgActive: 'bg-cyan-500', bgIdle: 'bg-cyan-500/20' },
  // Layer 2: Validate (purple-500)
  { color: '#8B5CF6', glowClass: 'shadow-glow-purple', bgActive: 'bg-neon-purple', bgIdle: 'bg-neon-purple/20' },
  // Layer 3+: Output (green-500)
  { color: '#10B981', glowClass: 'shadow-glow-green', bgActive: 'bg-neon-green', bgIdle: 'bg-neon-green/20' },
]

// Fallback for unknown icons
const DEFAULT_ICON = Brain

/**
 * Get color scheme based on layer index
 */
function getColorByLayer(layer: number): ColorScheme {
  if (layer < 0) return LAYER_COLORS[0]
  if (layer >= LAYER_COLORS.length) return LAYER_COLORS[LAYER_COLORS.length - 1]
  return LAYER_COLORS[layer]
}

/**
 * Get icon for agent type
 */
function getIcon(agentType: string): LucideIcon {
  return AGENT_ICONS[agentType.toLowerCase()] || DEFAULT_ICON
}

/**
 * Format duration in human-readable form
 */
function formatDuration(ms: number | null): string {
  if (ms === null) return '-'
  if (ms < 1000) return `${ms}ms`
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`
  return `${Math.floor(ms / 60000)}m ${Math.round((ms % 60000) / 1000)}s`
}

/**
 * Format token count
 */
function formatTokens(tokens: number): string {
  if (tokens === 0) return '0'
  if (tokens < 1000) return tokens.toString()
  return `${(tokens / 1000).toFixed(1)}k`
}

// ------------------------------------------------------------
//  Stats Tooltip Component
// ------------------------------------------------------------

interface StatsTooltipProps {
  stats: AgentStats
  status: AgentStatus
  colorScheme: ColorScheme
}

const StatsTooltip = memo(({ stats, status, colorScheme }: StatsTooltipProps) => (
  <div 
    className={cn(
      "absolute bottom-full mb-2 left-1/2 -translate-x-1/2 z-50",
      "min-w-[160px] px-3 py-2.5 rounded-lg",
      "bg-bg-elevated/95 backdrop-blur-md border border-white/10",
      "shadow-lg shadow-black/30",
      "pointer-events-none",
      "animate-in fade-in-0 zoom-in-95 duration-150"
    )}
  >
    {/* Arrow pointing down */}
    <div className="absolute -bottom-1.5 left-1/2 -translate-x-1/2 size-3 rotate-45 bg-bg-elevated/95 border-r border-b border-white/10" />
    
    {/* Content */}
    <div className="relative space-y-2">
      {/* Tokens */}
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-1.5 text-text-muted">
          <Zap className="size-3" style={{ color: colorScheme.color }} />
          <span className="text-[10px] uppercase tracking-wider">Tokens</span>
        </div>
        <span className="text-xs font-mono text-text-primary">{formatTokens(stats.tokens)}</span>
      </div>
      
      {/* Duration */}
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-1.5 text-text-muted">
          <Clock className="size-3" style={{ color: colorScheme.color }} />
          <span className="text-[10px] uppercase tracking-wider">Time</span>
        </div>
        <span className="text-xs font-mono text-text-primary">
          {status === 'running' && stats.startTime 
            ? 'Running...' 
            : formatDuration(stats.duration)}
        </span>
      </div>
      
      {/* Last Activity */}
      {stats.lastActivity && (
        <div className="pt-1 border-t border-white/5">
          <p className="text-[10px] text-text-muted truncate max-w-[140px]">
            {stats.lastActivity}
          </p>
        </div>
      )}
    </div>
  </div>
))

// ------------------------------------------------------------
//  Status Styles
// ------------------------------------------------------------

const STATUS_STYLES: Record<AgentStatus, { ring: string; animate: string }> = {
  idle: { ring: 'ring-1 ring-white/10', animate: '' },
  running: { ring: 'animate-gradient-border animate-border-glow', animate: '' },  // Rotating gradient border
  completed: { ring: 'ring-2 ring-neon-green/60', animate: '' },
  error: { ring: 'ring-2 ring-neon-rose', animate: '' },
}

// ------------------------------------------------------------
//  Component
// ------------------------------------------------------------

interface AgentNodeProps {
  data: AgentNodeData
}

const AgentNode = memo(({ data }: AgentNodeProps) => {
  const { label, agentType, status, layer, stats, outputPreview, upstreamEvents, onSelect } = data
  const [showTooltip, setShowTooltip] = useState(false)
  
  // Get icon by agent type, color by layer
  const Icon = getIcon(agentType)
  const colorScheme = getColorByLayer(layer)
  const statusStyle = STATUS_STYLES[status] || STATUS_STYLES.idle
  
  const isCompleted = status === 'completed'
  const isError = status === 'error'
  const isRunning = status === 'running'
  const hasUpstreamEvents = upstreamEvents && upstreamEvents.length > 0
  
  // Handle click for detail view
  const handleClick = () => {
    onSelect?.(agentType)
  }
  
  return (
    <div 
      className="relative cursor-pointer active:cursor-grabbing group"
      onMouseEnter={() => setShowTooltip(true)}
      onMouseLeave={() => setShowTooltip(false)}
      onClick={handleClick}
    >
      {/* Main node container - Enhanced card */}
      <div
        className={cn(
          "relative flex flex-col rounded-xl overflow-hidden",
          "backdrop-blur-sm transition-all duration-300",
          "hover:scale-[1.02] hover:brightness-110",
          "border",
          isRunning && 'border-neon-cyan shadow-glow-cyan',
          isCompleted && 'border-neon-green/60',
          isError && 'border-neon-rose',
          !isRunning && !isCompleted && !isError && 'border-white/10',
        )}
        style={{ minWidth: 140, maxWidth: 180 }}
      >
        {/* Header: Icon + Name + Status */}
        <div className={cn(
          "flex items-center gap-2 px-3 py-2",
          "bg-slate-900/80"
        )}>
          {/* Icon */}
          <div 
            className={cn(
              "size-7 rounded-lg flex items-center justify-center",
              colorScheme.bgIdle
            )}
          >
            <Icon 
              className="size-4"
              style={{ color: colorScheme.color }}
            />
          </div>
          
          {/* Name + Status */}
          <div className="flex-1 min-w-0">
            <span className="text-[11px] font-mono font-semibold capitalize text-text-primary block truncate">
              {label}
            </span>
            <span className={cn(
              "text-[9px] font-mono",
              isRunning && 'text-neon-cyan',
              isCompleted && 'text-neon-green',
              isError && 'text-neon-rose',
              !isRunning && !isCompleted && !isError && 'text-text-muted'
            )}>
              {isRunning ? '● streaming' : isCompleted ? '✓ done' : isError ? '✗ error' : '○ idle'}
            </span>
          </div>
        </div>
        
        {/* Upstream Info Section - Shows received data from upstream agents */}
        {hasUpstreamEvents && (
          <div className="px-3 py-1.5 bg-slate-900/60 border-t border-white/5">
            <div className="flex items-center gap-1 mb-1">
              <ArrowDownLeft className="size-2.5 text-text-muted" />
              <span className="text-[7px] font-mono text-text-muted tracking-wider uppercase">From Upstream</span>
            </div>
            <div className="flex flex-wrap gap-1">
              {upstreamEvents.slice(0, 2).map((evt, i) => (
                <div 
                  key={i} 
                  className="flex items-center gap-1.5 text-[8px] px-1.5 py-0.5 bg-slate-700/50 rounded max-w-[90px]"
                >
                  <span 
                    className="size-1.5 rounded-full flex-shrink-0"
                    style={{ backgroundColor: '#10B981' }}
                  />
                  <span className="text-slate-400 truncate capitalize">
                    {evt.fromAgent.replace(/_/g, ' ')}
                  </span>
                </div>
              ))}
              {upstreamEvents.length > 2 && (
                <span className="text-[8px] text-slate-500 px-1">
                  +{upstreamEvents.length - 2}
                </span>
              )}
            </div>
          </div>
        )}
        
        {/* Body: Output preview */}
        <div className="px-3 py-2 bg-slate-800/50 min-h-[36px]">
          {outputPreview ? (
            <p className="text-[9px] leading-relaxed text-text-secondary line-clamp-2">
              {outputPreview}
            </p>
          ) : (
            <p className="text-[9px] text-text-muted italic">
              {hasUpstreamEvents ? 'Processing input...' : 'Waiting for input...'}
            </p>
          )}
        </div>
        
        {/* Footer: Stats */}
        {stats && (stats.tokens > 0 || stats.duration) && (
          <div className="flex items-center gap-3 px-3 py-1.5 bg-slate-900/60 border-t border-white/5">
            {stats.tokens > 0 && (
              <div className="flex items-center gap-1">
                <Zap className="size-2.5" style={{ color: colorScheme.color }} />
                <span className="text-[8px] font-mono text-text-muted">
                  {formatTokens(stats.tokens)}
                </span>
              </div>
            )}
            {stats.duration && (
              <div className="flex items-center gap-1">
                <Clock className="size-2.5 text-text-muted" />
                <span className="text-[8px] font-mono text-text-muted">
                  {formatDuration(stats.duration)}
                </span>
              </div>
            )}
          </div>
        )}
        
        {/* Status indicator dot */}
        <div 
          className={cn(
            "absolute top-2 right-2 size-2 rounded-full transition-all",
            status === 'idle' && 'bg-slate-500',
            status === 'running' && 'bg-neon-cyan animate-status-pulse',
            status === 'completed' && 'bg-neon-green',
            status === 'error' && 'bg-neon-rose'
          )}
        />
      </div>
      
      {/* Stats Tooltip on hover */}
      {showTooltip && stats && (
        <StatsTooltip stats={stats} status={status} colorScheme={colorScheme} />
      )}
      
      {/* Handles for edges */}
      <Handle
        type="target"
        position={Position.Top}
        className="!bg-transparent !border-0 !w-4 !h-2"
      />
      <Handle
        type="source"
        position={Position.Bottom}
        className="!bg-transparent !border-0 !w-4 !h-2"
      />
    </div>
  )
})

AgentNode.displayName = 'AgentNode'

export default AgentNode
