import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from "react";
import { AgUiClient } from "@agui/sdk";
import { Activity, FileText, Plus, RefreshCw, Search, TestTube, X } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import GoalsPanel from "./panels/GoalsPanel";
import Composer from "./panels/Composer";
import DagPanel from "./panels/DagPanel";
import TracePanel from "./panels/TracePanel";
import ApiKeyModal from "./panels/ApiKeyModal";

type ChatRole = "user" | "assistant" | "system" | "tool" | string;

type ToolOutput = {
  toolCallId: string;
  name: string;
  status: "running" | "done";
  isMcp?: boolean;
  success?: boolean;
  durationMs?: number;
  error?: string;
  resultPreview?: string;
};

type Message = {
  id: string;
  role: ChatRole;
  content: string;
  toolOutputs?: ToolOutput[];
  isFinal?: boolean;
};

// ============================================================
//  MessageStore (perf)
//
//  Goal:
//  - Avoid re-rendering the whole chat list on every streamed token.
//  - Only the message being appended re-renders.
// ============================================================

class MessageStore {
  private map = new Map<string, Message>();
  private order: string[] = [];
  private orderVer = 0;
  private msgVer = new Map<string, number>();
  private listeners = new Set<() => void>();

  subscribe = (cb: () => void) => {
    this.listeners.add(cb);
    return () => this.listeners.delete(cb);
  };

  private emit() {
    for (const cb of this.listeners) cb();
  }

  getOrderVersion = () => this.orderVer;
  getOrder = () => this.order;
  getMessageVersion = (id: string) => this.msgVer.get(id) ?? 0;
  getMessage = (id: string) => this.map.get(id);

  clear() {
    this.map.clear();
    this.order = [];
    this.msgVer.clear();
    this.orderVer++;
    this.emit();
  }

  resetFromSnapshot(messages: Message[]) {
    this.map.clear();
    this.msgVer.clear();
    this.order = [];
    for (const m of messages) {
      if (!m?.id) continue;
      const msg: Message = { ...m, isFinal: true };
      this.map.set(msg.id, msg);
      this.msgVer.set(msg.id, 1);
      this.order.push(msg.id);
    }
    this.orderVer++;
    this.emit();
  }

  upsertMessage(msg: Message) {
    const id = String(msg?.id ?? "").trim();
    if (!id) return;

    const existing = this.map.get(id);
    const isNew = !existing;
    const next: Message = existing ? { ...existing, ...msg } : { ...msg };
    this.map.set(id, next);
    this.msgVer.set(id, (this.msgVer.get(id) ?? 0) + 1);

    if (isNew) {
      this.order = [...this.order, id];
      this.orderVer++;
    }

    this.emit();
  }

  ensureMessage(id: string, role: ChatRole) {
    const mid = String(id || "").trim();
    if (!mid) return;
    if (this.map.has(mid)) return;
    this.map.set(mid, { id: mid, role, content: "", isFinal: false });
    this.msgVer.set(mid, 1);
    this.order = [...this.order, mid];
    this.orderVer++;
    this.emit();
  }

  appendDelta(id: string, delta: string) {
    const mid = String(id || "").trim();
    if (!mid) return;
    const d = String(delta ?? "");
    if (!d) return;

    const existing = this.map.get(mid);
    if (!existing) {
      this.map.set(mid, { id: mid, role: "assistant", content: d, isFinal: false });
      this.msgVer.set(mid, 1);
      this.order = [...this.order, mid];
      this.orderVer++;
      this.emit();
      return;
    }

    existing.content = `${existing.content}${d}`;
    this.map.set(mid, existing);
    this.msgVer.set(mid, (this.msgVer.get(mid) ?? 0) + 1);
    this.emit();
  }

  markFinal(id: string) {
    const mid = String(id || "").trim();
    if (!mid) return;
    const existing = this.map.get(mid);
    if (!existing) return;
    if (existing.isFinal) return;
    existing.isFinal = true;
    this.map.set(mid, existing);
    this.msgVer.set(mid, (this.msgVer.get(mid) ?? 0) + 1);
    this.emit();
  }

  upsertTool(messageId: string, tool: ToolOutput) {
    const mid = String(messageId || "").trim();
    if (!mid || !tool?.toolCallId) return;
    const existing = this.map.get(mid);
    if (!existing) return;

    const arr = [...(existing.toolOutputs || [])];
    const idx = arr.findIndex((x) => x.toolCallId === tool.toolCallId);
    if (idx >= 0) arr[idx] = { ...arr[idx], ...tool };
    else arr.push(tool);

    this.map.set(mid, { ...existing, toolOutputs: arr });
    this.msgVer.set(mid, (this.msgVer.get(mid) ?? 0) + 1);
    this.emit();
  }
}

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

