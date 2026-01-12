import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { SraTransport } from "./transport/SraTransport";
import SharedApiKeyModal from "./panels/ApiKeyModal";

import { Activity, FileText, GitBranch, Plus, RefreshCw, Search, TestTube } from "lucide-react";
import Composer from "./panels/Composer";
import DagPanel from "./panels/DagPanel";
import TracePanel from "./panels/TracePanel";
import BriefPanel from "./panels/BriefPanel";
import DeliveryCenterPanel from "./panels/DeliveryCenterPanel";
import ComputePanel from "./panels/ComputePanel";
import { useWorkbenchController } from "./controller/useWorkbenchController";
import AgentCardsChatView from "./components/AgentCardsChatView";
import AgentHistoryModal from "./components/AgentHistoryModal";
import { SkillsMpModal } from "./components/SkillsMpModal";
import { ToolsCatalogModal } from "./components/ToolsCatalogModal";
import { WorkspaceModal } from "./components/WorkspaceModal";
import FilesPage from "./pages/FilesPage";

export interface SraWorkbenchAppProps {
  transport: SraTransport;
  ApiKeyModal?: React.ComponentType<ApiKeyModalProps>;
}

export interface ApiKeyModalProps {
  transport: SraTransport;
  open: boolean;
  defaultProvider: string;
  providers: string[];
  onClose: () => void;
  onSaved?: () => void;
}

// ============================================================
//  Shared Workbench root
//
//  中文说明：
//  - 复用 Web UI 的布局/交互，底层通过 transport 驱动 controller
//  - Query 约定：?view=files|dag&session=...（兼容 view=graph）
// ============================================================

