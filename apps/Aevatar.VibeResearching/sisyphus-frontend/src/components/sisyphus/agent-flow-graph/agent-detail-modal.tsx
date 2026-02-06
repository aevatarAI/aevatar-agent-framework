// ============================================================
//  Agent Detail Modal - Shows agent output and upstream events
//  Displays: agent name, output content, upstream event list
//  Layout: Two columns - Left: Upstream Events, Right: Output
// ============================================================

import { memo, useCallback } from 'react'
import { createPortal } from 'react-dom'
import { cn } from '@/lib/utils'
import type { AgentStatus, AgentStats } from '@/store/agent-topology-store'
import type { UpstreamEvent } from './agent-node'
import {
  Brain,
  BookOpen,
  Lightbulb,
  ShieldCheck,
  GitBranch,
  FileText,
  X,
  Clock,
  Zap,
  Inbox,
  ArrowRight,
  Copy,
  FileOutput,
  type LucideIcon
} from 'lucide-react'

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

const DEFAULT_ICON = Brain

function getIcon(agentType: string): LucideIcon {
  return AGENT_ICONS[agentType.toLowerCase()] || DEFAULT_ICON
}

// ------------------------------------------------------------
//  Formatting Utils
// ------------------------------------------------------------

function formatDuration(ms: number | null): string {
  if (ms === null) return '-'
  if (ms < 1000) return `${ms}ms`
  if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`
  return `${Math.floor(ms / 60000)}m ${Math.round((ms % 60000) / 1000)}s`
}

function formatTokens(tokens: number): string {
  if (tokens === 0) return '0'
  if (tokens < 1000) return tokens.toString()
  return `${(tokens / 1000).toFixed(1)}k`
}

function formatTimestamp(ts: number): string {
  const now = Date.now()
  const diff = now - ts
  if (diff < 60000) return `${Math.floor(diff / 1000)}s ago`
  if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`
  return new Date(ts).toLocaleTimeString('en-US', { 
    hour: '2-digit', 
    minute: '2-digit', 
    second: '2-digit' 
  })
}

// ------------------------------------------------------------
//  Component Props
// ------------------------------------------------------------

export interface AgentDetailModalProps {
  isOpen: boolean
  onClose: () => void
  agentType: string
  status: AgentStatus
  stats?: AgentStats
  output?: string
  upstreamEvents?: UpstreamEvent[]
}

// ------------------------------------------------------------
//  Main Component
// ------------------------------------------------------------

