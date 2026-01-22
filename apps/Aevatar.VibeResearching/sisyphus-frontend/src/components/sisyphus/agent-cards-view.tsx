import React, { useState, useMemo, memo } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { cn } from '@/lib/utils'
import { useSisyphusStore, type AgentRosterItem, type AgentStatusReport } from '@/store/sisyphus-store'
import { useStreamContentStore, selectAgentStream } from '@/store/stream-content-store'

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
  
  // Derive values from stream data
  const content = streamData?.content || ''
  const isStreaming = streamData?.isStreaming || false
  const isFinal = streamData?.isFinal || false
  const tokenCount = streamData?.tokenCount || 0
  const stepName = streamData?.stepName
  
  const hasContent = Boolean(content)

  const preview = useMemo(() => {
    const text = content.replace(/\s+/g, ' ').trim()
    if (text.length <= 260) return text
    return text.slice(0, 260) + '…'
  }, [content])

  const isRA = agent === 'research_assistant'

  // Agent name display with icon
  const agentDisplayName = agent.replace(/_/g, ' ')

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
          <div className="mt-1.5 flex items-center gap-2 text-[10px] text-text-muted font-mono">
            {providerName && (
              <>
                <span className="px-1.5 py-0.5 rounded bg-surface-elevated border border-border-subtle">
                  {providerName}
                </span>
                <span className="text-border-subtle">·</span>
              </>
            )}
            <span className="tabular-nums">{formatTokens(tokenCount)} tok</span>
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

      {/* Status Report (real-time work status) */}
      {statusReport?.statusText && (
        <div className={cn(
          "px-4 py-2 text-xs border-b",
          isStreaming
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
