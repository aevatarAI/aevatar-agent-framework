import React, { useEffect, useCallback, useState, useRef } from 'react';
import { Header, Sidebar, InteractionStream, WorkflowTopology, StatusBar } from '@/components/sisyphus';
import { useSisyphusStore } from '@/store/sisyphus-store';
import { useAxiomStream } from '@/hooks/use-axiom-stream';
import { useAgentStates } from '@/hooks/use-agent-states';
import { useSessionStatus } from '@/hooks/use-session-status';
import { listSessions, createSession, getDagSnapshot, getSessionEvents, parseWorkersFromEvents, abortCurrentSessionRequests, getSessionStatus, type AxiomSession } from '@/lib/axiom-client';
import type { DAGGraph } from '@/types';
import { cn } from '@/lib/utils';
import { useAuthStore } from '@/store/auth-store';

// Panel collapse threshold (percentage)
const COLLAPSE_THRESHOLD = 20;
const DEFAULT_LEFT_WIDTH = 50;

// Parse protobuf Timestamp or ISO string to ISO string
function parseTimestamp(val: unknown): string {
  if (!val) return '';
  if (typeof val === 'string') return val;
  if (typeof val === 'object' && val !== null && 'seconds' in val) {
    const ts = val as { seconds?: number; nanos?: number };
    if (ts.seconds) return new Date(ts.seconds * 1000).toISOString();
  }
  return '';
}

// Derive lifecycle status from backend SessionStatus (proto enum: 0=unspecified, 1=active, 2=paused, 3=archived)
// The value may arrive as a number (protobuf JSON) or a string.
function deriveLifecycleStatus(status?: unknown): 'active' | 'paused' | 'archived' {
  if (status == null) return 'active';
  // Numeric protobuf enum values
  if (typeof status === 'number') {
    if (status === 2) return 'paused';
    if (status === 3) return 'archived';
    return 'active';
  }
  // String values (camelCase enum name or lowercase)
  if (typeof status === 'string') {
    const s = status.toLowerCase();
    if (s === 'paused' || s === 'auto_paused' || s === 'session_status_paused') return 'paused';
    if (s === 'archived' || s === 'terminated' || s === 'session_status_archived') return 'archived';
  }
  return 'active';
}

// Shared mapper: backend AxiomSession → frontend SisyphusSession
function mapAxiomSession(s: AxiomSession) {
  return {
    id: s.sessionId,
    status: "pending" as "pending" | "running" | "completed" | "failed",
    phase: s.phase || "",
    progressPercent: s.progressPercent || 0,
    totalTokens: s.totalTokens || 0,
    totalLlmCalls: s.totalLlmCalls || 0,
    createdAt: parseTimestamp(s.createdAt),
    ownerId: s.ownerId || '',
    ownerName: s.ownerName || '',
    lifecycleStatus: deriveLifecycleStatus(s.status),
  };
}

// Transform backend DAG format to frontend format
// Backend uses fromId/toId, frontend expects source/target
const transformDagData = (rawData: unknown): DAGGraph | null => {
  if (!rawData || typeof rawData !== 'object') return null;
  const data = rawData as { nodes?: unknown[]; edges?: { fromId?: string; toId?: string; source?: string; target?: string }[] };
  if (!data.nodes || !data.edges) return null;
  
  const transformedEdges = data.edges.map(edge => ({
    source: edge.fromId || edge.source || '',
    target: edge.toId || edge.target || '',
  }));
  
  return {
    nodes: data.nodes as DAGGraph['nodes'],
    edges: transformedEdges,
  };
};

