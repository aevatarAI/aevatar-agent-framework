import { useMemo } from "react";
import { useSyncExternalStore } from "react";
import type { MessageStore } from "../../app/messageStore";

function estimateTokens(text: string): number {
  const s = String(text || "");
  if (!s) return 0;
  // Heuristic:
  // - Avoid dependency on provider-specific usage (streaming has no usage anyway).
  // - Roughly: 1 token ≈ 4 chars (works "ok-ish" for English; for Chinese it underestimates a bit).
  return Math.max(1, Math.ceil(s.length / 4));
}

function formatTokens(n: number): string {
  if (!Number.isFinite(n) || n <= 0) return "0";
  if (n < 1000) return String(n);
  if (n < 1_000_000) return `${(n / 1000).toFixed(1)}k`;
  return `${(n / 1_000_000).toFixed(2)}m`;
}

function WorkerCard(props: {
  messageId: string;
  store: MessageStore;
  messageMetaRef: { current: Record<string, any> };
  parseAgUiMessageId: (messageId: string) => { sessionId: string; agent: string; runId: string; rest: string };
  agentDisplayName: (agent: string) => string;
  activeAgentFilter: string;
  onFilterAgent: (agent: string) => void;
}) {
  const { messageId, store, messageMetaRef, parseAgUiMessageId, agentDisplayName, activeAgentFilter, onFilterAgent } = props;

  // Subscribe only to this message's version so streaming deltas don't re-render the whole grid.
  useSyncExternalStore(store.subscribe, () => store.getMessageVersion(messageId), () => 0);

  const msg = store.getMessage(messageId) ?? { id: messageId, role: "assistant", content: "", isFinal: false };
  const parsed = parseAgUiMessageId(messageId);
  const meta = messageMetaRef.current[messageId] ?? {};
  const agentRaw = String(meta?.agent ?? parsed.agent ?? "").trim();
  const agent = agentDisplayName(agentRaw);
  const stepName = String(meta?.stepName ?? "").trim();
  const providerName = String(meta?.providerName ?? "").trim();
  const isStreaming = msg.role === "assistant" && msg.isFinal !== true;
  const tokens = estimateTokens(msg.content || "");
  const isActive = activeAgentFilter === agentRaw;

  const preview = useMemo(() => {
    const t = String(msg.content || "").trim();
    if (!t) return "";
    // Keep card body stable; full content is still available in chat view.
    return t.length > 900 ? t.slice(0, 900) + "\n…(more in chat)…" : t;
  }, [msg.content]);

  return (
    <div className={`border rounded-xl p-3 bg-white hover:border-slate-300 transition ${isActive ? "border-indigo-300" : "border-slate-200"}`}>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2 min-w-0">
            <div className="text-sm font-semibold text-slate-900 font-mono truncate" title={agent}>
              {agentRaw || "agent"}
            </div>
            {isStreaming ? (
              <span className="text-[10px] px-2 py-0.5 rounded bg-amber-50 text-amber-700 border border-amber-200">STREAMING</span>
            ) : (
              <span className="text-[10px] px-2 py-0.5 rounded bg-emerald-50 text-emerald-700 border border-emerald-200">DONE</span>
            )}
          </div>
          <div className="mt-1 text-[11px] text-slate-600 font-mono break-words">
            {providerName || "-"} · {formatTokens(tokens)} tokens · {stepName || "vibe.worker"}
          </div>
        </div>
        <div className="shrink-0">
          <button
            type="button"
            onClick={() => onFilterAgent(agentRaw)}
            className={`text-[11px] px-2 py-1 rounded border transition ${
              isActive ? "bg-indigo-50 border-indigo-200 text-indigo-700" : "bg-white border-slate-200 text-slate-700 hover:bg-slate-50"
            }`}
            title="Filter chat to this agent"
          >
            Chat
          </button>
        </div>
      </div>

      <pre className="mt-2 text-xs text-slate-900 whitespace-pre-wrap break-words max-h-44 overflow-auto bg-slate-50 border border-slate-200 rounded-lg p-2">
        {preview || "…"}
      </pre>
    </div>
  );
}

export default function WorkersPanel(props: {
  store: MessageStore;
  messageMetaRef: { current: Record<string, any> };
  parseAgUiMessageId: (messageId: string) => { sessionId: string; agent: string; runId: string; rest: string };
  agentDisplayName: (agent: string) => string;
  chatAgentFilter: string;
  setChatAgentFilter: (agent: string) => void;
}) {
  const { store, messageMetaRef, parseAgUiMessageId, agentDisplayName, chatAgentFilter, setChatAgentFilter } = props;

  // Only re-render when the message order changes (new cards), not on every streaming delta.
  useSyncExternalStore(store.subscribe, store.getOrderVersion, () => 0);

  const { latestRunId, messageIds } = useMemo(() => {
    const order = store.getOrder();

    // Find latest run id from the tail of message order.
    let latest = "";
    for (let i = order.length - 1; i >= 0; i--) {
      const id = order[i];
      const msg = store.getMessage(id);
      if (!msg || msg.role !== "assistant") continue;
      const p = parseAgUiMessageId(id);
      if (p.runId) {
        latest = p.runId;
        break;
      }
    }

    if (!latest) return { latestRunId: "", messageIds: [] as string[] };

    // Only include “worker-projected” messages (those with message_meta),
    // so the cards match what VibeOrchestrator.Workers emits.
    const ids = order.filter((id) => {
      const msg = store.getMessage(id);
      if (!msg || msg.role !== "assistant") return false;
      const p = parseAgUiMessageId(id);
      if (p.runId !== latest) return false;
      return Boolean(messageMetaRef.current[id]?.agent);
    });

    return { latestRunId: latest, messageIds: ids };
  }, [store, messageMetaRef, parseAgUiMessageId]);

  if (!latestRunId) {
    return <div className="text-xs text-slate-500">No workers yet (run vibe once).</div>;
  }

  if (messageIds.length === 0) {
    return (
      <div className="text-xs text-slate-500">
        No worker cards for this run (<span className="font-mono">{latestRunId}</span>). Try running Vibe again.
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <div className="text-[11px] text-slate-500 font-mono break-words">
        run: {latestRunId} · cards: {messageIds.length}
      </div>

      <div className="grid grid-cols-1 gap-3">
        {messageIds.map((id) => (
          <WorkerCard
            key={id}
            messageId={id}
            store={store}
            messageMetaRef={messageMetaRef}
            parseAgUiMessageId={parseAgUiMessageId}
            agentDisplayName={agentDisplayName}
            activeAgentFilter={chatAgentFilter}
            onFilterAgent={(agent) => setChatAgentFilter(agent)}
          />
        ))}
      </div>
    </div>
  );
}


