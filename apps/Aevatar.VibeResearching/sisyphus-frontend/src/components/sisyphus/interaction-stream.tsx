import React, { useState, useMemo, useRef, useEffect, useCallback, memo } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { useSisyphusStore } from '@/store/sisyphus-store';
import { useStreamContentStore, selectAgentStream } from '@/store/stream-content-store';
import { cn } from '@/lib/utils';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogCloseButton } from '@/components/ui/dialog';
import Composer from './composer';
import ToolOutputDisplay from './tool-output-display';
import type { ChatMessage, ToolOutput } from '@/types';

// ============================================================
//  Throttle utility for scroll optimization
// ============================================================

function throttle<T extends (...args: unknown[]) => void>(fn: T, delay: number): T {
  let lastCall = 0;
  let timeoutId: ReturnType<typeof setTimeout> | null = null;
  
  return ((...args: unknown[]) => {
    const now = Date.now();
    const remaining = delay - (now - lastCall);
    
    if (remaining <= 0) {
      if (timeoutId) {
        clearTimeout(timeoutId);
        timeoutId = null;
      }
      lastCall = now;
      fn(...args);
    } else if (!timeoutId) {
      timeoutId = setTimeout(() => {
        lastCall = Date.now();
        timeoutId = null;
        fn(...args);
      }, remaining);
    }
  }) as T;
}

interface InteractionStreamProps {
  sessionId: string;
}

// NOTE: Legacy AgentChip, AgentDetailModalContent, AllAgentsModalContent
// have been replaced with stream-based versions below (AgentChipWithStream, etc.)

// ============================================================
//  Agents Panel (Workers-style horizontal chips)
//  PERFORMANCE: Uses isolated stream store for status detection
// ============================================================

interface AgentsPanelProps {
  agentNames: string[];
  isVibeMode: boolean;
  hasActiveRun: boolean;
}

// Single agent chip with isolated stream subscription
const AgentChipWithStream: React.FC<{ agentName: string; onClick: () => void }> = memo(({ agentName, onClick }) => {
  // FINE-GRAINED: Only re-renders when THIS agent's stream changes
  const streamData = useStreamContentStore(selectAgentStream(agentName));
  
  const isStreaming = streamData?.isStreaming || false;
  const isFinal = streamData?.isFinal || false;
  
  const statusConfig = isStreaming 
    ? { color: 'text-neon-cyan', bgColor: 'bg-neon-cyan/20', label: 'LIVE', animate: true }
    : isFinal 
      ? { color: 'text-neon-green', bgColor: 'bg-neon-green/20', label: 'DONE', animate: false }
      : { color: 'text-text-muted', bgColor: 'bg-surface-elevated', label: 'WAIT', animate: false };

  return (
    <div 
      onClick={onClick}
      className={cn(
        "flex-shrink-0 rounded-lg border px-3 py-2 flex items-center gap-2 cursor-pointer transition-all hover:scale-[1.02]",
        isStreaming 
          ? "border-neon-cyan/40 bg-neon-cyan/5" 
          : isFinal
            ? "border-neon-green/30 bg-neon-green/5"
            : "border-border-subtle bg-bg-surface/50 hover:border-border-default"
      )}
    >
      <div className={cn(
        "size-6 rounded flex items-center justify-center text-[10px] font-bold flex-shrink-0",
        statusConfig.bgColor, statusConfig.color
      )}>
        {agentName.charAt(0).toUpperCase()}
      </div>
      <span className="font-mono text-[10px] text-text-primary capitalize">
        {agentName.replace(/_/g, ' ')}
      </span>
      <span className={cn(
        "text-[8px] font-mono px-1.5 py-0.5 rounded flex-shrink-0",
        statusConfig.bgColor, statusConfig.color,
        statusConfig.animate && "animate-pulse"
      )}>
        {statusConfig.label}
      </span>
    </div>
  );
});