type InputMode = "chat" | "vibe";

function formatToolPayload(raw: string): { main: string; rawJson?: string } {
  const s = (raw ?? "").trim();
  if (!s) return { main: "" };

  try {
    const obj: any = JSON.parse(s);

    // Common Aevatar tool result shape:
    // { success: boolean, data: { "@type": "...StringValue", value: "..." }, errorMessage?: string }
    const value = obj?.data?.value;
    if (typeof value === "string" && value.length > 0) {
      return { main: value, rawJson: s };
    }

    // Other shapes: show a pretty JSON view as main
    const pretty = JSON.stringify(obj, null, 2);
    return { main: pretty, rawJson: s };
  } catch {
    // Not JSON: render as plain text
    return { main: s };
  }
}

export default function App() {
  const query = useMemo(() => new URLSearchParams(window.location.search), []);
  const pageView = query.get("view") || "";
  const sessionFromQuery = (query.get("session") || "").trim();
  const [apiInfo, setApiInfo] = useState<any>(null);
  const [sessions, setSessions] = useState<SessionInfo[]>([]);
  const [sessionId, setSessionId] = useState<string>("");
  const [mode, setMode] = useState<InputMode>("chat");
  const [isSending, setIsSending] = useState<boolean>(false);
  const storeRef = useRef<MessageStore | null>(null);
  if (!storeRef.current) storeRef.current = new MessageStore();
  const store = storeRef.current;
  const orderVer = useSyncExternalStore(store.subscribe, store.getOrderVersion, store.getOrderVersion);
  const messageIds = useMemo(() => store.getOrder(), [orderVer, store]);
  const [status, setStatus] = useState<"Disconnected" | "Connecting" | "Connected">("Disconnected");
  const [runStatus, setRunStatus] = useState<string>("");
  const [lastError, setLastError] = useState<string>("");

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
  const [vibeGoals, setVibeGoals] = useState<any>(null);
  const [vibeDag, setVibeDag] = useState<any>(null);
  const [vibeTrace, setVibeTrace] = useState<any>(null);
  const [vibeAgents, setVibeAgents] = useState<any>(null);

  const clientRef = useRef<any>(null);
  const messageMetaRef = useRef<Record<string, any>>({});
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const messagesScrollRef = useRef<HTMLDivElement>(null);

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
  const mcpTools = useMemo(
    () => tools.filter((t) => (t.source || "").toUpperCase() === "MCP"),
    [tools],
  );

  const filteredTools = useMemo(() => {
    const q = toolSearch.trim().toLowerCase();
    const list = q
      ? tools.filter((t) => t.name.toLowerCase().includes(q) || (t.description || "").toLowerCase().includes(q))
      : tools;
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

  function parseAgUiMessageId(messageId: string): { sessionId: string; agent: string; runId: string; rest: string } {
    const parts = String(messageId || "").split(":");
    // Expected: msg:{sessionId}:{agent}:{runId}(:rest...) | sys:...
    if (parts.length >= 4 && parts[0] === "msg") {
      return {
        sessionId: parts[1] || "",
        agent: parts[2] || "assistant",
        runId: parts[3] || "",
        rest: parts.slice(4).join(":"),
      };
    }
    return { sessionId: "", agent: "system", runId: "", rest: "" };
  }

  function agentDisplayName(agent: string): string {
    const a = String(agent || "").trim();
    if (!a) return "unknown";
    if (a === "assistant") return "research_assistant";
    return a;
  }

  function handleChatScroll() {
    const el = messagesScrollRef.current;
    if (!el) return;
    const nearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 48;
    setAtBottom(nearBottom);
    if (nearBottom) setHasNewActivity(false);
  }

  async function refreshInfo() {
    try {
      const res = await fetch("/api/info");
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setApiInfo(await res.json());
    } catch (e: any) {
      setLastError(`Failed to load /api/info: ${e?.message ?? String(e)}`);
    }
  }

  async function refreshSessions() {
    try {
      const res = await fetch("/api/sessions");
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const json = await res.json();
      setSessions(json.sessions ?? []);
    } catch (e: any) {
      setLastError(`Failed to load /api/sessions: ${e?.message ?? String(e)}`);
    }
  }

  async function createSession() {
    try {
      setLastError("");
      const res = await fetch("/api/sessions", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: "{}",
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const json = await res.json();
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
    setMcpReconnectBusy(true);
    try {
      setLastError("");
      const res = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/mcp/reconnect`, {
        method: "POST",
      });
      if (!res.ok) {
        const body = await res.text().catch(() => "");
        throw new Error(`HTTP ${res.status}${body ? `: ${body}` : ""}`);
      }
    } catch (e: any) {
      setLastError(`MCP reconnect failed: ${e?.message ?? String(e)}`);
    } finally {
      setMcpReconnectBusy(false);
    }
  }

  async function syncSkillPacks() {
    if (skillsSyncBusy) return;
    setSkillsSyncBusy(true);
    let alive = true;
    let timer: number | null = null;
    try {
      setLastError("");
      setSkillsSyncNote("");
      setSkillsSyncStatus(null);
      setSkillsSyncLogs([]);

      // Poll live status while sync is running (best-effort).
      const pollOnce = async () => {
        try {
          const r = await fetch("/api/skills/sync/status");
          if (!r.ok) return;
          const st = await r.json().catch(() => null);
          if (!alive || !st) return;
          setSkillsSyncStatus(st);
          const logs = Array.isArray(st?.logs) ? st.logs : [];
          setSkillsSyncLogs(logs.slice(-12));

          const cur = st?.current;
          if (st?.running && cur?.repoUrl) {
            setSkillsSyncNote(`Syncing: ${cur.packName ?? ""} (${cur.repoUrl})`);
          }
        } catch {
          // best-effort
        }
      };
      await pollOnce();
      timer = window.setInterval(pollOnce, 800);

      const res = await fetch("/api/skills/sync", { method: "POST" });
      const json = await res.json().catch(() => null);

      // Stop polling and fetch final snapshot once.
      if (timer != null) window.clearInterval(timer);
      await pollOnce();
      alive = false;

      if (!res.ok) {
        const body = json ? JSON.stringify(json) : "";
        throw new Error(`HTTP ${res.status}${body ? `: ${body}` : ""}`);
      }

      const packs: any[] = Array.isArray(json?.packs) ? json.packs : [];
      const okCount = packs.filter((p) => Boolean(p?.ok)).length;
      const total = packs.length;

      if (json?.ok === true) {
        setSkillsSyncNote(`Skills updated: ${okCount}/${total} pack(s) ok.`);
      } else {
        setSkillsSyncNote(`Skills update partial: ${okCount}/${total} pack(s) ok.`);
        setLastError(`Skills update partial failure: ${String(json?.error ?? "unknown")}`);
      }
    } catch (e: any) {
      setLastError(`Skills sync failed: ${e?.message ?? String(e)}`);
    } finally {
      alive = false;
      if (timer != null) window.clearInterval(timer);
      setSkillsSyncBusy(false);
    }
  }

  function disconnect() {
    try {
      clientRef.current?.close?.();
    } catch {
      // ignore
    }
    clientRef.current = null;
    setStatus("Disconnected");
  }

  function connectToSession(id: string) {
    const sid = (id || "").trim();
    if (!sid) return;

    disconnect();

    setSessionId(sid);
    setRunStatus("");
    setLastError("");
    setTools([]);
    store.clear();
    setWorkspace(null);
    setVibeGoals(null);
    setVibeDag(null);
    setVibeTrace(null);
    setVibeAgents(null);
    setRunSteps({ order: [], map: {} });
    setChatAgentFilter("all");
    setHasNewActivity(false);
    setAtBottom(true);
    setCollapsedMessages({});
    messageMetaRef.current = {};

    const url = `/api/sessions/${encodeURIComponent(sid)}/agui/events`;
    const client: any = new (AgUiClient as any)(url);
    clientRef.current = client;
    setStatus("Connecting");

    const upsertMessage = (msg: Message) => {
      if (!msg?.id) return;
      store.upsertMessage(msg);
    };

    const pullToolsSnapshot = async () => {
      try {
        const res = await fetch(`/api/sessions/${encodeURIComponent(sid)}/tools`);
        if (!res.ok) return;
        const json = await res.json().catch(() => null);
        const list = Array.isArray(json?.tools) ? json.tools : [];
        setTools(list as ToolSummary[]);
      } catch {
        // best-effort
      }
    };

    const pullGoalsSnapshot = async () => {
      try {
        const res = await fetch(`/api/sessions/${encodeURIComponent(sid)}/goals`);
        if (!res.ok) return;
        const json = await res.json().catch(() => null);
        const g = json?.goals;
        if (!g) return;
        setVibeGoals({ sessionId: sid, ...g });
      } catch {
        // best-effort
      }
    };

    const pullDagSnapshot = async () => {
      try {
        const res = await fetch(`/api/sessions/${encodeURIComponent(sid)}/dag`);
        if (!res.ok) return;
        const json = await res.json().catch(() => null);
        const d = json?.dag;
        if (!d) return;
        setVibeDag(d);
      } catch {
        // best-effort
      }
    };

    const upsertTool = (messageId: string, tool: ToolOutput) => {
      store.upsertTool(messageId, tool);
    };

    client.on("MESSAGES_SNAPSHOT", (evt: any) => {
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
    });

    client.on("TEXT_MESSAGE_START", (evt: any) => {
      const mid = evt?.messageId;
      const role = (evt?.role ?? "assistant") as ChatRole;
      if (!mid) return;
      upsertMessage({ id: mid, role, content: "", isFinal: false });

      // Default: keep agent outputs expanded while streaming.
      if (role === "assistant") {
        setCollapsedMessages((prev) => ({ ...prev, [mid]: false }));
      }

      if (atBottomRef.current) scheduleScrollToBottom("auto");
      else if (!hasNewActivityRef.current) setHasNewActivity(true);
    });

    client.on("TEXT_MESSAGE_CONTENT", (evt: any) => {
      const mid = evt?.messageId;
      const delta = evt?.delta ?? "";
      if (!mid || !delta) return;
      store.appendDelta(mid, delta);

      // Streaming UX: don't "refresh" the whole UI; just keep scroll sticky when user is at bottom.
      if (atBottomRef.current) scheduleScrollToBottom("auto");
      else if (!hasNewActivityRef.current) setHasNewActivity(true);
    });

    client.on("TEXT_MESSAGE_END", (evt: any) => {
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
    });

    client.on("STATE_SNAPSHOT", (evt: any) => {
      setWorkspace(evt?.snapshot ?? null);
    });

    // NOTE: current backend uses STATE_SNAPSHOT; DELTA is reserved for future.
    client.on("STATE_DELTA", (evt: any) => {
      const delta: any[] = Array.isArray(evt?.delta) ? evt.delta : [];
      if (delta.length === 0) return;
      setWorkspace((prev: any) => applyJsonPatch(prev, delta));
    });

    const pushSystem = (text2: string) => {
      const id2 = `sys:${sid}:${Date.now()}:${Math.random().toString(16).slice(2, 8)}`;
      store.upsertMessage({ id: id2, role: "system", content: text2, isFinal: true });
      if (atBottomRef.current) scheduleScrollToBottom("auto");
      else if (!hasNewActivityRef.current) setHasNewActivity(true);
    };

    client.on("RUN_STARTED", (evt: any) => {
      setRunStatus("Running…");
      setRunSteps({ order: [], map: {} });
      const rid = String(evt?.runId ?? "");
      pushSystem(rid ? `▶ Run started: ${rid}` : "▶ Run started");
    });
    client.on("RUN_FINISHED", (evt: any) => {
      setRunStatus("");
      pushSystem("■ Run finished");

      // Fallback: if TEXT_MESSAGE_CONTENT was missed, use RUN_FINISHED payload to backfill.
      const r = evt?.result ?? {};
      const mid = r?.assistantMessageId;
      const text = r?.assistant;
      if (typeof mid !== "string" || !mid) return;
      if (typeof text !== "string" || !text) return;

      const existing = store.getMessage(mid);
      if (!existing) {
        store.upsertMessage({ id: mid, role: "assistant", content: text, isFinal: true });
      } else if (!existing.content) {
        store.upsertMessage({ ...existing, content: text, isFinal: true });
      }
    });
    client.on("RUN_ERROR", (evt: any) => {
      setRunStatus(`Error: ${(evt?.message ?? "run failed").toString()}`);
      pushSystem(`✖ Run error: ${String(evt?.message ?? "run failed")}`);
    });

    client.on("STEP_STARTED", (evt: any) => {
      const stepName = String(evt?.stepName ?? "").trim();
      if (!stepName) return;
      setRunSteps((prev) => {
        const order = Array.isArray(prev?.order) ? [...prev.order] : [];
        const map = prev?.map ? { ...prev.map } : {};
        if (!map[stepName]) order.push(stepName);
        map[stepName] = { ...(map[stepName] || {}), status: "running", startedAt: evt?.timestamp ?? Date.now() };
        return { order, map };
      });
      pushSystem(`⏳ ${stepName} started`);
    });

    client.on("STEP_FINISHED", (evt: any) => {
      const stepName = String(evt?.stepName ?? "").trim();
      if (!stepName) return;
      setRunSteps((prev) => {
        const order = Array.isArray(prev?.order) ? [...prev.order] : [];
        const map = prev?.map ? { ...prev.map } : {};
        if (!map[stepName]) order.push(stepName);
        map[stepName] = { ...(map[stepName] || {}), status: "done", finishedAt: evt?.timestamp ?? Date.now() };
        return { order, map };
      });
      pushSystem(`✅ ${stepName} finished`);
    });

    client.on("CUSTOM", (evt: any) => {
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
        if (messageId) {
          messageMetaRef.current[messageId] = v;
        }
        return;
      }

      // ------------------------------------------------------------
      // Vibe snapshots (File-SSoT projections)
      // ------------------------------------------------------------
      if (name === "aevatar.vibe.goals_snapshot") {
        setVibeGoals(v);
        return;
      }
      if (name === "aevatar.vibe.goals_updated") {
        // Backend sends a minimal signal; pull latest snapshot via REST.
        void pullGoalsSnapshot();
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
      if (name === "aevatar.vibe.consensus_blocked") {
        // Keep last blocked info in trace area (debug).
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
        upsertTool(messageId, {
          toolCallId,
          name: toolName,
          status: "running",
          isMcp,
        });
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
    });

    // Standard AG-UI Tool Events
    client.on("TOOL_CALL_START", (evt: any) => {
      const mid = evt?.messageId;
      const toolCallId = evt?.toolCallId;
      const name = evt?.toolName;
      if (!mid || !toolCallId || !name) return;
      
      upsertTool(mid, {
        toolCallId,
        name,
        status: "running",
      });
    });

    client.on("TOOL_CALL_RESULT", (evt: any) => {
      const mid = evt?.messageId;
      const toolCallId = evt?.toolCallId;
      const result = evt?.result;
      if (!mid || !toolCallId) return;
      const msg = store.getMessage(mid);
      const existing = msg?.toolOutputs?.find((x) => x.toolCallId === toolCallId);
      if (!existing) return;
      upsertTool(mid, { toolCallId, name: existing.name, status: existing.status, resultPreview: result });
    });

    client.on("TOOL_CALL_END", (evt: any) => {
      const mid = evt?.messageId;
      const toolCallId = evt?.toolCallId;
      if (!mid || !toolCallId) return;
      const msg = store.getMessage(mid);
      const existing = msg?.toolOutputs?.find((x) => x.toolCallId === toolCallId);
      if (!existing) return;
      upsertTool(mid, {
        toolCallId,
        name: existing.name,
        status: "done",
        success: existing.success ?? true,
      });
    });

    client.on?.("open", () => {
      setStatus("Connected");
      void pullToolsSnapshot();
    });
    client.on?.("error", () => {
      setLastError("SSE disconnected (EventSource error). Check backend/proxy and session validity.");
      disconnect();
    });
  }

  async function send(payload: { text: string; mode: InputMode; toAgents: string[]; files: File[] }) {
    const text = (payload?.text ?? "").trim();
    if (!connected || !sessionId || !text || isSending) return;

    setIsSending(true);
    try {
      setLastError("");

      // 1) Upload attachments (optional)
      let attachmentPaths: string[] = [];
      const files = Array.isArray(payload?.files) ? payload.files : [];
      if (files.length > 0) {
        const fd = new FormData();
        for (const f of files) fd.append("files", f);

        const resUp = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/uploads`, {
          method: "POST",
          body: fd,
        });
        if (!resUp.ok) {
          const body = await resUp.text().catch(() => "");
          throw new Error(`Upload HTTP ${resUp.status}${body ? `: ${body}` : ""}`);
        }
        const jsonUp: any = await resUp.json().catch(() => null);
        attachmentPaths = Array.isArray(jsonUp?.attachmentPaths) ? jsonUp.attachmentPaths : [];
      }

      // 2) Single entrypoint input (optional routing hints)
      const toAgents = Array.isArray(payload?.toAgents) ? payload.toAgents.filter(Boolean) : [];
      const body = {
        message: text,
        mode: payload.mode,
        toAgents: toAgents.length > 0 ? toAgents : undefined,
        attachmentPaths: attachmentPaths.length > 0 ? attachmentPaths : undefined,
      };

      const res = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/input`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      });
      if (!res.ok) {
        const b = await res.text().catch(() => "");
        throw new Error(`HTTP ${res.status}${b ? `: ${b}` : ""}`);
      }
    } catch (e: any) {
      setLastError(`Send failed: ${e?.message ?? String(e)}`);
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

  useEffect(() => {
    // Support opening dedicated pages (e.g. DAG view) with a preselected session via query param.
    if (sessionFromQuery && !sessionId) {
      connectToSession(sessionFromQuery);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionFromQuery]);

  return (
    pageView === "dag" ? (
      <div className="h-screen bg-slate-50 text-slate-900 font-sans overflow-hidden">
        <div className="h-14 flex items-center justify-between px-6 bg-white border-b border-slate-200">
          <div className="flex items-center gap-3 min-w-0">
            <div className="font-semibold text-slate-900">DAG</div>
            <div className="text-xs text-slate-500 truncate">
              session: <span className="font-mono text-slate-900">{sessionId || sessionFromQuery || "(none)"}</span>
            </div>
          </div>
          <a
            href="/"
            className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700"
            title="Back to main"
          >
            Back
          </a>
        </div>
        <div className="p-4 h-[calc(100vh-56px)] overflow-auto">
          <div className="max-w-6xl mx-auto">
            <DagPanel
              sessionId={sessionId}
              connected={connected}
              snapshot={vibeDag}
              onSnapshot={(snap) => setVibeDag(snap)}
            />
          </div>
        </div>
      </div>
    ) : (
    <div className="flex h-screen bg-slate-50 text-slate-900 font-sans overflow-hidden">
      {/* Sidebar */}
      <div
        className="w-72 shrink-0 min-h-0 bg-white border-r border-slate-200 p-4 flex flex-col"
      >
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
              <span className="ml-auto text-[10px] text-slate-500 font-mono">
                {workspace?.materials?.items?.length ?? 0}
              </span>
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
                    {skillsSyncStatus?.current?.step
                      ? `step: ${String(skillsSyncStatus.current.step)}`
                      : "step: initializing…"}
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
              status === "Connected"
                ? "bg-emerald-50 text-emerald-700 border-emerald-200"
                : "bg-rose-50 text-rose-700 border-rose-200"
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
            className="mt-2 w-full text-xs bg-white hover:bg-slate-50 border border-slate-200 rounded px-2 py-2 transition flex items-center justify-center gap-2"
            title="Save API key into encrypted user secrets (~/.aevatar/secrets.json)"
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
              <div
                ref={messagesScrollRef}
                onScroll={handleChatScroll}
                className="absolute inset-0 overflow-y-auto px-6 py-6 space-y-6"
              >
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
                      onToggleCollapse={() =>
                        setCollapsedMessages((prev) => ({ ...prev, [id]: !Boolean(prev[id]) }))
                      }
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
                    setAtBottom(true);
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
              busy={isSending}
              onSend={send}
            />
          </div>
        </div>

        {/* Vibe cards (outside chat) */}
        <div className="w-[420px] shrink-0 min-h-0 border-l border-slate-200 bg-slate-50 p-4 overflow-y-auto">
          <div className="space-y-4">
            <div className="text-xs text-slate-500">
              Vibe cards live here (goals / DAG / trace / agents). Chat stays centered.
            </div>

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
                      <details
                        key={step}
                        open={open}
                        className="border border-slate-200 rounded-xl bg-slate-50 px-3 py-2"
                      >
                        <summary className="cursor-pointer select-none flex items-center gap-2 text-xs">
                          <span className={`font-mono ${st === "done" ? "text-emerald-700" : st === "running" ? "text-indigo-700" : "text-slate-600"}`}>
                            {st === "done" ? "done" : st === "running" ? "run" : "…" }
                          </span>
                          <span className="font-mono text-slate-900 break-all">{step}</span>
                        </summary>
                        <div className="mt-2 text-[11px] text-slate-600">
                          {runSteps.map?.[step]?.startedAt ? `startedAt: ${new Date(runSteps.map[step].startedAt).toLocaleTimeString()}` : ""}
                          {runSteps.map?.[step]?.finishedAt ? ` · finishedAt: ${new Date(runSteps.map[step].finishedAt).toLocaleTimeString()}` : ""}
                                </div>
                      </details>
                              );
                  })
                          )}
                        </div>
            </details>

            <details open className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
              <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                <Activity size={14} className="text-indigo-600" />
                <span className="text-sm font-semibold text-slate-900">Goals</span>
                <span className="ml-auto text-xs text-slate-500 font-mono">
                  {Array.isArray(vibeGoals?.items) ? vibeGoals.items.length : 0}
                </span>
              </summary>
              <div className="p-4">
                <GoalsPanel
                  sessionId={sessionId}
                  connected={connected}
                  snapshot={vibeGoals}
                  onSaved={(snap) => setVibeGoals(snap)}
                />
                    </div>
            </details>

            <details className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
              <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                <FileText size={14} className="text-indigo-600" />
                <span className="text-sm font-semibold text-slate-900">DAG</span>
                <span className="ml-auto text-xs text-slate-500 font-mono">
                  n={Array.isArray(vibeDag?.nodes) ? vibeDag.nodes.length : 0} e=
                  {Array.isArray(vibeDag?.edges) ? vibeDag.edges.length : 0}
                </span>
              </summary>
              <div className="p-4">
                <DagPanel
                  sessionId={sessionId}
                  connected={connected}
                  snapshot={vibeDag}
                  onSnapshot={(snap) => setVibeDag(snap)}
                />
                </div>
            </details>

            <details open className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
              <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                <Activity size={14} className="text-indigo-600" />
                <span className="text-sm font-semibold text-slate-900">Trace</span>
                <span className="ml-auto text-xs text-slate-500 font-mono">
                  {Array.isArray(vibeTrace?.items) ? vibeTrace.items.length : 0}
                </span>
              </summary>
              <div className="p-4">
                <TracePanel snapshot={vibeTrace} />
              </div>
            </details>

            <details className="bg-white border border-slate-200 rounded-2xl shadow-sm overflow-hidden">
              <summary className="cursor-pointer select-none px-4 py-3 flex items-center gap-2 border-b border-slate-200">
                <Activity size={14} className="text-indigo-600" />
                <span className="text-sm font-semibold text-slate-900">Agents</span>
                <span className="ml-auto text-xs text-slate-500 font-mono">
                  {Array.isArray(vibeAgents?.roster) ? vibeAgents.roster.length : 0}
                </span>
              </summary>
              <div className="p-4 text-xs">
                {Array.isArray(vibeAgents?.roster) ? (
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
                    {vibeAgents.roster.map((r: any) => (
            <button
                        key={String(r?.agent ?? "")}
                        onClick={() => setChatAgentFilter(String(r?.agent ?? ""))}
                        className={`w-full flex items-center justify-between gap-3 rounded px-2 py-1 border transition ${
                          chatAgentFilter === String(r?.agent ?? "")
                            ? "bg-indigo-50 border-indigo-200 text-indigo-700"
                            : "bg-white border-slate-200 text-slate-700 hover:bg-slate-50"
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

      {/* Tools Browser Modal */}
      <ApiKeyModal
        open={apiKeyOpen}
        defaultProvider={apiInfo?.llm?.default ?? ""}
        providers={Array.isArray(apiInfo?.llm?.providers) ? apiInfo.llm.providers : []}
        onClose={() => setApiKeyOpen(false)}
        onSaved={() => void refreshInfo()}
      />

      {toolsOpen && (
        <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
          <div className="w-full max-w-3xl bg-white border border-slate-200 rounded-xl overflow-hidden">
            <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200">
              <div className="text-sm font-medium text-slate-900">Tools Catalog</div>
              <button
                onClick={() => setToolsOpen(false)}
                className="p-2 rounded hover:bg-slate-100 transition"
                aria-label="Close"
              >
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
              <button
                onClick={() => setWorkspaceOpen(false)}
                className="p-2 rounded hover:bg-slate-100 transition"
                aria-label="Close"
              >
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

function ChatMessageRow(props: {
  id: string;
  store: MessageStore;
  messageMetaRef: { current: Record<string, any> };
  parseAgUiMessageId: (messageId: string) => { sessionId: string; agent: string; runId: string; rest: string };
  agentDisplayName: (agent: string) => string;
  collapsed: boolean;
  onToggleCollapse: () => void;
}) {
  const { id, store, messageMetaRef, parseAgUiMessageId, agentDisplayName, collapsed, onToggleCollapse } = props;

  // Subscribe only to this message's version so token appends don't re-render the whole list.
  useSyncExternalStore(store.subscribe, () => store.getMessageVersion(id), () => 0);

  const msg = store.getMessage(id) ?? { id, role: "assistant", content: "", isFinal: false };
  const meta = parseAgUiMessageId(msg.id);
  const agentMeta = messageMetaRef.current[msg.id]?.agent ?? meta.agent;
  const agent = agentDisplayName(agentMeta);

  const isSystem = msg.role === "system";
  const isAssistant = msg.role === "assistant";
  const isAgentOutput = isAssistant && !isSystem;
  const isCollapsed = isAgentOutput && collapsed;
  const isStreaming = isAssistant && msg.isFinal !== true;

  const preview = (() => {
    const t = String(msg.content || "").replace(/\s+/g, " ").trim();
    if (!t) return "";
    if (t.length <= 220) return t;
    return t.slice(0, 220) + "…";
  })();

  return (
    <div
      className={`flex gap-4 ${isAssistant ? "bg-slate-50 border border-slate-200 p-4 rounded-2xl" : ""} ${
        isSystem ? "opacity-90" : ""
      }`}
    >
      <div
        className={`w-8 h-8 rounded-full flex items-center justify-center shrink-0 border ${
          isAssistant
            ? "bg-indigo-600 border-indigo-600 text-white"
            : isSystem
              ? "bg-white border-slate-200 text-slate-500"
              : "bg-slate-900 border-slate-900 text-white"
        }`}
        title={agent}
      >
        {isAssistant ? "AI" : isSystem ? "•" : "U"}
      </div>

      <div className="flex-1 space-y-2 min-w-0">
        <div className="flex items-center gap-2">
          <span
            className={`text-[11px] px-2 py-0.5 rounded border font-mono ${
              isAssistant
                ? "bg-indigo-50 text-indigo-700 border-indigo-200"
                : isSystem
                  ? "bg-slate-50 text-slate-600 border-slate-200"
                  : "bg-slate-50 text-slate-700 border-slate-200"
            }`}
            title={msg.id}
          >
            {isSystem ? "system" : agent}
          </span>

          {isAgentOutput && (
            <button
              onClick={onToggleCollapse}
              className="text-[11px] px-2 py-0.5 rounded border bg-white hover:bg-slate-50 border-slate-200 text-slate-700"
              title={isCollapsed ? "Expand" : "Collapse"}
            >
              {isCollapsed ? "Expand" : "Collapse"}
            </button>
          )}

          {isCollapsed && <span className="text-[11px] text-slate-500 truncate">{preview || "…"}</span>}
        </div>

        {!isCollapsed && (
          <>
            {isStreaming ? (
              <pre className="text-sm text-slate-900 whitespace-pre-wrap break-words">{msg.content || "…"}</pre>
            ) : (
              <div className="prose prose-sm prose-slate max-w-none leading-relaxed">
                {msg.content ? <ReactMarkdown remarkPlugins={[remarkGfm]}>{msg.content}</ReactMarkdown> : <span className="text-slate-400 italic">…</span>}
              </div>
            )}

            {msg.toolOutputs && msg.toolOutputs.length > 0 && (
              <div className="mt-4 grid gap-2">
                {msg.toolOutputs.map((tool) => (
                  <div key={tool.toolCallId} className="bg-white border border-slate-200 rounded-xl p-3 text-xs font-mono">
                    <div className="text-indigo-700 mb-1 flex items-center gap-1">
                      <TestTube size={12} /> {tool.name}
                      {tool.isMcp && (
                        <span className="ml-2 text-[10px] px-1.5 py-0.5 rounded bg-indigo-50 text-indigo-700 border border-indigo-200">
                          MCP
                        </span>
                      )}
                      {tool.status === "running" && (
                        <span className="ml-2 text-[10px] px-1.5 py-0.5 rounded bg-slate-100 text-slate-700 border border-slate-200">
                          RUNNING
                        </span>
                      )}
                      {tool.status === "done" && tool.success === false && (
                        <span className="ml-2 text-[10px] px-1.5 py-0.5 rounded bg-rose-50 text-rose-700 border border-rose-200">
                          FAIL
                        </span>
                      )}
                      {tool.durationMs != null && tool.status === "done" && (
                        <span className="ml-2 text-[10px] text-slate-500">{tool.durationMs}ms</span>
                      )}
                    </div>
                    {tool.error ? (
                      <pre className="text-rose-700 whitespace-pre-wrap break-words max-h-[60vh] overflow-auto">{tool.error}</pre>
                    ) : tool.resultPreview ? (
                      (() => {
                        const { main, rawJson } = formatToolPayload(tool.resultPreview!);
                        return (
                          <div className="space-y-2">
                            <pre className="text-slate-900 whitespace-pre-wrap break-words max-h-[60vh] overflow-auto">{main}</pre>
                            {rawJson && (
                              <details className="text-slate-600">
                                <summary className="cursor-pointer select-none">Raw JSON</summary>
                                <pre className="mt-2 text-slate-700 whitespace-pre-wrap break-words max-h-[60vh] overflow-auto">{rawJson}</pre>
                              </details>
                            )}
                          </div>
                        );
                      })()
                    ) : (
                      <div className="text-slate-500 italic">no output</div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

function applyJsonPatch(base: any, ops: any[]): any {
  // Minimal RFC6902 subset: add/replace on objects/arrays. Best-effort; fallback to base.
  let cur: any = base ?? {};

  const clone = (v: any) => (v && typeof v === "object" ? JSON.parse(JSON.stringify(v)) : v);
  cur = clone(cur);

  const getPath = (obj: any, parts: string[]) => {
    let node = obj;
    for (let i = 0; i < parts.length; i++) {
      if (node == null) return { parent: null, key: "" };
      if (i === parts.length - 1) return { parent: node, key: parts[i] };
      node = node[parts[i]];
    }
    return { parent: null, key: "" };
  };

  for (const op of ops) {
    const kind = String(op?.op ?? "");
    const path = String(op?.path ?? "");
    if (!kind || !path || !path.startsWith("/")) continue;
    const parts = path
      .split("/")
      .slice(1)
      .map((p) => p.replace(/~1/g, "/").replace(/~0/g, "~"));

    const { parent, key } = getPath(cur, parts);
    if (parent == null) continue;

    if (kind === "replace") {
      parent[key] = op?.value;
      continue;
    }

    if (kind === "add") {
      if (key === "-" && Array.isArray(parent)) {
        parent.push(op?.value);
      } else if (Array.isArray(parent)) {
        const idx = Number(key);
        if (!Number.isNaN(idx)) parent.splice(idx, 0, op?.value);
      } else {
        parent[key] = op?.value;
      }
    }
  }

  return cur;
}


