import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from "react";

import type { SraTransport, SraUploadFile } from "../transport/SraTransport";
import { applyJsonPatch } from "../app/jsonPatch";
import { MessageStore, type ChatRole, type Message, type ToolOutput } from "../app/messageStore";
import { runSkillsSync } from "../app/skillsSync";
import { applyUiBootstrapCustomEvent } from "../app/uiBootstrap";
import { agentDisplayName, parseAgUiMessageId } from "../app/messageId";

type SessionInfo = {
  sessionId: string;
  createdAt: string;
  providerName?: string;
};

type ToolSummary = {
  name: string;
  description?: string;
  category?: string;
  source?: string;
  tags?: string[];
};

type InputMode = "chat" | "vibe" | "vibe_loop";

// ============================================================
//  Shared controller hook (transport-driven)
//
//  中文说明：
//  - 把原 Web `useAppController` 的核心逻辑搬到 shared UI core
//  - 所有网络与 SSE 通过 `SraTransport` 注入，避免宿主耦合
// ============================================================

export function useWorkbenchController(args: { transport: SraTransport }) {
  const transport = args.transport;

  const [apiInfo, setApiInfo] = useState<any>(null);
  const [sessions, setSessions] = useState<SessionInfo[]>([]);
  const [sessionId, setSessionId] = useState<string>("");
  const [mode, setMode] = useState<InputMode>("vibe");
  const [isSending, setIsSending] = useState<boolean>(false);

  const storeRef = useRef<MessageStore | null>(null);
  if (!storeRef.current) storeRef.current = new MessageStore();
  const store = storeRef.current;

  const orderVer = useSyncExternalStore(store.subscribe, store.getOrderVersion, store.getOrderVersion);
  const messageIds = useMemo(() => store.getOrder(), [orderVer, store]);

  const [status, setStatus] = useState<"Disconnected" | "Connecting" | "Connected">("Disconnected");
  const [runStatus, setRunStatus] = useState<string>("");
  const [lastError, setLastError] = useState<string>("");
  const [activeRunId, setActiveRunId] = useState<string>("");
  const [lastPrompt, setLastPrompt] = useState<string>("");

  const [tools, setTools] = useState<ToolSummary[]>([]);
  const [toolsOpen, setToolsOpen] = useState(false);
  const [toolSearch, setToolSearch] = useState("");
  const [mcpReconnectBusy, setMcpReconnectBusy] = useState(false);
  const [skillsSyncBusy, setSkillsSyncBusy] = useState(false);
  const [skillsSyncNote, setSkillsSyncNote] = useState("");
  const [skillsSyncStatus, setSkillsSyncStatus] = useState<any>(null);
  const [skillsSyncLogs, setSkillsSyncLogs] = useState<any[]>([]);

  const [workspace, setWorkspace] = useState<any>(null);
  const [workspaceOpen, setWorkspaceOpen] = useState(false);
  const [apiKeyOpen, setApiKeyOpen] = useState(false);

  // Vibe snapshots (CUSTOM aevatar.vibe.*)
  const [vibeBrief, setVibeBrief] = useState<any>(null);
  const [vibeDag, setVibeDag] = useState<any>(null);
  const [vibeTrace, setVibeTrace] = useState<any>(null);
  const [vibeAgents, setVibeAgents] = useState<any>(null);
  const [vibeDelivery, setVibeDelivery] = useState<any>(null);
  const [vibeComputeDecision, setVibeComputeDecision] = useState<any>(null);

  // Per-agent provider mapping (Agents panel config)
  const [agentProviders, setAgentProviders] = useState<Record<string, string>>({});
  const [agentProvidersBusy, setAgentProvidersBusy] = useState(false);
  const [agentProvidersError, setAgentProvidersError] = useState("");

  const refreshDeliverables = async () => {
    const sid = (sessionId || "").trim();
    if (!sid) return;
    try {
      const json = await transport.getDeliverables(sid);
      if ((json as any)?.brief) setVibeBrief({ sessionId: sid, ...((json as any).brief as any) });
      if ((json as any)?.delivery) setVibeDelivery((json as any).delivery);
    } catch {
      // best-effort
    }
  };

  const connRef = useRef<{ close: () => void } | null>(null);
  const messageMetaRef = useRef<Record<string, any>>({});
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const messagesScrollRef = useRef<HTMLDivElement>(null);
  const runIdRef = useRef<string>("");

  // Chat UX: filter + "new messages" indicator (no forced jump when user scrolls up)
  const [chatAgentFilter, setChatAgentFilter] = useState<string>("all");
  const [atBottom, setAtBottom] = useState<boolean>(true);
  const [hasNewActivity, setHasNewActivity] = useState<boolean>(false);
  const [collapsedMessages, setCollapsedMessages] = useState<Record<string, boolean>>({});

  const atBottomRef = useRef<boolean>(true);
  const hasNewActivityRef = useRef<boolean>(false);
  const scrollRafRef = useRef<number | null>(null);
  useEffect(() => {
    atBottomRef.current = atBottom;
  }, [atBottom]);
  useEffect(() => {
    hasNewActivityRef.current = hasNewActivity;
  }, [hasNewActivity]);

  function scheduleScrollToBottom(behavior: ScrollBehavior = "auto") {
    if (scrollRafRef.current != null) return;
    scrollRafRef.current = window.requestAnimationFrame(() => {
      scrollRafRef.current = null;
      messagesEndRef.current?.scrollIntoView({ behavior });
    });
  }

  // Vibe run steps -> cards (STEP_STARTED/STEP_FINISHED)
  const [runSteps, setRunSteps] = useState<{ order: string[]; map: Record<string, any> }>({ order: [], map: {} });

  const connected = useMemo(() => status === "Connected" && Boolean(sessionId), [status, sessionId]);
  const mcpTools = useMemo(() => tools.filter((t) => (t.source || "").toUpperCase() === "MCP"), [tools]);

  const filteredTools = useMemo(() => {
    const q = toolSearch.trim().toLowerCase();
    const list = q ? tools.filter((t) => t.name.toLowerCase().includes(q) || (t.description || "").toLowerCase().includes(q)) : tools;
    return list.slice(0, 200);
  }, [tools, toolSearch]);

  const agentsInChat = useMemo(() => {
    const set = new Set<string>();
    for (const id of messageIds) {
      const meta = parseAgUiMessageId(id);
      const a = agentDisplayName(meta.agent);
      if (a && a !== "system" && a !== "user") set.add(a);
    }
    return Array.from(set.values()).sort();
  }, [messageIds]);

  const visibleMessageIds = useMemo(() => {
    if (chatAgentFilter === "all") return messageIds;
    return messageIds.filter((id) => {
      const m = store.getMessage(id);
      const role = m?.role ?? "assistant";
      if (role === "user" || role === "system") return true;
      const meta = parseAgUiMessageId(id);
      const agentMeta = messageMetaRef.current[id]?.agent ?? meta.agent;
      const a = agentDisplayName(agentMeta);
      return a === chatAgentFilter;
    });
  }, [chatAgentFilter, messageIds, store]);

  function handleChatScroll() {
    const el = messagesScrollRef.current;
    if (!el) return;
    const nearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 48;
    setAtBottom(nearBottom);
    if (nearBottom) setHasNewActivity(false);
  }

  async function refreshInfo() {
    try {
      setApiInfo(await transport.info());
    } catch (e: any) {
      setLastError(`Failed to load /api/info: ${e?.message ?? String(e)}`);
    }
  }

  async function refreshSessions() {
    try {
      const json = await transport.listSessions();
      setSessions((json as any)?.sessions ?? []);
    } catch (e: any) {
      setLastError(`Failed to load /api/sessions: ${e?.message ?? String(e)}`);
    }
  }

  async function createSession() {
    try {
      setLastError("");
      const json = await transport.createSession();
      if (json?.sessionId) {
        await refreshSessions();
        connectToSession(json.sessionId);
      } else {
        throw new Error("missing sessionId");
      }
    } catch (e: any) {
      setLastError(`Create session failed: ${e?.message ?? String(e)}`);
    }
  }

  async function reconnectMcp() {
    if (!connected || !sessionId || mcpReconnectBusy) return;
    if (!transport.postJson) {
      setLastError("MCP reconnect unavailable: transport lacks postJson.");
      return;
    }

    setMcpReconnectBusy(true);
    try {
      setLastError("");
      await transport.postJson(`/api/sessions/${encodeURIComponent(sessionId)}/mcp/reconnect`);
    } catch (e: any) {
      setLastError(`MCP reconnect failed: ${e?.message ?? String(e)}`);
    } finally {
      setMcpReconnectBusy(false);
    }
  }

  async function syncSkillPacks() {
    await runSkillsSync({
      transport,
      deps: {
        busy: skillsSyncBusy,
        setBusy: setSkillsSyncBusy,
        setLastError,
        setNote: setSkillsSyncNote,
        setStatus: setSkillsSyncStatus,
        setLogs: setSkillsSyncLogs,
      },
    });
  }

  function disconnect() {
    try {
      connRef.current?.close?.();
    } catch {
      // ignore
    }
    connRef.current = null;
    setStatus("Disconnected");
  }

  function connectToSession(id: string) {
    const sid = (id || "").trim();
    if (!sid) return;

    disconnect();

    setSessionId(sid);
    runIdRef.current = "";
    setRunStatus("");
    setLastError("");
    setActiveRunId("");
    setLastPrompt("");
    setTools([]);
    store.clear();
    setWorkspace(null);
    setVibeBrief(null);
    setVibeDag(null);
    setVibeTrace(null);
    setVibeAgents(null);
    setVibeDelivery(null);
    setVibeComputeDecision(null);
    setRunSteps({ order: [], map: {} });
    setChatAgentFilter("all");
    setHasNewActivity(false);
    setAtBottom(true);
    setCollapsedMessages({});
    messageMetaRef.current = {};

    const getJson = transport.getJson;

    const upsertMessage = (msg: Message) => {
      if (!msg?.id) return;
      store.upsertMessage(msg);
    };

    const upsertTool = (messageId: string, tool: ToolOutput) => {
      store.upsertTool(messageId, tool);
    };

    const pullToolsSnapshot = async () => {
      if (!getJson) return;
      try {
        const json = await getJson(`/api/sessions/${encodeURIComponent(sid)}/tools`);
        const list = Array.isArray((json as any)?.tools) ? (json as any).tools : [];
        setTools(list as ToolSummary[]);
      } catch {
        // best-effort
      }
    };

    // Goals removed: executable intent lives in DAG plan nodes.

    const pullDeliverables = async () => {
      try {
        await refreshDeliverables();
      } catch {
        // best-effort
      }
    };

    const pullDagSnapshot = async () => {
      if (!getJson) return;
      try {
        const json = await getJson(`/api/sessions/${encodeURIComponent(sid)}/dag`);
        const d = (json as any)?.dag;
        if (!d) return;
        setVibeDag(d);
      } catch {
        // best-effort
      }
    };

    const pullAgentProviders = async () => {
      if (!getJson) return;
      try {
        const json = await getJson(`/api/sessions/${encodeURIComponent(sid)}/agent-providers`);
        const map = (json as any)?.map && typeof (json as any).map === "object" ? (json as any).map : {};
        setAgentProviders(map);
      } catch {
        // best-effort
      }
    };

    const pushSystem = (text2: string) => {
      const id2 = `sys:${sid}:${Date.now()}:${Math.random().toString(16).slice(2, 8)}`;
      store.upsertMessage({ id: id2, role: "system", content: text2, isFinal: true });
      if (atBottomRef.current) scheduleScrollToBottom("auto");
      else if (!hasNewActivityRef.current) setHasNewActivity(true);
    };

    const upsertStepSystem = (stepName: string, st: "running" | "done") => {
      const rid = runIdRef.current || "run";
      const mid = `sys:${sid}:step:${rid}:${stepName}`;
      const text = st === "running" ? `⏳ ${stepName}` : `✅ ${stepName}`;
      store.upsertMessage({ id: mid, role: "system", content: text, isFinal: true });
      if (atBottomRef.current) scheduleScrollToBottom("auto");
      else if (!hasNewActivityRef.current) setHasNewActivity(true);
    };

    const handleEvent = (evt: any) => {
      const type = String(evt?.type ?? "").trim();
      if (!type) return;

      if (type === "MESSAGES_SNAPSHOT") {
        const arr: any[] = evt?.messages ?? [];
        const list: Message[] = [];
        for (const m of arr) {
          if (!m?.id) continue;
          list.push({
            id: m.id,
            role: (m.role ?? "assistant") as ChatRole,
            content: m.content ?? "",
            isFinal: true,
          });
        }
        store.resetFromSnapshot(list);
        return;
      }

      if (type === "TEXT_MESSAGE_START") {
        const mid = evt?.messageId;
        const role = (evt?.role ?? "assistant") as ChatRole;
        if (!mid) return;
        // Preserve existing content (e.g. optimistic prompt) so a start event doesn't wipe it.
        const existing = store.getMessage(mid);
        const existingContent = typeof existing?.content === "string" ? existing.content : "";
        upsertMessage({ id: mid, role, content: existingContent, isFinal: false });

        // Default: keep agent outputs expanded while streaming.
        if (role === "assistant") {
          setCollapsedMessages((prev) => ({ ...prev, [mid]: false }));
        }

        if (atBottomRef.current) scheduleScrollToBottom("auto");
        else if (!hasNewActivityRef.current) setHasNewActivity(true);
        return;
      }

      if (type === "TEXT_MESSAGE_CONTENT") {
        const mid = evt?.messageId;
        const delta = evt?.delta ?? "";
        if (!mid || !delta) return;
        // User prompt is emitted as a single "full text" delta.
        // If we already have prompt content (optimistic projection or snapshot), avoid duplicating it.
        const existing = store.getMessage(mid);
        if ((existing?.role ?? "") === "user" && String(existing?.content ?? "").trim().length > 0) {
          return;
        }
        store.appendDelta(mid, delta);
        if (atBottomRef.current) scheduleScrollToBottom("auto");
        else if (!hasNewActivityRef.current) setHasNewActivity(true);
        return;
      }

      if (type === "TEXT_MESSAGE_END") {
        const mid = String(evt?.messageId ?? "").trim();
        if (!mid) return;
        store.markFinal(mid);

        // Auto-collapse worker agent outputs once finished (still user-toggleable).
        const msg = store.getMessage(mid);
        if ((msg?.role ?? "assistant") !== "assistant") return;
        const metaAgent = messageMetaRef.current[mid]?.agent;
        const agent = agentDisplayName(metaAgent ?? parseAgUiMessageId(mid).agent);
        if (agent && agent !== "research_assistant") {
          setCollapsedMessages((prev) => ({ ...prev, [mid]: true }));
        }
        return;
      }

      if (type === "STATE_SNAPSHOT") {
        setWorkspace(evt?.snapshot ?? null);
        return;
      }

      if (type === "STATE_DELTA") {
        const delta: any[] = Array.isArray(evt?.delta) ? evt.delta : [];
        if (delta.length === 0) return;
        setWorkspace((prev: any) => applyJsonPatch(prev, delta));
        return;
      }

      if (type === "RUN_STARTED") {
        setRunStatus("Running…");
        setRunSteps({ order: [], map: {} });
        const rid = String(evt?.runId ?? "");
        runIdRef.current = rid || runIdRef.current || `${Date.now()}`;
        if (rid) setActiveRunId(rid);
        pushSystem(rid ? `▶ Run started: ${rid}` : "▶ Run started");
        return;
      }

      if (type === "RUN_FINISHED") {
        setRunStatus("");
        pushSystem("■ Run finished");

        // Fallback: if TEXT_MESSAGE_CONTENT was missed, use RUN_FINISHED payload to backfill.
        const r = evt?.result ?? {};
        const mid = r?.assistantMessageId;
        const text = r?.assistant;
        if (typeof mid !== "string" || !mid) return;
        if (typeof text !== "string" || !text) return;

        const existing = store.getMessage(mid);
        if (!existing) store.upsertMessage({ id: mid, role: "assistant", content: text, isFinal: true });
        else if (!existing.content) store.upsertMessage({ ...existing, content: text, isFinal: true });
        return;
      }

      if (type === "RUN_ERROR") {
        setRunStatus(`Error: ${(evt?.message ?? "run failed").toString()}`);
        pushSystem(`✖ Run error: ${String(evt?.message ?? "run failed")}`);
        return;
      }

      if (type === "STEP_STARTED") {
        const stepName = String(evt?.stepName ?? "").trim();
        if (!stepName) return;
        setRunSteps((prev) => {
          const order = Array.isArray(prev?.order) ? [...prev.order] : [];
          const map = prev?.map ? { ...prev.map } : {};
          if (!map[stepName]) order.push(stepName);
          map[stepName] = { ...(map[stepName] || {}), status: "running", startedAt: evt?.timestamp ?? Date.now() };
          return { order, map };
        });
        upsertStepSystem(stepName, "running");
        return;
      }

      if (type === "STEP_FINISHED") {
        const stepName = String(evt?.stepName ?? "").trim();
        if (!stepName) return;
        setRunSteps((prev) => {
          const order = Array.isArray(prev?.order) ? [...prev.order] : [];
          const map = prev?.map ? { ...prev.map } : {};
          if (!map[stepName]) order.push(stepName);
          map[stepName] = { ...(map[stepName] || {}), status: "done", finishedAt: evt?.timestamp ?? Date.now() };
          return { order, map };
        });
        upsertStepSystem(stepName, "done");
        return;
      }

      if (type === "CUSTOM") {
        const name = evt?.name;
        const v = evt?.value ?? {};

        if (name === "aevatar.scientific.mcp_reconnect_started") {
          setRunStatus("Reconnecting MCP…");
          return;
        }
        if (name === "aevatar.scientific.mcp_reconnect_finished") {
          setRunStatus("");
          return;
        }
        if (name === "aevatar.scientific.mcp_reconnect_error") {
          setRunStatus("");
          setLastError(`MCP reconnect error: ${String(v?.error ?? "unknown error")}`);
          return;
        }

        if (name === "aevatar.scientific.tools_snapshot") {
          const list = Array.isArray(v?.tools) ? v.tools : [];
          setTools(list as ToolSummary[]);
          return;
        }

        if (name === "aevatar.vibe.message_meta") {
          const messageId = String(v?.messageId ?? "").trim();
          if (messageId) messageMetaRef.current[messageId] = v;
          return;
        }

        // UI bootstrap (file-backed snapshots)
        const boot = applyUiBootstrapCustomEvent({ name, value: v, store, messageMetaRef, setRunSteps });
        if (boot.handled) return;

        // ------------------------------------------------------------
        // Vibe snapshots (File-SSoT projections)
        // ------------------------------------------------------------
        // Goals removed: executable intent lives in DAG plan nodes.
        if (name === "aevatar.vibe.brief_snapshot") {
          setVibeBrief(v);
          return;
        }
        if (name === "aevatar.vibe.brief_updated") {
          void pullDeliverables();
          return;
        }
        if (name === "aevatar.vibe.dag_snapshot") {
          setVibeDag(v?.dag ?? null);
          return;
        }
        if (name === "aevatar.vibe.dag_updated") {
          void pullDagSnapshot();
          return;
        }
        if (name === "aevatar.vibe.trace_snapshot") {
          setVibeTrace(v);
          return;
        }
        if (name === "aevatar.vibe.round_summary") {
          setVibeTrace((prev: any) => {
            const arr: any[] = Array.isArray(prev?.items) ? prev.items : [];
            const next = [...arr, v].slice(-200);
            return { sessionId: sid, items: next };
          });
          return;
        }
        if (name === "aevatar.vibe.agents_snapshot") {
          setVibeAgents(v);
          return;
        }
        if (name === "aevatar.vibe.agent_providers_snapshot") {
          const map = v?.map && typeof v.map === "object" ? v.map : {};
          setAgentProviders(map as any);
          setAgentProvidersError("");
          setAgentProvidersBusy(false);
          return;
        }
        if (name === "aevatar.vibe.delivery_snapshot") {
          setVibeDelivery(v?.delivery ?? null);
          return;
        }
        if (name === "aevatar.vibe.delivery_updated") {
          void pullDeliverables();
          return;
        }
        if (name === "aevatar.vibe.compute_decision") {
          setVibeComputeDecision(v);
          return;
        }
        if (name === "aevatar.vibe.consensus_blocked") {
          setVibeTrace((prev: any) => {
            const arr: any[] = Array.isArray(prev?.items) ? prev.items : [];
            const next = [...arr, { ...v, kind: "consensus_blocked" }].slice(-200);
            return { sessionId: sid, items: next };
          });
          return;
        }

        // Backward compatibility for legacy custom events (optional if backend still sends them)
        if (name === "aevatar.scientific.tool_start") {
          const rid = v?.runId;
          const toolCallId = v?.toolCallId;
          const toolName = v?.toolName;
          const isMcp = Boolean(v?.isMcp);
          if (!rid || !toolCallId || !toolName) return;
          const messageId = `msg:${sid}:assistant:${rid}`;
          upsertTool(messageId, { toolCallId, name: toolName, status: "running", isMcp });
          return;
        }
        if (name === "aevatar.scientific.tool_end") {
          const rid = v?.runId;
          const toolCallId = v?.toolCallId;
          const toolName = v?.toolName;
          if (!rid || !toolCallId || !toolName) return;
          const messageId = `msg:${sid}:assistant:${rid}`;
          upsertTool(messageId, {
            toolCallId,
            name: toolName,
            status: "done",
            isMcp: Boolean(v?.isMcp),
            success: Boolean(v?.success),
            durationMs: typeof v?.durationMs === "number" ? v.durationMs : undefined,
            error: v?.error ? String(v.error) : undefined,
            resultPreview: v?.resultPreview ? String(v.resultPreview) : undefined,
          });
        }

        return;
      }

      // Standard AG-UI Tool Events
      if (type === "TOOL_CALL_START") {
        const mid = evt?.messageId;
        const toolCallId = evt?.toolCallId;
        const name = evt?.toolName;
        if (!mid || !toolCallId || !name) return;
        upsertTool(mid, { toolCallId, name, status: "running" });
        return;
      }
      if (type === "TOOL_CALL_RESULT") {
        const mid = evt?.messageId;
        const toolCallId = evt?.toolCallId;
        const result = evt?.result;
        if (!mid || !toolCallId) return;
        const msg = store.getMessage(mid);
        const existing = msg?.toolOutputs?.find((x) => x.toolCallId === toolCallId);
        if (!existing) return;
        upsertTool(mid, { toolCallId, name: existing.name, status: existing.status, resultPreview: result });
        return;
      }
      if (type === "TOOL_CALL_END") {
        const mid = evt?.messageId;
        const toolCallId = evt?.toolCallId;
        if (!mid || !toolCallId) return;
        const msg = store.getMessage(mid);
        const existing = msg?.toolOutputs?.find((x) => x.toolCallId === toolCallId);
        if (!existing) return;
        upsertTool(mid, { toolCallId, name: existing.name, status: "done", success: existing.success ?? true });
      }
    };

    connRef.current = transport.connectAgUiEvents(sid, {
      onStatus: (st, detail) => {
        if (st === "Connecting") setStatus("Connecting");
        else if (st === "Connected") {
          setStatus("Connected");
          void pullToolsSnapshot();
          void pullAgentProviders();
        } else if (st === "Disconnected") {
          setStatus("Disconnected");
        } else if (st === "Error") {
          // Web EventSource errors should stop; Node SSE handles reconnect internally.
          setLastError(detail ? `SSE error: ${detail}` : "SSE error");
          if (!transport.capabilities.nodeSse) disconnect();
        }
      },
      onEvent: (evt) => {
        handleEvent(evt);
      },
      onError: (_err) => {
        if (transport.capabilities.nodeSse) return;
        setLastError("SSE disconnected (EventSource error). Check backend/proxy and session validity.");
        disconnect();
      },
    });
  }

  async function send(payload: { text: string; mode: InputMode; toAgents: string[]; files: File[] }): Promise<boolean> {
    const text = (payload?.text ?? "").trim();
    if (!connected || !sessionId || !text || isSending) return false;

    setIsSending(true);
    try {
      setLastError("");

      // ------------------------------------------------------------
      // Slash commands (local-only helpers, do NOT start a run)
      // ------------------------------------------------------------
      if (text === "/status" || text === "/agents") {
        const getJson = transport.getJson;
        if (!getJson) throw new Error("Transport does not support GET JSON.");
        const json: any = await getJson(`/api/sessions/${encodeURIComponent(sessionId)}/status`);
        const st = json?.agents ? json : null;

        const lines: string[] = [];
        lines.push("### Agent status");
        if (st?.runId) lines.push(`- runId: \`${String(st.runId)}\``);
        if (Array.isArray(st?.steps?.running) && st.steps.running.length > 0) {
          lines.push(`- running steps: ${st.steps.running.map((x: any) => `\`${String(x)}\``).join(", ")}`);
        } else {
          lines.push("- running steps: (none)");
        }
        if (Array.isArray(st?.agents) && st.agents.length > 0) {
          lines.push("");
          lines.push("Agents:");
          for (const a of st.agents) {
            const agent = String(a?.agent ?? "").trim() || "agent";
            const stepName = String(a?.stepName ?? "").trim();
            const status = String(a?.status ?? "").trim() || "unknown";
            const provider = String(a?.providerName ?? "").trim();
            lines.push(`- **${agent}**: ${status}${stepName ? ` (${stepName})` : ""}${provider ? ` [${provider}]` : ""}`);
          }
        }
        if (Array.isArray(st?.runningTools) && st.runningTools.length > 0) {
          lines.push("");
          lines.push(`Running tools (${st.runningTools.length}):`);
          for (const t of st.runningTools.slice(0, 10)) {
            lines.push(`- ${String(t?.toolName ?? "tool")} (${String(t?.toolCallId ?? "")})`);
          }
        }

        const msg = lines.join("\n").trim();
        const mid = `sys:${sessionId}:assistant:status:${Date.now()}`;
        store.upsertMessage({ id: mid, role: "assistant", content: msg, isFinal: true });
        if (atBottomRef.current) scheduleScrollToBottom("auto");
        else if (!hasNewActivityRef.current) setHasNewActivity(true);
        return true;
      }

      // 1) Upload attachments (optional)
      let attachmentPaths: string[] = [];
      const files = Array.isArray(payload?.files) ? payload.files : [];
      if (files.length > 0) {
        const up: SraUploadFile[] = [];
        for (const f of files) {
          if (!f) continue;
          const ab = await f.arrayBuffer();
          up.push({
            filename: f.name || "attachment",
            contentType: f.type || "application/octet-stream",
            data: new Uint8Array(ab),
          });
        }
        const resUp = await transport.uploadAttachments(sessionId, up);
        attachmentPaths = Array.isArray(resUp?.attachmentPaths) ? resUp.attachmentPaths : [];
      }

      // 2) Single entrypoint input (optional routing hints)
      const toAgents = Array.isArray(payload?.toAgents) ? payload.toAgents.filter(Boolean) : [];
      const res = await transport.sendInput(sessionId, {
        message: text,
        mode: payload.mode,
        toAgents: toAgents.length > 0 ? toAgents : undefined,
        attachmentPaths: attachmentPaths.length > 0 ? attachmentPaths : undefined,
      });

      // ------------------------------------------------------------
      // Optimistic UI projection:
      // - Always show the Prompt + cards immediately after SEND, even if SSE lags.
      // - Use runId returned by HTTP response as the source of truth.
      // ------------------------------------------------------------
      const rid = String(res?.runId ?? "").trim();
      if (rid) {
        runIdRef.current = rid;
        setActiveRunId(rid);
        setLastPrompt(text);
        setRunStatus("Running…");
        setRunSteps({ order: [], map: {} });

        // Prompt (user) message: render immediately (avoid "stone into sea" UX).
        const userMid = `msg:${sessionId}:user:${rid}`;
        store.upsertMessage({ id: userMid, role: "user", content: text, isFinal: true });

        if (atBottomRef.current) scheduleScrollToBottom("auto");
        else if (!hasNewActivityRef.current) setHasNewActivity(true);
      }

      return true;
    } catch (e: any) {
      setLastError(`Send failed: ${e?.message ?? String(e)}`);
      return false;
    } finally {
      setIsSending(false);
    }
  }

  useEffect(() => {
    void refreshInfo();
    void refreshSessions();
    return () => disconnect();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return {
    apiInfo,
    sessions,
    sessionId,
    mode,
    setMode,
    isSending,
    store,
    messageIds,
    visibleMessageIds,
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
    setVibeBrief,
    vibeDag,
    setVibeDag,
    vibeTrace,
    setVibeTrace,
    vibeAgents,
    setVibeAgents,
    vibeDelivery,
    setVibeDelivery,
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
    refreshSessions,
    createSession,
    reconnectMcp,
    syncSkillPacks,
    disconnect,
    connectToSession,
    send,
  };
}