const AgentsPanel: React.FC<AgentsPanelProps> = memo(({ agentNames, isVibeMode, hasActiveRun }) => {
  const [showModal, setShowModal] = useState(false);
  const [selectedAgentName, setSelectedAgentName] = useState<string | null>(null);
  
  // Get streaming count from isolated store
  const streamingCount = useStreamContentStore((s) => 
    agentNames.filter(name => s.agentStreams[name]?.isStreaming).length
  );

  // Don't show if nothing to display
  if (!isVibeMode || !hasActiveRun || agentNames.length === 0) return null;

  const handleChipClick = (agentName: string) => {
    setSelectedAgentName(agentName);
    setShowModal(true);
  };

  const handleBack = () => {
    setSelectedAgentName(null);
  };

  return (
    <div className="mt-4 relative z-10">
      {/* Header */}
      <div className="flex items-center justify-between mb-2">
        <div className="flex items-center gap-2">
          <svg className="size-4 text-neon-gold" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0z" />
          </svg>
          <span className="text-xs font-display font-medium text-neon-gold tracking-wider">AGENTS</span>
          <span className="text-[9px] text-text-dimmed font-mono tabular-nums">
            <span className="text-neon-gold">{streamingCount}</span>/{agentNames.length}
          </span>
        </div>
        <button
          onClick={() => { setSelectedAgentName(null); setShowModal(true); }}
          className="text-[9px] px-2 py-0.5 rounded border border-border-subtle text-text-muted hover:text-neon-gold hover:border-neon-gold/40 transition-colors"
        >
          VIEW ALL
        </button>
      </div>

      {/* Horizontal scrolling chips - each chip has its own subscription */}
      <div className="flex gap-2 overflow-x-auto pb-2 scrollbar-thin scrollbar-thumb-border-subtle scrollbar-track-transparent">
        {agentNames.map((name) => (
          <AgentChipWithStream 
            key={name} 
            agentName={name}
            onClick={() => handleChipClick(name)}
          />
        ))}
      </div>

      {/* Agent Detail Modal - uses isolated stream store internally */}
      <Dialog open={showModal} onOpenChange={setShowModal}>
        {selectedAgentName ? (
          <AgentDetailModalWithStream agentName={selectedAgentName} onBack={handleBack} />
        ) : (
          <AllAgentsModalWithStream agentNames={agentNames} onSelectAgent={setSelectedAgentName} />
        )}
      </Dialog>
    </div>
  );
});

// Agent detail modal using isolated stream store
const AgentDetailModalWithStream: React.FC<{ agentName: string; onBack: () => void }> = ({ agentName, onBack }) => {
  const streamData = useStreamContentStore(selectAgentStream(agentName));
  
  if (!streamData) return null;

  const { content, isStreaming, isFinal, tokenCount, providerName, stepName } = streamData;

  return (
    <DialogContent className="max-w-2xl mx-4 bg-bg-surface/95 backdrop-blur-md border border-border-default rounded-lg shadow-lg text-text-primary max-h-[70vh] overflow-hidden flex flex-col">
      <DialogHeader className="p-4 border-b border-border-default flex-shrink-0">
        <div className="flex items-center gap-3">
          <button 
            onClick={onBack}
            className="flex h-8 w-8 items-center justify-center rounded-lg bg-surface-elevated hover:bg-bg-elevated transition-colors"
            aria-label="Back to list"
          >
            <svg className="w-4 h-4 text-text-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
            </svg>
          </button>
          <div className={cn(
            "flex h-10 w-10 items-center justify-center rounded-lg text-lg font-bold",
            isStreaming ? "bg-neon-cyan/20 text-neon-cyan" : isFinal ? "bg-neon-green/20 text-neon-green" : "bg-surface-elevated text-text-muted"
          )}>
            {agentName.charAt(0).toUpperCase()}
          </div>
          <div className="flex-1 min-w-0">
            <DialogTitle className={cn(
              "text-lg font-display font-semibold capitalize",
              isStreaming ? "text-neon-cyan" : isFinal ? "text-neon-green" : "text-text-primary"
            )}>
              {agentName.replace(/_/g, ' ')}
            </DialogTitle>
            <DialogDescription className="text-xs text-text-muted font-mono tabular-nums">
              {tokenCount}t · {isStreaming ? 'streaming' : isFinal ? 'completed' : 'waiting'}
            </DialogDescription>
          </div>
          {isStreaming && (
            <span className="text-[9px] px-2 py-1 rounded-full bg-neon-cyan text-bg-base font-bold animate-pulse">LIVE</span>
          )}
        </div>
        <DialogCloseButton />
      </DialogHeader>

      {(providerName || stepName) && (
        <div className="px-4 py-2 border-b border-border-default flex flex-wrap gap-2 flex-shrink-0">
          {providerName && (
            <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-bg-elevated border border-border-subtle">
              <span className="text-[9px] text-text-subtle font-mono">PROVIDER:</span>
              <span className="text-[10px] text-neon-violet font-mono">{providerName}</span>
            </div>
          )}
          {stepName && (
            <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-bg-elevated border border-border-subtle">
              <span className="text-[9px] text-text-subtle font-mono">STEP:</span>
              <span className="text-[10px] text-neon-cyan font-mono">{stepName}</span>
            </div>
          )}
        </div>
      )}

      <div className="flex-1 overflow-y-auto p-4">
        {content ? (
          <div className="rounded-lg bg-bg-elevated p-4 border border-border-subtle">
            <pre className="text-sm text-text-primary font-mono whitespace-pre-wrap break-words leading-relaxed">
              {content}
              {isStreaming && <span className="animate-pulse text-neon-cyan">▌</span>}
            </pre>
          </div>
        ) : (
          <div className="flex flex-col items-center justify-center py-12 text-center">
            <div className="flex gap-1.5 mb-3">
              <div className="size-2 rounded-full bg-text-dimmed/40 animate-pulse" />
              <div className="size-2 rounded-full bg-text-dimmed/40 animate-pulse" style={{ animationDelay: '150ms' }} />
              <div className="size-2 rounded-full bg-text-dimmed/40 animate-pulse" style={{ animationDelay: '300ms' }} />
            </div>
            <p className="text-sm text-text-muted font-mono">Waiting for response…</p>
          </div>
        )}
      </div>
    </DialogContent>
  );
};

