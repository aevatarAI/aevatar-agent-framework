import { useEffect, useMemo, useRef, useState } from "react";
import { ChevronDown, Send, Upload } from "lucide-react";

type InputMode = "chat" | "vibe";

type AgentRosterItem = {
  agent: string;
  agentId?: string;
};

export default function Composer(props: {
  sessionId: string;
  connected: boolean;
  mode: InputMode;
  onModeChange: (mode: InputMode) => void;
  roster?: AgentRosterItem[] | null;
  providers?: string[] | null;
  providerName?: string | null;
  onProviderChange?: (providerName: string) => void;
  busy: boolean;
  onSend: (payload: {
    text: string;
    mode: InputMode;
    toAgents: string[];
    providerName?: string;
    files: File[];
  }) => Promise<void>;
}) {
  const { sessionId, connected, mode, onModeChange, roster, providers, providerName, onProviderChange, busy, onSend } = props;
  const [text, setText] = useState("");
  const [files, setFiles] = useState<File[]>([]);
  const [toAgents, setToAgents] = useState<string[]>([]); // empty => default research_assistant

  const agentDetailsRef = useRef<HTMLDetailsElement | null>(null);
  const providerDetailsRef = useRef<HTMLDetailsElement | null>(null);

  // Close dropdowns on outside click / ESC (details doesn't do this by default).
  useEffect(() => {
    function closeAll() {
      agentDetailsRef.current?.removeAttribute("open");
      providerDetailsRef.current?.removeAttribute("open");
    }

    function onPointerDown(ev: PointerEvent) {
      const t = ev.target as Node | null;
      if (!t) return;

      const agentEl = agentDetailsRef.current;
      const providerEl = providerDetailsRef.current;

      if (agentEl?.open && !agentEl.contains(t)) agentEl.removeAttribute("open");
      if (providerEl?.open && !providerEl.contains(t)) providerEl.removeAttribute("open");
    }

    function onKeyDown(ev: KeyboardEvent) {
      if (ev.key === "Escape") closeAll();
    }

    document.addEventListener("pointerdown", onPointerDown, true);
    document.addEventListener("keydown", onKeyDown, true);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown, true);
      document.removeEventListener("keydown", onKeyDown, true);
    };
  }, []);

  const agents = useMemo(() => {
    const base = Array.isArray(roster) && roster.length > 0 ? roster.map((x) => x.agent).filter(Boolean) : [];
    const fallback = ["research_assistant", "planner", "reasoner", "librarian", "verifier", "dag_builder", "paper_editor"];
    const list = base.length > 0 ? base : fallback;
    // Sort but keep research_assistant first.
    const uniq = Array.from(new Set(list));
    return uniq.sort((a, b) => {
      if (a === "research_assistant") return -1;
      if (b === "research_assistant") return 1;
      return a.localeCompare(b);
    });
  }, [roster]);

  const providerOptions = useMemo(() => {
    const list = Array.isArray(providers) ? providers.filter(Boolean) : [];
    return Array.from(new Set(list)).sort((a, b) => a.localeCompare(b));
  }, [providers]);

  const toLabel = useMemo(() => {
    const uniq = Array.from(new Set((toAgents || []).map((x) => String(x ?? "").trim()).filter(Boolean)));
    if (uniq.includes("*")) return "All agents";
    if (uniq.length === 0) return "research_assistant";
    if (uniq.length === 1) return uniq[0];
    return `${uniq[0]} +${uniq.length - 1}`;
  }, [toAgents]);

  const isBroadcast = useMemo(() => (toAgents || []).some((x) => String(x).trim() === "*"), [toAgents]);

  function toggleAgent(a: string) {
    const key = String(a ?? "").trim();
    if (!key) return;
    setToAgents((prev) => {
      const cur = Array.from(new Set((prev || []).map((x) => String(x ?? "").trim()).filter(Boolean)));
      if (cur.includes("*")) return cur; // broadcast is exclusive
      return cur.includes(key) ? cur.filter((x) => x !== key) : [...cur, key];
    });
  }

  function toggleBroadcast() {
    setToAgents((prev) => {
      const cur = Array.from(new Set((prev || []).map((x) => String(x ?? "").trim()).filter(Boolean)));
      return cur.includes("*") ? [] : ["*"];
    });
  }

  async function send() {
    const t = text.trim();
    if (!connected || !sessionId || busy || !t) return;
    await onSend({
      text: t,
      mode,
      toAgents,
      providerName: providerName ? String(providerName) : undefined,
      files,
    });
    setText("");
    setFiles([]);
  }

  return (
    <div className="p-4 bg-white border-t border-slate-200">
      <div className="max-w-4xl mx-auto space-y-2">
        {/* NOTE: keep overflow visible so dropdown menus can escape the card without being clipped */}
        <div className="rounded-2xl border border-slate-200 bg-white shadow-sm overflow-visible">
          <div className="p-4">
            <input
              type="text"
              value={text}
              onChange={(e) => setText(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && void send()}
              placeholder={
                !sessionId
                  ? "Create/select a session first…"
                  : !connected
                    ? "Connecting… (SSE)"
                    : mode === "vibe"
                      ? "Vibe researching… (goals + DAG + trace + multi-agent)"
                      : "Ask a scientific question…"
              }
              className="w-full bg-white border border-slate-200 rounded-xl px-3 py-3 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 transition placeholder-slate-400 text-sm text-slate-900"
              disabled={!connected || busy}
            />
          </div>

          {/* Bottom bar (opencode-ish): mode switch + agents + model + actions */}
          <div className="border-t border-slate-200 px-4 py-2 flex flex-wrap items-center gap-3">
            {/* Mode switch: chat (yellow) / vibe (blue) */}
            <div className="inline-flex rounded-lg border border-slate-200 overflow-hidden shrink-0">
              <button
                type="button"
                onClick={() => onModeChange("chat")}
                disabled={!connected || busy}
                className={`px-3 py-1 text-xs font-medium transition ${
                  mode === "chat" ? "bg-amber-400 text-slate-900" : "bg-white text-slate-700 hover:bg-slate-50"
                } disabled:opacity-50`}
                title="Chat mode"
              >
                chat
              </button>
              <button
                type="button"
                onClick={() => onModeChange("vibe")}
                disabled={!connected || busy}
                className={`px-3 py-1 text-xs font-medium transition ${
                  mode === "vibe" ? "bg-indigo-600 text-white" : "bg-white text-slate-700 hover:bg-slate-50"
                } disabled:opacity-50`}
                title="Vibe mode"
              >
                vibe
              </button>
            </div>

            {/* Agents dropdown (single / broadcast) */}
            <details
              ref={agentDetailsRef}
              className="relative min-w-0 max-w-[240px]"
              onToggle={(e) => {
                const d = e.currentTarget;
                if (d.open) providerDetailsRef.current?.removeAttribute("open");
              }}
            >
              <summary className="cursor-pointer select-none text-sm text-slate-900 flex items-center gap-2 min-w-0 [&::-webkit-details-marker]:hidden">
                <span className="font-semibold truncate">{toLabel}</span>
                <ChevronDown size={14} className="text-slate-400 shrink-0" />
              </summary>
              <div className="absolute left-0 bottom-full mb-2 z-50 w-60 max-h-[60vh] overflow-y-auto overscroll-contain rounded-xl border border-slate-200 bg-white shadow-xl">
                <div className="px-3 py-2 text-[11px] text-slate-500">
                  Empty = default to <span className="font-mono">research_assistant</span>. Select multiple to broadcast.
                </div>

                <label className="flex items-center gap-2 px-3 py-2 text-sm hover:bg-slate-50 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={isBroadcast}
                    onChange={() => toggleBroadcast()}
                    className="accent-indigo-600"
                  />
                  <span className="font-semibold">All agents (broadcast)</span>
                </label>

                <div className="border-t border-slate-200" />

                {agents.map((a) => {
                  const checked = (toAgents || []).some((x) => String(x).trim() === a);
                  return (
                    <label key={a} className="flex items-center gap-2 px-3 py-2 text-sm hover:bg-slate-50 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={checked}
                        disabled={isBroadcast}
                        onChange={() => toggleAgent(a)}
                        className="accent-indigo-600 disabled:opacity-50"
                      />
                      <span className="font-mono">{a}</span>
                    </label>
                  );
                })}

                {(toAgents?.length ?? 0) > 0 && (
                  <>
                    <div className="border-t border-slate-200" />
                    <button
                      type="button"
                      onClick={() => setToAgents([])}
                      className="w-full text-left px-3 py-2 text-sm hover:bg-slate-50 text-slate-700"
                    >
                      Clear selection
                    </button>
                  </>
                )}
              </div>
            </details>

            {/* Model / provider dropdown */}
            <details
              ref={providerDetailsRef}
              className="relative min-w-0 max-w-[240px]"
              onToggle={(e) => {
                const d = e.currentTarget;
                if (d.open) agentDetailsRef.current?.removeAttribute("open");
              }}
            >
              <summary className="cursor-pointer select-none text-sm text-slate-900 flex items-center gap-2 min-w-0 [&::-webkit-details-marker]:hidden">
                <span className="font-semibold truncate">{providerName || "default"}</span>
                <ChevronDown size={14} className="text-slate-400 shrink-0" />
              </summary>
              <div className="absolute left-0 bottom-full mb-2 z-50 w-60 max-h-[60vh] overflow-y-auto overscroll-contain rounded-xl border border-slate-200 bg-white shadow-xl">
                {providerOptions.length === 0 ? (
                  <div className="px-3 py-2 text-xs text-slate-500">No providers (check appsettings / secrets)</div>
                ) : (
                  providerOptions.map((p) => (
                    <button
                      key={p}
                      type="button"
                      onClick={() => {
                        onProviderChange?.(p);
                        providerDetailsRef.current?.removeAttribute("open");
                      }}
                      className={`w-full text-left px-3 py-2 text-sm hover:bg-slate-50 ${
                        p === providerName ? "bg-amber-50 text-amber-900" : "text-slate-900"
                      }`}
                    >
                      {p}
                    </button>
                  ))
                )}
              </div>
            </details>

            <div className="ml-auto flex items-center gap-2">
              <label
                className="p-2 rounded-lg hover:bg-slate-50 border border-slate-200 text-slate-700 cursor-pointer"
                title="Attach files"
              >
                <Upload size={16} />
                <input
                  type="file"
                  multiple
                  className="hidden"
                  onChange={(e) => setFiles(Array.from(e.target.files || []))}
                  disabled={!connected || busy}
                />
              </label>
              <button
                onClick={() => void send()}
                className="p-2 bg-indigo-600 rounded-lg hover:bg-indigo-500 transition text-white disabled:opacity-50 disabled:hover:bg-indigo-600"
                disabled={!connected || busy}
                title="Send"
              >
                <Send size={16} />
              </button>
            </div>
          </div>
        </div>

        {files.length > 0 && (
          <div className="text-[11px] text-slate-500 break-words">Files: {files.map((f) => f.name).join(", ")}</div>
        )}
        <div className="text-center text-xs text-slate-400 mt-2">Powered by Aevatar Framework & Claude Scientific Skills</div>
      </div>
    </div>
  );
}


