import React, { useState, useMemo, memo } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { cn } from '@/lib/utils'
import { useSisyphusStore, type AgentRosterItem, type AgentStatusReport, type AgentStateData, type AgentLlmStatus, type SessionRunningTool } from '@/store/sisyphus-store'
import { useStreamContentStore, selectAgentStream } from '@/store/stream-content-store'
import { useShallow } from 'zustand/react/shallow'
import AgentTimeline from './agent-timeline'
import WorkflowSteps from './workflow-steps'

// ============================================================
//  Helper: Format duration in seconds
// ============================================================
function formatDuration(startedAt: string | undefined): string {
  if (!startedAt) return ""
  const start = new Date(startedAt).getTime()
  const now = Date.now()
  const diffSec = Math.floor((now - start) / 1000)
  if (diffSec < 1) return "<1s"
  if (diffSec < 60) return `${diffSec}s`
  return `${Math.floor(diffSec / 60)}m ${diffSec % 60}s`
}

// ============================================================
//  Agent Cards View - Displays all agent outputs during a vibe run
//  PERFORMANCE: Uses isolated stream store for fine-grained updates
// ============================================================

// Default agents if roster is empty
const DEFAULT_AGENTS = [
  "research_assistant", "planner", "reasoner",
  "librarian", "verifier", "dag_builder", "paper_editor"
]

// Format token count
function formatTokens(n: number): string {
  if (!Number.isFinite(n) || n <= 0) return "0"
  if (n < 1000) return String(n)
  if (n < 1_000_000) return `${(n / 1000).toFixed(1)}k`
  return `${(n / 1_000_000).toFixed(2)}m`
}

// ============================================================
//  Single Agent Card - Uses isolated stream store
//  PERFORMANCE: Each card only re-renders when its own stream changes
// ============================================================

interface AgentCardProps {
  agent: string
  providerName?: string
  statusReport?: AgentStatusReport
  onOpenHistory?: () => void
}

