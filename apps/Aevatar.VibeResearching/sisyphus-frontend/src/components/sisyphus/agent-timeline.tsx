import React, { memo, useMemo } from 'react'
import { cn } from '@/lib/utils'
import { useSisyphusStore, type AgentLlmStatus, type SessionRunningTool } from '@/store/sisyphus-store'
import { useShallow } from 'zustand/react/shallow'
import { useStreamContentStore } from '@/store/stream-content-store'

// ============================================================
//  Agent Timeline - Horizontal timeline showing agent activity
//  Shows parallel work status across all agents
// ============================================================

interface AgentTimelineProps {
  agentNames: string[]
  className?: string
}

const AgentTimelineRow: React.FC<{ agentName: string }> = memo(({ agentName }) => {
  // Stream data for this agent
  const streamData = useStreamContentStore((s) => s.agentStreams[agentName])
  
  // LLM status
  const llmStatus = useSisyphusStore((s) => s.agentLlmStatus[agentName] as AgentLlmStatus | undefined)
  
  // Running tools for this agent (use shallow compare)
  const runningTools = useSisyphusStore(
    useShallow((s) => {
      if (!s.sessionStatus) return [] as SessionRunningTool[]
      return s.sessionStatus.runningTools.filter(
        (t) => t.targetAgent.toLowerCase() === agentName.toLowerCase()
      )
    })
  )
  
  // Agent status from API (use shallow compare)
  const agentStatusFromApi = useSisyphusStore(
    useShallow((s) => {
      if (!s.sessionStatus) return null
      return s.sessionStatus.agents.find(
        (a) => a.agent.toLowerCase() === agentName.toLowerCase()
      ) || null
    })
  )
  
  const isStreaming = streamData?.isStreaming || false
  const isFinal = streamData?.isFinal || false
  const isRequesting = llmStatus?.phase === 'llm.request'
  const hasRunningTools = runningTools.length > 0
  const isRunning = agentStatusFromApi?.status === 'running'
  
  // Determine activity status
  const status = useMemo(() => {
    if (isStreaming) return 'streaming'
    if (isRequesting) return 'llm'
    if (hasRunningTools) return 'tool'
    if (isRunning) return 'running'
    if (isFinal) return 'done'
    return 'idle'
  }, [isStreaming, isRequesting, hasRunningTools, isRunning, isFinal])
  
  // Status label
  const statusLabel = useMemo(() => {
    if (isRequesting && llmStatus?.model) return llmStatus.model
    if (hasRunningTools) return runningTools[0].toolName
    if (agentStatusFromApi?.stepName) return agentStatusFromApi.stepName
    return status
  }, [isRequesting, llmStatus?.model, hasRunningTools, runningTools, agentStatusFromApi?.stepName, status])
  
  const statusConfig = {
    streaming: { color: 'bg-neon-cyan', textColor: 'text-neon-cyan', animate: true },
    llm: { color: 'bg-neon-gold', textColor: 'text-neon-gold', animate: true },
    tool: { color: 'bg-neon-purple', textColor: 'text-neon-purple', animate: true },
    running: { color: 'bg-neon-cyan/60', textColor: 'text-neon-cyan', animate: true },
    done: { color: 'bg-neon-green', textColor: 'text-neon-green', animate: false },
    idle: { color: 'bg-text-dimmed/30', textColor: 'text-text-muted', animate: false },
  }[status]
  
  return (
    <div className="flex items-center gap-3 py-1.5">
      {/* Agent Name */}
      <div className="w-24 flex-shrink-0">
        <span className={cn(
          "text-xs font-mono capitalize truncate",
          statusConfig.textColor
        )}>
          {agentName.replace(/_/g, ' ')}
        </span>
      </div>
      
      {/* Progress Bar */}
      <div className="flex-1 h-5 bg-bg-elevated rounded-md overflow-hidden relative">
        <div 
          className={cn(
            "absolute inset-y-0 left-0 transition-all duration-500",
            statusConfig.color,
            statusConfig.animate && "animate-pulse"
          )}
          style={{ 
            width: status === 'idle' ? '0%' : status === 'done' ? '100%' : '60%' 
          }}
        />
        
        {/* Status Label Inside Bar */}
        <div className="absolute inset-0 flex items-center px-2">
          <span className={cn(
            "text-[9px] font-mono truncate",
            status === 'idle' ? 'text-text-dimmed' : 'text-bg-base'
          )}>
            {statusLabel}
          </span>
        </div>
      </div>
      
      {/* Status Indicator */}
      <div className={cn(
        "size-2 rounded-full flex-shrink-0",
        statusConfig.color,
        statusConfig.animate && "animate-pulse"
      )} />
    </div>
  )
})

const AgentTimeline: React.FC<AgentTimelineProps> = ({ agentNames, className }) => {
  if (agentNames.length === 0) return null
  
  return (
    <div className={cn("rounded-lg border border-border-subtle bg-bg-surface/60 p-3", className)}>
      {/* Header */}
      <div className="flex items-center gap-2 mb-3">
        <svg className="size-4 text-neon-gold" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M13 10V3L4 14h7v7l9-11h-7z" />
        </svg>
        <span className="text-xs font-display font-medium text-neon-gold tracking-wider">AGENT ACTIVITY</span>
      </div>
      
      {/* Timeline Rows */}
      <div className="space-y-0.5">
        {agentNames.map((name) => (
          <AgentTimelineRow key={name} agentName={name} />
        ))}
      </div>
      
      {/* Legend */}
      <div className="mt-3 pt-2 border-t border-border-subtle flex flex-wrap gap-3 text-[9px] text-text-muted">
        <div className="flex items-center gap-1">
          <span className="size-2 rounded-full bg-neon-cyan" />
          <span>Streaming</span>
        </div>
        <div className="flex items-center gap-1">
          <span className="size-2 rounded-full bg-neon-gold" />
          <span>LLM</span>
        </div>
        <div className="flex items-center gap-1">
          <span className="size-2 rounded-full bg-neon-purple" />
          <span>Tool</span>
        </div>
        <div className="flex items-center gap-1">
          <span className="size-2 rounded-full bg-neon-green" />
          <span>Done</span>
        </div>
      </div>
    </div>
  )
}

export default AgentTimeline
