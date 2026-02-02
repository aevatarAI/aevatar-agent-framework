import React, { useState, useMemo, useRef, useEffect, useLayoutEffect, useCallback, memo } from 'react';
import { createPortal } from 'react-dom';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { useSisyphusStore } from '@/store/sisyphus-store';
import { useStreamContentStore, selectAgentStream } from '@/store/stream-content-store';
import { cn } from '@/lib/utils';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogCloseButton } from '@/components/ui/dialog';
import Composer from './composer';
import ToolOutputDisplay from './tool-output-display';
import AgentTimeline from './agent-timeline';
import WorkflowSteps from './workflow-steps';
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

// ============================================================
//  Agent Dashboard Drawer - Slides in from right
// ============================================================
interface AgentDashboardDrawerProps {
  open: boolean;
  onClose: () => void;
  agentNames: string[];
}

const AgentDashboardDrawer: React.FC<AgentDashboardDrawerProps> = memo(({ open, onClose, agentNames }) => {
  // Prevent body scroll when drawer is open
  useEffect(() => {
    if (open) {
      document.body.style.overflow = 'hidden';
    } else {
      document.body.style.overflow = '';
    }
    return () => {
      document.body.style.overflow = '';
    };
  }, [open]);

  if (!open) return null;

  return createPortal(
    <div className="fixed inset-0 z-50">
      {/* Backdrop */}
      <div 
        className="absolute inset-0 bg-bg-base/60 backdrop-blur-sm animate-in fade-in duration-200"
        onClick={onClose}
      />
      
      {/* Drawer Panel */}
      <div className={cn(
        "absolute top-0 right-0 h-full w-full max-w-2xl",
        "bg-bg-surface/95 backdrop-blur-md border-l border-border-default",
        "shadow-2xl shadow-black/20",
        "animate-in slide-in-from-right duration-300",
        "flex flex-col"
      )}>
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-border-default">
          <div className="flex items-center gap-3">
            <div className="size-9 rounded-lg bg-neon-gold/20 flex items-center justify-center">
              <svg className="size-5 text-neon-gold" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0z" />
              </svg>
            </div>
            <div>
              <h2 className="text-base font-display font-semibold text-neon-gold tracking-wide">Agent Dashboard</h2>
              <p className="text-[10px] text-text-muted">Multi-agent work visualization</p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="size-8 rounded-lg flex items-center justify-center text-text-muted hover:text-text-primary hover:bg-surface-elevated transition-colors"
          >
            <svg className="size-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
        
        {/* Content */}
        <div className="flex-1 overflow-y-auto p-5 space-y-5">
          {/* Status Overview - Timeline and Steps side by side on larger screens */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <AgentTimeline agentNames={agentNames} />
            <WorkflowSteps />
          </div>
          
          {/* Agent Cards */}
          <div className="space-y-3">
            <h3 className="text-xs font-display font-medium text-text-muted tracking-wider">AGENT OUTPUTS</h3>
            <div className="grid grid-cols-1 gap-3">
              {agentNames.map((name) => (
                <DrawerAgentCard key={name} agentName={name} />
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>,
    document.body
  );
});

// Compact agent card for the drawer
const DrawerAgentCard: React.FC<{ agentName: string }> = memo(({ agentName }) => {
  const [collapsed, setCollapsed] = useState(true);
  const streamData = useStreamContentStore(selectAgentStream(agentName));
  const llmStatus = useSisyphusStore((s) => s.agentLlmStatus[agentName]);
  
  const content = streamData?.content || '';
  const isStreaming = streamData?.isStreaming || false;
  const isFinal = streamData?.isFinal || false;
  const tokenCount = streamData?.tokenCount || 0;
  const stepName = streamData?.stepName;
  
  const isRequesting = llmStatus?.phase === 'llm.request';
  const llmModel = llmStatus?.model;
  
  const preview = useMemo(() => {
    const text = content.replace(/\s+/g, ' ').trim();
    if (text.length <= 150) return text;
    return text.slice(0, 150) + '…';
  }, [content]);
  
  const hasContent = Boolean(content);

  return (
    <div className={cn(
      "rounded-lg border overflow-hidden transition-all",
      isStreaming 
        ? "border-neon-cyan bg-neon-cyan/5" 
        : isFinal
          ? "border-neon-green/50 bg-neon-green/5"
          : hasContent 
            ? "border-border-default bg-bg-surface" 
            : "border-border-subtle bg-bg-surface/60"
    )}>
      {/* Header */}
      <div
        className={cn(
          "px-3 py-2 flex items-center justify-between gap-2 cursor-pointer",
          isStreaming ? "bg-neon-cyan/10" : isFinal ? "bg-neon-green/10" : "bg-surface-elevated/30"
        )}
        onClick={() => hasContent && setCollapsed(!collapsed)}
      >
        <div className="flex items-center gap-2 min-w-0">
          <div className={cn(
            "size-6 rounded flex items-center justify-center text-[10px] font-bold flex-shrink-0",
            isStreaming ? "bg-neon-cyan/20 text-neon-cyan" : isFinal ? "bg-neon-green/20 text-neon-green" : "bg-surface-elevated text-text-muted"
          )}>
            {agentName.charAt(0).toUpperCase()}
          </div>
          <span className={cn(
            "text-xs font-semibold capitalize truncate",
            isStreaming ? "text-neon-cyan" : isFinal ? "text-neon-green" : "text-text-primary"
          )}>
            {agentName.replace(/_/g, ' ')}
          </span>
          {isStreaming && (
            <span className="text-[8px] px-1.5 py-0.5 rounded-full bg-neon-cyan text-bg-base font-bold animate-pulse flex-shrink-0">LIVE</span>
          )}
          {isRequesting && (
            <span className="text-[8px] px-1.5 py-0.5 rounded bg-neon-gold/20 text-neon-gold font-mono flex-shrink-0">
              LLM {llmModel ? `· ${llmModel}` : ''}
            </span>
          )}
        </div>
        <div className="flex items-center gap-2 flex-shrink-0">
          <span className="text-[9px] text-text-muted font-mono tabular-nums">{tokenCount} tok</span>
          {stepName && <span className="text-[9px] text-text-dimmed truncate max-w-[80px]">{stepName}</span>}
          {hasContent && (
            <svg className={cn("size-3 text-text-muted transition-transform", !collapsed && "rotate-180")} fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
            </svg>
          )}
        </div>
      </div>
      
      {/* Content */}
      {!collapsed && hasContent && (
        <div className="px-3 py-2 border-t border-border-subtle max-h-48 overflow-y-auto">
          <p className="text-[11px] text-text-secondary leading-relaxed whitespace-pre-wrap">
            {content}
            {isStreaming && <span className="text-neon-cyan animate-pulse">▌</span>}
          </p>
        </div>
      )}
      
      {/* Preview when collapsed */}
      {collapsed && preview && (
        <div className="px-3 py-1.5 border-t border-border-subtle">
          <p className="text-[10px] text-text-muted truncate">
            {preview}
            {isStreaming && <span className="text-neon-cyan animate-pulse">▌</span>}
          </p>
        </div>
      )}
    </div>
  );
});

// Inline expanded agent detail card (shown below chips when selected)
const AgentExpandedCard: React.FC<{ agentName: string; onClose: () => void }> = memo(({ agentName, onClose }) => {
  const streamData = useStreamContentStore(selectAgentStream(agentName));
  const llmStatus = useSisyphusStore((s) => s.agentLlmStatus[agentName]);
  const sessionStatus = useSisyphusStore((s) => s.sessionStatus);
  
  const runningTools = useMemo(() => {
    if (!sessionStatus) return [];
    return sessionStatus.runningTools.filter(
      (t) => t.targetAgent.toLowerCase() === agentName.toLowerCase()
    );
  }, [sessionStatus, agentName]);
  
  const agentStatusFromApi = useMemo(() => {
    if (!sessionStatus) return null;
    return sessionStatus.agents.find(
      (a) => a.agent.toLowerCase() === agentName.toLowerCase()
    ) || null;
  }, [sessionStatus, agentName]);

  const content = streamData?.content || '';
  const isStreaming = streamData?.isStreaming || false;
  const isFinal = streamData?.isFinal || false;
  const tokenCount = streamData?.tokenCount || 0;
  const stepName = streamData?.stepName || agentStatusFromApi?.stepName;
  
  const isRequesting = llmStatus?.phase === 'llm.request';
  const llmModel = llmStatus?.model;
  
  // Format LLM duration
  const llmDuration = useMemo(() => {
    if (!isRequesting || !llmStatus?.timestamp) return "";
    const diffSec = Math.floor((Date.now() - llmStatus.timestamp) / 1000);
    return diffSec < 1 ? "<1s" : `${diffSec}s`;
  }, [isRequesting, llmStatus?.timestamp]);
  
  const preview = useMemo(() => {
    const text = content.replace(/\s+/g, ' ').trim();
    if (text.length <= 200) return text;
    return text.slice(0, 200) + '…';
  }, [content]);

  return (
    <div className={cn(
      "mt-2 rounded-lg border overflow-hidden animate-in slide-in-from-top-2 duration-200",
      isStreaming 
        ? "border-neon-cyan bg-neon-cyan/5" 
        : isFinal
          ? "border-neon-green/50 bg-neon-green/5"
          : "border-border-default bg-bg-surface"
    )}>
      {/* Header */}
      <div className={cn(
        "px-3 py-2 flex items-center justify-between gap-2",
        isStreaming ? "bg-neon-cyan/10" : isFinal ? "bg-neon-green/10" : "bg-surface-elevated/50"
      )}>
        <div className="flex items-center gap-2 min-w-0">
          <div className={cn(
            "size-6 rounded flex items-center justify-center text-[10px] font-bold flex-shrink-0",
            isStreaming ? "bg-neon-cyan/20 text-neon-cyan" : isFinal ? "bg-neon-green/20 text-neon-green" : "bg-surface-elevated text-text-muted"
          )}>
            {agentName.charAt(0).toUpperCase()}
          </div>
          <span className={cn(
            "text-xs font-semibold capitalize truncate",
            isStreaming ? "text-neon-cyan" : isFinal ? "text-neon-green" : "text-text-primary"
          )}>
            {agentName.replace(/_/g, ' ')}
          </span>
          {isStreaming && (
            <span className="text-[8px] px-1.5 py-0.5 rounded-full bg-neon-cyan text-bg-base font-bold animate-pulse flex-shrink-0">LIVE</span>
          )}
        </div>
        <button
          onClick={onClose}
          className="size-5 rounded flex items-center justify-center text-text-muted hover:text-text-primary hover:bg-surface-elevated transition-colors flex-shrink-0"
        >
          <svg className="size-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>
      
      {/* Status Row */}
      <div className="px-3 py-1.5 flex items-center gap-2 text-[9px] text-text-muted border-b border-border-subtle">
        <span className="font-mono tabular-nums">{tokenCount} tok</span>
        {stepName && (
          <>
            <span className="text-border-subtle">·</span>
            <span className="truncate">{stepName}</span>
          </>
        )}
      </div>
      
      {/* Activity Status */}
      {(isRequesting || runningTools.length > 0) && (
        <div className="px-3 py-1.5 space-y-1 border-b border-border-subtle bg-bg-elevated/30">
          {isRequesting && (
            <div className="flex items-center gap-2 text-[9px] text-neon-gold">
              <svg className="size-3 animate-spin" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
              </svg>
              <span className="font-mono">LLM: {llmModel || 'requesting'}</span>
              {llmDuration && <span className="font-mono tabular-nums ml-auto">{llmDuration}</span>}
            </div>
          )}
          {runningTools.map((tool) => (
            <div key={tool.toolCallId} className="flex items-center gap-2 text-[9px] text-neon-purple">
              <svg className="size-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
              </svg>
              <span className="font-mono truncate">{tool.toolName}</span>
            </div>
          ))}
        </div>
      )}
      
      {/* Content Preview */}
      <div className="px-3 py-2 max-h-32 overflow-y-auto">
        {preview ? (
          <p className="text-[11px] text-text-secondary leading-relaxed">
            {preview}
            {isStreaming && <span className="text-neon-cyan animate-pulse">▌</span>}
          </p>
        ) : (
          <p className="text-[10px] text-text-dimmed italic">Waiting for output…</p>
        )}
      </div>
    </div>
  );
});

const AgentsPanel: React.FC<AgentsPanelProps> = memo(({ agentNames, isVibeMode, hasActiveRun }) => {
  const [showDrawer, setShowDrawer] = useState(false);
  const [expandedAgent, setExpandedAgent] = useState<string | null>(null);
  
  // Get streaming count from isolated store
  const streamingCount = useStreamContentStore((s) => 
    agentNames.filter(name => s.agentStreams[name]?.isStreaming).length
  );

  // Don't show if nothing to display
  if (!isVibeMode || !hasActiveRun || agentNames.length === 0) return null;

  const handleChipClick = (agentName: string) => {
    // Toggle: if same agent clicked, collapse; else expand new one
    setExpandedAgent(prev => prev === agentName ? null : agentName);
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
          onClick={() => setShowDrawer(true)}
          className={cn(
            "group relative text-[10px] font-medium px-3 py-1.5 rounded-lg transition-all duration-300 flex items-center gap-1.5",
            "bg-gradient-to-r from-neon-gold/20 to-neon-gold/10",
            "border border-neon-gold/50 text-neon-gold",
            "hover:from-neon-gold/30 hover:to-neon-gold/20 hover:border-neon-gold hover:shadow-glow-gold",
            "active:scale-95"
          )}
        >
          {/* Pulse indicator */}
          <span className="absolute -top-0.5 -right-0.5 size-2 rounded-full bg-neon-gold animate-ping opacity-75" />
          <span className="absolute -top-0.5 -right-0.5 size-2 rounded-full bg-neon-gold" />
          
          {/* Icon */}
          <svg className="size-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M4 5a1 1 0 011-1h14a1 1 0 011 1v2a1 1 0 01-1 1H5a1 1 0 01-1-1V5zM4 13a1 1 0 011-1h6a1 1 0 011 1v6a1 1 0 01-1 1H5a1 1 0 01-1-1v-6zM16 13a1 1 0 011-1h2a1 1 0 011 1v6a1 1 0 01-1 1h-2a1 1 0 01-1-1v-6z" />
          </svg>
          <span>Dashboard</span>
          <svg className="size-3 transition-transform group-hover:translate-x-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
          </svg>
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
      
      {/* Inline Expanded Agent Card */}
      {expandedAgent && (
        <AgentExpandedCard 
          agentName={expandedAgent} 
          onClose={() => setExpandedAgent(null)} 
        />
      )}

      {/* Full Dashboard Drawer */}
      <AgentDashboardDrawer 
        open={showDrawer} 
        onClose={() => setShowDrawer(false)}
        agentNames={agentNames}
      />
    </div>
  );
});

// Agent detail modal using isolated stream store (reserved for future use)
export const AgentDetailModalWithStream: React.FC<{ agentName: string; onBack: () => void }> = ({ agentName, onBack }) => {
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

// All agents modal using isolated stream store (reserved for future use)
export const AllAgentsModalWithStream: React.FC<{ 
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

// System message bubble for interruption responses
const SystemMessageBubble: React.FC<{ message: ChatMessage; formatTime: (timestamp: number) => string }> = memo(({ message, formatTime }) => {
  return (
    <div className="mx-auto max-w-xl animate-fade-in">
      <div className="rounded-lg border border-neon-violet/30 bg-neon-violet/10 px-4 py-3">
        <div className="flex items-center gap-2 mb-2">
          <div className="size-5 rounded flex items-center justify-center bg-neon-violet/20">
            <svg className="w-3 h-3 text-neon-violet" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          </div>
          <span className="text-[10px] font-mono text-neon-violet tracking-wider">SYSTEM</span>
          <span className="text-[9px] text-text-dimmed font-mono tabular-nums ml-auto">
            {formatTime(message.timestamp)}
          </span>
        </div>
        <p className="text-sm text-text-primary leading-relaxed">{message.content}</p>
      </div>
    </div>
  );
});

const MessageBubble: React.FC<MessageBubbleProps> = ({ message, index, formatTime }) => {
  const [collapsed, setCollapsed] = useState(false);
  const isAgent = message.role === 'agent';
  const isSystem = message.role === 'system';

  // Render system messages with special bubble
  if (isSystem) {
    return <SystemMessageBubble message={message} formatTime={formatTime} />;
  }

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
  const bubbleRef = useRef<HTMLDivElement | null>(null);
  
  // FINE-GRAINED SUBSCRIPTION: Only re-renders when THIS agent's stream changes
  const streamData = useStreamContentStore(selectAgentStream(agentName));
  const currentSessionId = useSisyphusStore((s) => s.currentSessionId);
  
  // Derive display values from stream data
  const content = streamData?.content || '';
  const isStreaming = streamData?.isStreaming || false;
  const isFinal = streamData?.isFinal || false;
  const tokenCount = streamData?.tokenCount || 0;
  const providerName = streamData?.providerName;
  const stepName = streamData?.stepName;

  useEffect(() => {
    if (!content && !isStreaming) return;
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:currentSessionId || '',runId:'',hypothesisId:'H63',location:'interaction-stream.tsx:ResearchAssistantBubble',message:'ra_stream_state',data:{agentName,contentLen:content.length,isStreaming,isFinal,tokenCount},timestamp:Date.now()})}).catch(()=>{});
    // #endregion
  }, [content, isStreaming, isFinal, tokenCount, agentName, currentSessionId]);

  useLayoutEffect(() => {
    if (!content && !isStreaming) return;
    const el = bubbleRef.current;
    if (!el || typeof window === 'undefined') return;
    const rect = el.getBoundingClientRect();
    const style = window.getComputedStyle(el);
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:currentSessionId || '',runId:'',hypothesisId:'H80',location:'interaction-stream.tsx:ResearchAssistantBubble',message:'ra_dom_state',data:{agentName,width:Math.round(rect.width),height:Math.round(rect.height),display:style.display,visibility:style.visibility,opacity:style.opacity,offsetParent:!!el.offsetParent,scrollHeight:el.scrollHeight},timestamp:Date.now()})}).catch(()=>{});
    // #endregion
  }, [content, isStreaming, isFinal, collapsed, agentName, currentSessionId]);

  useLayoutEffect(() => {
    if (!content && !isStreaming) return;
    const el = bubbleRef.current;
    if (!el || typeof window === 'undefined') return;
    const style = window.getComputedStyle(el);
    const className = typeof el.className === 'string' ? el.className : String(el.className || '');
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:currentSessionId || '',runId:'',hypothesisId:'H96',location:'interaction-stream.tsx:ResearchAssistantBubble',message:'ra_anim_state',data:{agentName,opacity:style.opacity,transform:style.transform,animationName:style.animationName,animationDuration:style.animationDuration,animationDelay:style.animationDelay,animationPlayState:style.animationPlayState,animationFillMode:style.animationFillMode,animationTiming:style.animationTimingFunction,className:className.slice(0,120),classNameLen:className.length},timestamp:Date.now()})}).catch(()=>{});
    // #endregion
    const rafId = window.requestAnimationFrame(() => {
      const nextStyle = window.getComputedStyle(el);
      const parent = el.parentElement;
      const parentStyle = parent ? window.getComputedStyle(parent) : null;
      // #region agent log
      fetch('http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:currentSessionId || '',runId:'',hypothesisId:'H97',location:'interaction-stream.tsx:ResearchAssistantBubble',message:'ra_anim_next_frame',data:{agentName,opacity:nextStyle.opacity,transform:nextStyle.transform,animationName:nextStyle.animationName,animationPlayState:nextStyle.animationPlayState,parentOpacity:parentStyle?.opacity ?? '',parentVisibility:parentStyle?.visibility ?? '',parentPointerEvents:parentStyle?.pointerEvents ?? ''},timestamp:Date.now()})}).catch(()=>{});
      // #endregion
    });
    return () => window.cancelAnimationFrame(rafId);
  }, [content, isStreaming, isFinal, collapsed, agentName, currentSessionId]);
  
  const preview = useMemo(() => {
    const text = content.replace(/\s+/g, ' ').trim();
    if (text.length <= 220) return text;
    return text.slice(0, 220) + '…';
  }, [content]);
  
  // Don't render if no content
  if (!content && !isStreaming) return null;
  
  return (
    <>
      <div ref={bubbleRef} className="bg-bg-surface/50 border border-border-subtle rounded-2xl animate-fade-in overflow-hidden">
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
  const scrollLogCountRef = useRef(0);

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

  useEffect(() => {
    const container = scrollContainerRef.current;
    if (!container || scrollLogCountRef.current >= 5) return;
    const endRect = scrollEndRef.current?.getBoundingClientRect();
    scrollLogCountRef.current += 1;
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:sessionId || '',runId:'',hypothesisId:'H81',location:'interaction-stream.tsx:InteractionStream',message:'scroll_state',data:{hasRaContent,messagesCount:messages.length,raTokenCount,scrollTop:Math.round(container.scrollTop),scrollHeight:container.scrollHeight,clientHeight:container.clientHeight,endTop:endRect ? Math.round(endRect.top) : null},timestamp:Date.now()})}).catch(()=>{});
    // #endregion
  }, [messages.length, raTokenCount, isSending, hasRaContent, sessionId]);

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
  const agentNamesKey = useMemo(() => otherAgentNames.join('|'), [otherAgentNames]);

  useEffect(() => {
    const store = useStreamContentStore.getState();
    const streamKeys = Object.keys(store.agentStreams);
    const summary = otherAgentNames.map((agent) => {
      const data = store.agentStreams[agent];
      return {
        agent,
        hasContent: Boolean(data?.content),
        contentLen: data?.content?.length ?? 0,
        isStreaming: data?.isStreaming ?? false,
        isFinal: data?.isFinal ?? false,
      };
    });
    const active = summary.filter(s => s.hasContent || s.isStreaming || s.isFinal);
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:sessionId || '',runId:currentRunId || '',hypothesisId:'H103',location:'interaction-stream.tsx:InteractionStream',message:'agents_panel_snapshot',data:{rosterCount:otherAgentNames.length,agentNames:otherAgentNames.slice(0,8),streamKeys:streamKeys.slice(0,8),activeCount:active.length,activeAgents:active.map(a=>a.agent).slice(0,8),streamingCount:active.filter(a=>a.isStreaming).length,finalCount:active.filter(a=>a.isFinal).length},timestamp:Date.now()})}).catch(()=>{});
    // #endregion
  }, [sessionId, currentRunId, agentNamesKey]);

  return (
    <div className="card p-5 flex flex-col h-full relative overflow-hidden cyber-corners">
      {/* Background */}
      <div className="absolute top-0 left-0 w-full h-32 bg-neon-cyan/5 blur-3xl pointer-events-none" />
      <div className="absolute bottom-0 right-0 w-64 h-64 bg-neon-gold/5 blur-3xl pointer-events-none" />
      
      {/* Header */}
      <div className="flex items-center justify-between mb-5 relative z-10">
        <div className="flex items-center gap-4">
          <div className="size-10 rounded-lg bg-neon-cyan shadow-glow-cyan flex items-center justify-center">
            <svg className="size-5 text-bg-base" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
            </svg>
          </div>
          <div>
            <h2 className="font-display text-sm font-semibold text-neon-cyan tracking-wider text-balance">
              Interaction Stream
            </h2>
            <p className="text-xs text-text-muted text-pretty">Research dialogue with Sisyphus</p>
          </div>
        </div>
        
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

      {/* Agents Panel (horizontal chips with inline expand + drawer) */}
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
