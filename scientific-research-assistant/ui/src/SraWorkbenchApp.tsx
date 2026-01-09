import { useCallback, useEffect, useMemo, useState } from "react";
import type { SraTransport } from "./transport/SraTransport";
import SharedApiKeyModal from "./panels/ApiKeyModal";

import { Activity, FileText, Plus, RefreshCw, Search, TestTube, X } from "lucide-react";
import GoalsPanel from "./panels/GoalsPanel";
import Composer from "./panels/Composer";
import DagPanel from "./panels/DagPanel";
import TracePanel from "./panels/TracePanel";
import BriefPanel from "./panels/BriefPanel";
import DeliveryCenterPanel from "./panels/DeliveryCenterPanel";
import ComputePanel from "./panels/ComputePanel";
import { useWorkbenchController } from "./controller/useWorkbenchController";
import ChatMessageRow from "./components/ChatMessageRow";
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
    visibleMessageIds,
    status,
    runStatus,
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
    vibeGoals,
    setVibeGoals,
    vibeBrief,
    vibeDag,
    setVibeDag,
    vibeTrace,
    vibeAgents,
    vibeDelivery,
    vibeComputeDecision,
    setVibeComputeDecision,
    selectedProviderName,
    setSelectedProviderName,
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

  return (
    pageView === "files" ? (
      <FilesPage
        transport={transport}
        sessionId={sessionId}
        sessionFromQuery={sessionFromQuery}
        initialPath={pathFromQuery}
        onBack={() => navigate({ view: "" })}
      />
    ) : pageView === "dag" ? (
      <div className="h-screen bg-slate-50 text-slate-900 font-sans overflow-hidden">
        <div className="h-14 flex items-center justify-between px-6 bg-white border-b border-slate-200">
          <div className="flex items-center gap-3 min-w-0">
            <div className="font-semibold text-slate-900">DAG</div>
            <div className="text-xs text-slate-500 truncate">
              session: <span className="font-mono text-slate-900">{sessionId || sessionFromQuery || "(none)"}</span>
            </div>
          </div>
          <button
            type="button"
            onClick={() => navigate({ view: "" })}
            className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700"
            title="Back to main"
          >
            Back
          </button>
        </div>
        <div className="p-4 h-[calc(100vh-56px)] overflow-auto">
          <div className="max-w-6xl mx-auto">
            <DagPanel transport={transport} sessionId={sessionId} connected={connected} snapshot={vibeDag} onSnapshot={(snap) => setVibeDag(snap)} />
          </div>
        </div>
      </div>
    ) : (
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

          <div className="space-y-2">
            <button
              onClick={createSession}
              className="flex items-center justify-center gap-2 w-full p-2 rounded bg-indigo-600 hover:bg-indigo-500 transition text-sm text-white"
            >
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

            <div className="mt-4 space-y-2">
              <button className="flex items-center gap-3 w-full p-2 rounded hover:bg-slate-50 text-slate-700 transition text-sm">
                <Activity size={16} className="text-slate-500" /> Current Session
              </button>
              <button className="flex items-center gap-3 w-full p-2 rounded hover:bg-slate-50 text-slate-700 transition text-sm">
                <FileText size={16} className="text-slate-500" /> Saved Reports
              </button>
              <button
                type="button"
                onClick={() => navigate({ view: "files", session: sessionId || sessionFromQuery })}
                className="flex items-center gap-3 w-full p-2 rounded hover:bg-slate-50 text-slate-700 transition text-sm disabled:opacity-50"
                disabled={!(sessionId || sessionFromQuery) || !transport.capabilities.filesApi}
                title={
                  !transport.capabilities.filesApi
                    ? "Files are local-only (loopback sidecar required)."
                    : sessionId || sessionFromQuery
                      ? "Browse and edit session files"
                      : "Connect to a session first"
                }
              >
                <FileText size={16} className="text-slate-500" /> Files
              </button>

              {/* DAG has a dedicated page via the card "Open" button; keep sidebar minimal. */}
            </div>
          </div>

          <div className="mt-auto">
            <div className="mt-6 bg-slate-50 border border-slate-200 rounded-lg p-3">
              <div className="text-xs text-slate-500 mb-2">Claude Scientific Skills (MCP)</div>
              <div className="text-sm flex items-center justify-between">
                <span className="text-slate-700">MCP Tools</span>
                <span className="text-indigo-600 font-mono">{mcpTools.length}</span>
              </div>
              <div className="text-xs text-slate-500 mt-1">
                Total tools: <span className="font-mono">{tools.length}</span>
              </div>
              <button
                onClick={() => setToolsOpen(true)}
                className="mt-3 w-full text-xs bg-white hover:bg-slate-50 border border-slate-200 rounded px-2 py-2 transition flex items-center justify-center gap-2"
                disabled={!connected}
                title={connected ? "Browse tools" : "Connect to a session first"}
              >
                <Search size={14} /> Browse tools
              </button>

              <button
                onClick={() => setWorkspaceOpen(true)}
                className="mt-2 w-full text-xs bg-white hover:bg-slate-50 border border-slate-200 rounded px-2 py-2 transition flex items-center justify-center gap-2 disabled:opacity-50"
                disabled={!connected}
                title={connected ? "View workspace state (facts/sources/graph)" : "Connect to a session first"}
              >
                <FileText size={14} /> Workspace
                <span className="ml-auto text-[10px] text-slate-500 font-mono">{workspace?.materials?.items?.length ?? 0}</span>
              </button>

              <button
                onClick={() => void reconnectMcp()}
                className="mt-2 w-full text-xs bg-white hover:bg-slate-50 border border-slate-200 rounded px-2 py-2 transition flex items-center justify-center gap-2 disabled:opacity-50"
                disabled={!connected || mcpReconnectBusy}
                title={connected ? "Reconnect MCP server and refresh tools" : "Connect to a session first"}
              >
                {mcpReconnectBusy ? "Reconnecting…" : "Reconnect MCP"}
              </button>

              <button
                onClick={() => void syncSkillPacks()}
                className="mt-2 w-full text-xs bg-white hover:bg-slate-50 border border-slate-200 rounded px-2 py-2 transition flex items-center justify-center gap-2 disabled:opacity-50"
                disabled={skillsSyncBusy}
                title="Sync local Agent Skills packs (git clone/pull). No restart needed."
              >
                <RefreshCw size={14} /> {skillsSyncBusy ? "Updating Skills…" : "Update Skills"}
              </button>
              {skillsSyncNote && <div className="mt-1 text-[11px] text-slate-500 break-words">{skillsSyncNote}</div>}
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
        <div className="flex-1 flex min-w-0 min-h-0 bg-slate-50 overflow-hidden">
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
                <div ref={messagesScrollRef} onScroll={handleChatScroll} className="absolute inset-0 overflow-y-auto px-6 py-6 space-y-6">
                  {visibleMessageIds.length === 0 ? (
                    <div className="max-w-3xl mx-auto border border-slate-200 rounded-xl p-6 bg-slate-50">
                      <div className="text-sm text-slate-900 font-medium">
                        {chatAgentFilter === "all" ? "No messages yet" : `No messages for ${chatAgentFilter}`}
                      </div>
                      <div className="text-xs text-slate-500 mt-2 leading-relaxed">
                        - Create/select a session (left sidebar)
                        <br />- Wait for SSE to connect (Status: Connected)
                        <br />- Send a message below
                      </div>
                      {lastError && <div className="mt-3 text-xs text-rose-600 break-words">{lastError}</div>}
                    </div>
                  ) : (
                    visibleMessageIds.map((id) => (
                      <ChatMessageRow
                        key={id}
                        id={id}
                        store={store}
                        messageMetaRef={messageMetaRef}
                        parseAgUiMessageId={parseAgUiMessageId}
                        agentDisplayName={agentDisplayName}
                        collapsed={Boolean(collapsedMessages[id])}
                        onToggleCollapse={() => setCollapsedMessages((prev) => ({ ...prev, [id]: !Boolean(prev[id]) }))}
                      />
                    ))
                  )}
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

              <Composer
                sessionId={sessionId}
                connected={connected}
                mode={mode}
                onModeChange={setMode}
                roster={Array.isArray(vibeAgents?.roster) ? vibeAgents.roster : null}
                providers={Array.isArray(apiInfo?.llm?.providers) ? apiInfo.llm.providers : []}
                providerName={selectedProviderName || String(apiInfo?.llm?.default ?? "")}
                onProviderChange={setSelectedProviderName}
                busy={isSending}
                onSend={send}
              />
            </div>
          </div>

          {/* Vibe cards (outside chat) */}
          <div className="w-[420px] shrink-0 min-h-0 border-l border-slate-200 bg-slate-50 p-4 overflow-y-auto">
            <div className="space-y-4">
              <div className="text-xs text-slate-500">Vibe cards live here (goals / DAG / trace / agents). Chat stays centered.</div>

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

              <details open className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
                <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                  <Activity size={14} className="text-indigo-600" />
                  <span className="text-sm font-semibold text-slate-900">Goals</span>
                  <span className="ml-auto text-xs text-slate-500 font-mono">{Array.isArray(vibeGoals?.items) ? vibeGoals.items.length : 0}</span>
                </summary>
                <div className="p-4">
                  <GoalsPanel
                    transport={transport}
                    sessionId={sessionId}
                    connected={connected}
                    snapshot={vibeGoals}
                    onSaved={(snap) => setVibeGoals(snap)}
                  />
                </div>
              </details>

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

              <details className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
                <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                  <Activity size={14} className="text-indigo-600" />
                  <span className="text-sm font-semibold text-slate-900">Agents</span>
                  <span className="ml-auto text-xs text-slate-500 font-mono">{Array.isArray(vibeAgents?.roster) ? vibeAgents.roster.length : 0}</span>
                </summary>
                <div className="p-4 text-xs">
                  {Array.isArray(vibeAgents?.roster) ? (
                    <div className="space-y-2">
                      <button
                        onClick={() => setChatAgentFilter("all")}
                        className={`w-full flex items-center justify-between gap-3 rounded px-2 py-1 border transition ${
                          chatAgentFilter === "all" ? "bg-indigo-50 border-indigo-200 text-indigo-700" : "bg-white border-slate-200 text-slate-700 hover:bg-slate-50"
                        }`}
                        title="Show all agents in chat"
                      >
                        <span className="font-mono">all</span>
                        <span className="text-[10px] text-slate-500">chat</span>
                      </button>
                      {vibeAgents.roster.map((r: any) => (
                        <button
                          key={String(r?.agent ?? "")}
                          onClick={() => setChatAgentFilter(String(r?.agent ?? ""))}
                          className={`w-full flex items-center justify-between gap-3 rounded px-2 py-1 border transition ${
                            chatAgentFilter === String(r?.agent ?? "") ? "bg-indigo-50 border-indigo-200 text-indigo-700" : "bg-white border-slate-200 text-slate-700 hover:bg-slate-50"
                          }`}
                          title="Filter chat to this agent"
                        >
                          <span className="font-mono">{String(r?.agent ?? "")}</span>
                          <span className="font-mono text-[10px] text-slate-500 break-all">{String(r?.agentId ?? "")}</span>
                        </button>
                      ))}
                    </div>
                  ) : (
                    <div className="text-slate-500">No roster yet (connect a session).</div>
                  )}
                </div>
              </details>
            </div>
          </div>
        </div>

        {/* API Key / LLM Modal (shared, transport-driven; local-only endpoints) */}
        <ApiKeyModal
          transport={transport}
          open={apiKeyOpen}
          defaultProvider={apiInfo?.llm?.default ?? ""}
          providers={Array.isArray(apiInfo?.llm?.providers) ? apiInfo.llm.providers : []}
          onClose={() => setApiKeyOpen(false)}
          onSaved={() => void refreshInfo()}
        />

        {/* Tools Browser Modal */}
        {toolsOpen && (
          <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
            <div className="w-full max-w-3xl bg-white border border-slate-200 rounded-xl overflow-hidden">
              <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200">
                <div className="text-sm font-medium text-slate-900">Tools Catalog</div>
                <button onClick={() => setToolsOpen(false)} className="p-2 rounded hover:bg-slate-100 transition" aria-label="Close">
                  <X size={16} />
                </button>
              </div>

              <div className="p-4 border-b border-slate-200">
                <div className="flex items-center gap-2 bg-slate-50 border border-slate-200 rounded-lg px-3 py-2">
                  <Search size={14} className="text-slate-400" />
                  <input
                    value={toolSearch}
                    onChange={(e) => setToolSearch(e.target.value)}
                    placeholder="Search tool name / description…"
                    className="w-full bg-transparent outline-none text-sm text-slate-900 placeholder:text-slate-400"
                  />
                  <div className="text-xs text-slate-500 font-mono">
                    {mcpTools.length}/{tools.length}
                  </div>
                </div>
                <div className="text-xs text-slate-500 mt-2">Showing up to 200 results (type to filter). MCP tools are tagged.</div>
              </div>

              <div className="max-h-[65vh] overflow-y-auto p-4 space-y-2">
                {filteredTools.length === 0 ? (
                  <div className="text-sm text-slate-500">No tools matched.</div>
                ) : (
                  filteredTools.map((t) => (
                    <div key={t.name} className="border border-slate-200 rounded-lg p-3 hover:border-slate-300 transition bg-white">
                      <div className="flex items-center justify-between gap-3">
                        <div className="text-sm text-slate-900 font-mono break-all">{t.name}</div>
                        <div className="flex items-center gap-2 shrink-0">
                          {(t.source || "").toUpperCase() === "MCP" && (
                            <span className="text-[10px] px-2 py-0.5 rounded bg-indigo-50 text-indigo-700 border border-indigo-200">
                              MCP
                            </span>
                          )}
                          {t.category && (
                            <span className="text-[10px] px-2 py-0.5 rounded bg-slate-50 text-slate-700 border border-slate-200">
                              {t.category}
                            </span>
                          )}
                        </div>
                      </div>
                      {t.description && <div className="text-xs text-slate-600 mt-1 whitespace-pre-wrap">{t.description}</div>}
                    </div>
                  ))
                )}
              </div>
            </div>
          </div>
        )}

        {/* Workspace Modal */}
        {workspaceOpen && (
          <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
            <div className="w-full max-w-3xl bg-white border border-slate-200 rounded-xl overflow-hidden">
              <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200">
                <div className="text-sm font-medium text-slate-900">Workspace State (STATE_SNAPSHOT)</div>
                <button onClick={() => setWorkspaceOpen(false)} className="p-2 rounded hover:bg-slate-100 transition" aria-label="Close">
                  <X size={16} />
                </button>
              </div>

              <div className="p-4">
                {workspace ? (
                  <pre className="text-xs text-slate-900 whitespace-pre-wrap break-words max-h-[70vh] overflow-auto bg-slate-50 border border-slate-200 rounded-lg p-3">
                    {JSON.stringify(workspace, null, 2)}
                  </pre>
                ) : (
                  <div className="text-sm text-slate-500">No workspace state yet (run vibe once).</div>
                )}
              </div>
            </div>
          </div>
        )}
      </div>
    )
  );
}


