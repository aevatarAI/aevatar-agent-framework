import { useMemo, useState } from "react";
import { Send } from "lucide-react";

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
  busy: boolean;
  onSend: (payload: {
    text: string;
    mode: InputMode;
    toAgents: string[];
    files: File[];
  }) => Promise<void>;
}) {
  const { sessionId, connected, mode, onModeChange, roster, busy, onSend } = props;
  const [text, setText] = useState("");
  const [files, setFiles] = useState<File[]>([]);
  const [toAgents, setToAgents] = useState<string[]>([]);

  const agents = useMemo(() => {
    const base = Array.isArray(roster) && roster.length > 0 ? roster.map((x) => x.agent).filter(Boolean) : [];
    const fallback = ["research_assistant", "planner", "reasoner", "librarian", "verifier", "dag_builder"];
    const list = base.length > 0 ? base : fallback;
    // Sort but keep research_assistant first.
    const uniq = Array.from(new Set(list));
    return uniq.sort((a, b) => {
      if (a === "research_assistant") return -1;
      if (b === "research_assistant") return 1;
      return a.localeCompare(b);
    });
  }, [roster]);

  function toggleAgent(a: string) {
    setToAgents((prev) => (prev.includes(a) ? prev.filter((x) => x !== a) : [...prev, a]));
  }

  async function send() {
    const t = text.trim();
    if (!connected || !sessionId || busy || !t) return;
    await onSend({ text: t, mode, toAgents, files });
    setText("");
    setFiles([]);
    setToAgents([]);
  }

  return (
    <div className="p-4 bg-white border-t border-slate-200">
      <div className="max-w-4xl mx-auto space-y-2">
        <div className="flex items-center gap-2">
          <select
            value={mode}
            onChange={(e) => onModeChange(e.target.value as InputMode)}
            className="bg-white border border-slate-200 rounded-lg px-2 py-1 text-xs text-slate-700 outline-none"
            disabled={!connected || busy}
            title="Run mode"
          >
            <option value="chat">chat</option>
            <option value="vibe">vibe</option>
          </select>

          <details className="relative">
            <summary className="cursor-pointer select-none text-xs px-2 py-1 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700">
              To ({toAgents.length === 0 ? "research_assistant" : toAgents.length})
            </summary>
            {/* 
              UX: Composer sits at the bottom of the viewport, so dropdown must open upward
              to avoid overflowing below the screen.
            */}
            <div className="absolute left-0 bottom-full mb-2 z-50 w-64 max-h-[60vh] overflow-y-auto overscroll-contain rounded-lg border border-slate-200 bg-white p-2 shadow-xl">
              <div className="text-[11px] text-slate-500 mb-2">
                Empty = default to research_assistant. Select multiple to broadcast.
              </div>
              {agents.map((a) => (
                <label key={a} className="flex items-center gap-2 py-1 text-xs text-slate-700">
                  <input
                    type="checkbox"
                    checked={toAgents.includes(a)}
                    onChange={() => toggleAgent(a)}
                    className="accent-indigo-600"
                  />
                  <span className="font-mono">{a}</span>
                </label>
              ))}
            </div>
          </details>

          <label className="ml-auto text-xs px-2 py-1 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 cursor-pointer">
            Attach
            <input
              type="file"
              multiple
              className="hidden"
              onChange={(e) => setFiles(Array.from(e.target.files || []))}
              disabled={!connected || busy}
            />
          </label>
        </div>

        {files.length > 0 && (
          <div className="text-[11px] text-slate-500 break-words">
            Files: {files.map((f) => f.name).join(", ")}
          </div>
        )}

        <div className="relative">
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
            className="w-full bg-white border border-slate-200 rounded-xl pl-3 pr-12 py-3 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 transition placeholder-slate-400 text-sm text-slate-900"
            disabled={!connected || busy}
          />
          <button
            onClick={() => void send()}
            className="absolute right-2 top-1/2 -translate-y-1/2 p-2 bg-indigo-600 rounded-lg hover:bg-indigo-500 transition text-white disabled:opacity-50 disabled:hover:bg-indigo-600"
            disabled={!connected || busy}
            title="Send"
          >
            <Send size={16} />
          </button>
        </div>
        <div className="text-center text-xs text-slate-400 mt-2">Powered by Aevatar Framework & Claude Scientific Skills</div>
      </div>
    </div>
  );
}