// All agents modal using isolated stream store
const AllAgentsModalWithStream: React.FC<{ 
  agentNames: string[]; 
  onSelectAgent: (name: string) => void;
}> = ({ agentNames, onSelectAgent }) => {
  const agentStreams = useStreamContentStore((s) => s.agentStreams);
  
  const streamingCount = agentNames.filter(name => agentStreams[name]?.isStreaming).length;
  const completedCount = agentNames.filter(name => agentStreams[name]?.isFinal).length;

  return (
    <DialogContent className="max-w-2xl mx-4 bg-bg-surface/95 backdrop-blur-md border border-border-default rounded-lg shadow-lg text-text-primary max-h-[70vh] overflow-hidden flex flex-col">
      <DialogHeader className="p-4 border-b border-border-default flex-shrink-0">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-neon-gold/20">
            <svg className="w-5 h-5 text-neon-gold" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0z" />
            </svg>
          </div>
          <div>
            <DialogTitle className="text-lg font-display font-semibold text-neon-gold">All Agents</DialogTitle>
            <DialogDescription className="text-xs text-text-muted font-mono tabular-nums">
              {agentNames.length} agents · {streamingCount} active · {completedCount} completed
            </DialogDescription>
          </div>
        </div>
        <DialogCloseButton />
      </DialogHeader>

      <div className="flex-1 overflow-y-auto p-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          {agentNames.map((name) => {
            const stream = agentStreams[name];
            const isStreaming = stream?.isStreaming || false;
            const isFinal = stream?.isFinal || false;
            
            return (
              <button
                key={name}
                onClick={() => onSelectAgent(name)}
                className={cn(
                  "rounded-lg border p-3 text-left transition-all hover:scale-[1.01]",
                  isStreaming 
                    ? "border-neon-cyan/40 bg-neon-cyan/5" 
                    : isFinal
                      ? "border-neon-green/30 bg-neon-green/5"
                      : "border-border-subtle bg-bg-surface/50 hover:border-border-default"
                )}
              >
                <div className="flex items-center gap-3">
                  <div className={cn(
                    "size-8 rounded flex items-center justify-center text-sm font-bold",
                    isStreaming ? "bg-neon-cyan/20 text-neon-cyan" : isFinal ? "bg-neon-green/20 text-neon-green" : "bg-surface-elevated text-text-muted"
                  )}>
                    {name.charAt(0).toUpperCase()}
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className={cn(
                      "text-sm font-semibold capitalize truncate",
                      isStreaming ? "text-neon-cyan" : isFinal ? "text-neon-green" : "text-text-primary"
                    )}>
                      {name.replace(/_/g, ' ')}
                    </p>
                    <p className="text-[10px] text-text-muted font-mono">
                      {stream?.providerName || '—'} · {stream?.tokenCount || 0}t
                    </p>
                  </div>
                  <span className={cn(
                    "text-[9px] font-mono px-2 py-1 rounded",
                    isStreaming ? "bg-neon-cyan/20 text-neon-cyan animate-pulse" : isFinal ? "bg-neon-green/20 text-neon-green" : "bg-surface-elevated text-text-muted"
                  )}>
                    {isStreaming ? 'LIVE' : isFinal ? 'DONE' : 'WAIT'}
                  </span>
                </div>
              </button>
            );
          })}
        </div>
      </div>
    </DialogContent>
  );
};

