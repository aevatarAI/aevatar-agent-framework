import React, { useState } from 'react';
import { useSisyphusStore } from '@/store/sisyphus-store';
import { cn } from '@/lib/utils';

// Format ISO timestamp to relative or absolute time
function formatSessionTime(isoString?: string): string {
  if (!isoString) return '';
  try {
    const date = new Date(isoString);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMin = Math.floor(diffMs / 60000);
    const diffHr = Math.floor(diffMs / 3600000);
    const diffDay = Math.floor(diffMs / 86400000);
    
    if (diffMin < 1) return 'Just now';
    if (diffMin < 60) return `${diffMin}m ago`;
    if (diffHr < 24) return `${diffHr}h ago`;
    if (diffDay < 7) return `${diffDay}d ago`;
    
    // Absolute date for older sessions
    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  } catch {
    return '';
  }
}

// ============================================================
//  Sidebar - Navigation and Session Management
// ============================================================

interface SidebarProps {
  onCreateSession: () => void;
  onSelectSession: (sessionId: string) => void;
  onRefreshSessions: () => void;
  onOpenSettings: () => void;
  activeView: 'chat' | 'settings';
}

const Sidebar: React.FC<SidebarProps> = ({
  onCreateSession,
  onSelectSession,
  onRefreshSessions,
  onOpenSettings,
  activeView,
}) => {
  // FINE-GRAINED SUBSCRIPTIONS: Only subscribe to what we need
  const sessions = useSisyphusStore((s) => s.sessions);
  const currentSessionId = useSisyphusStore((s) => s.currentSessionId);
  const [collapsed, setCollapsed] = useState(false);

  const handleCreateSession = () => {
    onCreateSession();
  };

  return (
    <aside
      className={cn(
        'flex flex-col border-r border-border-subtle bg-surface/60 backdrop-blur-sm transition-all duration-300 relative',
        collapsed ? 'w-16' : 'w-72'
      )}
    >
      {/* Top Actions */}
      <div className={cn('p-3 space-y-2', collapsed && 'px-2')}>
        {/* Toggle Button Row */}
        <div className="flex items-center">
          <button
            onClick={() => setCollapsed(!collapsed)}
            aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
            className={cn(
              'p-2.5 rounded-lg',
              'text-text-muted hover:text-neon-cyan hover:bg-surface-elevated',
              'transition-all duration-200'
            )}
          >
            <svg
              className={cn('size-5 transition-transform duration-200', collapsed && 'rotate-180')}
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={1.5}
                d="M11 19l-7-7 7-7m8 14l-7-7 7-7"
              />
            </svg>
          </button>
        </div>

        {/* New Chat Button */}
        <button
          onClick={handleCreateSession}
          aria-label="Create new chat session"
          className={cn('btn-primary w-full justify-center', collapsed && 'p-2.5')}
        >
          <svg className="size-4 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          {!collapsed && <span className="font-semibold">New Chat</span>}
        </button>

        {/* Settings Button */}
        <button
          onClick={onOpenSettings}
          aria-label="Open settings"
          className={cn(
            'w-full px-3 py-2.5 rounded-lg text-sm font-mono transition-all duration-200',
            'flex items-center justify-center gap-2',
            activeView === 'settings'
              ? 'bg-neon-cyan/10 text-neon-cyan border border-neon-cyan/40'
              : 'text-text-primary bg-surface hover:text-neon-cyan hover:bg-surface-elevated border border-border-default',
            collapsed && 'p-2.5'
          )}
        >
          <svg className="size-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={1.5}
              d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z"
            />
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={1.5}
              d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"
            />
          </svg>
          {!collapsed && <span>Settings</span>}
        </button>
      </div>

      {/* Sessions List */}
      {!collapsed && (
        <div className="flex-1 overflow-y-auto pb-4 px-3">
          <div className="flex items-center justify-between mb-3 px-1">
            <span className="text-[10px] text-neon-cyan font-display tracking-widest uppercase">
              Sessions
            </span>
            <button
              onClick={onRefreshSessions}
              aria-label="Refresh sessions"
              className="icon-btn p-1.5 hover:text-neon-cyan"
            >
              <svg className="size-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={1.5}
                  d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
                />
              </svg>
            </button>
          </div>

          <div className="space-y-2">
            {sessions.length === 0 ? (
              <div className="text-center py-8">
                <div className="size-12 mx-auto rounded-lg bg-surface-elevated flex items-center justify-center mb-3 border border-border-default cyber-corners">
                  <svg
                    className="size-6 text-text-muted"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      strokeWidth={1.5}
                      d="M20 13V6a2 2 0 00-2-2H6a2 2 0 00-2 2v7m16 0v5a2 2 0 01-2 2H6a2 2 0 01-2-2v-5m16 0h-2.586a1 1 0 00-.707.293l-2.414 2.414a1 1 0 01-.707.293h-3.172a1 1 0 01-.707-.293l-2.414-2.414A1 1 0 006.586 13H4"
                    />
                  </svg>
                </div>
                <p className="text-sm text-text-muted font-mono text-pretty">No sessions yet</p>
              </div>
            ) : (
              sessions.map((session, index) => (
                <button
                  key={session.id}
                  onClick={() => onSelectSession(session.id)}
                  className={cn(
                    'w-full text-left p-4 rounded-lg transition-all duration-200 relative group',
                    session.id === currentSessionId && activeView === 'chat'
                      ? 'bg-neon-cyan/10 border border-neon-cyan/40 shadow-glow-cyan'
                      : 'bg-surface/50 hover:bg-surface-elevated border border-transparent hover:border-border-default'
                  )}
                  style={{ animationDelay: `${index * 50}ms` }}
                >
                  <div className="flex items-center gap-3 mb-2">
                    <div
                      className={cn(
                        'status-dot',
                        session.status === 'running' ? 'status-dot-active' : 'status-dot-idle'
                      )}
                    />
                    <span className="text-sm font-mono text-text-primary truncate tabular-nums">
                      {session.id.slice(0, 8)}...
                    </span>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-[10px] font-mono text-text-muted tabular-nums">
                      {formatSessionTime(session.createdAt)}
                    </span>
                    {session.status === 'running' && (
                      <span className="text-[10px] font-mono text-neon-green bg-neon-green/10 px-1.5 py-0.5 rounded">
                        LIVE
                      </span>
                    )}
                  </div>

                  {session.id === currentSessionId && activeView === 'chat' && (
                    <div className="absolute left-0 top-0 bottom-0 w-[2px] bg-neon-cyan rounded-full shadow-glow-cyan" />
                  )}
                </button>
              ))
            )}
          </div>
        </div>
      )}

    </aside>
  );
};

export default Sidebar;
