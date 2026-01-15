import React, { useState, useMemo } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { useSisyphusStore, type AgentMessage } from '@/store/sisyphus-store';
import { cn } from '@/lib/utils';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogCloseButton } from '@/components/ui/dialog';
import Composer from './composer';
import ToolOutputDisplay from './tool-output-display';
import type { ChatMessage, ToolOutput } from '@/types';

interface InteractionStreamProps {
  sessionId: string;
}

// ============================================================
//  Agent Chip (Horizontal scroll style)
// ============================================================

const AgentChip: React.FC<{ msg: AgentMessage; onClick: () => void }> = ({ msg, onClick }) => {
  const isStreaming = msg.isStreaming;
  const isFinal = msg.isFinal;
  
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
      {/* Avatar */}
      <div className={cn(
        "size-6 rounded flex items-center justify-center text-[10px] font-bold flex-shrink-0",
        statusConfig.bgColor, statusConfig.color
      )}>
        {msg.agent.charAt(0).toUpperCase()}
      </div>
      
      {/* Name */}
      <span className="font-mono text-[10px] text-text-primary capitalize">
        {msg.agent.replace(/_/g, ' ')}
      </span>
      
      {/* Status */}
      <span className={cn(
        "text-[8px] font-mono px-1.5 py-0.5 rounded flex-shrink-0",
        statusConfig.bgColor, statusConfig.color,
        statusConfig.animate && "animate-pulse"
      )}>
        {statusConfig.label}
      </span>
    </div>
  );
};

// ============================================================
//  Agent Detail Modal Content
// ============================================================