const AgentDetailModal = memo(({
  isOpen,
  onClose,
  agentType,
  status,
  stats,
  output,
  upstreamEvents,
}: AgentDetailModalProps) => {
  const Icon = getIcon(agentType)
  const hasOutput = output && output.length > 0
  const hasUpstreamEvents = upstreamEvents && upstreamEvents.length > 0
  
  // Close on backdrop click
  const handleBackdropClick = useCallback((e: React.MouseEvent) => {
    if (e.target === e.currentTarget) onClose()
  }, [onClose])
  
  // Close on Escape key
  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Escape') onClose()
  }, [onClose])
  
  // Copy output to clipboard
  const handleCopyOutput = useCallback(() => {
    if (output) {
      navigator.clipboard.writeText(output)
    }
  }, [output])
  
  if (!isOpen) return null
  
  // Status color
  const statusColor = status === 'running' ? '#22D3EE' 
    : status === 'completed' ? '#10B981'
    : status === 'error' ? '#F43F5E'
    : '#64748B'
  
  const isStreaming = status === 'running'
  
  return createPortal(
    <div 
      className="fixed inset-0 z-[10000] flex items-center justify-center p-4"
      onClick={handleBackdropClick}
      onKeyDown={handleKeyDown}
      tabIndex={-1}
    >
      {/* Backdrop */}
      <div className="absolute inset-0 bg-black/70 backdrop-blur-sm" />
      
      {/* Modal */}
      <div 
        className={cn(
          "relative w-full max-w-3xl max-h-[85vh] overflow-hidden",
          "bg-slate-900 rounded-2xl border border-white/10",
          "shadow-2xl shadow-black/50",
          "flex flex-col",
          "animate-in fade-in-0 zoom-in-95 duration-200"
        )}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-white/10 bg-slate-800/80">
          <div className="flex items-center gap-3">
            {/* Icon */}
            <div 
              className="size-10 rounded-xl flex items-center justify-center"
              style={{ backgroundColor: `${statusColor}20` }}
            >
              <Icon className="size-5" style={{ color: statusColor }} />
            </div>
            
            {/* Title */}
            <div>
              <h2 className="text-lg font-semibold text-white capitalize font-mono">
                {agentType.replace(/_/g, ' ')}
              </h2>
              <p className="text-xs text-slate-400">
                LLMAgent · {stats?.duration ? formatDuration(stats.duration) : 'Processing...'}
              </p>
            </div>
          </div>
          
          <div className="flex items-center gap-3">
            {/* Status Badge */}
            <div 
              className="flex items-center gap-2 px-3 py-1.5 rounded-lg"
              style={{ backgroundColor: `${statusColor}20` }}
            >
              <span 
                className={cn(
                  "size-2 rounded-full",
                  isStreaming && "animate-pulse"
                )}
                style={{ backgroundColor: statusColor }}
              />
              <span 
                className="text-xs font-medium"
                style={{ color: statusColor }}
              >
                {status === 'running' ? 'Running' 
                  : status === 'completed' ? 'Completed'
                  : status === 'error' ? 'Error'
                  : 'Idle'}
              </span>
            </div>
            
            {/* Close Button */}
            <button
              onClick={onClose}
              className="size-8 rounded-lg bg-slate-700/50 hover:bg-slate-700 
                         flex items-center justify-center transition-colors"
            >
              <X className="size-4 text-slate-400" />
            </button>
          </div>
        </div>
        
        {/* Stats Bar */}
        <div className="flex items-center justify-around px-5 py-3 bg-slate-800/50 border-b border-white/5">
          <div className="text-center">
            <p className="text-lg font-semibold font-mono" style={{ color: statusColor }}>
              {formatTokens(stats?.tokens || 0)}
            </p>
            <p className="text-[10px] text-slate-500">tokens</p>
          </div>
          <div className="w-px h-8 bg-slate-700" />
          <div className="text-center">
            <p className="text-lg font-semibold font-mono" style={{ color: statusColor }}>
              {stats?.duration ? formatDuration(stats.duration) : '-'}
            </p>
            <p className="text-[10px] text-slate-500">elapsed</p>
          </div>
          <div className="w-px h-8 bg-slate-700" />
          <div className="text-center">
            <p className="text-lg font-semibold font-mono text-white">
              {upstreamEvents?.length || 0}
            </p>
            <p className="text-[10px] text-slate-500">upstream events</p>
          </div>
        </div>
        
        {/* Two-Column Content */}
        <div className="flex-1 flex min-h-0 overflow-hidden">
          {/* Left Column: Upstream Events */}
          <div className="w-[280px] flex-shrink-0 border-r border-white/5 flex flex-col">
            <div className="px-4 py-3 border-b border-white/5 bg-slate-800/30">
              <div className="flex items-center gap-2">
                <Inbox className="size-4 text-cyan-400" />
                <span className="text-sm font-medium text-white">Upstream Events</span>
              </div>
            </div>
            
            <div className="flex-1 overflow-y-auto p-3 space-y-2 scrollbar-thin scrollbar-thumb-transparent hover:scrollbar-thumb-slate-600 scrollbar-track-transparent transition-colors">
              {hasUpstreamEvents ? (
                upstreamEvents.map((event, index) => (
                  <div 
                    key={index}
                    className="p-3 rounded-lg bg-slate-800/50 border border-white/5"
                  >
                    {/* Event Header */}
                    <div className="flex items-center justify-between mb-2">
                      <div className="flex items-center gap-2">
                        <span 
                          className="size-2 rounded-full"
                          style={{ backgroundColor: '#10B981' }}
                        />
                        <span className="text-xs font-mono text-white capitalize">
                          {event.fromAgent.replace(/_/g, ' ')}
                        </span>
                      </div>
                      <span className="text-[10px] font-mono text-slate-500 px-1.5 py-0.5 bg-slate-700/50 rounded">
                        {event.eventType}
                      </span>
                    </div>
                    
                    {/* Event Content - Vertical scroll only, no horizontal */}
                    <div className="p-2 rounded bg-slate-900/50 max-h-[150px] overflow-y-auto overflow-x-hidden scrollbar-thin scrollbar-thumb-transparent hover:scrollbar-thumb-slate-500 scrollbar-track-transparent">
                      <p className="text-[11px] text-slate-400 leading-relaxed break-words whitespace-pre-wrap">
                        {event.preview}
                      </p>
                    </div>
                    
                    {/* Event Footer */}
                    <p className="mt-2 text-[9px] font-mono text-slate-600">
                      {formatTimestamp(event.timestamp)} · {event.preview.length} chars
                    </p>
                  </div>
                ))
              ) : (
                <div className="flex flex-col items-center justify-center h-full text-center py-8">
                  <Inbox className="size-8 text-slate-600 mb-2" />
                  <p className="text-xs text-slate-500">No upstream events</p>
                </div>
              )}
            </div>
          </div>
          
          {/* Right Column: Output */}
          <div className="flex-1 flex flex-col min-w-0">
            <div className="px-4 py-3 border-b border-white/5 bg-slate-800/30 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <FileOutput className="size-4 text-emerald-400" />
                <span className="text-sm font-medium text-white">Output</span>
                {isStreaming && (
                  <span className="flex items-center gap-1.5 px-2 py-0.5 rounded bg-cyan-500/20 text-[10px] text-cyan-400">
                    <span className="size-1.5 rounded-full bg-cyan-400 animate-pulse" />
                    Streaming
                  </span>
                )}
              </div>
              
              {hasOutput && (
                <button
                  onClick={handleCopyOutput}
                  className="flex items-center gap-1.5 px-2 py-1 rounded bg-slate-700/50 hover:bg-slate-700 
                             text-xs text-slate-400 hover:text-slate-300 transition-colors"
                >
                  <Copy className="size-3" />
                  Copy
                </button>
              )}
            </div>
            
            <div className="flex-1 overflow-y-auto p-4 scrollbar-thin scrollbar-thumb-transparent hover:scrollbar-thumb-slate-600 scrollbar-track-transparent">
              {hasOutput ? (
                <div className="rounded-lg bg-slate-800/50 border border-white/5 p-4 h-full overflow-y-auto scrollbar-thin scrollbar-thumb-transparent hover:scrollbar-thumb-slate-500 scrollbar-track-transparent">
                  <pre className="text-sm text-slate-300 whitespace-pre-wrap font-mono leading-relaxed">
                    {output}
                    {isStreaming && <span className="text-cyan-400 animate-pulse">▊</span>}
                  </pre>
                </div>
              ) : (
                <div className="flex flex-col items-center justify-center h-full text-center py-8">
                  <FileText className="size-8 text-slate-600 mb-2" />
                  <p className="text-xs text-slate-500">
                    {status === 'idle' ? 'Waiting for input...' 
                      : status === 'running' ? 'Generating output...'
                      : 'No output available'}
                  </p>
                </div>
              )}
            </div>
          </div>
        </div>
        
        {/* Footer */}
        <div className="px-5 py-3 border-t border-white/5 bg-slate-800/30 flex items-center justify-between">
          <div className="flex items-center gap-2 text-slate-500">
            <Clock className="size-3" />
            <span className="text-xs">
              {stats?.startTime 
                ? `Started ${formatTimestamp(stats.startTime)}`
                : 'Not started'}
            </span>
          </div>
          
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium text-slate-300 
                       bg-slate-700/50 hover:bg-slate-700 rounded-lg transition-colors"
          >
            Close
          </button>
        </div>
      </div>
    </div>,
    document.body
  )
})

AgentDetailModal.displayName = 'AgentDetailModal'

export default AgentDetailModal