// ============================================================
//  Message Bubble with Markdown + Tool Outputs
// ============================================================

interface MessageBubbleProps {
  message: ChatMessage;
  index: number;
  formatTime: (timestamp: number) => string;
}

const MessageBubble: React.FC<MessageBubbleProps> = ({ message, index, formatTime }) => {
  const [collapsed, setCollapsed] = useState(false);
  const isAgent = message.role === 'agent';
  const extMessage = message as ChatMessage & { toolOutputs?: ToolOutput[] };
  const hasToolOutputs = Array.isArray(extMessage.toolOutputs) && extMessage.toolOutputs.length > 0;

  // Preview for collapsed state
  const preview = useMemo(() => {
    const text = (message.content || '').replace(/\s+/g, ' ').trim();
    if (text.length <= 200) return text;
    return text.slice(0, 200) + '…';
  }, [message.content]);

  return (
    <div
      className={cn(
        "chat-bubble animate-fade-in relative",
        isAgent ? "agent" : "user"
      )}
      style={{ animationDelay: `${Math.min(index, 10) * 0.05}s` }}
    >
      {/* Header */}
      <div className="flex items-center gap-3 mb-2">
        <div className={cn(
          "w-6 h-6 rounded flex items-center justify-center text-xs font-bold",
          isAgent
            ? "bg-neon-cyan/20 text-neon-cyan"
            : "bg-neon-gold/20 text-neon-gold"
        )}>
          {isAgent ? 'S' : 'U'}
        </div>
        <span className={cn(
          "text-xs font-display font-medium tracking-wider",
          isAgent ? "text-neon-cyan" : "text-neon-gold"
        )}>
          {message.role === 'user' ? 'YOU' : (message.agentName || 'SISYPHUS')}
        </span>
        <span className="text-[10px] text-text-dimmed font-mono tabular-nums">
          {formatTime(message.timestamp)}
        </span>

        {/* Collapse toggle for agent messages */}
        {isAgent && message.content && message.content.length > 200 && (
          <button
            onClick={() => setCollapsed(!collapsed)}
            className="text-[10px] px-2 py-0.5 rounded border border-border-subtle bg-bg-elevated text-text-muted hover:text-text-primary transition-colors"
          >
            {collapsed ? 'Expand' : 'Collapse'}
          </button>
        )}
      </div>

      {/* Content */}
      <div className="pl-9">
        {collapsed ? (
          <p className="text-xs text-text-muted truncate">{preview}</p>
        ) : (
          <div className="prose prose-sm prose-invert max-w-none text-text-primary leading-relaxed
            prose-headings:text-neon-cyan prose-headings:font-display
            prose-a:text-neon-cyan prose-a:no-underline hover:prose-a:underline
            prose-code:text-neon-gold prose-code:bg-bg-elevated prose-code:px-1 prose-code:py-0.5 prose-code:rounded prose-code:text-xs
            prose-pre:bg-bg-void prose-pre:border prose-pre:border-border-subtle prose-pre:rounded-lg
            prose-strong:text-text-primary prose-strong:font-semibold
            prose-ul:marker:text-neon-cyan prose-ol:marker:text-neon-cyan
          ">
            <ReactMarkdown remarkPlugins={[remarkGfm]}>
              {message.content || '…'}
            </ReactMarkdown>
          </div>
        )}

        {/* Tool Outputs */}
        {hasToolOutputs && !collapsed && extMessage.toolOutputs && (
          <ToolOutputDisplay tools={extMessage.toolOutputs} />
        )}
      </div>
    </div>
  );
};

// ============================================================
//  Research Assistant Bubble (using isolated stream store)
//  PERFORMANCE: Uses fine-grained selector to only re-render
//  when research_assistant's stream content changes
// ============================================================

interface ResearchAssistantBubbleProps {
  agentName?: string;
}

