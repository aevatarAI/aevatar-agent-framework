import React from 'react';
import { useSisyphusStore } from '@/store/sisyphus-store';
import type { WorkerAgent } from '@/types';
import { cn } from '@/lib/utils';

interface WorkerStatusProps {
  sessionId: string;
  compact?: boolean;
}

// Workers data from store (no demo data)

const getWorkerIcon = (stepType: string) => {
  switch (stepType) {
    case 'search':
      return (
        <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
        </svg>
      );
    case 'extract':
      return (
        <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
        </svg>
      );
    case 'synthesize':
      return (
        <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" />
        </svg>
      );
    default:
      return (
        <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
        </svg>
      );
  }
};

// Compact Worker Card
const CompactWorkerCard: React.FC<{ worker: WorkerAgent }> = ({ worker }) => {
  const getStatusStyle = () => {
    if (worker.streaming) return { bg: 'var(--neon-cyan)', glow: 'var(--glow-cyan)' };
    switch (worker.status) {
      case 'completed': return { bg: 'var(--neon-green)', glow: 'var(--glow-green)' };
      case 'error': return { bg: 'var(--neon-red)', glow: 'none' };
      case 'running': return { bg: 'var(--neon-orange)', glow: 'none' };
      default: return { bg: 'var(--text-muted)', glow: 'none' };
    }
  };

  const statusStyle = getStatusStyle();

  return (
    <div className={cn(
      "flex-shrink-0 w-52 p-3 rounded-lg bg-surface/80 backdrop-blur-sm border transition-all duration-300 relative overflow-hidden group",
      worker.streaming 
        ? "border-neon-cyan/50 shadow-glow-cyan" 
        : "border-border-subtle hover:border-border-default"
    )}>
      {/* Scan line for streaming */}
      {worker.streaming && (
        <div className="absolute top-0 left-0 right-0 h-[2px] overflow-hidden">
          <div className="w-full h-full bg-neon-cyan animate-scan-line" />
        </div>
      )}
      
      <div className="relative z-10">
        <div className="flex items-center gap-2.5 mb-2">
          <div className={cn(
            "w-6 h-6 rounded flex items-center justify-center",
            worker.streaming 
              ? "bg-neon-cyan/20 text-neon-cyan" 
              : "bg-surface-accent text-text-muted"
          )}>
            {getWorkerIcon(worker.stepType ?? 'unknown')}
          </div>
          <span className="text-sm font-medium text-text-primary truncate flex-1">{worker.name}</span>
          <div 
            className={cn("w-2 h-2 rounded-full", worker.streaming && "animate-pulse")}
            style={{ backgroundColor: statusStyle.bg, boxShadow: statusStyle.glow }}
          />
        </div>
        
        <div className="text-[10px] text-text-muted truncate mb-1.5 font-mono tabular-nums">
          {worker.provider} · <span className="text-neon-purple">{worker.tokenIndex.toLocaleString()}</span> tok
        </div>
        
        <div className={cn(
          "text-[10px] text-text-dimmed truncate h-4",
          worker.streaming && "typing-cursor"
        )}>
          {worker.streamContent || worker.lastResponse || "Idle"}
        </div>
        
        {/* Progress bar */}
        {worker.streaming && (
          <div className="mt-2 h-1 bg-surface-accent rounded-full overflow-hidden">
            <div 
              className="h-full bg-neon-cyan rounded-full animate-pulse"
              style={{ width: '60%' }}
            />
          </div>
        )}
      </div>
    </div>
  );
};

// Full Worker Card
const WorkerCard: React.FC<{ worker: WorkerAgent; index: number }> = ({ worker, index }) => {
  const getStatusBadge = () => {
    if (worker.streaming) return 'badge-cyan';
    switch (worker.status) {
      case 'completed': return 'badge-green';
      case 'error': return 'badge-red';
      default: return 'badge';
    }
  };

  const getStatusText = () => {
    if (worker.streaming) return 'STREAMING';
    return (worker.status ?? 'PENDING').toUpperCase();
  };

  return (
    <div
      className={cn(
        "worker-card relative animate-fade-in",
        worker.streaming && "streaming"
      )}
      style={{ animationDelay: `${index * 0.05}s` }}
    >
      <div className="p-4">
        <div className="flex items-center justify-between mb-3">
          <div className="flex items-center gap-3">
            <div className={cn(
              "w-8 h-8 rounded-lg flex items-center justify-center",
              worker.streaming ? "bg-neon-cyan/20 text-neon-cyan" : "bg-surface-accent text-text-muted"
            )}>
              {getWorkerIcon(worker.stepType ?? 'unknown')}
            </div>
            <div>
              <h3 className="text-sm font-semibold text-text-primary">{worker.name}</h3>
              <span className="text-[10px] text-text-dimmed font-mono tracking-wider">{(worker.stepType ?? 'unknown').toUpperCase()}</span>
            </div>
          </div>
          <span className={cn("badge text-[10px]", getStatusBadge())}>
            {getStatusText()}
          </span>
        </div>
        
        <div className="flex items-center gap-3 text-xs text-text-muted mb-3 tabular-nums">
          <span className="text-neon-purple">{worker.provider}</span>
          <span className="text-text-dimmed">·</span>
          <span className="font-mono text-neon-cyan">{worker.tokenIndex.toLocaleString()} tokens</span>
        </div>
        
        <div className={cn(
          "text-xs p-3 rounded-lg bg-surface/50 border border-border-subtle min-h-[52px] max-h-[72px] overflow-hidden",
          worker.streaming && "typing-cursor border-neon-cyan/30"
        )}>
          <span className="text-text-secondary">
            {worker.streamContent || worker.lastResponse || "Waiting for task..."}
          </span>
        </div>
      </div>
    </div>
  );
};

