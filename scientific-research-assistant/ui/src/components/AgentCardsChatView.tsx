import { useEffect, useMemo, useState, useSyncExternalStore } from "react";
import { ChevronDown, TestTube } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { MessageStore } from "../app/messageStore";

function estimateTokens(text: string): number {
  const s = String(text || "");
  if (!s) return 0;
  // Heuristic:
  // - Streaming mode often lacks real usage numbers.
  // - Rough approximation: 1 token ≈ 4 chars (English-ish). Chinese tends to be slightly higher.
  return Math.max(1, Math.ceil(s.length / 4));
}

function formatTokens(n: number): string {
  if (!Number.isFinite(n) || n <= 0) return "0";
  if (n < 1000) return String(n);
  if (n < 1_000_000) return `${(n / 1000).toFixed(1)}k`;
  return `${(n / 1_000_000).toFixed(2)}m`;
}

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

function AgentCard(props: {
  agent: string;
  messageId: string;
  stepName?: string;
  providerName?: string;
  store: MessageStore;
  collapsed: boolean;
  onToggleCollapse: () => void;
  onOpenHistory: () => void;
}) {
  const { agent, messageId, stepName, providerName, store, collapsed, onToggleCollapse, onOpenHistory } = props;

  // Subscribe only to this message's version so token appends don't re-render the whole grid.
  useSyncExternalStore(store.subscribe, () => store.getMessageVersion(messageId), () => 0);

  const msg = store.getMessage(messageId);
  const hasMsg = Boolean(msg);
  const role = msg?.role ?? "assistant";
  const isStreaming = role === "assistant" && msg?.isFinal !== true;
  const tokens = estimateTokens(msg?.content || "");
  const canCollapse = hasMsg && role === "assistant";

  const tools = Array.isArray(msg?.toolOutputs) ? msg!.toolOutputs! : [];
  const toolCount = tools.length;
  const toolsRunning = tools.some((t) => t.status === "running");
  const [toolsOpen, setToolsOpen] = useState<boolean>(() => toolsRunning);
  const [toolsUserToggled, setToolsUserToggled] = useState(false);
  useEffect(() => {
    // Auto-open when tools start running, but don't fight the user's explicit toggle.
    if (!toolsUserToggled && toolsRunning) setToolsOpen(true);
  }, [toolsRunning, toolsUserToggled]);

  const preview = useMemo(() => {
    const t = String(msg?.content || "").replace(/\s+/g, " ").trim();
    if (!t) return "";
    if (t.length <= 260) return t;
    return t.slice(0, 260) + "…";
  }, [msg?.content]);

  return (
    <div className={`border rounded-2xl bg-white shadow-sm overflow-hidden ${hasMsg ? "border-slate-200" : "border-slate-200/70 opacity-80"}`}>
      <div
        className={`px-4 py-3 border-b border-slate-200 flex items-start justify-between gap-3 ${
          canCollapse ? "cursor-pointer hover:bg-slate-50" : ""
        }`}
        role={canCollapse ? "button" : undefined}
        tabIndex={canCollapse ? 0 : undefined}
        aria-expanded={canCollapse ? !collapsed : undefined}
        onClick={() => {
          if (canCollapse) onToggleCollapse();
        }}
        onKeyDown={(e) => {
          if (!canCollapse) return;
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            onToggleCollapse();
          }
        }}
        title={canCollapse ? (collapsed ? "Expand" : "Collapse") : undefined}
      >
        <div className="min-w-0">
          <div className="flex items-center gap-2 min-w-0">
            <div className="text-sm font-semibold text-slate-900 font-mono truncate" title={messageId}>
              {agent}
            </div>
            {isStreaming ? (
              <span className="text-[10px] px-2 py-0.5 rounded bg-amber-50 text-amber-700 border border-amber-200">STREAMING</span>
            ) : hasMsg ? (
              <span className="text-[10px] px-2 py-0.5 rounded bg-emerald-50 text-emerald-700 border border-emerald-200">DONE</span>
            ) : (
              <span className="text-[10px] px-2 py-0.5 rounded bg-slate-50 text-slate-700 border border-slate-200">WAIT</span>
            )}
          </div>
          <div className="mt-1 text-[11px] text-slate-600 font-mono break-words">
            {providerName || "-"} · {formatTokens(tokens)} tokens · {stepName || "-"}
          </div>
        </div>

        <div className="shrink-0 flex items-center gap-2">
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              onOpenHistory();
            }}
            className="text-[11px] px-2 py-1 rounded border bg-white hover:bg-slate-50 border-slate-200 text-slate-700"
            title="Open full history"
          >
            History
          </button>
          {canCollapse && (
            <div className="text-[11px] text-slate-500 font-mono flex items-center gap-1 select-none">
              <ChevronDown size={14} className={`transition-transform ${collapsed ? "" : "rotate-180"}`} />
              {collapsed ? "Expand" : "Collapse"}
            </div>
          )}
        </div>
      </div>

      <div className="p-4">
        {!hasMsg ? (
          <div className="text-xs text-slate-500">Waiting for output…</div>
        ) : collapsed ? (
          <div className="space-y-2">
            <pre className="text-xs text-slate-900 whitespace-pre-wrap break-words max-h-56 overflow-auto bg-slate-50 border border-slate-200 rounded-xl p-3">
              {preview || "…"}
            </pre>
            {toolCount > 0 && <div className="text-[11px] text-slate-500 font-mono">tools: {toolCount}</div>}
          </div>
        ) : (
          <>
            {isStreaming ? (
              <pre className="text-sm text-slate-900 whitespace-pre-wrap break-words max-h-[55vh] overflow-auto">{msg?.content || "…"}</pre>
            ) : (
              <div className="prose prose-sm prose-slate max-w-none leading-relaxed">
                {msg?.content ? <ReactMarkdown remarkPlugins={[remarkGfm]}>{msg.content}</ReactMarkdown> : <span className="text-slate-400 italic">…</span>}
              </div>
            )}

            {toolCount > 0 && (
              <details
                className="mt-4 border border-slate-200 rounded-xl bg-slate-50"
                open={toolsOpen}
                onToggle={(e) => {
                  setToolsUserToggled(true);
                  setToolsOpen(e.currentTarget.open);
                }}
              >
                <summary className="cursor-pointer select-none px-3 py-2 text-xs font-medium text-slate-700 flex items-center justify-between gap-3">
                  <div className="flex items-center gap-2 min-w-0">
                    <TestTube size={14} className="text-indigo-600" />
                    <span className="font-mono">Tools</span>
                    <span className="text-slate-500 font-mono">({toolCount})</span>
                    {toolsRunning && (
                      <span className="text-[10px] px-1.5 py-0.5 rounded bg-white text-slate-700 border border-slate-200">
                        RUNNING
                      </span>
                    )}
                  </div>
                  <ChevronDown size={14} className={`text-slate-500 transition-transform ${toolsOpen ? "rotate-180" : ""}`} />
                </summary>

                <div className="p-3 grid gap-2">
                  {tools.map((tool) => (
                    <div key={tool.toolCallId} className="bg-white border border-slate-200 rounded-xl p-3 text-xs font-mono">
                      <div className="text-indigo-700 mb-1 flex items-center gap-1">
                        <TestTube size={12} /> {tool.name}
                        {tool.isMcp && (
                          <span className="ml-2 text-[10px] px-1.5 py-0.5 rounded bg-indigo-50 text-indigo-700 border border-indigo-200">MCP</span>
                        )}
                        {tool.status === "running" && (
                          <span className="ml-2 text-[10px] px-1.5 py-0.5 rounded bg-slate-100 text-slate-700 border border-slate-200">RUNNING</span>
                        )}
                        {tool.status === "done" && tool.success === false && (
                          <span className="ml-2 text-[10px] px-1.5 py-0.5 rounded bg-rose-50 text-rose-700 border border-rose-200">FAIL</span>
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
              </details>
            )}
          </>
        )}
      </div>
    </div>
  );
}

export default function AgentCardsChatView(props: {
  sessionId: string;
  store: MessageStore;
  roster: any[] | null;
  messageMetaRef: { current: Record<string, any> };
  parseAgUiMessageId: (messageId: string) => { sessionId: string; agent: string; runId: string; rest: string };
  agentDisplayName: (agent: string) => string;
  agentProviders?: Record<string, string>;
  fallbackProviderName?: string;
  runIdHint?: string;
  promptHint?: string;
  chatAgentFilter: string;
  collapsedMessages: Record<string, boolean>;
  onToggleCollapse: (messageId: string) => void;
  onOpenHistory: (agent: string) => void;
  lastError?: string;
}) {
  const { sessionId, store, roster, messageMetaRef, parseAgUiMessageId, agentDisplayName, chatAgentFilter, collapsedMessages, onToggleCollapse, lastError } =
    props;
  const onOpenHistory = props.onOpenHistory;
  const fallbackProviderName = String(props.fallbackProviderName ?? "").trim();

  // Only re-render when message order changes (new cards), not on every streaming delta.
  useSyncExternalStore(store.subscribe, store.getOrderVersion, () => 0);

  const latestRunIdFromStore = useMemo(() => {
    const order = store.getOrder();
    for (let i = order.length - 1; i >= 0; i--) {
      const id = order[i];
      const p = parseAgUiMessageId(id);
      if (p.runId) return p.runId;
    }
    return "";
  }, [store, parseAgUiMessageId]);

  const runId = useMemo(() => {
    const hint = String(props.runIdHint ?? "").trim();
    return latestRunIdFromStore || hint;
  }, [latestRunIdFromStore, props.runIdHint]);

  const agents = useMemo(() => {
    const r = Array.isArray(roster) ? roster : [];
    const arr = r
      .map((x) => String(x?.agent ?? "").trim())
      .filter(Boolean)
      .map((x) => x.toLowerCase());
    if (arr.length > 0) return arr;
    return ["research_assistant"];
  }, [roster]);

  const filteredAgents = useMemo(() => {
    if (chatAgentFilter === "all") return agents;
    const q = String(chatAgentFilter || "").trim();
    if (!q) return agents;
    return agents.filter((a) => agentDisplayName(a) === q);
  }, [agents, chatAgentFilter, agentDisplayName]);

  const buildMessageId = (agent: string) => {
    const a = agent === "research_assistant" ? "assistant" : agent;
    return `msg:${sessionId}:${a}:${runId}`;
  };

  const userMessageId = runId ? `msg:${sessionId}:user:${runId}` : "";
  const userMsg = userMessageId ? store.getMessage(userMessageId) : null;

  if (!runId) {
    return (
      <div className="border border-slate-200 rounded-xl p-6 bg-slate-50">
        <div className="text-sm text-slate-900 font-medium">No run yet</div>
        <div className="text-xs text-slate-500 mt-2 leading-relaxed">
          - Select a session (left sidebar)
          <br />- Wait for SSE to connect (Status: Connected)
          <br />- Send a message below
        </div>
        {lastError && <div className="mt-3 text-xs text-rose-600 break-words">{lastError}</div>}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Prompt (user) */}
      <div className="border border-slate-200 rounded-2xl bg-white shadow-sm overflow-hidden">
        <div className="px-4 py-3 border-b border-slate-200 flex items-center justify-between gap-3">
          <div className="min-w-0">
            <div className="text-sm font-semibold text-slate-900">Prompt</div>
            <div className="mt-1 text-[11px] text-slate-600 font-mono break-words">run: {runId}</div>
          </div>
          <div className="text-[11px] text-slate-500 font-mono">
            {fallbackProviderName ? `default: ${fallbackProviderName}` : "per-agent providers"}
          </div>
        </div>
        <div className="p-4">
          <pre className="text-sm text-slate-900 whitespace-pre-wrap break-words">
            {String(userMsg?.content ?? "").trim() || String(props.promptHint ?? "").trim() || "…"}
          </pre>
        </div>
      </div>

      {/* Agent cards (non-sequential) */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {filteredAgents.map((a) => {
          const display = agentDisplayName(a);
          const mid = buildMessageId(a);
          const stepName = String(messageMetaRef.current[mid]?.stepName ?? "").trim();
          const collapsed = Boolean(collapsedMessages[mid]);
          const isRa = display === "research_assistant";
          return (
            <div key={a} className={isRa ? "md:col-span-2" : ""}>
              {(() => {
                const meta = messageMetaRef.current[mid] ?? {};
                const metaProvider = String(meta?.providerName ?? "").trim();
                const mappedProvider = String((props.agentProviders && props.agentProviders[display]) || "").trim();
                const p = metaProvider || mappedProvider || fallbackProviderName;
                return (
              <AgentCard
                agent={display}
                messageId={mid}
                stepName={stepName || (isRa ? "vibe" : "")}
                providerName={p}
                store={store}
                collapsed={collapsed}
                onToggleCollapse={() => onToggleCollapse(mid)}
                onOpenHistory={() => onOpenHistory(display)}
              />
                );
              })()}
            </div>
          );
        })}
      </div>
    </div>
  );
}


