import { useEffect, useMemo, useRef, useState } from "react";
import { AgUiClient } from "@agui/sdk";
import { Activity, FileText, Plus, Search, Send, TestTube, X } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

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
};

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
  const [apiInfo, setApiInfo] = useState<any>(null);
  const [sessions, setSessions] = useState<SessionInfo[]>([]);
  const [sessionId, setSessionId] = useState<string>("");
  const [input, setInput] = useState<string>("");
  const [mode, setMode] = useState<InputMode>("chat");
  const [isSending, setIsSending] = useState<boolean>(false);

  const [messages, setMessages] = useState<Message[]>([]);
  const [status, setStatus] = useState<"Disconnected" | "Connecting" | "Connected">("Disconnected");
  const [runStatus, setRunStatus] = useState<string>("");
  const [lastError, setLastError] = useState<string>("");

  const [tools, setTools] = useState<ToolSummary[]>([]);
  const [toolsOpen, setToolsOpen] = useState(false);
  const [toolSearch, setToolSearch] = useState("");
  const [mcpReconnectBusy, setMcpReconnectBusy] = useState(false);

  const [workspace, setWorkspace] = useState<any>(null);
  const [workspaceOpen, setWorkspaceOpen] = useState(false);

  const clientRef = useRef<any>(null);
  const messageMapRef = useRef<Map<string, Message>>(new Map());
  const messagesEndRef = useRef<HTMLDivElement>(null);

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
    setMessages([]);
    setWorkspace(null);
    messageMapRef.current.clear();

    const url = `/api/sessions/${encodeURIComponent(sid)}/agui/events`;
    const client: any = new (AgUiClient as any)(url);
    clientRef.current = client;
    setStatus("Connecting");

    const upsertMessage = (msg: Message) => {
      const map = messageMapRef.current;
      const existing = map.get(msg.id);
      map.set(
        msg.id,
        existing
          ? {
              ...existing,
              ...msg,
              toolOutputs: msg.toolOutputs ?? existing.toolOutputs,
            }
          : msg,
      );
      setMessages(Array.from(map.values()));
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

    const appendDelta = (id2: string, role: ChatRole, delta: string) => {
      const map = messageMapRef.current;
      const existing = map.get(id2);
      if (!existing) {
        map.set(id2, { id: id2, role, content: delta });
      } else {
        existing.content = `${existing.content}${delta}`;
        map.set(id2, existing);
      }
      setMessages(Array.from(map.values()));
    };

    const upsertTool = (messageId: string, tool: ToolOutput) => {
      const map = messageMapRef.current;
      const msg = map.get(messageId);
      if (!msg) return;

      const arr = [...(msg.toolOutputs || [])];
      const idx = arr.findIndex((x) => x.toolCallId === tool.toolCallId);
      if (idx >= 0) arr[idx] = tool;
      else arr.push(tool);

      map.set(messageId, { ...msg, toolOutputs: arr });
      setMessages(Array.from(map.values()));
    };

    client.on("MESSAGES_SNAPSHOT", (evt: any) => {
      const arr: any[] = evt?.messages ?? [];
      messageMapRef.current.clear();
      for (const m of arr) {
        if (!m?.id) continue;
        messageMapRef.current.set(m.id, {
          id: m.id,
          role: (m.role ?? "assistant") as ChatRole,
          content: m.content ?? "",
        });
      }
      setMessages(Array.from(messageMapRef.current.values()));
    });

    client.on("TEXT_MESSAGE_START", (evt: any) => {
      const mid = evt?.messageId;
      const role = (evt?.role ?? "assistant") as ChatRole;
      if (!mid) return;
      upsertMessage({ id: mid, role, content: "" });
    });

    client.on("TEXT_MESSAGE_CONTENT", (evt: any) => {
      const mid = evt?.messageId;
      const delta = evt?.delta ?? "";
      if (!mid || !delta) return;
      const role = messageMapRef.current.get(mid)?.role ?? "assistant";
      appendDelta(mid, role, delta);
    });

    client.on("TEXT_MESSAGE_END", (_evt: any) => {
      // no-op
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

    client.on("RUN_STARTED", (_evt: any) => setRunStatus("Running…"));
    client.on("RUN_FINISHED", (evt: any) => {
      setRunStatus("");

      // Fallback: if TEXT_MESSAGE_CONTENT was missed, use RUN_FINISHED payload to backfill.
      const r = evt?.result ?? {};
      const mid = r?.assistantMessageId;
      const text = r?.assistant;
      if (typeof mid !== "string" || !mid) return;
      if (typeof text !== "string" || !text) return;

      const map = messageMapRef.current;
      const existing = map.get(mid);
      if (!existing) {
        map.set(mid, { id: mid, role: "assistant", content: text });
      } else if (!existing.content) {
        existing.content = text;
        map.set(mid, existing);
      }
      setMessages(Array.from(map.values()));
    });
    client.on("RUN_ERROR", (evt: any) => setRunStatus(`Error: ${(evt?.message ?? "run failed").toString()}`));

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

    client.on?.("open", () => {
      setStatus("Connected");
      void pullToolsSnapshot();
    });
    client.on?.("error", () => {
      setLastError("SSE disconnected (EventSource error). Check backend/proxy and session validity.");
      disconnect();
    });
  }

  async function send() {
    const text = input.trim();
    if (!connected || !sessionId || !text || isSending) return;

    setIsSending(true);
    try {
      setLastError("");
      const res = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/input`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ message: text, mode }),
      });
      if (!res.ok) {
        const body = await res.text().catch(() => "");
        throw new Error(`HTTP ${res.status}${body ? `: ${body}` : ""}`);
      }
      setInput("");
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
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  return (
    <div className="flex h-screen bg-gray-900 text-white font-sans overflow-hidden">
      {/* Sidebar */}
      <div className="w-72 bg-gray-800 border-r border-gray-700 p-4 hidden md:flex flex-col">
        <div className="flex items-center gap-2 mb-6">
          <TestTube className="text-purple-400" />
          <div className="min-w-0">
            <div className="font-bold text-lg tracking-tight truncate">Sci-Assist</div>
            <div className="text-xs text-gray-500 truncate">AG-UI sessions (snapshot-first SSE)</div>
          </div>
        </div>

        <div className="space-y-2">
          <button
            onClick={createSession}
            className="flex items-center justify-center gap-2 w-full p-2 rounded bg-purple-600 hover:bg-purple-500 transition text-sm"
          >
            <Plus size={16} /> New session
          </button>

          <div className="mt-3 text-xs text-gray-400">Sessions</div>
          <div className="max-h-48 overflow-y-auto space-y-1">
            {sessions.length === 0 ? (
              <div className="text-xs text-gray-500">No sessions yet.</div>
            ) : (
              sessions.map((s) => (
                <button
                  key={s.sessionId}
                  onClick={() => connectToSession(s.sessionId)}
                  className={`w-full text-left p-2 rounded border text-xs transition ${
                    s.sessionId === sessionId
                      ? "bg-gray-900 border-purple-700 text-purple-200"
                      : "bg-gray-800 border-gray-700 text-gray-300 hover:bg-gray-700"
                  }`}
                >
                  <div className="font-mono">{s.sessionId}</div>
                  <div className="text-[11px] text-gray-500 truncate">{s.createdAt}</div>
                </button>
              ))
            )}
          </div>

          <div className="mt-4 space-y-2">
            <button className="flex items-center gap-3 w-full p-2 rounded hover:bg-gray-700 text-gray-300 transition text-sm">
              <Activity size={16} /> Current Session
            </button>
            <button className="flex items-center gap-3 w-full p-2 rounded hover:bg-gray-700 text-gray-300 transition text-sm">
              <FileText size={16} /> Saved Reports
            </button>
          </div>
        </div>

        <div className="mt-auto">
          <div className="mt-6 bg-gray-900/40 border border-gray-700 rounded-lg p-3">
            <div className="text-xs text-gray-400 mb-2">Claude Scientific Skills (MCP)</div>
            <div className="text-sm flex items-center justify-between">
              <span className="text-gray-300">MCP Tools</span>
              <span className="text-purple-300 font-mono">{mcpTools.length}</span>
            </div>
            <div className="text-xs text-gray-500 mt-1">
              Total tools: <span className="font-mono">{tools.length}</span>
            </div>
            <button
              onClick={() => setToolsOpen(true)}
              className="mt-3 w-full text-xs bg-gray-800 hover:bg-gray-700 border border-gray-700 rounded px-2 py-2 transition flex items-center justify-center gap-2"
              disabled={!connected}
              title={connected ? "Browse tools" : "Connect to a session first"}
            >
              <Search size={14} /> Browse tools
            </button>

            <button
              onClick={() => setWorkspaceOpen(true)}
              className="mt-2 w-full text-xs bg-gray-800 hover:bg-gray-700 border border-gray-700 rounded px-2 py-2 transition flex items-center justify-center gap-2 disabled:opacity-50"
              disabled={!connected}
              title={connected ? "View workspace state (materials/graph)" : "Connect to a session first"}
            >
              <FileText size={14} /> Workspace
              <span className="ml-auto text-[10px] text-gray-400 font-mono">
                {workspace?.materials?.items?.length ?? 0}
              </span>
            </button>

            <button
              onClick={() => void reconnectMcp()}
              className="mt-2 w-full text-xs bg-gray-800 hover:bg-gray-700 border border-gray-700 rounded px-2 py-2 transition flex items-center justify-center gap-2 disabled:opacity-50"
              disabled={!connected || mcpReconnectBusy}
              title={connected ? "Reconnect MCP server and refresh tools" : "Connect to a session first"}
            >
              {mcpReconnectBusy ? "Reconnecting…" : "Reconnect MCP"}
            </button>
          </div>

          <div
            className={`mt-4 text-xs px-2 py-1 rounded ${
              status === "Connected" ? "bg-green-900 text-green-300" : "bg-red-900 text-red-300"
            }`}
          >
            Status: {status}
          </div>

          {lastError && <div className="mt-2 text-[11px] text-red-300 break-words">{lastError}</div>}

          <div className="mt-2 text-[11px] text-gray-500">
            Default LLM: <span className="font-mono">{apiInfo?.llm?.default ?? "-"}</span>
          </div>
        </div>
      </div>

      {/* Main */}
      <div className="flex-1 flex flex-col relative">
        <header className="h-14 border-b border-gray-800 flex items-center px-6 bg-gray-900/50 backdrop-blur">
          <div className="flex items-center gap-3 min-w-0">
            <div className="text-sm font-medium text-gray-400 truncate">
              Session: <span className="font-mono text-gray-200">{sessionId || "(none)"}</span>
            </div>
            {runStatus && (
              <span className="text-xs px-2 py-1 rounded bg-gray-800 border border-gray-700 text-gray-300 truncate">
                {runStatus}
              </span>
            )}
          </div>
        </header>

        {/* Messages */}
        <div className="flex-1 overflow-y-auto p-6 space-y-6 scrollbar-thin scrollbar-thumb-gray-700">
          {messages.length === 0 ? (
            <div className="max-w-3xl mx-auto border border-gray-800 rounded-xl p-6 bg-gray-900/40">
              <div className="text-sm text-gray-200 font-medium">No messages yet</div>
              <div className="text-xs text-gray-500 mt-2 leading-relaxed">
                - Create/select a session (left sidebar)<br />
                - Wait for SSE to connect (Status: Connected)<br />
                - Send a message below
              </div>
              {lastError && <div className="mt-3 text-xs text-red-300 break-words">{lastError}</div>}
            </div>
          ) : (
            messages.map((msg) => (
              <div
                key={msg.id}
                className={`flex gap-4 ${msg.role === "assistant" ? "bg-gray-800/50 p-4 rounded-lg" : ""}`}
              >
                <div
                  className={`w-8 h-8 rounded-full flex items-center justify-center shrink-0 ${
                    msg.role === "assistant" ? "bg-purple-600" : "bg-blue-600"
                  }`}
                >
                  {msg.role === "assistant" ? "AI" : "U"}
                </div>
                <div className="flex-1 space-y-2 min-w-0">
                  <div className="prose prose-invert prose-sm max-w-none leading-relaxed">
                    {msg.content ? (
                      <ReactMarkdown remarkPlugins={[remarkGfm]}>{msg.content}</ReactMarkdown>
                    ) : (
                      <span className="text-gray-500 italic">…</span>
                    )}
                  </div>

                  {msg.toolOutputs && msg.toolOutputs.length > 0 && (
                    <div className="mt-4 grid gap-2">
                      {msg.toolOutputs.map((tool) => (
                        <div
                          key={tool.toolCallId}
                          className="bg-gray-900 border border-gray-700 rounded p-3 text-xs font-mono"
                        >
                          <div className="text-purple-400 mb-1 flex items-center gap-1">
                            <TestTube size={12} /> {tool.name}
                            {tool.isMcp && (
                              <span className="ml-2 text-[10px] px-1.5 py-0.5 rounded bg-purple-900/60 text-purple-200 border border-purple-800">
                                MCP
                              </span>
                            )}
                            {tool.status === "running" && (
                              <span className="ml-2 text-[10px] px-1.5 py-0.5 rounded bg-gray-800 text-gray-200 border border-gray-700">
                                RUNNING
                              </span>
                            )}
                            {tool.status === "done" && tool.success === false && (
                              <span className="ml-2 text-[10px] px-1.5 py-0.5 rounded bg-red-900/60 text-red-200 border border-red-800">
                                FAIL
                              </span>
                            )}
                            {tool.durationMs != null && tool.status === "done" && (
                              <span className="ml-2 text-[10px] text-gray-400">{tool.durationMs}ms</span>
                            )}
                          </div>
                          {tool.error ? (
                            <pre className="text-red-300 whitespace-pre-wrap break-words max-h-[60vh] overflow-auto">
                              {tool.error}
                            </pre>
                          ) : tool.resultPreview ? (
                            (() => {
                              const { main, rawJson } = formatToolPayload(tool.resultPreview!);
                              return (
                                <div className="space-y-2">
                                  <pre className="text-gray-200 whitespace-pre-wrap break-words max-h-[60vh] overflow-auto">
                                    {main}
                                  </pre>
                                  {rawJson && (
                                    <details className="text-gray-400">
                                      <summary className="cursor-pointer select-none">Raw JSON</summary>
                                      <pre className="mt-2 text-gray-300 whitespace-pre-wrap break-words max-h-[60vh] overflow-auto">
                                        {rawJson}
                                      </pre>
                                    </details>
                                  )}
                                </div>
                              );
                            })()
                          ) : (
                            <div className="text-gray-500 italic">no output</div>
                          )}
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            ))
          )}
          <div ref={messagesEndRef} />
        </div>

        {/* Input */}
        <div className="p-4 bg-gray-900 border-t border-gray-800">
          <div className="max-w-4xl mx-auto relative">
            <div className="absolute left-2 top-1/2 -translate-y-1/2">
              <select
                value={mode}
                onChange={(e) => setMode(e.target.value as InputMode)}
                className="bg-gray-900/50 border border-gray-700 rounded-lg px-2 py-1 text-xs text-gray-200 outline-none"
                disabled={!connected || isSending}
                title="Run mode"
              >
                <option value="chat">chat</option>
                <option value="vibe">vibe</option>
              </select>
            </div>
            <input
              type="text"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && void send()}
              placeholder={
                !sessionId
                  ? "Create/select a session first…"
                  : status !== "Connected"
                    ? "Connecting… (SSE)"
                    : mode === "vibe"
                      ? "Vibe researching… (axioms + references + multi-agent)"
                      : "Ask a scientific question…"
              }
              className="w-full bg-gray-800/50 border border-gray-700 rounded-xl pl-20 pr-12 py-3 focus:outline-none focus:ring-2 focus:ring-purple-500/50 transition placeholder-gray-500 text-sm"
              disabled={!connected || isSending}
            />
            <button
              onClick={() => void send()}
              className="absolute right-2 top-1/2 -translate-y-1/2 p-2 bg-purple-600 rounded-lg hover:bg-purple-500 transition text-white disabled:opacity-50 disabled:hover:bg-purple-600"
              disabled={!connected || isSending}
            >
              <Send size={16} />
            </button>
          </div>
          <div className="text-center text-xs text-gray-600 mt-2">Powered by Aevatar Framework & Claude Scientific Skills</div>
        </div>
      </div>

      {/* Tools Browser Modal */}
      {toolsOpen && (
        <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-4">
          <div className="w-full max-w-3xl bg-gray-900 border border-gray-700 rounded-xl overflow-hidden">
            <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
              <div className="text-sm font-medium text-gray-200">Tools Catalog</div>
              <button onClick={() => setToolsOpen(false)} className="p-2 rounded hover:bg-gray-800 transition" aria-label="Close">
                <X size={16} />
              </button>
            </div>

            <div className="p-4 border-b border-gray-800">
              <div className="flex items-center gap-2 bg-gray-800/50 border border-gray-700 rounded-lg px-3 py-2">
                <Search size={14} className="text-gray-400" />
                <input
                  value={toolSearch}
                  onChange={(e) => setToolSearch(e.target.value)}
                  placeholder="Search tool name / description…"
                  className="w-full bg-transparent outline-none text-sm text-gray-200 placeholder:text-gray-500"
                />
                <div className="text-xs text-gray-500 font-mono">
                  {mcpTools.length}/{tools.length}
                </div>
              </div>
              <div className="text-xs text-gray-500 mt-2">Showing up to 200 results (type to filter). MCP tools are tagged.</div>
            </div>

            <div className="max-h-[65vh] overflow-y-auto p-4 space-y-2">
              {filteredTools.length === 0 ? (
                <div className="text-sm text-gray-500">No tools matched.</div>
              ) : (
                filteredTools.map((t) => (
                  <div key={t.name} className="border border-gray-800 rounded-lg p-3 hover:border-gray-700 transition">
                    <div className="flex items-center justify-between gap-3">
                      <div className="text-sm text-gray-200 font-mono break-all">{t.name}</div>
                      <div className="flex items-center gap-2 shrink-0">
                        {(t.source || "").toUpperCase() === "MCP" && (
                          <span className="text-[10px] px-2 py-0.5 rounded bg-purple-900/60 text-purple-200 border border-purple-800">
                            MCP
                          </span>
                        )}
                        {t.category && (
                          <span className="text-[10px] px-2 py-0.5 rounded bg-gray-800 text-gray-300 border border-gray-700">
                            {t.category}
                          </span>
                        )}
                      </div>
                    </div>
                    {t.description && <div className="text-xs text-gray-400 mt-1 whitespace-pre-wrap">{t.description}</div>}
                  </div>
                ))
              )}
            </div>
          </div>
        </div>
      )}

      {/* Workspace Modal */}
      {workspaceOpen && (
        <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-4">
          <div className="w-full max-w-3xl bg-gray-900 border border-gray-700 rounded-xl overflow-hidden">
            <div className="flex items-center justify-between px-4 py-3 border-b border-gray-800">
              <div className="text-sm font-medium text-gray-200">Workspace State (STATE_SNAPSHOT)</div>
              <button
                onClick={() => setWorkspaceOpen(false)}
                className="p-2 rounded hover:bg-gray-800 transition"
                aria-label="Close"
              >
                <X size={16} />
              </button>
            </div>

            <div className="p-4">
              {workspace ? (
                <pre className="text-xs text-gray-200 whitespace-pre-wrap break-words max-h-[70vh] overflow-auto bg-gray-950/40 border border-gray-800 rounded-lg p-3">
                  {JSON.stringify(workspace, null, 2)}
                </pre>
              ) : (
                <div className="text-sm text-gray-500">No workspace state yet (run vibe once).</div>
              )}
            </div>
          </div>
        </div>
      )}
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