const ResearchAssistantBubble: React.FC<ResearchAssistantBubbleProps> = memo(({ agentName = 'research_assistant' }) => {
  const [collapsed, setCollapsed] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  
  // FINE-GRAINED SUBSCRIPTION: Only re-renders when THIS agent's stream changes
  const streamData = useStreamContentStore(selectAgentStream(agentName));
  
  // Derive display values from stream data
  const content = streamData?.content || '';
  const isStreaming = streamData?.isStreaming || false;
  const isFinal = streamData?.isFinal || false;
  const tokenCount = streamData?.tokenCount || 0;
  const providerName = streamData?.providerName;
  const stepName = streamData?.stepName;
  
  const preview = useMemo(() => {
    const text = content.replace(/\s+/g, ' ').trim();
    if (text.length <= 220) return text;
    return text.slice(0, 220) + '…';
  }, [content]);
  
  // Don't render if no content
  if (!content && !isStreaming) return null;
  
  return (
    <>
      <div className="bg-bg-surface/50 border border-border-subtle rounded-2xl animate-fade-in overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between gap-3 px-4 py-3 border-b border-border-subtle/50">
          <div className="flex items-center gap-3 min-w-0">
            {/* Agent name + status */}
            <span className="text-sm font-mono font-medium text-text-primary">
              {agentName}
            </span>
            {isStreaming ? (
              <span className="flex items-center gap-1.5 text-[10px] px-2 py-0.5 rounded bg-neon-gold/10 text-neon-gold border border-neon-gold/30">
                <span className="w-1.5 h-1.5 rounded-full bg-neon-gold animate-pulse" />
                STREAMING
              </span>
            ) : isFinal ? (
              <span className="text-[10px] px-2 py-0.5 rounded bg-neon-green/10 text-neon-green border border-neon-green/30">
                DONE
              </span>
            ) : null}
          </div>

          {/* Right side: History + Collapse */}
          <div className="flex items-center gap-2">
            {/* History button */}
            <button
              onClick={() => setHistoryOpen(true)}
              className="text-[11px] px-2.5 py-1 rounded border bg-bg-elevated hover:bg-bg-surface border-border-subtle text-text-muted hover:text-text-primary transition-colors"
            >
              History
            </button>
            
            {/* Collapse toggle */}
            <button
              onClick={() => setCollapsed(!collapsed)}
              className="flex items-center gap-1 text-[11px] px-2.5 py-1 rounded border bg-bg-elevated hover:bg-bg-surface border-border-subtle text-text-muted hover:text-text-primary transition-colors"
            >
              <svg className={cn("w-3 h-3 transition-transform", collapsed && "-rotate-90")} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
              </svg>
              {collapsed ? 'Expand' : 'Collapse'}
            </button>
          </div>
        </div>

        {/* Meta info row */}
        <div className="flex items-center gap-3 px-4 py-2 bg-bg-void/30 text-[11px] text-text-muted font-mono">
          {providerName && (
            <span className="text-neon-cyan">{providerName}</span>
          )}
          <span>·</span>
          <span className="tabular-nums">{tokenCount} tokens</span>
          {stepName && (
            <>
              <span>·</span>
              <span className="text-neon-violet">{stepName}</span>
            </>
          )}
        </div>

        {/* Content */}
        {!collapsed && (
          <div className="px-4 py-3">
            {isStreaming ? (
              // Streaming: show raw pre for real-time updates
              <pre className="text-sm text-text-primary whitespace-pre-wrap break-words leading-relaxed">
                {content || '…'}
                <span className="animate-pulse text-neon-cyan">▌</span>
              </pre>
            ) : (
              // Final: render as Markdown
              <div className="prose prose-sm prose-invert max-w-none text-text-primary leading-relaxed
                prose-headings:text-neon-cyan prose-headings:font-display
                prose-a:text-neon-cyan prose-a:no-underline hover:prose-a:underline
                prose-code:text-neon-gold prose-code:bg-bg-elevated prose-code:px-1 prose-code:py-0.5 prose-code:rounded prose-code:text-xs
                prose-pre:bg-bg-void prose-pre:border prose-pre:border-border-subtle prose-pre:rounded-lg
                prose-strong:text-text-primary prose-strong:font-semibold
                prose-ul:marker:text-neon-cyan prose-ol:marker:text-neon-cyan
              ">
                {content ? (
                  <ReactMarkdown remarkPlugins={[remarkGfm]}>{content}</ReactMarkdown>
                ) : (
                  <span className="text-text-dimmed italic">…</span>
                )}
              </div>
            )}
          </div>
        )}

        {/* Collapsed preview */}
        {collapsed && (
          <div className="px-4 py-2">
            <p className="text-sm text-text-muted truncate">{preview || '…'}</p>
          </div>
        )}
      </div>

      {/* History Modal */}
      <Dialog open={historyOpen} onOpenChange={setHistoryOpen}>
        <DialogContent className="max-w-2xl mx-4 bg-bg-surface/95 backdrop-blur-md border border-border-default rounded-lg shadow-lg text-text-primary max-h-[80vh] overflow-hidden flex flex-col">
          {/* Header */}
          <DialogHeader className="p-4 border-b border-border-default flex-shrink-0">
            <div className="flex items-center gap-3">
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-neon-violet/20">
                <span className="text-sm font-bold text-neon-violet">AI</span>
              </div>
              <div>
                <DialogTitle className="text-lg font-display font-semibold text-neon-violet">
                  {agentName}
                </DialogTitle>
                <DialogDescription className="text-xs text-text-muted font-mono tabular-nums">
                  {providerName || '—'} · {tokenCount} tokens · {stepName || '—'}
                </DialogDescription>
              </div>
              {isStreaming ? (
                <span className="ml-auto text-[10px] px-2 py-1 rounded bg-neon-gold/10 text-neon-gold border border-neon-gold/30 animate-pulse">
                  STREAMING
                </span>
              ) : isFinal ? (
                <span className="ml-auto text-[10px] px-2 py-1 rounded bg-neon-green/10 text-neon-green border border-neon-green/30">
                  DONE
                </span>
              ) : null}
            </div>
            <DialogCloseButton />
          </DialogHeader>

          {/* Meta badges */}
          <div className="px-4 py-2 border-b border-border-default flex flex-wrap gap-2 flex-shrink-0">
            {providerName && (
              <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-bg-elevated border border-border-subtle">
                <span className="text-[9px] text-text-subtle font-mono">PROVIDER:</span>
                <span className="text-[10px] text-neon-cyan font-mono">{providerName}</span>
              </div>
            )}
            <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-bg-elevated border border-border-subtle">
              <span className="text-[9px] text-text-subtle font-mono">TOKENS:</span>
              <span className="text-[10px] text-neon-gold font-mono tabular-nums">{tokenCount}</span>
            </div>
            {stepName && (
              <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-bg-elevated border border-border-subtle">
                <span className="text-[9px] text-text-subtle font-mono">STEP:</span>
                <span className="text-[10px] text-neon-violet font-mono">{stepName}</span>
              </div>
            )}
            <div className={cn(
              "flex items-center gap-1.5 px-2 py-1 rounded border",
              isStreaming 
                ? "bg-neon-gold/10 text-neon-gold border-neon-gold/30"
                : isFinal 
                  ? "bg-neon-green/10 text-neon-green border-neon-green/30"
                  : "bg-bg-elevated text-text-muted border-border-subtle"
            )}>
              <span className="text-[9px] text-text-subtle font-mono">STATUS:</span>
              <span className="text-[10px] font-mono">
                {isStreaming ? 'streaming' : isFinal ? 'completed' : 'waiting'}
              </span>
            </div>
          </div>

          {/* Content */}
          <div className="flex-1 overflow-y-auto p-4">
            {content ? (
              <div className="rounded-lg bg-bg-elevated p-4 border border-border-subtle">
                <div className="prose prose-sm prose-invert max-w-none text-text-primary leading-relaxed
                  prose-headings:text-neon-cyan prose-headings:font-display
                  prose-a:text-neon-cyan prose-a:no-underline hover:prose-a:underline
                  prose-code:text-neon-gold prose-code:bg-bg-void prose-code:px-1 prose-code:py-0.5 prose-code:rounded prose-code:text-xs
                  prose-pre:bg-bg-void prose-pre:border prose-pre:border-border-subtle prose-pre:rounded-lg
                  prose-strong:text-text-primary prose-strong:font-semibold
                  prose-ul:marker:text-neon-cyan prose-ol:marker:text-neon-cyan
                ">
                  <ReactMarkdown remarkPlugins={[remarkGfm]}>{content}</ReactMarkdown>
                </div>
                {isStreaming && (
                  <span className="animate-pulse text-neon-cyan text-lg">▌</span>
                )}
              </div>
            ) : (
              <div className="flex flex-col items-center justify-center py-12 text-center">
                <div className="flex gap-1.5 mb-3">
                  <div className="size-2 rounded-full bg-text-dimmed/40 animate-pulse" />
                  <div className="size-2 rounded-full bg-text-dimmed/40 animate-pulse" style={{ animationDelay: '150ms' }} />
                  <div className="size-2 rounded-full bg-text-dimmed/40 animate-pulse" style={{ animationDelay: '300ms' }} />
                </div>
                <p className="text-sm text-text-muted font-mono">Waiting for response…</p>
              </div>
            )}
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
});

// ============================================================
//  Interaction Stream Main Component
//  PERFORMANCE OPTIMIZATIONS:
//  1. Throttled scroll (100ms) to prevent layout thrashing
//  2. Fine-grained store subscriptions
//  3. Isolated stream content store for real-time updates
// ============================================================

// Fallback roster when agents_snapshot not received (matches original frontend)
const FALLBACK_AGENTS = ['planner', 'reasoner', 'librarian', 'verifier', 'dag_builder', 'paper_editor'];

const InteractionStream: React.FC<InteractionStreamProps> = ({ sessionId }) => {
  // FINE-GRAINED SUBSCRIPTIONS: Only subscribe to what we need
  const messages = useSisyphusStore((s) => s.messages);
  const researchBrief = useSisyphusStore((s) => s.researchBrief);
  const isConnected = useSisyphusStore((s) => s.isConnected);
  const isSending = useSisyphusStore((s) => s.isSending);
  const inputMode = useSisyphusStore((s) => s.inputMode);
  const currentRunId = useSisyphusStore((s) => s.currentRunId);
  const agentRoster = useSisyphusStore((s) => s.agentRoster);

  // Use isolated stream store for detecting if RA has content
  const raStreamData = useStreamContentStore(selectAgentStream('research_assistant'));
  const hasRaContent = Boolean(raStreamData?.content || raStreamData?.isStreaming);

  // Auto-switch to cards view when vibe run starts
  const isVibeMode = inputMode === 'vibe' || inputMode === 'vibe_loop';
  const hasActiveRun = Boolean(currentRunId);

  // Ref for auto-scroll container
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const scrollEndRef = useRef<HTMLDivElement>(null);

  // THROTTLED SCROLL: Prevent layout thrashing during rapid updates
  const scrollToBottom = useCallback(
    throttle(() => {
      if (scrollEndRef.current) {
        scrollEndRef.current.scrollIntoView({ behavior: 'smooth', block: 'end' });
      }
    }, 100),  // 100ms throttle
    []
  );

  // Auto-scroll when content changes (uses token count change, not content string)
  const raTokenCount = raStreamData?.tokenCount || 0;
  useEffect(() => {
    scrollToBottom();
  }, [messages.length, raTokenCount, isSending, scrollToBottom]);

  const formatTime = useCallback((timestamp: number) => {
    return new Date(timestamp).toLocaleTimeString('en-US', { 
      hour12: false, 
      hour: '2-digit', 
      minute: '2-digit',
      second: '2-digit'
    });
  }, []);

  // Filter messages: only show user messages + system messages
  const filteredMessages = useMemo(() => {
    return messages.filter(msg => 
      msg.role === 'user' || 
      msg.role === 'system' ||
      msg.agentName === 'SISYPHUS' ||
      !msg.agentName // fallback for old messages
    );
  }, [messages]);
  
  // Get agents from roster for the panel (lightweight, no content needed)
  const otherAgentNames = useMemo(() => {
    const effectiveRoster = agentRoster.length > 0 
      ? agentRoster.filter(r => r.agent !== 'research_assistant')
      : FALLBACK_AGENTS.map(a => ({ agent: a }));
    return effectiveRoster.map(r => r.agent);
  }, [agentRoster]);

  return (
    <div className="card p-5 flex flex-col h-full relative overflow-hidden cyber-corners">
      {/* Background */}
      <div className="absolute top-0 left-0 w-full h-32 bg-neon-cyan/5 blur-3xl pointer-events-none" />
      <div className="absolute bottom-0 right-0 w-64 h-64 bg-neon-gold/5 blur-3xl pointer-events-none" />
      
      {/* Header */}
      <div className="flex items-center justify-between mb-5 relative z-10">
        <div className="flex items-center gap-4">
          <div className="size-10 rounded-lg bg-neon-cyan flex items-center justify-center shadow-glow-cyan">
            <svg className="size-5 text-bg-base" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
            </svg>
          </div>
          <div>
            <h2 className="font-display text-sm font-semibold text-neon-cyan tracking-wider text-balance">Interaction Stream</h2>
            <p className="text-xs text-text-muted text-pretty">Research dialogue with Sisyphus</p>
          </div>
        </div>
        
        <div className="flex items-center gap-3">
          <span className={cn(
            "badge flex items-center gap-2",
            isSending ? "badge-gold" : isConnected ? "badge-green" : "badge-rose"
          )}>
            <div className={cn(
              "w-1.5 h-1.5 rounded-full animate-pulse",
              isSending ? "bg-neon-gold" : isConnected ? "bg-neon-green" : "bg-neon-rose"
            )} />
            {isSending ? 'Sending' : isConnected ? 'Active' : 'Offline'}
          </span>
        </div>
      </div>

      {/* Content Area */}
      <div ref={scrollContainerRef} className="flex-1 overflow-y-auto space-y-4 pr-2 relative z-10">
        {/* Chat Messages View - Only user / system messages + RA bubble */}
        {filteredMessages.length === 0 && !hasRaContent ? (
          <div className="flex flex-col items-center justify-center h-full text-center py-12">
            <div className="size-16 rounded-lg bg-surface-elevated flex items-center justify-center mb-4 border border-border-default cyber-corners">
              <svg className="size-8 text-text-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
              </svg>
            </div>
            <p className="text-sm text-text-secondary mb-2 font-display text-balance">
              No messages yet
            </p>
            <p className="text-xs text-text-muted max-w-xs text-pretty">
              Start a conversation with Sisyphus to explore research topics
            </p>
          </div>
        ) : (
          <>
            {filteredMessages.map((msg, index) => (
              <MessageBubble key={msg.id} message={msg} index={index} formatTime={formatTime} />
            ))}
            
            {/* Research Assistant Bubble - uses isolated stream store */}
            <ResearchAssistantBubble agentName="research_assistant" />
          </>
        )}

            {/* Research Brief */}
            {researchBrief && (
              <div className="card bg-surface-elevated/50 backdrop-blur-sm border-neon-purple/30 p-5 animate-fade-in cyber-corners">
                <div className="flex items-center gap-3 mb-4">
                  <div className="w-8 h-8 rounded-lg bg-neon-purple/20 flex items-center justify-center">
                    <svg className="w-4 h-4 text-neon-purple" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                    </svg>
                  </div>
                  <span className="font-display text-[10px] font-medium text-neon-purple tracking-[0.2em] uppercase">Research Brief</span>
                </div>
                
                <h3 className="text-base font-semibold text-text-primary mb-3">{researchBrief.title}</h3>
                <p className="text-sm text-text-secondary mb-4 leading-relaxed">{researchBrief.summary}</p>
                
                <div className="flex items-center gap-3 flex-wrap">
                  {researchBrief.keywords.map((keyword, i) => (
                    <span key={i} className="badge badge-purple text-[10px]">{keyword}</span>
                  ))}
                </div>
              </div>
            )}

        {/* Typing Indicator */}
        {isConnected && isSending && (
          <div className="flex items-center gap-3 p-4 rounded-lg bg-surface/50 border border-border-subtle animate-fade-in">
            <div className="flex gap-1.5">
              <div className="w-2.5 h-2.5 rounded-full bg-neon-cyan animate-pulse shadow-glow-cyan" style={{ animationDelay: '0s' }} />
              <div className="w-2.5 h-2.5 rounded-full bg-neon-gold animate-pulse shadow-glow-gold" style={{ animationDelay: '0.2s' }} />
              <div className="w-2.5 h-2.5 rounded-full bg-neon-purple animate-pulse shadow-glow-purple" style={{ animationDelay: '0.4s' }} />
            </div>
            <span className="text-xs text-text-muted font-mono">Sisyphus is processing...</span>
          </div>
        )}

        {/* Scroll anchor - auto-scroll target */}
        <div ref={scrollEndRef} className="h-px" />
      </div>

      {/* Agents Panel (horizontal chips) - uses isolated stream store */}
      <AgentsPanel 
        agentNames={otherAgentNames}
        isVibeMode={isVibeMode}
        hasActiveRun={hasActiveRun}
      />

      {/* Enhanced Composer */}
      <Composer sessionId={sessionId} connected={isConnected} />
    </div>
  );
};

export default InteractionStream;