const WorkerStatus: React.FC<WorkerStatusProps> = ({ compact = false }) => {
  // FINE-GRAINED SUBSCRIPTION: Only subscribe to workers
  const storeWorkers = useSisyphusStore((s) => s.workers);
  
  const workers: WorkerAgent[] = Object.values(storeWorkers);
  const activeCount = workers.filter((w: WorkerAgent) => w.streaming || w.status === 'running').length;
  const totalTokens = workers.reduce((sum: number, w: WorkerAgent) => sum + w.tokenIndex, 0);

  // Compact horizontal layout
  if (compact) {
    return (
      <div className="card p-4 relative overflow-hidden cyber-corners">
        {/* Decorative glow */}
        <div className="absolute top-0 right-0 w-24 h-24 bg-neon-cyan/10 blur-2xl pointer-events-none" />
        
        <div className="flex items-center justify-between mb-3 relative z-10">
          <div className="flex items-center gap-3">
            <div className="size-8 rounded-lg bg-neon-cyan/20 flex items-center justify-center border border-neon-cyan/30">
              <svg className="size-4 text-neon-cyan" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
              </svg>
            </div>
            <span className="font-display text-xs font-medium text-text-primary tracking-wider">
              Live Workers
              <span className="ml-2 text-neon-cyan text-glow-cyan tabular-nums">{activeCount}</span>
            </span>
          </div>
          <span className="text-[10px] text-neon-gold font-mono tabular-nums">
            {totalTokens.toLocaleString()} tok
          </span>
        </div>
        
        <div className="flex gap-3 overflow-x-auto pb-2 relative z-10">
          {workers.map((worker: WorkerAgent) => (
            <CompactWorkerCard key={worker.id} worker={worker} />
          ))}
        </div>
      </div>
    );
  }

  // Full grid layout
  return (
    <div className="card p-5 cyber-corners">
      <div className="flex items-center justify-between mb-5">
        <div className="flex items-center gap-4">
          <div className="size-10 rounded-lg bg-neon-cyan flex items-center justify-center shadow-glow-cyan">
            <svg className="size-5 text-bg-base" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
            </svg>
          </div>
          <div>
            <h2 className="font-display text-sm font-semibold text-neon-cyan tracking-wider text-balance">LIVE WORKERS</h2>
            <p className="text-xs text-text-muted tabular-nums">{activeCount} agents active</p>
          </div>
        </div>
        
        <button aria-label="View all workers" className="btn-ghost text-xs group">
          <span className="group-hover:text-neon-cyan transition-colors">View All</span>
          <svg className="size-3 ml-1 group-hover:translate-x-1 transition-transform" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
          </svg>
        </button>
      </div>

      <div className="grid grid-cols-2 gap-4">
        {workers.map((worker: WorkerAgent, index: number) => (
          <WorkerCard key={worker.id} worker={worker} index={index} />
        ))}
      </div>
      
      <div className="flex items-center justify-between mt-5 pt-4 border-t border-border-subtle text-xs">
        <div className="flex items-center gap-6">
          <span className="text-text-muted">
            Total Tokens: <span className="text-neon-cyan font-mono tabular-nums text-glow-cyan">
              {totalTokens.toLocaleString()}
            </span>
          </span>
          <span className="text-text-muted">
            Avg Latency: <span className="text-neon-green font-mono tabular-nums">42ms</span>
          </span>
        </div>
        <span className="text-text-dimmed font-mono tabular-nums flex items-center gap-2">
          <div className="size-1.5 rounded-full bg-neon-green animate-pulse" />
          Last sync: 2s ago
        </span>
      </div>
    </div>
  );
};

export default WorkerStatus;
