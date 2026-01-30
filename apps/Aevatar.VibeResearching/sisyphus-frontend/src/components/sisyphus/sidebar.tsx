import React, { useState } from 'react';
import { useSisyphusStore } from '@/store/sisyphus-store';
import { useAuthStore } from '@/store/auth-store';
import { usePermission } from '@/hooks/use-permission';
import { cn } from '@/lib/utils';
import { Avatar } from '@/components/ui/avatar';
import { pauseSession, resumeSession, terminateSession } from '@/lib/axiom-client/session';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogFooter,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog';

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
//  Session Card - Individual session entry with lifecycle controls
// ============================================================

import type { SisyphusSession } from '@/types';

interface SessionCardProps {
  session: SisyphusSession;
  index: number;
  isSelected: boolean;
  onSelect: (sessionId: string) => void;
  onSetLifecycleStatus: (status: 'active' | 'paused' | 'archived', sessionId: string) => void;
}

const SessionCard: React.FC<SessionCardProps> = ({
  session,
  index,
  isSelected,
  onSelect,
  onSetLifecycleStatus,
}) => {
  const status = session.lifecycleStatus ?? 'active';
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [resumeNotice, setResumeNotice] = useState<string | null>(null);
  const currentUser = useAuthStore((s) => s.user);
  const { canManageSession } = usePermission();
  const canManage = canManageSession(session);

  // Resolve owner name: prefer backend ownerName, fallback to current user match
  const isMe = session.ownerId && currentUser && session.ownerId === currentUser.id;
  const ownerName = session.ownerName
    || (isMe ? (currentUser!.name || currentUser!.userName) : '');
  const ownerAvatarUrl = isMe ? currentUser!.avatarUrl : undefined;

  const handlePause = async (e: React.MouseEvent) => {
    e.stopPropagation();
    try {
      const result = await pauseSession(session.id);
      if (result.ok) onSetLifecycleStatus('paused', session.id);
    } catch (_) { /* SSE reconciles */ }
  };

  const handleResume = async (e: React.MouseEvent) => {
    e.stopPropagation();
    try {
      const result = await resumeSession(session.id);
      if (result.ok) {
        onSetLifecycleStatus('active', session.id);
        if (!result.autoResumed) {
          setResumeNotice('No research progress to resume from. Please enter a research direction in the chat to start.');
          setTimeout(() => setResumeNotice(null), 5000);
        }
      }
    } catch (_) { /* SSE reconciles */ }
  };

  const handleTerminateConfirm = async () => {
    setConfirmOpen(false);
    try {
      const result = await terminateSession(session.id);
      if (result.ok) onSetLifecycleStatus('archived', session.id);
    } catch (_) { /* SSE reconciles */ }
  };

  return (
    <div
      className={cn(
        'w-full text-left rounded-lg transition-all duration-200 relative group',
        isSelected
          ? 'bg-neon-cyan/10 border border-neon-cyan/40 shadow-glow-cyan'
          : 'bg-surface/50 hover:bg-surface-elevated border border-transparent hover:border-border-default'
      )}
      style={{ animationDelay: `${index * 50}ms` }}
    >
      {/* Status badge — top-right */}
      <div className="absolute top-3 right-3 z-10">
        {status === 'paused' ? (
          <span className="text-[10px] font-mono text-neon-orange bg-neon-orange/10 px-1.5 py-0.5 rounded border border-neon-orange/30">
            PAUSED
          </span>
        ) : status === 'archived' ? (
          <span className="text-[10px] font-mono text-text-muted bg-surface-elevated px-1.5 py-0.5 rounded border border-border-default">
            ENDED
          </span>
        ) : (
          <span className="text-[10px] font-mono text-neon-green bg-neon-green/10 px-1.5 py-0.5 rounded border border-neon-green/30">
            ACTIVE
          </span>
        )}
      </div>
      {/* Action icons — bottom-right, aligned with timestamp */}
      {isSelected && status !== 'archived' && canManage && (
        <div className="absolute bottom-3 right-3 z-10 flex items-center gap-1">
          {status === 'active' ? (
            <button
              onClick={handlePause}
              title="Pause"
              className="size-6 flex items-center justify-center rounded text-neon-orange hover:bg-neon-orange/15 hover:shadow-[0_0_8px_rgba(255,160,0,0.5)] border border-neon-orange/30 transition-all"
            >
              <svg className="size-3" fill="currentColor" viewBox="0 0 24 24"><rect x="6" y="4" width="4" height="16" rx="1" /><rect x="14" y="4" width="4" height="16" rx="1" /></svg>
            </button>
          ) : status === 'paused' ? (
            <button
              onClick={handleResume}
              title="Resume"
              className="size-6 flex items-center justify-center rounded text-neon-green hover:bg-neon-green/15 hover:shadow-[0_0_8px_rgba(0,255,135,0.5)] border border-neon-green/30 transition-all"
            >
              <svg className="size-3" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
            </button>
          ) : null}
          <button
            onClick={(e) => { e.stopPropagation(); setConfirmOpen(true); }}
            title="Terminate"
            className="size-6 flex items-center justify-center rounded text-neon-red hover:bg-neon-red/15 hover:shadow-[0_0_8px_rgba(255,50,50,0.5)] border border-neon-red/30 transition-all"
          >
            <svg className="size-3" fill="currentColor" viewBox="0 0 24 24"><rect x="6" y="6" width="12" height="12" rx="1" /></svg>
          </button>
        </div>
      )}

      <button
        onClick={() => onSelect(session.id)}
        className="w-full text-left p-4 pb-2 pr-20"
      >
        <div className="flex items-center gap-3 mb-2">
          <div
            className={cn(
              'status-dot',
              status === 'paused' || status === 'archived'
                ? 'status-dot-idle'
                : session.status === 'running' ? 'status-dot-active' : 'status-dot-idle'
            )}
          />
          <span className="text-sm font-mono text-text-primary truncate tabular-nums">
            {session.id.slice(0, 8)}...
          </span>
        </div>
        {ownerName && (
          <div className="flex items-center gap-1.5 mb-1.5">
            <Avatar src={ownerAvatarUrl} name={ownerName} size="sm" className="!size-5 !text-[8px]" />
            <span className="text-[10px] font-mono text-text-secondary truncate">{ownerName}</span>
          </div>
        )}
        <span className="text-[10px] font-mono text-text-muted tabular-nums">
          {formatSessionTime(session.createdAt)}
        </span>
      </button>


      {isSelected && (
        <div className="absolute left-0 top-0 bottom-0 w-[2px] bg-neon-cyan rounded-full shadow-glow-cyan" />
      )}

      {/* Resume notice toast */}
      {resumeNotice && (
        <div className="absolute left-0 right-0 -bottom-12 z-50 mx-1 animate-in fade-in slide-in-from-top-2 duration-300">
          <div className="relative flex items-start gap-2 px-3 py-2 rounded-lg bg-bg-tertiary/95 backdrop-blur border border-neon-cyan/30 shadow-[0_0_12px_rgba(0,255,249,0.15)]">
            <span className="text-[10px] font-mono text-neon-cyan leading-tight flex-1">
              {resumeNotice}
            </span>
            <button
              onClick={(e) => { e.stopPropagation(); setResumeNotice(null); }}
              className="text-text-muted hover:text-text-primary flex-shrink-0 mt-0.5"
            >
              <svg className="size-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        </div>
      )}

      {/* Terminate confirmation dialog */}
      <Dialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Terminate Session</DialogTitle>
            <DialogDescription>
              This will permanently end the session. Research progress is preserved but no new input will be accepted.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <button
              onClick={() => setConfirmOpen(false)}
              className="px-4 py-2 rounded-lg text-sm font-mono text-text-primary bg-surface-elevated hover:bg-surface border border-border-default transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleTerminateConfirm}
              className="px-4 py-2 rounded-lg text-sm font-mono text-neon-red bg-neon-red/10 hover:bg-neon-red/20 border border-neon-red/40 transition-colors"
            >
              Terminate
            </button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
};

// ============================================================
//  Sidebar - Navigation and Session Management
// ============================================================

interface SidebarProps {
  onCreateSession: () => void;
  onSelectSession: (sessionId: string) => void;
  onRefreshSessions: () => void;
}

// Collapsible section header
const SectionHeader: React.FC<{
  label: string;
  count: number;
  expanded: boolean;
  onToggle: () => void;
}> = ({ label, count, expanded, onToggle }) => (
  <button
    onClick={onToggle}
    className="w-full flex items-center gap-1.5 px-1 py-1.5 group"
  >
    <svg
      className={cn('size-3 text-text-muted transition-transform duration-200', expanded && 'rotate-90')}
      fill="none"
      stroke="currentColor"
      viewBox="0 0 24 24"
    >
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
    </svg>
    <span className="text-[10px] text-neon-cyan font-display tracking-widest uppercase">
      {label}
    </span>
    <span className="text-[10px] font-mono text-text-muted tabular-nums">
      ({count})
    </span>
  </button>
);

const Sidebar: React.FC<SidebarProps> = ({
  onCreateSession,
  onSelectSession,
  onRefreshSessions,
}) => {
  // FINE-GRAINED SUBSCRIPTIONS: Only subscribe to what we need
  const sessions = useSisyphusStore((s) => s.sessions);
  const currentSessionId = useSisyphusStore((s) => s.currentSessionId);
  const setLifecycleStatus = useSisyphusStore((s) => s.setLifecycleStatus);
  const currentUser = useAuthStore((s) => s.user);
  const { isAnonymous } = usePermission();
  const [collapsed, setCollapsed] = useState(false);
  const [myExpanded, setMyExpanded] = useState(true);
  const [otherExpanded, setOtherExpanded] = useState(true);

  // Split sessions by ownership
  const mySessions = sessions.filter((s) => s.ownerId && currentUser && s.ownerId === currentUser.id);
  const otherSessions = sessions.filter((s) => !s.ownerId || !currentUser || s.ownerId !== currentUser.id);

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

        {/* New Chat Button - Hidden for anonymous users */}
        {!isAnonymous && (
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
        )}

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
            <div className="space-y-3">
              {/* My Sessions */}
              <div>
                <SectionHeader
                  label="My Sessions"
                  count={mySessions.length}
                  expanded={myExpanded}
                  onToggle={() => setMyExpanded(!myExpanded)}
                />
                <div
                  className="grid transition-[grid-template-rows] duration-300 ease-in-out"
                  style={{ gridTemplateRows: myExpanded ? '1fr' : '0fr' }}
                >
                  <div className="overflow-hidden">
                    <div className="space-y-2 pt-1">
                      {mySessions.length === 0 ? (
                        <p className="text-[10px] font-mono text-text-muted px-1 py-2">No sessions</p>
                      ) : (
                        mySessions.map((session, index) => (
                          <SessionCard
                            key={session.id}
                            session={session}
                            index={index}
                            isSelected={session.id === currentSessionId}
                            onSelect={onSelectSession}
                            onSetLifecycleStatus={setLifecycleStatus}
                          />
                        ))
                      )}
                    </div>
                  </div>
                </div>
              </div>

              {/* Other Sessions */}
              <div>
                <SectionHeader
                  label="Other Sessions"
                  count={otherSessions.length}
                  expanded={otherExpanded}
                  onToggle={() => setOtherExpanded(!otherExpanded)}
                />
                <div
                  className="grid transition-[grid-template-rows] duration-300 ease-in-out"
                  style={{ gridTemplateRows: otherExpanded ? '1fr' : '0fr' }}
                >
                  <div className="overflow-hidden">
                    <div className="space-y-2 pt-1">
                      {otherSessions.length === 0 ? (
                        <p className="text-[10px] font-mono text-text-muted px-1 py-2">No sessions</p>
                      ) : (
                        otherSessions.map((session, index) => (
                          <SessionCard
                            key={session.id}
                            session={session}
                            index={index}
                            isSelected={session.id === currentSessionId}
                            onSelect={onSelectSession}
                            onSetLifecycleStatus={setLifecycleStatus}
                          />
                        ))
                      )}
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>
      )}

    </aside>
  );
};

export default Sidebar;