export function SraWorkbenchApp(props: SraWorkbenchAppProps) {
  const { transport } = props;
  const ApiKeyModal = props.ApiKeyModal ?? SharedApiKeyModal;

  type RouteView = "" | "files" | "dag";
  type Route = { view: RouteView; session: string; path: string };

  const canUpdateUrl = useMemo(() => {
    // Only mutate URL in normal browsers; keep Obsidian (app://) conservative.
    return window.location.protocol === "http:" || window.location.protocol === "https:";
  }, []);

  const parseRouteFromLocation = useCallback((): Route => {
    try {
      const q = new URLSearchParams(window.location.search);
      const viewRaw = String(q.get("view") ?? "");
      const view: RouteView = viewRaw === "files" ? "files" : viewRaw === "dag" || viewRaw === "graph" ? "dag" : "";
      return {
        view,
        session: String(q.get("session") ?? "").trim(),
        path: String(q.get("path") ?? "").trim(),
      };
    } catch {
      return { view: "", session: "", path: "" };
    }
  }, []);

  const [route, setRoute] = useState<Route>(() => parseRouteFromLocation());
  const [agentHistoryOpen, setAgentHistoryOpen] = useState(false);
  const [agentHistoryAgent, setAgentHistoryAgent] = useState<string>("");
  const [skillsMpOpen, setSkillsMpOpen] = useState(false);
  const agentsCardRef = useRef<HTMLDetailsElement | null>(null);

  const navigate = useCallback(
    (next: Partial<Route>, opts?: { replace?: boolean }) => {
      setRoute((prev) => {
        const merged: Route = { ...prev, ...next };
        // Only Files view uses `path`
        if (merged.view !== "files") merged.path = "";

        if (canUpdateUrl) {
          const q = new URLSearchParams();
          if (merged.view) q.set("view", merged.view);
          if (merged.session) q.set("session", merged.session);
          if (merged.view === "files" && merged.path) q.set("path", merged.path);
          const search = q.toString();
          const url = `${window.location.pathname}${search ? `?${search}` : ""}${window.location.hash || ""}`;
          if (opts?.replace) window.history.replaceState(null, "", url);
          else window.history.pushState(null, "", url);
        }

        return merged;
      });
    },
    [canUpdateUrl],
  );

  useEffect(() => {
    // Keep route in sync with back/forward in Web host.
    if (!canUpdateUrl) return;
    const onPop = () => setRoute(parseRouteFromLocation());
    window.addEventListener("popstate", onPop);
    return () => window.removeEventListener("popstate", onPop);
  }, [canUpdateUrl, parseRouteFromLocation]);

  const pageView = route.view;
  const sessionFromQuery = route.session;
  const pathFromQuery = route.path;

  const ctrl = useWorkbenchController({ transport });
  const {
    apiInfo,
    sessions,
    sessionId,
    mode,
    setMode,
    isSending,
    store,
    status,
    runStatus,
    activeRunId,
    lastPrompt,
    lastError,
    tools,
    toolsOpen,
    setToolsOpen,
    toolSearch,
    setToolSearch,
    mcpReconnectBusy,
    skillsSyncBusy,
    skillsSyncNote,
    skillsSyncStatus,
    skillsSyncLogs,
    workspace,
    workspaceOpen,
    setWorkspaceOpen,
    apiKeyOpen,
    setApiKeyOpen,
    vibeBrief,
    vibeDag,
    setVibeDag,
    vibeTrace,
    vibeAgents,
    vibeDelivery,
    vibeComputeDecision,
    setVibeComputeDecision,
    agentProviders,
    setAgentProviders,
    agentProvidersBusy,
    setAgentProvidersBusy,
    agentProvidersError,
    setAgentProvidersError,
    refreshDeliverables,
    messageMetaRef,
    messagesEndRef,
    messagesScrollRef,
    chatAgentFilter,
    setChatAgentFilter,
    atBottom,
    hasNewActivity,
    setHasNewActivity,
    collapsedMessages,
    setCollapsedMessages,
    runSteps,
    connected,
    mcpTools,
    filteredTools,
    agentsInChat,
    parseAgUiMessageId,
    agentDisplayName,
    handleChatScroll,
    refreshInfo,
    createSession,
    reconnectMcp,
    syncSkillPacks,
    connectToSession,
    send,
  } = ctrl;

  useEffect(() => {
    // Support opening dedicated pages (e.g. DAG view) with a preselected session via query param.
    if (sessionFromQuery && !sessionId) {
      connectToSession(sessionFromQuery);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionFromQuery]);

  useEffect(() => {
    // URL 作为“可分享状态”的一部分：选中 session 后写回 query（仅 Web host 生效）。
    const sid = String(sessionId || "").trim();
    if (!sid) return;
    if (sid === String(route.session || "").trim()) return;
    navigate({ session: sid }, { replace: true });
  }, [navigate, route.session, sessionId]);

  return (
    <div className="flex h-screen bg-slate-50 text-slate-900 font-sans overflow-hidden">
        {/* Sidebar */}
        <div className="w-72 shrink-0 min-h-0 bg-white border-r border-slate-200 p-4 flex flex-col">
          <div className="flex items-center gap-2 mb-6">
            <TestTube className="text-indigo-600" />
            <div className="min-w-0">
              <div className="font-bold text-lg tracking-tight truncate">Sci-Assist</div>
              <div className="text-xs text-slate-500 truncate">AG-UI sessions (snapshot-first SSE)</div>
            </div>
          </div>
          <div className="space-y-4">
            <div>
              <div className="text-xs text-slate-500 mb-2">Pages</div>
              {(() => {
                const sid = sessionId || sessionFromQuery;
                const navItems = [
                  { key: "chat", label: "Chat", view: "" as const, icon: Activity, enabled: true, title: "Back to chat" },
                  { key: "files", label: "Files", view: "files" as const, icon: FileText, enabled: Boolean(sid) && transport.capabilities.filesApi, title: transport.capabilities.filesApi ? (sid ? "Browse and edit session files" : "Connect to a session first") : "Files are local-only." },
                  { key: "dag", label: "DAG", view: "dag" as const, icon: GitBranch, enabled: Boolean(sid), title: sid ? "Open DAG view" : "Connect to a session first" },
                ];
                return (
                  <div className="space-y-1">
                    {navItems.map((it) => {
                      const active = pageView === it.view;
                      const Icon = it.icon;
                      return (
                        <button
                          key={it.key}
                          type="button"
                          onClick={() => navigate({ view: it.view, session: sid })}
                          disabled={!it.enabled}
                          title={it.title}
                          className={`w-full flex items-center gap-3 p-2 rounded border text-sm transition disabled:opacity-50 ${
                            active
                              ? "bg-indigo-50 border-indigo-200 text-indigo-700"
                              : "bg-white border-slate-200 text-slate-700 hover:bg-slate-50"
                          }`}
                        >
                          <Icon size={16} className={active ? "text-indigo-600" : "text-slate-500"} />
                          <span className="truncate">{it.label}</span>
                        </button>
                      );
                    })}
                  </div>
                );
              })()}
            </div>
            {/* Sessions */}
            <div>
              <button onClick={createSession} className="flex items-center justify-center gap-2 w-full p-2 rounded bg-indigo-600 hover:bg-indigo-500 transition text-sm text-white">
                <Plus size={16} /> New session
              </button>
              <div className="mt-3 text-xs text-slate-500">Sessions</div>
              <div className="max-h-48 overflow-y-auto space-y-1">
                {sessions.length === 0 ? (
                  <div className="text-xs text-slate-500">No sessions yet.</div>
                ) : (
                  sessions.map((s) => (
                    <button
                      key={s.sessionId}
                      onClick={() => connectToSession(s.sessionId)}
                      className={`w-full text-left p-2 rounded border text-xs transition ${
                        s.sessionId === sessionId
                          ? "bg-indigo-50 border-indigo-200 text-indigo-700"
                          : "bg-white border-slate-200 text-slate-700 hover:bg-slate-50"
                      }`}
                    >
                      <div className="font-mono">{s.sessionId}</div>
                      <div className="text-[11px] text-slate-500 truncate">{s.createdAt}</div>
                    </button>
                  ))
                )}
              </div>
            </div>
          </div>
          <div className="mt-auto">
            <details className="mt-6 bg-slate-50 border border-slate-200 rounded-lg overflow-hidden" open>
              <summary className="cursor-pointer select-none px-3 py-2 text-xs text-slate-600 flex items-center justify-between">
                <span>Tools & MCP</span>
                <span className="font-mono text-slate-700">mcp {mcpTools.length} / all {tools.length}</span>
              </summary>
              <div className="p-3">
                <button onClick={() => setToolsOpen(true)} className="w-full text-xs bg-white hover:bg-slate-50 border border-slate-200 rounded px-2 py-2 transition flex items-center justify-center gap-2 disabled:opacity-50" disabled={!connected} title={connected ? "Browse tools" : "Connect to a session first"}>
                  <Search size={14} /> Browse tools
                </button>
                <button onClick={() => setWorkspaceOpen(true)} className="mt-2 w-full text-xs bg-white hover:bg-slate-50 border border-slate-200 rounded px-2 py-2 transition flex items-center justify-center gap-2 disabled:opacity-50" disabled={!connected} title={connected ? "View workspace state (facts/sources/graph)" : "Connect to a session first"}>
                  <FileText size={14} /> Workspace
                  <span className="ml-auto text-[10px] text-slate-500 font-mono">{workspace?.materials?.items?.length ?? 0}</span>
                </button>
                <div className="grid grid-cols-2 gap-2 mt-2">
                  <button
                    onClick={() => void reconnectMcp()}
                    className="text-xs bg-white hover:bg-slate-50 border border-slate-200 rounded px-2 py-2 transition disabled:opacity-50"
                    disabled={!connected || mcpReconnectBusy}
                    title={connected ? "Reconnect MCP server and refresh tools" : "Connect to a session first"}
                  >
                    {mcpReconnectBusy ? "Reconnecting…" : "Reconnect MCP"}
                  </button>
                  <button
                    onClick={() => void syncSkillPacks()}
                    className="text-xs bg-white hover:bg-slate-50 border border-slate-200 rounded px-2 py-2 transition disabled:opacity-50 inline-flex items-center justify-center gap-2"
                    disabled={skillsSyncBusy}
                    title="Sync local Agent Skills packs (git clone/pull). No restart needed."
                  >
                    <RefreshCw size={14} /> {skillsSyncBusy ? "Updating…" : "Update Skills"}
                  </button>
                </div>
                <button
                  onClick={() => setSkillsMpOpen(true)}
                  className="mt-2 w-full text-xs bg-white hover:bg-slate-50 border border-slate-200 rounded px-2 py-2 transition"
                  title="Search & install Agent Skills from SkillsMP (local-only)."
                >
                  SkillsMP
                </button>
                {skillsSyncNote && <div className="mt-2 text-[11px] text-slate-500 break-words">{skillsSyncNote}</div>}
                {(skillsSyncBusy || skillsSyncLogs.length > 0) && (
                  <div className="mt-2 max-h-28 overflow-auto rounded border border-slate-200 bg-white p-2 font-mono text-[10px] text-slate-600">
                    {skillsSyncBusy && (
                      <div className="mb-1 text-slate-500">
                        {skillsSyncStatus?.current?.step ? `step: ${String(skillsSyncStatus.current.step)}` : "step: initializing…"}
                      </div>
                    )}
                    {skillsSyncLogs.length === 0 ? (
                      <div className="text-slate-400">waiting for sync logs…</div>
                    ) : (
                      skillsSyncLogs.map((l, idx) => (
                        <div key={idx} className="whitespace-pre-wrap break-words">
                          {String(l?.message ?? "")}
                        </div>
                      ))
                    )}
                  </div>
                )}
              </div>
            </details>
            <div
              className={`mt-4 text-xs px-2 py-1 rounded border ${
                status === "Connected" ? "bg-emerald-50 text-emerald-700 border-emerald-200" : "bg-rose-50 text-rose-700 border-rose-200"
              }`}
            >
              Status: {status}
            </div>
            {lastError && <div className="mt-2 text-[11px] text-rose-600 break-words">{lastError}</div>}
            <div className="mt-2 text-[11px] text-slate-500">
              Default LLM: <span className="font-mono">{apiInfo?.llm?.default ?? "-"}</span>
            </div>
            <button
              onClick={() => setApiKeyOpen(true)}
              className="mt-2 w-full text-xs bg-white hover:bg-slate-50 border border-slate-200 rounded px-2 py-2 transition flex items-center justify-center gap-2 disabled:opacity-50"
              disabled={!transport.capabilities.revealApiKey}
              title={
                transport.capabilities.revealApiKey
                  ? "Configure API keys / providers"
                  : "Local-only (loopback sidecar required)."
              }
            >
              Set API Key
            </button>
          </div>
        </div>

        {/* Main */}
        <div className="flex-1 min-w-0 min-h-0 bg-slate-50 overflow-hidden">
          {pageView === "files" ? (
            <FilesPage transport={transport} sessionId={sessionId} sessionFromQuery={sessionFromQuery} initialPath={pathFromQuery} embedded />
          ) : pageView === "dag" ? (
            <div className="h-full flex flex-col overflow-hidden">
              <div className="h-14 flex items-center justify-between px-6 bg-white border-b border-slate-200">
                <div className="flex items-center gap-3 min-w-0">
                  <div className="font-semibold text-slate-900">DAG</div>
                  <div className="text-xs text-slate-500 truncate">
                    session: <span className="font-mono text-slate-900">{sessionId || sessionFromQuery || "(none)"}</span>
                  </div>
                </div>
                {runStatus && (
                  <span className="text-xs px-2 py-1 rounded bg-slate-50 border border-slate-200 text-slate-700 truncate">{runStatus}</span>
                )}
              </div>
              <div className="flex-1 min-h-0 overflow-auto p-4">
                <div className="max-w-6xl mx-auto">
                  <DagPanel transport={transport} sessionId={sessionId} connected={connected} snapshot={vibeDag} onSnapshot={(snap) => setVibeDag(snap)} />
                </div>
              </div>
            </div>
          ) : (
            <div className="flex min-w-0 min-h-0 h-full overflow-hidden">
              {/* Chat (center stage) */}
              <div className="flex-1 min-w-0 min-h-0 flex justify-center overflow-hidden">
            <div className="w-full max-w-3xl min-w-0 min-h-0 flex flex-col bg-white border-x border-slate-200">
              <header className="h-14 border-b border-slate-200 flex items-center justify-between px-6 bg-white/80 backdrop-blur">
                <div className="flex items-center gap-3 min-w-0">
                  <div className="text-sm font-medium text-slate-600 truncate">
                    Session: <span className="font-mono text-slate-900">{sessionId || "(none)"}</span>
                  </div>
                  {runStatus && (
                    <span className="text-xs px-2 py-1 rounded bg-slate-50 border border-slate-200 text-slate-700 truncate">
                      {runStatus}
                    </span>
                  )}
                </div>

                <div className="flex items-center gap-2">
                  <select
                    value={chatAgentFilter}
                    onChange={(e) => setChatAgentFilter(e.target.value)}
                    className="text-xs bg-white border border-slate-200 rounded-lg px-2 py-1 text-slate-700"
                    title="Filter chat by agent"
                  >
                    <option value="all">All agents</option>
                    {agentsInChat.map((a) => (
                      <option key={a} value={a}>
                        {a}
                      </option>
                    ))}
                  </select>
                  {chatAgentFilter !== "all" && (
                    <button
                      onClick={() => setChatAgentFilter("all")}
                      className="text-xs px-2 py-1 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700"
                      title="Clear filter"
                    >
                      Clear
                    </button>
                  )}
                </div>
              </header>

              {/* Messages */}
              <div className="flex-1 min-h-0 relative">
                <div ref={messagesScrollRef} onScroll={handleChatScroll} className="absolute inset-0 overflow-y-auto px-6 py-6">
                  <AgentCardsChatView
                    sessionId={sessionId}
                    store={store}
                    roster={Array.isArray(vibeAgents?.roster) ? vibeAgents.roster : null}
                    messageMetaRef={messageMetaRef}
                    parseAgUiMessageId={parseAgUiMessageId}
                    agentDisplayName={agentDisplayName}
                    runIdHint={activeRunId}
                    promptHint={lastPrompt}
                    chatAgentFilter={chatAgentFilter}
                    collapsedMessages={collapsedMessages}
                    onToggleCollapse={(mid) => setCollapsedMessages((prev) => ({ ...prev, [mid]: !Boolean(prev[mid]) }))}
                    onOpenHistory={(a) => {
                      setAgentHistoryAgent(a);
                      setAgentHistoryOpen(true);
                    }}
                    lastError={lastError}
                  />
                  <div ref={messagesEndRef} />
                </div>

                {hasNewActivity && !atBottom && (
                  <button
                    onClick={() => {
                      messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
                      setHasNewActivity(false);
                    }}
                    className="absolute right-4 bottom-4 px-3 py-2 rounded-full bg-indigo-600 text-white text-xs shadow-lg hover:bg-indigo-500"
                    title="Jump to newest"
                  >
                    New messages ↓
                  </button>
                )}
              </div>

              {/* Hint: per-agent provider config lives in the right Agents card */}
              {sessionId && (
                <div className="px-6 py-2 border-t border-slate-200 bg-white">
                  <div className="flex items-center justify-between gap-3 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2">
                    <div className="min-w-0 text-xs text-slate-700">
                      <span className="font-semibold text-slate-900">模型已改为按 Agent 配置</span>
                      <span className="ml-2 text-slate-600">在右侧最上方的 Agents 卡片里设置每个 agent 的 provider。</span>
                    </div>
                    <button
                      type="button"
                      className="shrink-0 text-xs px-2 py-1 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700"
                      onClick={() => {
                        try {
                          if (agentsCardRef.current) agentsCardRef.current.open = true;
                          agentsCardRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
                        } catch {
                          // ignore
                        }
                      }}
                      title="Scroll to Agents"
                    >
                      去配置
                    </button>
                  </div>
                </div>
              )}

              <Composer
                sessionId={sessionId}
                connected={connected}
                mode={mode}
                onModeChange={setMode}
                roster={Array.isArray(vibeAgents?.roster) ? vibeAgents.roster : null}
                busy={isSending}
                onSend={send}
              />
            </div>
          </div>

          {/* Vibe cards (outside chat) */}
          <div className="w-[420px] shrink-0 min-h-0 border-l border-slate-200 bg-slate-50 p-4 overflow-y-auto">
            <div className="space-y-4">
              <div className="text-xs text-slate-500">Vibe cards live here (goals / DAG / trace / agents). Chat stays centered.</div>

              <details
                ref={agentsCardRef}
                open
                className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden ring-1 ring-indigo-100"
              >
                <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                  <Activity size={14} className="text-indigo-600" />
                  <span className="text-sm font-semibold text-slate-900">Agents</span>
                  <span className="ml-auto text-xs text-slate-500 font-mono">{Array.isArray(vibeAgents?.roster) ? vibeAgents.roster.length : 0}</span>
                </summary>
                <div className="p-4 text-xs space-y-3">
                  {!sessionId ? (
                    <div className="text-slate-500">Connect a session to configure per-agent providers.</div>
                  ) : (
                    <>
                      <div className="text-[11px] text-slate-500 leading-relaxed">
                        每个 agent 可绑定不同的 provider（File-SSoT：<span className="font-mono">artifacts/ui/agent_providers.json</span>）。
                      </div>

                      {agentProvidersError && <div className="text-[11px] text-rose-600 break-words">{agentProvidersError}</div>}

                      <div className="space-y-2">
                        <button
                          onClick={() => setChatAgentFilter("all")}
                          className={`w-full flex items-center justify-between gap-3 rounded px-2 py-1 border transition ${
                            chatAgentFilter === "all"
                              ? "bg-indigo-50 border-indigo-200 text-indigo-700"
                              : "bg-white border-slate-200 text-slate-700 hover:bg-slate-50"
                          }`}
                          title="Show all agents in chat"
                        >
                          <span className="font-mono">all</span>
                          <span className="text-[10px] text-slate-500">chat</span>
                        </button>

                        {(Array.isArray(vibeAgents?.roster) ? vibeAgents.roster : []).map((r: any) => {
                          const agent = String(r?.agent ?? "").trim();
                          const current = String((agentProviders as any)?.[agent] ?? "").trim();
                          const options = Array.isArray(apiInfo?.llm?.providers) ? (apiInfo.llm.providers as any[]) : [];
                          return (
                            <div key={agent} className="border border-slate-200 rounded-lg p-2 bg-white">
                              <div className="flex items-center justify-between gap-2">
                                <button
                                  onClick={() => setChatAgentFilter(agent)}
                                  className={`text-left flex items-center gap-2 min-w-0 px-2 py-1 rounded border transition ${
                                    chatAgentFilter === agent
                                      ? "bg-indigo-50 border-indigo-200 text-indigo-700"
                                      : "bg-white border-slate-200 text-slate-700 hover:bg-slate-50"
                                  }`}
                                  title="Filter chat to this agent"
                                >
                                  <span className="font-mono truncate">{agent}</span>
                                </button>

                                <select
                                  className="text-[11px] border border-slate-200 rounded px-2 py-1 bg-white max-w-[220px]"
                                  value={current || ""}
                                  disabled={agentProvidersBusy || !transport.putJson}
                                  onChange={async (e) => {
                                    const v = String(e.target.value || "").trim();
                                    setAgentProvidersBusy(true);
                                    setAgentProvidersError("");
                                    try {
                                      if (!transport.putJson) throw new Error("agent providers API unavailable in this host");
                                      const body: any = { agent, providerName: v };
                                      const res = await transport.putJson(`/api/sessions/${encodeURIComponent(sessionId)}/agent-providers`, body);
                                      const map = res?.map && typeof res.map === "object" ? res.map : {};
                                      setAgentProviders(map);
                                    } catch (err: any) {
                                      setAgentProvidersError(err?.message ?? String(err));
                                    } finally {
                                      setAgentProvidersBusy(false);
                                    }
                                  }}
                                >
                                  <option value="">default</option>
                                  {options.map((p: any) => (
                                    <option key={String(p)} value={String(p)}>
                                      {String(p)}
                                    </option>
                                  ))}
                                </select>
                              </div>
                              <div className="mt-1 text-[10px] text-slate-500 font-mono break-all">{String(r?.agentId ?? "")}</div>
                            </div>
                          );
                        })}

                        {!transport.putJson && (
                          <div className="text-[11px] text-slate-500">
                            This host transport doesn’t support writing agent providers (no `putJson`).
                          </div>
                        )}
                      </div>
                    </>
                  )}
                </div>
              </details>

              <details open className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
                <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                  <Activity size={14} className="text-indigo-600" />
                  <span className="text-sm font-semibold text-slate-900">Run Steps</span>
                  <span className="ml-auto text-xs text-slate-500 font-mono">{runSteps.order.length}</span>
                </summary>
                <div className="p-4 space-y-2">
                  {runSteps.order.length === 0 ? (
                    <div className="text-xs text-slate-500">No steps yet (run vibe once).</div>
                  ) : (
                    runSteps.order.map((step) => {
                      const st = runSteps.map?.[step]?.status ?? "pending";
                      const open = st === "running";
                      return (
                        <details key={step} open={open} className="border border-slate-200 rounded-xl bg-slate-50 px-3 py-2">
                          <summary className="cursor-pointer select-none flex items-center gap-2 text-xs">
                            <span
                              className={`font-mono ${
                                st === "done" ? "text-emerald-700" : st === "running" ? "text-indigo-700" : "text-slate-600"
                              }`}
                            >
                              {st === "done" ? "done" : st === "running" ? "run" : "…"}
                            </span>
                            <span className="font-mono text-slate-900 break-all">{step}</span>
                          </summary>
                          <div className="mt-2 text-[11px] text-slate-600">
                            {runSteps.map?.[step]?.startedAt
                              ? `startedAt: ${new Date(runSteps.map[step].startedAt).toLocaleTimeString()}`
                              : ""}
                            {runSteps.map?.[step]?.finishedAt
                              ? ` · finishedAt: ${new Date(runSteps.map[step].finishedAt).toLocaleTimeString()}`
                              : ""}
                          </div>
                        </details>
                      );
                    })
                  )}
                </div>
              </details>

              <details open className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
                <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                  <FileText size={14} className="text-indigo-600" />
                  <span className="text-sm font-semibold text-slate-900">Brief</span>
                  <span className="ml-auto text-xs text-slate-500 font-mono">
                    v{typeof vibeBrief?.version === "number" ? vibeBrief.version : 0}
                  </span>
                </summary>
                <div className="p-4">
                  <BriefPanel sessionId={sessionId} connected={connected} snapshot={vibeBrief} onRefresh={() => void refreshDeliverables()} />
                </div>
              </details>

              {/* Goals are removed: executable intent lives in DAG plan nodes. */}

              <details className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
                <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                  <TestTube size={14} className="text-indigo-600" />
                  <span className="text-sm font-semibold text-slate-900">Compute</span>
                </summary>
                <div className="p-4">
                  <ComputePanel
                    transport={transport}
                    sessionId={sessionId}
                    connected={connected}
                    lastDecision={vibeComputeDecision}
                    onDecision={(d) => setVibeComputeDecision(d)}
                  />
                </div>
              </details>

              <details className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
                <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                  <FileText size={14} className="text-indigo-600" />
                  <span className="text-sm font-semibold text-slate-900">DAG</span>
                  <span className="ml-auto text-xs text-slate-500 font-mono">
                    n={Array.isArray(vibeDag?.nodes) ? vibeDag.nodes.length : 0} e={Array.isArray(vibeDag?.edges) ? vibeDag.edges.length : 0}
                  </span>
                </summary>
                <div className="p-4">
                  <DagPanel
                    transport={transport}
                    sessionId={sessionId}
                    connected={connected}
                    snapshot={vibeDag}
                    onSnapshot={(snap) => setVibeDag(snap)}
                    onOpenPage={() => navigate({ view: "dag", session: sessionId || sessionFromQuery })}
                  />
                </div>
              </details>

              <details open className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
                <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                  <Activity size={14} className="text-indigo-600" />
                  <span className="text-sm font-semibold text-slate-900">Delivery Center</span>
                  <span className="ml-auto text-xs text-slate-500 font-mono">
                    v{typeof vibeDelivery?.delivery?.version === "number" ? vibeDelivery.delivery.version : 0}
                  </span>
                </summary>
                <div className="p-4">
                  <DeliveryCenterPanel sessionId={sessionId} connected={connected} snapshot={vibeDelivery} onRefresh={() => void refreshDeliverables()} />
                </div>
              </details>

              <details open className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
                <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                  <Activity size={14} className="text-indigo-600" />
                  <span className="text-sm font-semibold text-slate-900">Trace</span>
                  <span className="ml-auto text-xs text-slate-500 font-mono">{Array.isArray(vibeTrace?.items) ? vibeTrace.items.length : 0}</span>
                </summary>
                <div className="p-4">
                  <TracePanel snapshot={vibeTrace} />
                </div>
              </details>
            </div>
          </div>
            </div>
          )}
        </div>

        {/* API Key / LLM Modal (shared, transport-driven; local-only endpoints) */}
        <ApiKeyModal
          transport={transport}
          open={apiKeyOpen}
          defaultProvider={apiInfo?.llm?.default ?? ""}
          providers={Array.isArray(apiInfo?.llm?.providersAll) ? apiInfo.llm.providersAll : (Array.isArray(apiInfo?.llm?.providers) ? apiInfo.llm.providers : [])}
          onClose={() => setApiKeyOpen(false)}
          onSaved={() => void refreshInfo()}
        />

        <AgentHistoryModal
          open={agentHistoryOpen}
          onClose={() => setAgentHistoryOpen(false)}
          sessionId={sessionId}
          agent={agentHistoryAgent || "agent"}
          store={store}
          messageMetaRef={messageMetaRef}
          parseAgUiMessageId={parseAgUiMessageId}
          agentDisplayName={agentDisplayName}
        />

        <SkillsMpModal open={skillsMpOpen} onClose={() => setSkillsMpOpen(false)} transport={transport} />

        <ToolsCatalogModal
          open={toolsOpen}
          onClose={() => setToolsOpen(false)}
          tools={tools}
          mcpTools={mcpTools}
          filteredTools={filteredTools}
          toolSearch={toolSearch}
          setToolSearch={setToolSearch}
        />

        <WorkspaceModal open={workspaceOpen} onClose={() => setWorkspaceOpen(false)} workspace={workspace} />
      </div>
  );
}


