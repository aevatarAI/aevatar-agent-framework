import React, { memo, useMemo } from 'react'
import { cn } from '@/lib/utils'
import { useSisyphusStore, type SessionStatusStep, type SessionRunningTool } from '@/store/sisyphus-store'
import { useDagInteractions } from '@/hooks/use-dag-interactions'

// ============================================================
//  Workflow Steps - Shows step execution order and status
//  Displays running/done/pending steps with tools
// ============================================================

// Format step name for display
function formatStepName(name: string): string {
  return name
    .replace(/^vibe\./, '')
    .replace(/_/g, ' ')
    .replace(/\./g, ' › ')
}

// Format duration
function formatDuration(startedAt?: string, finishedAt?: string): string {
  if (!startedAt) return ""
  const start = new Date(startedAt).getTime()
  const end = finishedAt ? new Date(finishedAt).getTime() : Date.now()
  const diffSec = Math.floor((end - start) / 1000)
  if (diffSec < 1) return "<1s"
  if (diffSec < 60) return `${diffSec}s`
  return `${Math.floor(diffSec / 60)}m ${diffSec % 60}s`
}

interface WorkflowStepsProps {
  className?: string
  maxSteps?: number
}

const StepItem: React.FC<{
  name: string
  step: SessionStatusStep | undefined
  isRunning: boolean
  isDone: boolean
  runningTools: SessionRunningTool[]
}> = memo(({ name, step, isRunning, isDone, runningTools }) => {
  const statusConfig = useMemo(() => {
    if (isRunning) return { 
      icon: '🔄', 
      color: 'text-neon-cyan', 
      bgColor: 'bg-neon-cyan/10',
      borderColor: 'border-neon-cyan/30',
      animate: true 
    }
    if (isDone) return { 
      icon: '✅', 
      color: 'text-neon-green', 
      bgColor: 'bg-neon-green/5',
      borderColor: 'border-neon-green/20',
      animate: false 
    }
    return { 
      icon: '⏳', 
      color: 'text-text-muted', 
      bgColor: 'bg-bg-elevated/30',
      borderColor: 'border-border-subtle',
      animate: false 
    }
  }, [isRunning, isDone])
  
  const duration = formatDuration(step?.startedAt, step?.finishedAt)
  
  return (
    <div className={cn(
      "rounded-lg border p-2.5 transition-all",
      statusConfig.bgColor,
      statusConfig.borderColor,
      isRunning && "shadow-[0_0_12px_rgba(0,240,255,0.1)]"
    )}>
      <div className="flex items-center gap-2">
        {/* Status Icon */}
        <span className={cn(
          "text-sm flex-shrink-0",
          statusConfig.animate && "animate-spin"
        )}>
          {statusConfig.icon}
        </span>
        
        {/* Step Name */}
        <span className={cn(
          "text-xs font-mono capitalize flex-1 truncate",
          statusConfig.color
        )}>
          {formatStepName(name)}
        </span>
        
        {/* Duration */}
        {duration && (
          <span className={cn(
            "text-[10px] font-mono tabular-nums",
            isRunning ? "text-neon-cyan" : "text-text-dimmed"
          )}>
            {duration}
          </span>
        )}
      </div>
      
      {/* Running Tools */}
      {runningTools.length > 0 && (
        <div className="mt-2 pl-6 space-y-1">
          {runningTools.map((tool) => (
            <div 
              key={tool.toolCallId}
              className="flex items-center gap-2 text-[10px] text-neon-purple"
            >
              <svg className="size-3 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
              </svg>
              <span className="font-mono truncate">{tool.toolName}</span>
              <span className="font-mono tabular-nums ml-auto">
                {formatDuration(tool.startedAt)}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
})

const WorkflowSteps: React.FC<WorkflowStepsProps> = ({ className, maxSteps = 10 }) => {
  const sessionStatus = useSisyphusStore((s) => s.sessionStatus)
  const { dagStats } = useDagInteractions()
  
  const { stepsToShow, runningSteps, doneSteps, runningToolsByStep } = useMemo(() => {
    if (!sessionStatus) {
      return { 
        stepsToShow: [] as string[], 
        runningSteps: new Set<string>(), 
        doneSteps: new Set<string>(),
        runningToolsByStep: new Map<string, SessionRunningTool[]>()
      }
    }
    
    const running = new Set(sessionStatus.steps.running)
    const done = new Set(sessionStatus.steps.done)
    
    // Build tool map by step
    const toolMap = new Map<string, SessionRunningTool[]>()
    for (const tool of sessionStatus.runningTools) {
      // Try to associate tool with a step based on agent's current step
      const agent = sessionStatus.agents.find(
        (a) => a.agent.toLowerCase() === tool.targetAgent.toLowerCase()
      )
      if (agent?.stepName) {
        const existing = toolMap.get(agent.stepName) || []
        existing.push(tool)
        toolMap.set(agent.stepName, existing)
      }
    }
    
    // Show steps: running first, then recent done, then pending
    const order = sessionStatus.steps.order
    const runningList = order.filter((s) => running.has(s))
    const doneList = order.filter((s) => done.has(s)).slice(-3) // Last 3 done
    const pendingList = order.filter((s) => !running.has(s) && !done.has(s)).slice(0, 2) // Next 2 pending
    
    const combined = [...runningList, ...doneList, ...pendingList]
    const unique = [...new Set(combined)].slice(0, maxSteps)
    
    return {
      stepsToShow: unique,
      runningSteps: running,
      doneSteps: done,
      runningToolsByStep: toolMap,
    }
  }, [sessionStatus, maxSteps])
  
  if (stepsToShow.length === 0) return null
  
  const stepsMap = sessionStatus?.steps.map || {}
  
  return (
    <div className={cn("rounded-lg border border-border-subtle bg-bg-surface/60 p-3", className)}>
      {/* Header */}
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-2">
          <svg className="size-4 text-neon-cyan" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4" />
          </svg>
          <span className="text-xs font-display font-medium text-neon-cyan tracking-wider">WORKFLOW STEPS</span>
        </div>
        
        {/* Summary */}
        <div className="text-[10px] font-mono text-text-muted">
          <span className="text-neon-cyan">{runningSteps.size}</span> running · 
          <span className="text-neon-green ml-1">{doneSteps.size}</span> done
          {dagStats.totalCount > 0 && (
            <>
              {' · '}
              <span className="text-blue-400">P:{dagStats.planCount}</span>
              {' '}
              <span className="text-green-400">K:{dagStats.knowledgeCount}</span>
            </>
          )}
        </div>
      </div>
      
      {/* Steps List */}
      <div className="space-y-2">
        {stepsToShow.map((name) => (
          <StepItem
            key={name}
            name={name}
            step={stepsMap[name]}
            isRunning={runningSteps.has(name)}
            isDone={doneSteps.has(name)}
            runningTools={runningToolsByStep.get(name) || []}
          />
        ))}
      </div>
    </div>
  )
}

export default WorkflowSteps