const AgentCard: React.FC<AgentCardProps> = memo(({
  agent,
  providerName,
  statusReport,
  onOpenHistory
}) => {
  const [collapsed, setCollapsed] = useState(false)
  
  // FINE-GRAINED: Only subscribe to THIS agent's stream data
  const streamData = useStreamContentStore(selectAgentStream(agent))
  
  // API data: precise token usage and history (5s polling)
  const agentState = useSisyphusStore((s) => s.agentStates[agent] as AgentStateData | undefined)
  
  // SSE data: real-time LLM request status
  const llmStatus = useSisyphusStore((s) => s.agentLlmStatus[agent] as AgentLlmStatus | undefined)
  
  // Session status: running tools for this agent (use shallow compare to prevent infinite loop)
  const runningTools = useSisyphusStore(
    useShallow((s) => {
      if (!s.sessionStatus) return [] as SessionRunningTool[]
      return s.sessionStatus.runningTools.filter(
        (t) => t.targetAgent.toLowerCase() === agent.toLowerCase()
      )
    })
  )
  
  // Session status: agent's current step status
  const agentStatusFromApi = useSisyphusStore(
    useShallow((s) => {
      if (!s.sessionStatus) return null
      return s.sessionStatus.agents.find(
        (a) => a.agent.toLowerCase() === agent.toLowerCase()
      ) || null
    })
  )
  
  // Derive values from stream data
  const content = streamData?.content || ''
  const isStreaming = streamData?.isStreaming || false
  const isFinal = streamData?.isFinal || false
  const stepName = streamData?.stepName || agentStatusFromApi?.stepName
  
  // Prefer API token count (precise) over stream estimate
  const tokenCount = agentState?.totalTokenUsed ?? streamData?.tokenCount ?? 0
  const historyCount = agentState?.history?.length ?? 0
  
  // LLM requesting state
  const isRequesting = llmStatus?.phase === 'llm.request'
  const llmModel = llmStatus?.model
  
  // LLM request duration
  const llmDuration = useMemo(() => {
    if (!isRequesting || !llmStatus?.timestamp) return ""
    const diffSec = Math.floor((Date.now() - llmStatus.timestamp) / 1000)
    return diffSec < 1 ? "<1s" : `${diffSec}s`
  }, [isRequesting, llmStatus?.timestamp])
  
  // Overall activity status
  const isActive = isStreaming || isRequesting || runningTools.length > 0 || agentStatusFromApi?.status === 'running'
  
  const hasContent = Boolean(content)

  const preview = useMemo(() => {
    const text = content.replace(/\s+/g, ' ').trim()
    if (text.length <= 260) return text
    return text.slice(0, 260) + '…'
  }, [content])

  const isRA = agent === 'research_assistant'

  // Agent name display with icon
  const agentDisplayName = agent.replace(/_/g, ' ')
  
  // Format relative time for last activity
  const lastActivityText = useMemo(() => {
    if (!agentState?.lastActivity) return null
    const lastTime = new Date(agentState.lastActivity).getTime()
    const now = Date.now()
    const diffSec = Math.floor((now - lastTime) / 1000)
    if (diffSec < 5) return 'just now'
    if (diffSec < 60) return `${diffSec}s ago`
    if (diffSec < 3600) return `${Math.floor(diffSec / 60)}m ago`
    return `${Math.floor(diffSec / 3600)}h ago`
  }, [agentState?.lastActivity])

  return (
    <div className={cn(
      "rounded-xl border overflow-hidden transition-all",
      isStreaming 
        ? "border-neon-cyan bg-neon-cyan/5 shadow-[0_0_24px_rgba(0,240,255,0.15)]" 
        : isFinal
          ? "border-neon-green/50 bg-neon-green/5"
          : hasContent 
            ? "border-border-default bg-bg-surface" 
            : "border-border-subtle bg-bg-surface/60",
      isRA && "md:col-span-2"
    )}>
      {/* Header */}
      <div
        className={cn(
          "px-4 py-3 flex items-start justify-between gap-3",
          isStreaming 
            ? "bg-neon-cyan/10 border-b border-neon-cyan/20" 
            : isFinal 
              ? "bg-neon-green/10 border-b border-neon-green/20"
              : "bg-surface-elevated/50 border-b border-border-subtle",
          hasContent && "cursor-pointer hover:bg-surface-elevated/80"
        )}
        onClick={() => hasContent && setCollapsed(!collapsed)}
      >
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2.5 flex-wrap">
            {/* Agent Icon */}
            <div className={cn(
              "size-7 rounded-lg flex items-center justify-center text-[11px] font-bold",
              isStreaming 
                ? "bg-neon-cyan/20 text-neon-cyan" 
                : isFinal
                  ? "bg-neon-green/20 text-neon-green"
                  : "bg-surface-elevated text-text-muted"
            )}>
              {agent.charAt(0).toUpperCase()}
            </div>
            <span className={cn(
              "text-sm font-semibold capitalize",
              isStreaming ? "text-neon-cyan" : isFinal ? "text-neon-green" : "text-text-primary"
            )}>
              {agentDisplayName}
            </span>
            {/* Status Badge */}
            {isStreaming ? (
              <span className="text-[9px] px-2 py-0.5 rounded-full bg-neon-cyan text-bg-base font-bold tracking-wide animate-pulse">
                LIVE
              </span>
            ) : isFinal ? (
              <span className="text-[9px] px-2 py-0.5 rounded-full bg-neon-green text-bg-base font-bold tracking-wide">
                DONE
              </span>
            ) : (
              <span className="text-[9px] px-2 py-0.5 rounded-full bg-text-dimmed/30 text-text-muted font-medium tracking-wide">
                IDLE
              </span>
            )}
          </div>
          {/* Meta Info */}
          <div className="mt-1.5 flex items-center gap-2 text-[10px] text-text-muted font-mono flex-wrap">
            {providerName && (
              <>
                <span className="px-1.5 py-0.5 rounded bg-surface-elevated border border-border-subtle">
                  {providerName}
                </span>
                <span className="text-border-subtle">·</span>
              </>
            )}
            <span className="tabular-nums">{formatTokens(tokenCount)} tok</span>
            {historyCount > 0 && (
              <>
                <span className="text-border-subtle">·</span>
                <span className="tabular-nums">{historyCount} msgs</span>
              </>
            )}
            {lastActivityText && (
              <>
                <span className="text-border-subtle">·</span>
                <span>{lastActivityText}</span>
              </>
            )}
            {stepName && (
              <>
                <span className="text-border-subtle">·</span>
                <span>{stepName}</span>
              </>
            )}
          </div>
          
        </div>

        <div className="flex items-center gap-2 flex-shrink-0">
          {onOpenHistory && (
            <button
              onClick={(e) => { e.stopPropagation(); onOpenHistory() }}
              className={cn(
                "text-[10px] px-2 py-1 rounded-md font-mono",
                "border border-border-subtle bg-bg-surface text-text-secondary",
                "hover:border-neon-cyan/40 hover:text-neon-cyan transition-colors"
              )}
            >
              History
            </button>
          )}
          {hasContent && (
            <svg className={cn(
              "size-4 transition-transform text-text-muted", 
              !collapsed && "rotate-180"
            )} fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
            </svg>
          )}
        </div>
      </div>

      {/* Current Work Panel - LLM requests and running tools */}
      {(isRequesting || runningTools.length > 0) && (
        <div className="px-4 py-2 border-b border-border-subtle bg-bg-elevated/30 space-y-1.5">
          {/* LLM Request */}
          {isRequesting && (
            <div className="flex items-center gap-2 text-[10px]">
              <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-neon-gold/10 border border-neon-gold/30 text-neon-gold">
                <svg className="size-3 animate-spin" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                </svg>
                <span className="font-mono font-medium">LLM</span>
              </div>
              <span className="text-text-muted font-mono truncate flex-1">
                {llmModel || 'requesting'}
              </span>
              {llmDuration && (
                <span className="text-neon-gold font-mono tabular-nums">{llmDuration}</span>
              )}
            </div>
          )}
          
          {/* Running Tools */}
          {runningTools.map((tool) => (
            <div key={tool.toolCallId} className="flex items-center gap-2 text-[10px]">
              <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-neon-purple/10 border border-neon-purple/30 text-neon-purple">
                <svg className="size-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                </svg>
                <span className="font-mono font-medium">Tool</span>
              </div>
              <span className="text-text-muted font-mono truncate flex-1">{tool.toolName}</span>
              <span className="text-neon-purple font-mono tabular-nums">{formatDuration(tool.startedAt)}</span>
            </div>
          ))}
        </div>
      )}

      {/* Status Report (real-time work status) */}
      {statusReport?.statusText && (
        <div className={cn(
          "px-4 py-2 text-xs border-b",
          isActive
            ? "bg-neon-cyan/5 border-neon-cyan/20 text-neon-cyan"
            : "bg-surface-elevated/30 border-border-subtle text-text-muted"
        )}>
          <div className="flex items-center gap-2">
            <span className="size-1.5 rounded-full bg-current animate-pulse" />
            <span className="italic truncate">{statusReport.statusText}</span>
            {statusReport.progress !== undefined && statusReport.progress > 0 && (
              <span className="text-[10px] font-mono ml-auto">
                {Math.round(statusReport.progress * 100)}%
              </span>
            )}
          </div>
        </div>
      )}

      {/* Progress Bar (when available) */}
      {statusReport?.progress !== undefined && statusReport.progress > 0 && (
        <div className="h-1 bg-bg-elevated">
          <div 
            className={cn(
              "h-full transition-all duration-300",
              isActive ? "bg-neon-cyan" : "bg-neon-green"
            )}
            style={{ width: `${Math.min(100, Math.round(statusReport.progress * 100))}%` }}
          />
        </div>
      )}

      {/* Content */}
      <div className="p-4">
        {!hasContent ? (
          <div className="flex items-center gap-3 py-2">
            <div className="flex gap-1">
              <div className="size-1.5 rounded-full bg-text-dimmed/40 animate-pulse" style={{ animationDelay: '0ms' }} />
              <div className="size-1.5 rounded-full bg-text-dimmed/40 animate-pulse" style={{ animationDelay: '150ms' }} />
              <div className="size-1.5 rounded-full bg-text-dimmed/40 animate-pulse" style={{ animationDelay: '300ms' }} />
            </div>
            <span className="text-xs text-text-dimmed">Waiting for output…</span>
          </div>
        ) : collapsed ? (
          <div className="space-y-2">
            <pre className="text-xs text-text-secondary whitespace-pre-wrap break-words max-h-56 overflow-auto bg-bg-elevated/50 border border-border-subtle rounded-lg p-3">
              {preview || '…'}
            </pre>
          </div>
        ) : (
          <>
            {isStreaming ? (
              <pre className="text-sm text-text-primary whitespace-pre-wrap break-words max-h-[55vh] overflow-auto leading-relaxed">
                {content || '…'}
                <span className="animate-pulse text-neon-cyan">▌</span>
              </pre>
            ) : (
              <div className="prose prose-sm prose-invert max-w-none leading-relaxed
                prose-headings:text-neon-cyan prose-headings:font-display
                prose-a:text-neon-cyan prose-code:text-neon-gold prose-code:bg-bg-elevated prose-code:px-1.5 prose-code:py-0.5 prose-code:rounded
                prose-pre:bg-bg-void prose-pre:border prose-pre:border-border-subtle
              ">
                {content ? (
                  <ReactMarkdown remarkPlugins={[remarkGfm]}>{content}</ReactMarkdown>
                ) : (
                  <span className="text-text-dimmed italic">…</span>
                )}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  )
})

// ============================================================
//  Agent Cards View Main Component
//  PERFORMANCE: Fine-grained subscriptions for non-streaming state
// ============================================================

interface AgentCardsViewProps {
  sessionId: string
  onOpenAgentHistory?: (agent: string) => void
}

const AgentCardsView: React.FC<AgentCardsViewProps> = ({
  sessionId: _sessionId,  // Reserved for future use
  onOpenAgentHistory
}) => {
  // FINE-GRAINED SUBSCRIPTIONS: Only subscribe to what we need
  const currentRunId = useSisyphusStore((s) => s.currentRunId)
  const userPrompt = useSisyphusStore((s) => s.userPrompt)
  const agentRoster = useSisyphusStore((s) => s.agentRoster)
  const agentProviders = useSisyphusStore((s) => s.agentProviders)
  const agentStatusReports = useSisyphusStore((s) => s.agentStatusReports)
  const apiInfo = useSisyphusStore((s) => s.apiInfo)

  // Build agent list from roster or defaults
  const agents = useMemo(() => {
    const fromRoster = agentRoster
      .map((r: AgentRosterItem) => r.agent?.toLowerCase())
      .filter(Boolean)
    if (fromRoster.length > 0) return fromRoster
    return DEFAULT_AGENTS
  }, [agentRoster])

  // Get default provider name
  const defaultProvider = apiInfo?.llm?.default || ''

  // No run yet
  if (!currentRunId) {
    return (
      <div className="flex flex-col items-center justify-center py-12 px-6">
        <div className="size-16 rounded-xl bg-surface-elevated border border-border-subtle flex items-center justify-center mb-5">
          <svg className="size-8 text-text-dimmed" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z" />
          </svg>
        </div>
        <div className="text-sm text-text-primary font-semibold mb-2">No Active Run</div>
        <div className="text-xs text-text-muted text-center leading-relaxed max-w-xs">
          Send a <span className="text-neon-cyan font-medium">vibe</span> message to start multi-agent collaboration. 
          Each agent will process your request in parallel.
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-5">
      {/* Prompt Card */}
      <div className="rounded-xl border border-neon-purple/30 bg-neon-purple/5 overflow-hidden">
        <div className="px-4 py-3 bg-neon-purple/10 border-b border-neon-purple/20 flex items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            {/* User Icon */}
            <div className="size-8 rounded-lg bg-neon-purple/20 flex items-center justify-center">
              <svg className="size-4 text-neon-purple" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
              </svg>
            </div>
            <div className="min-w-0">
              <div className="text-sm font-semibold text-neon-purple">Your Prompt</div>
              <div className="mt-0.5 text-[10px] text-text-muted font-mono truncate">
                run: <span className="text-neon-purple/80">{currentRunId}</span>
              </div>
            </div>
          </div>
          {defaultProvider && (
            <div className="text-[10px] px-2 py-1 rounded bg-surface-elevated border border-border-subtle text-text-muted font-mono flex-shrink-0">
              {defaultProvider}
            </div>
          )}
        </div>
        <div className="p-4">
          <p className="text-sm text-text-primary leading-relaxed">
            {userPrompt || '…'}
          </p>
        </div>
      </div>

      {/* Status Overview - Timeline and Steps */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <AgentTimeline agentNames={agents} />
        <WorkflowSteps />
      </div>

      {/* Agent Cards Grid - Each card subscribes to its own stream */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {agents.map((agent) => {
          const provider = agentProviders[agent] || defaultProvider
          const statusReport = agentStatusReports[agent]
          return (
            <AgentCard
              key={agent}
              agent={agent}
              providerName={provider}
              statusReport={statusReport}
              onOpenHistory={onOpenAgentHistory ? () => onOpenAgentHistory(agent) : undefined}
            />
          )
        })}
      </div>
    </div>
  )
}

export default AgentCardsView