const AgentDetailModalContent: React.FC<{ agent: AgentMessage | null; onBack: () => void }> = ({ agent, onBack }) => {
  if (!agent) return null;

  const isStreaming = agent.isStreaming;
  const isFinal = agent.isFinal;

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
            {agent.agent.charAt(0).toUpperCase()}
          </div>
          <div className="flex-1 min-w-0">
            <DialogTitle className={cn(
              "text-lg font-display font-semibold capitalize",
              isStreaming ? "text-neon-cyan" : isFinal ? "text-neon-green" : "text-text-primary"
            )}>
              {agent.agent.replace(/_/g, ' ')}
            </DialogTitle>
            <DialogDescription className="text-xs text-text-muted font-mono tabular-nums">
              {agent.tokenCount}t · {isStreaming ? 'streaming' : isFinal ? 'completed' : 'waiting'}
            </DialogDescription>
          </div>
          {isStreaming && (
            <span className="text-[9px] px-2 py-1 rounded-full bg-neon-cyan text-bg-base font-bold animate-pulse">LIVE</span>
          )}
        </div>
        <DialogCloseButton />
      </DialogHeader>

      {/* Meta badges */}
      {(agent.providerName || agent.stepName) && (
        <div className="px-4 py-2 border-b border-border-default flex flex-wrap gap-2 flex-shrink-0">
          {agent.providerName && (
            <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-bg-elevated border border-border-subtle">
              <span className="text-[9px] text-text-subtle font-mono">PROVIDER:</span>
              <span className="text-[10px] text-neon-violet font-mono">{agent.providerName}</span>
            </div>
          )}
          {agent.stepName && (
            <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-bg-elevated border border-border-subtle">
              <span className="text-[9px] text-text-subtle font-mono">STEP:</span>
              <span className="text-[10px] text-neon-cyan font-mono">{agent.stepName}</span>
            </div>
          )}
        </div>
      )}

      {/* Content */}
      <div className="flex-1 overflow-y-auto p-4">
        {agent.content ? (
          <div className="rounded-lg bg-bg-elevated p-4 border border-border-subtle">
            <pre className="text-sm text-text-primary font-mono whitespace-pre-wrap break-words leading-relaxed">
              {agent.content}
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

// ============================================================
//  All Agents Modal Content (List View)
// ============================================================

const AllAgentsModalContent: React.FC<{ 
  agents: AgentMessage[]; 
  onSelectAgent: (agent: AgentMessage) => void;
}> = ({ agents, onSelectAgent }) => {
  const streamingCount = agents.filter(a => a.isStreaming).length;
  const completedCount = agents.filter(a => a.isFinal).length;

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
              {agents.length} agents · {streamingCount} active · {completedCount} completed
            </DialogDescription>
          </div>
        </div>
        <DialogCloseButton />
      </DialogHeader>

      <div className="flex-1 overflow-y-auto p-4">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          {agents.map((agent) => {
            const isStreaming = agent.isStreaming;
            const isFinal = agent.isFinal;
            
            return (
              <button
                key={agent.agent}
                onClick={() => onSelectAgent(agent)}
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
                    {agent.agent.charAt(0).toUpperCase()}
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className={cn(
                      "text-sm font-semibold capitalize truncate",
                      isStreaming ? "text-neon-cyan" : isFinal ? "text-neon-green" : "text-text-primary"
                    )}>
                      {agent.agent.replace(/_/g, ' ')}
                    </p>
                    <p className="text-[10px] text-text-muted font-mono">
                      {agent.providerName || '—'} · {agent.tokenCount}t
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
//  Agents Panel (Workers-style horizontal chips)
// ============================================================

interface AgentsPanelProps {
  agentMessages: AgentMessage[];
  isVibeMode: boolean;
  hasActiveRun: boolean;
}

const AgentsPanel: React.FC<AgentsPanelProps> = ({ agentMessages, isVibeMode, hasActiveRun }) => {
  const [showModal, setShowModal] = useState(false);
  const [selectedAgent, setSelectedAgent] = useState<AgentMessage | null>(null);
  
  const streamingAgents = agentMessages.filter(m => m.isStreaming);

  // Don't show if nothing to display
  if (!isVibeMode || !hasActiveRun || agentMessages.length === 0) return null;

  const handleChipClick = (agent: AgentMessage) => {
    setSelectedAgent(agent);
    setShowModal(true);
  };

  const handleBack = () => {
    setSelectedAgent(null);
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
            <span className="text-neon-gold">{streamingAgents.length}</span>/{agentMessages.length}
          </span>
        </div>
        <button
          onClick={() => { setSelectedAgent(null); setShowModal(true); }}
          className="text-[9px] px-2 py-0.5 rounded border border-border-subtle text-text-muted hover:text-neon-gold hover:border-neon-gold/40 transition-colors"
        >
          VIEW ALL
        </button>
      </div>

      {/* Horizontal scrolling chips */}
      <div className="flex gap-2 overflow-x-auto pb-2 scrollbar-thin scrollbar-thumb-border-subtle scrollbar-track-transparent">
        {agentMessages.map((msg) => (
          <AgentChip 
            key={msg.agent} 
            msg={msg} 
            onClick={() => handleChipClick(msg)}
          />
        ))}
      </div>

      {/* Agent Detail Modal */}
      <Dialog open={showModal} onOpenChange={setShowModal}>
        {selectedAgent ? (
          <AgentDetailModalContent agent={selectedAgent} onBack={handleBack} />
        ) : (
          <AllAgentsModalContent agents={agentMessages} onSelectAgent={setSelectedAgent} />
        )}
      </Dialog>
    </div>
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
//  Research Assistant Bubble (from agentMessages)
//  Format inspired by original frontend ChatMessageRow
// ============================================================

interface ResearchAssistantBubbleProps {
  message: AgentMessage;
}

const ResearchAssistantBubble: React.FC<ResearchAssistantBubbleProps> = ({ message }) => {
  const [collapsed, setCollapsed] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const isStreaming = message.isStreaming;
  const isFinal = message.isFinal;
  
  const preview = useMemo(() => {
    const text = (message.content || '').replace(/\s+/g, ' ').trim();
    if (text.length <= 220) return text;
    return text.slice(0, 220) + '…';
  }, [message.content]);
  
  return (
    <>
      <div className="bg-bg-surface/50 border border-border-subtle rounded-2xl animate-fade-in overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between gap-3 px-4 py-3 border-b border-border-subtle/50">
          <div className="flex items-center gap-3 min-w-0">
            {/* Agent name + status */}
            <span className="text-sm font-mono font-medium text-text-primary">
              research_assistant
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
          {message.providerName && (
            <span className="text-neon-cyan">{message.providerName}</span>
          )}
          <span>·</span>
          <span className="tabular-nums">{message.tokenCount || 0} tokens</span>
          {message.stepName && (
            <>
              <span>·</span>
              <span className="text-neon-violet">{message.stepName}</span>
            </>
          )}
        </div>

        {/* Content */}
        {!collapsed && (
          <div className="px-4 py-3">
            {isStreaming ? (
              // Streaming: show raw pre for real-time updates
              <pre className="text-sm text-text-primary whitespace-pre-wrap break-words leading-relaxed">
                {message.content || '…'}
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
                {message.content ? (
                  <ReactMarkdown remarkPlugins={[remarkGfm]}>{message.content}</ReactMarkdown>
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
                  research_assistant
                </DialogTitle>
                <DialogDescription className="text-xs text-text-muted font-mono tabular-nums">
                  {message.providerName || '—'} · {message.tokenCount || 0} tokens · {message.stepName || '—'}
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
            {message.providerName && (
              <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-bg-elevated border border-border-subtle">
                <span className="text-[9px] text-text-subtle font-mono">PROVIDER:</span>
                <span className="text-[10px] text-neon-cyan font-mono">{message.providerName}</span>
              </div>
            )}
            <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-bg-elevated border border-border-subtle">
              <span className="text-[9px] text-text-subtle font-mono">TOKENS:</span>
              <span className="text-[10px] text-neon-gold font-mono tabular-nums">{message.tokenCount || 0}</span>
            </div>
            {message.stepName && (
              <div className="flex items-center gap-1.5 px-2 py-1 rounded bg-bg-elevated border border-border-subtle">
                <span className="text-[9px] text-text-subtle font-mono">STEP:</span>
                <span className="text-[10px] text-neon-violet font-mono">{message.stepName}</span>
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
            {message.content ? (
              <div className="rounded-lg bg-bg-elevated p-4 border border-border-subtle">
                <div className="prose prose-sm prose-invert max-w-none text-text-primary leading-relaxed
                  prose-headings:text-neon-cyan prose-headings:font-display
                  prose-a:text-neon-cyan prose-a:no-underline hover:prose-a:underline
                  prose-code:text-neon-gold prose-code:bg-bg-void prose-code:px-1 prose-code:py-0.5 prose-code:rounded prose-code:text-xs
                  prose-pre:bg-bg-void prose-pre:border prose-pre:border-border-subtle prose-pre:rounded-lg
                  prose-strong:text-text-primary prose-strong:font-semibold
                  prose-ul:marker:text-neon-cyan prose-ol:marker:text-neon-cyan
                ">
                  <ReactMarkdown remarkPlugins={[remarkGfm]}>{message.content}</ReactMarkdown>
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
};

// ============================================================
//  Interaction Stream Main Component
// ============================================================

// Fallback roster when agents_snapshot not received (matches original frontend)
const FALLBACK_AGENTS = ['planner', 'reasoner', 'librarian', 'verifier', 'dag_builder', 'paper_editor'];

const InteractionStream: React.FC<InteractionStreamProps> = ({ sessionId }) => {
  const { messages, researchBrief, isConnected, isSending, inputMode, currentRunId, agentMessages, agentRoster, agentProviders } = useSisyphusStore();

  // Auto-switch to cards view when vibe run starts
  const isVibeMode = inputMode === 'vibe' || inputMode === 'vibe_loop';
  const hasActiveRun = Boolean(currentRunId);

  const formatTime = (timestamp: number) => {
    return new Date(timestamp).toLocaleTimeString('en-US', { 
      hour12: false, 
      hour: '2-digit', 
      minute: '2-digit',
      second: '2-digit'
    });
  };

  // Filter messages: only show user messages + research_assistant (main dialogue)
  const filteredMessages = useMemo(() => {
    return messages.filter(msg => 
      msg.role === 'user' || 
      msg.role === 'system' ||
      msg.agentName === 'research_assistant' ||
      msg.agentName === 'SISYPHUS' ||
      !msg.agentName // fallback for old messages
    );
  }, [messages]);

  // Get research_assistant message for main chat
  const raMessage = agentMessages['research_assistant'];
  
  
  // Get all agents (from roster + messages + fallback) except research_assistant
  const otherAgentMessages = useMemo(() => {
    // Start with agents from agentMessages
    const messageAgents = Object.values(agentMessages).filter(
      am => am.agent !== 'research_assistant'
    );
    const messageAgentNames = new Set(messageAgents.map(m => m.agent));
    
    // Use roster if available, otherwise fallback
    const effectiveRoster = agentRoster.length > 0 
      ? agentRoster.filter(r => r.agent !== 'research_assistant')
      : FALLBACK_AGENTS.map(a => ({ agent: a }));
    
    // Add agents from effective roster that don't have messages yet
    const pendingAgents = effectiveRoster
      .filter(r => !messageAgentNames.has(r.agent))
      .map(r => ({
        agent: r.agent,
        content: '',
        isStreaming: false,
        isFinal: false,
        tokenCount: 0,
        providerName: agentProviders[r.agent] || undefined,
        stepName: undefined,
      } as AgentMessage));
    
    return [...messageAgents, ...pendingAgents];
  }, [agentMessages, agentRoster, agentProviders]);

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
      <div className="flex-1 overflow-y-auto space-y-4 pr-2 relative z-10">
        {/* Chat Messages View - Only research_assistant / user / system */}
        {filteredMessages.length === 0 && !raMessage ? (
          <div className="flex flex-col items-center justify-center h-full text-center py-12">
            <div className="size-16 rounded-lg bg-surface-elevated flex items-center justify-center mb-4 border border-border-subtle cyber-corners">
              <svg className="size-8 text-text-dimmed" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
              </svg>
            </div>
            <p className="text-sm text-text-muted mb-2 font-display text-balance">
              No messages yet
            </p>
            <p className="text-xs text-text-dimmed max-w-xs text-pretty">
              Start a conversation with Sisyphus to explore research topics
            </p>
          </div>
        ) : (
          <>
            {filteredMessages.map((msg, index) => (
              <MessageBubble key={msg.id} message={msg} index={index} formatTime={formatTime} />
            ))}
            
            {/* Research Assistant Message from agentMessages */}
            {raMessage && raMessage.content && (
              <ResearchAssistantBubble message={raMessage} />
            )}
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
      </div>

      {/* Agents Panel (horizontal chips) */}
      <AgentsPanel 
        agentMessages={otherAgentMessages}
        isVibeMode={isVibeMode}
        hasActiveRun={hasActiveRun}
      />

      {/* Enhanced Composer */}
      <Composer sessionId={sessionId} connected={isConnected} />
    </div>
  );
};

export default InteractionStream;
