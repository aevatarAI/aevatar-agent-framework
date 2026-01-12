import { useEffect, useState, useSyncExternalStore } from "react";
import { ChevronDown, TestTube } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { MessageStore } from "../app/messageStore";

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

export default function ChatMessageRow(props: {
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

  const tools = Array.isArray(msg.toolOutputs) ? msg.toolOutputs : [];
  const toolCount = tools.length;
  const toolsRunning = tools.some((t) => t.status === "running");
  const [toolsOpen, setToolsOpen] = useState<boolean>(() => toolsRunning);
  const [toolsUserToggled, setToolsUserToggled] = useState(false);
  useEffect(() => {
    // Auto-open when tools start running, but don't fight the user's explicit toggle.
    if (!toolsUserToggled && toolsRunning) setToolsOpen(true);
  }, [toolsRunning, toolsUserToggled]);

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
                {msg.content ? (
                  <ReactMarkdown remarkPlugins={[remarkGfm]}>{msg.content}</ReactMarkdown>
                ) : (
                  <span className="text-slate-400 italic">…</span>
                )}
              </div>
            )}

            {toolCount > 0 && (
              <details
                className="mt-4 border border-slate-200 rounded-xl bg-white"
                open={toolsOpen}
                onToggle={(e) => {
                  setToolsUserToggled(true);
                  setToolsOpen(e.currentTarget.open);
                }}
              >
                <summary className="cursor-pointer select-none px-3 py-2 text-xs font-medium text-slate-700 bg-slate-50 rounded-xl flex items-center justify-between gap-3">
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
                                  <pre className="mt-2 text-slate-700 whitespace-pre-wrap break-words max-h-[60vh] overflow-auto">
                                    {rawJson}
                                  </pre>
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