const App: React.FC = () => {
  const { currentSessionId, isConnected, setSessions, setCurrentSession, resetForNewSession, setDag, updateWorker, restoreMilestoneForSession, setActiveMilestoneNodeId, restoreRunningSession } = useSisyphusStore();
  
  // Resizable panel state
  const [leftPanelWidth, setLeftPanelWidth] = useState(DEFAULT_LEFT_WIDTH);
  const [rightCollapsed, setRightCollapsed] = useState(false);
  const isDragging = useRef(false);
  const containerRef = useRef<HTMLDivElement>(null);

  // Connect to AG-UI event stream
  useAxiomStream({ sessionId: currentSessionId, enabled: true });
  
  // Poll agent states API (5s interval) for precise token usage and history
  useAgentStates({
    sessionId: currentSessionId,
    intervalMs: 5000,
    includeHistory: true,
    historyLimit: 50,
    enabled: isConnected,  // Only poll when connected
  });
  
  // Poll session status API (3s interval) for workflow steps, tools, agent status
  useSessionStatus({
    sessionId: currentSessionId,
    intervalMs: 3000,
    enabled: isConnected,  // Only poll when connected
  });

  // Fetch sessions on mount (only once)
  useEffect(() => {
    const fetchSessions = async () => {
      try {
        const data = await listSessions();
        const mapped = data.map(mapAxiomSession);
        setSessions(mapped);

        // Auto-select first session if available
        if (mapped.length > 0) {
          const firstSessionId = mapped[0].id;
          setCurrentSession(firstSessionId);
          
          // Fetch DAG for initial session
          try {
            const rawDag = await getDagSnapshot(firstSessionId);
            const dagData = transformDagData(rawDag);
            if (dagData) {
              setDag(dagData);
            }
          } catch {
            // DAG might not exist yet
          }
          
          // Load historical workers
          try {
            const eventsText = await getSessionEvents(firstSessionId);
            const workersMap = parseWorkersFromEvents(eventsText);
            workersMap.forEach((worker) => {
              updateWorker({
                id: worker.id,
                name: worker.name,
                status: worker.status,
                provider: worker.provider,
                stepId: worker.stepId,
                stepType: worker.stepType,
                tokenIndex: worker.tokenIndex,
                lastResponse: worker.lastResponse,
                history: worker.history,
              });
            });
          } catch {
            // Events might not exist
          }
          
          // Check if session has an active run (for page refresh scenarios)
          try {
            const status = await getSessionStatus(firstSessionId);
            if (status && status.runId && status.runId.length > 0) {
              restoreRunningSession({
                runId: status.runId,
                agents: status.agents || [],
              });
            }
          } catch {
            // Ignore status fetch errors on initial load
          }
        }
      } catch (error) {
        console.error("Failed to fetch sessions:", error);
      }
    };
    fetchSessions();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // Run only on mount

  // Drag handlers
  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    isDragging.current = true;
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
  }, []);

  useEffect(() => {
    const handleMouseMove = (e: MouseEvent) => {
      if (!isDragging.current || !containerRef.current) return;
      
      const container = containerRef.current;
      const rect = container.getBoundingClientRect();
      const newWidth = ((e.clientX - rect.left) / rect.width) * 100;
      
      // Auto-collapse right panel when dragged far right
      if (newWidth > 100 - COLLAPSE_THRESHOLD) {
        setRightCollapsed(true);
      } else {
        setRightCollapsed(false);
        setLeftPanelWidth(Math.min(85, Math.max(30, newWidth)));
      }
    };

    const handleMouseUp = () => {
      isDragging.current = false;
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
    };

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);

    return () => {
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
    };
  }, []);

  // Toggle right panel
  const toggleRightPanel = useCallback(() => {
    setRightCollapsed(prev => !prev);
    if (rightCollapsed) {
      setLeftPanelWidth(DEFAULT_LEFT_WIDTH);
    }
  }, [rightCollapsed]);

  // Handle session selection - reset workers and data for new session
  const handleSelectSession = useCallback(async (sessionId: string | null) => {
    if (sessionId !== currentSessionId) {
      // Abort any pending requests from previous session first
      abortCurrentSessionRequests();
      // Reset workers, DAG, messages when switching sessions
      resetForNewSession();
    }
    setCurrentSession(sessionId);

    if (!sessionId) return;

    // Restore the active milestone for this session (if any)
    restoreMilestoneForSession(sessionId);

    // Parallelize DAG and events fetching for better performance
    try {
      const [rawDag, eventsText] = await Promise.all([
        getDagSnapshot(sessionId).catch(err => {
          console.warn('[App] Failed to fetch DAG for session:', sessionId, err);
          return null;
        }),
        getSessionEvents(sessionId).catch(err => {
          console.warn('[App] Failed to load historical events:', err);
          return '';
        }),
      ]);

      // Process DAG data
      if (rawDag) {
        const dagData = transformDagData(rawDag);
        if (dagData) {
          setDag(dagData);
          
          // Detect active milestone from DAG nodes (planStatus === 'Active')
          // This ensures milestone is set even after page refresh
          const activeNode = dagData.nodes.find(
            (n: { planStatus?: string }) => n.planStatus === 'Active'
          );
          if (activeNode) {
            setActiveMilestoneNodeId(activeNode.id, sessionId);
          }
        }
      }

      // Process historical workers
      if (eventsText) {
        const workersMap = parseWorkersFromEvents(eventsText);
        workersMap.forEach((worker) => {
          updateWorker({
            id: worker.id,
            name: worker.name,
            status: worker.status,
            provider: worker.provider,
            stepId: worker.stepId,
            stepType: worker.stepType,
            tokenIndex: worker.tokenIndex,
            lastResponse: worker.lastResponse,
            history: worker.history,
          });
        });
      }
      
      // Check if session has an active run (for page refresh / session switch scenarios)
      try {
        const status = await getSessionStatus(sessionId);
        if (status && status.runId && status.runId.length > 0) {
          restoreRunningSession({
            runId: status.runId,
            agents: status.agents || [],
          });
        }
      } catch {
        // Ignore status fetch errors
      }
    } catch (err) {
      // AbortError is expected when switching sessions quickly
      if (err instanceof Error && err.name === 'AbortError') {
        return;
      }
      // Ignore other errors silently
    }
  }, [currentSessionId, resetForNewSession, setCurrentSession, setDag, updateWorker, restoreMilestoneForSession, setActiveMilestoneNodeId, restoreRunningSession]);

  // Create session handler - creates new session and switches to it
  const handleCreateSession = useCallback(async () => {
    try {
      const result = await createSession();
      if (result.ok && result.sessionId) {
        // Optimistic update: prepend new session to list immediately
        // This avoids cache hit issues from listSessions() returning stale data
        const currentUser = useAuthStore.getState().user;
        const newSession = {
          id: result.sessionId,
          status: "pending" as const,
          phase: "",
          progressPercent: 0,
          totalTokens: 0,
          totalLlmCalls: 0,
          createdAt: new Date().toISOString(),
          ownerId: currentUser?.id,
          ownerName: currentUser?.displayName || currentUser?.username,
          lifecycleStatus: 'active' as const,
        };
        
        // Get current sessions and prepend the new one
        const currentSessions = useSisyphusStore.getState().sessions;
        setSessions([newSession, ...currentSessions]);

        // Reset state and connect to new session
        resetForNewSession();
        setCurrentSession(result.sessionId);

        // Fetch global DAG data (shared across all sessions)
        try {
          const rawDag = await getDagSnapshot(result.sessionId);
          const dagData = transformDagData(rawDag);
          if (dagData) {
            setDag(dagData);
          }
        } catch (err) {
          console.warn('[App] Failed to fetch DAG for new session:', result.sessionId, err);
        }

      } else {
        console.error("Failed to create session:", result.error);
      }
    } catch (error) {
      console.error("Error creating session:", error);
    }
  }, [setSessions, setCurrentSession, resetForNewSession, setDag]);

  const handleRefreshSessions = useCallback(async () => {
    try {
      const data = await listSessions();
      const mapped = data.map(mapAxiomSession);
      setSessions(mapped);
    } catch (error) {
      console.error("Failed to refresh sessions:", error);
    }
  }, [setSessions]);

  return (
    <div className="h-dvh flex flex-col bg-gradient-subtle overflow-hidden relative">
      {/* Background grid */}
      <div className="absolute inset-0 bg-grid-animated pointer-events-none opacity-30" />
      
      {/* Cyber corner decorations */}
      <div className="absolute top-0 left-0 size-32 border-l-2 border-t-2 border-neon-cyan/30 pointer-events-none" />
      <div className="absolute top-0 right-0 size-32 border-r-2 border-t-2 border-neon-gold/30 pointer-events-none" />
      <div className="absolute bottom-0 left-0 size-32 border-l-2 border-b-2 border-neon-purple/30 pointer-events-none" />
      <div className="absolute bottom-0 right-0 size-32 border-r-2 border-b-2 border-neon-cyan/30 pointer-events-none" />
      
      <Header />
      
      <div className="flex flex-1 overflow-hidden relative z-10">
        <Sidebar
          onCreateSession={handleCreateSession}
          onSelectSession={handleSelectSession}
          onRefreshSessions={handleRefreshSessions}
        />
        
        <main ref={containerRef} className="flex-1 flex overflow-hidden bg-dots relative">
          {currentSessionId ? (
            <>
              {/* Left Panel */}
              <div 
                className="h-full flex flex-col overflow-hidden py-4 pl-4 pr-1 transition-all duration-300"
                style={{ width: rightCollapsed ? '100%' : `${leftPanelWidth}%` }}
              >
                <div className="flex-1 overflow-hidden">
                  <InteractionStream sessionId={currentSessionId} />
                </div>
              </div>

              {/* Resizable Divider - Hidden by default, visible on hover */}
              {!rightCollapsed && (
                <div
                  className="w-4 flex-shrink-0 group relative cursor-col-resize -mx-1.5"
                  onMouseDown={handleMouseDown}
                >
                  {/* Hover detection area */}
                  <div className="absolute inset-0" />
                  {/* Visible line on hover */}
                  <div className="absolute inset-y-0 left-1/2 -translate-x-1/2 w-0.5 bg-transparent group-hover:bg-neon-cyan/60 transition-all duration-200" />
                  {/* Glow effect on hover */}
                  <div className="absolute inset-y-0 left-1/2 -translate-x-1/2 w-4 bg-transparent group-hover:bg-neon-cyan/5 transition-all duration-200" />
                  {/* Center collapse button */}
                  <button
                    type="button"
                    aria-label="Collapse right panel"
                    onMouseDown={(e) => {
                      // Prevent starting a drag resize when clicking the button
                      e.preventDefault();
                      e.stopPropagation();
                    }}
                    onClick={(e) => {
                      e.preventDefault();
                      e.stopPropagation();
                      toggleRightPanel();
                    }}
                    className={cn(
                      "absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2",
                      "h-16 w-2 rounded-full",
                      "bg-transparent group-hover:bg-neon-cyan group-hover:shadow-glow-cyan",
                      "transition-all duration-200"
                    )}
                  >
                    <span className="sr-only">Collapse</span>
                  </button>
                </div>
              )}

              {/* Toggle Button - Only show when collapsed */}
              {rightCollapsed && (
                <button
                  onClick={toggleRightPanel}
                  aria-label="Show Workflow DAG"
                  className="absolute z-30 top-1/2 right-4 -translate-y-1/2 h-10 px-4 flex items-center gap-2 bg-bg-surface/90 backdrop-blur-md border border-neon-cyan/50 text-neon-cyan text-xs font-mono font-semibold rounded-lg hover:border-neon-cyan hover:bg-neon-cyan/15 hover:shadow-glow-cyan transition-all duration-200"
                >
                  <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
                  </svg>
                  <span className="tracking-widest">DAG</span>
                </button>
              )}

              {/* Right Panel */}
              {!rightCollapsed && (
                <div 
                  className="h-full overflow-hidden py-4 pl-1 pr-4 transition-all duration-300"
                  style={{ width: `${100 - leftPanelWidth}%` }}
                >
                  {/* key forces complete remount on session change, avoiding stale state issues */}
                  <WorkflowTopology key={currentSessionId} sessionId={currentSessionId} fullHeight />
                </div>
              )}
            </>
          ) : (
            <div className="flex-1 flex items-center justify-center">
              <div className="text-center animate-fade-in relative">
                {/* Decorative rings */}
                <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
                  <div className="size-48 rounded-full border border-neon-cyan/20 animate-pulse" />
                  <div className="absolute size-64 rounded-full border border-neon-gold/10" style={{ animationDelay: '0.5s' }} />
                </div>
                
                <div className="size-20 rounded-lg bg-neon-cyan flex items-center justify-center mx-auto mb-6 shadow-glow-cyan relative cyber-corners">
                  <svg className="size-10 text-bg-base relative z-10" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z" />
                  </svg>
                </div>
                <h2 className="font-display text-2xl font-bold text-neon-cyan text-glow-cyan tracking-wider mb-3 text-balance">NO ACTIVE SESSION</h2>
                <p className="text-sm text-text-secondary max-w-md mx-auto leading-relaxed text-pretty">
                  Initialize a new research session to begin exploring the frontiers of knowledge with <span className="text-neon-cyan">Sisyphus</span>.
                </p>
                
                <div className="mt-8 flex items-center justify-center gap-2">
                  <div className="size-2 rounded-full bg-neon-cyan animate-pulse" />
                  <div className="size-2 rounded-full bg-neon-gold animate-pulse" style={{ animationDelay: '0.2s' }} />
                  <div className="size-2 rounded-full bg-neon-purple animate-pulse" style={{ animationDelay: '0.4s' }} />
                </div>
              </div>
            </div>
          )}
        </main>
      </div>
      
      {currentSessionId && <StatusBar sessionId={currentSessionId} />}
    </div>
  );
};

export default App;
