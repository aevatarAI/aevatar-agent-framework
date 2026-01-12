import { useEffect, useMemo } from "react";
import { useSyncExternalStore } from "react";
import { X, TestTube } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { MessageStore } from "../app/messageStore";

function estimateTokens(text: string): number {
  const s = String(text || "");
  if (!s) return 0;
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
    const value = obj?.data?.value;
    if (typeof value === "string" && value.length > 0) return { main: value, rawJson: s };
    return { main: JSON.stringify(obj, null, 2), rawJson: s };
  } catch {
    return { main: s };
  }
}

function Section(props: { title: string; children: React.ReactNode; defaultOpen?: boolean }) {
  const { title, children, defaultOpen } = props;
  return (
    <details className="border border-slate-200 rounded-xl bg-white" open={defaultOpen}>
      <summary className="cursor-pointer select-none px-3 py-2 text-xs font-medium text-slate-700 bg-slate-50 rounded-xl">
        {title}
      </summary>
      <div className="p-3">{children}</div>
    </details>
  );
}

export default function AgentHistoryModal(props: {
  open: boolean;
  onClose: () => void;
  sessionId: string;
  agent: string;
  store: MessageStore;
  messageMetaRef: { current: Record<string, any> };
  parseAgUiMessageId: (messageId: string) => { sessionId: string; agent: string; runId: string; rest: string };
  agentDisplayName: (agent: string) => string;
}) {
  const { open, onClose, sessionId, agent, store, messageMetaRef, parseAgUiMessageId, agentDisplayName } = props;

  // Keep modal content updated (new runs) without re-render per token.
  useSyncExternalStore(store.subscribe, store.getOrderVersion, () => 0);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  const items = useMemo(() => {
    const want = String(agent || "").trim();
    const order = store.getOrder();
    const runs: any[] = [];

    // Walk newest-first and pick this agent's assistant messages.
    for (let i = order.length - 1; i >= 0; i--) {
      const id = order[i];
      const msg = store.getMessage(id);
      if (!msg) continue;
      if ((msg.role ?? "assistant") !== "assistant") continue;

      const parsed = parseAgUiMessageId(id);
      if (!parsed.runId) continue;

      const metaAgent = messageMetaRef.current[id]?.agent ?? parsed.agent;
      const disp = agentDisplayName(metaAgent);
      if (disp !== want) continue;

      const userId = `msg:${sessionId}:user:${parsed.runId}`;
      const userMsg = store.getMessage(userId);

      runs.push({
        runId: parsed.runId,
        stepName: String(messageMetaRef.current[id]?.stepName ?? "").trim(),
        messageId: id,
        user: String(userMsg?.content ?? "").trim(),
        response: String(msg.content ?? ""),
        toolOutputs: msg.toolOutputs ?? [],
        isFinal: msg.isFinal === true,
      });
    }

    // De-dupe by runId (should already be unique, but be defensive).
    const seen = new Set<string>();
    return runs.filter((r) => {
      if (!r?.runId || seen.has(r.runId)) return false;
      seen.add(r.runId);
      return true;
    });
  }, [agent, store, sessionId, messageMetaRef, parseAgUiMessageId, agentDisplayName]);

  const stats = useMemo(() => {
    let total = 0;
    for (const it of items) total += estimateTokens(it.response || "");
    return { conversations: items.length, totalTokens: total };
  }, [items]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4" onMouseDown={onClose}>
      <div
        className="w-full max-w-4xl bg-white border border-slate-200 rounded-2xl overflow-hidden shadow-xl"
        onMouseDown={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200 bg-white">
          <div className="min-w-0">
            <div className="text-sm font-semibold text-slate-900 font-mono truncate">{agent}</div>
            <div className="text-[11px] text-slate-500 font-mono break-words">
              per-agent providers · {formatTokens(stats.totalTokens)} tokens · {stats.conversations} conversations
            </div>
          </div>
          <button onClick={onClose} className="p-2 rounded hover:bg-slate-100 transition" aria-label="Close">
            <X size={16} />
          </button>
        </div>

        <div className="max-h-[75vh] overflow-auto p-4 space-y-3">
          {items.length === 0 ? (
            <div className="text-sm text-slate-500">No conversation history yet.</div>
          ) : (
            items.map((it: any, idx: number) => {
              const tokens = estimateTokens(it.response || "");
              const streaming = it.isFinal !== true;
              const err = "";
              return (
                <details key={it.runId} className="border border-slate-200 rounded-2xl bg-white" open={idx === 0}>
                  <summary className="cursor-pointer select-none px-4 py-3 flex items-center justify-between gap-3">
                    <div className="min-w-0">
                      <div className="text-xs text-slate-900 font-mono truncate">run {it.runId}</div>
                      <div className="mt-1 text-[11px] text-slate-500 font-mono break-words">
                        {it.stepName || "-"} · {formatTokens(tokens)} tokens{streaming ? " · streaming…" : ""}{err}
                      </div>
                    </div>
                    <div className="text-[11px] text-slate-500 font-mono">{it.messageId}</div>
                  </summary>

                  <div className="px-4 pb-4 space-y-3">
                    <Section title="User Prompt" defaultOpen={true}>
                      <pre className="text-sm text-slate-900 whitespace-pre-wrap break-words">{it.user || "…"}</pre>
                    </Section>

                    <Section title="Response" defaultOpen={true}>
                      {streaming ? (
                        <pre className="text-sm text-slate-900 whitespace-pre-wrap break-words max-h-[55vh] overflow-auto">{it.response || "…"}</pre>
                      ) : (
                        <div className="prose prose-sm prose-slate max-w-none leading-relaxed">
                          {it.response ? <ReactMarkdown remarkPlugins={[remarkGfm]}>{it.response}</ReactMarkdown> : <span className="text-slate-400 italic">…</span>}
                        </div>
                      )}
                    </Section>

                    {Array.isArray(it.toolOutputs) && it.toolOutputs.length > 0 && (
                      <Section title={`Tools (${it.toolOutputs.length})`} defaultOpen={false}>
                        <div className="grid gap-2">
                          {it.toolOutputs.map((tool: any) => (
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
                      </Section>
                    )}
                  </div>
                </details>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
}


