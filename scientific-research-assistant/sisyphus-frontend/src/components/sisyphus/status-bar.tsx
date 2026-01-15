import React from 'react';
import { useSisyphusStore } from '@/store/sisyphus-store';
import { cn } from '@/lib/utils';

interface StatusBarProps {
  sessionId: string;
}

const StatusBar: React.FC<StatusBarProps> = ({}) => {
  const { isConnected } = useSisyphusStore();

  const status = isConnected ? 'connected' : 'idle';

  return (
    <footer className="h-9 flex items-center justify-between px-6 text-xs border-t border-border-subtle bg-surface/80 backdrop-blur-xl relative overflow-hidden neon-line-top">
      {/* Scan effect */}
      <div className="absolute top-0 left-0 right-0 h-full overflow-hidden pointer-events-none">
        <div className="w-20 h-full bg-neon-cyan/5 animate-scan-line" />
      </div>
      
      {/* Left - Connection Status */}
      <div className="flex items-center gap-2 relative z-10">
        <div className={cn(
          "status-dot",
          status === 'connected' ? "status-dot-active" : "status-dot-idle"
        )} />
        <span className={cn(
          "font-mono uppercase tracking-wider",
          status === 'connected' ? "text-neon-green text-glow-cyan" : "text-text-muted"
        )}>
          {status}
        </span>
      </div>
      
      {/* Right - Version */}
      <div className="flex items-center gap-4 relative z-10">
        <div className="flex items-center gap-1.5">
          <div className="w-1.5 h-1.5 rounded-full bg-neon-cyan animate-pulse" />
          <div className="w-1.5 h-1.5 rounded-full bg-neon-gold animate-pulse" style={{ animationDelay: '0.2s' }} />
          <div className="w-1.5 h-1.5 rounded-full bg-neon-purple animate-pulse" style={{ animationDelay: '0.4s' }} />
        </div>
        
        <span className="font-display text-[10px] text-text-dimmed tracking-[0.2em]">
          SISYPHUS <span className="text-neon-purple">v.0.1.0</span>
        </span>
      </div>
    </footer>
  );
};

export default StatusBar;
