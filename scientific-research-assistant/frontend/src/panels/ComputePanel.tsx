import { useMemo, useState } from "react";

type ComputeDecision = {
  sessionId?: string;
  planId?: string;
  action?: string;
  comment?: string;
  path?: string;
  createdAt?: string;
};

export default function ComputePanel(props: {
  sessionId: string;
  connected: boolean;
  lastDecision: ComputeDecision | null;
  onDecision?: (d: ComputeDecision) => void;
}) {
  const { sessionId, connected, lastDecision, onDecision } = props;
  const [planId, setPlanId] = useState<string>("");
  const [comment, setComment] = useState<string>("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>("");

  const last = useMemo(() => lastDecision ?? null, [lastDecision]);

  async function post(action: "execute" | "degrade" | "skip") {
    if (!connected || !sessionId || busy) return;
    setBusy(true);
    setError("");
    try {
      const body = {
        planId: planId.trim(),
        action,
        comment: comment.trim(),
      };
      const res = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/compute/decision`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      });
      if (!res.ok) {
        const t = await res.text().catch(() => "");
        throw new Error(`HTTP ${res.status}${t ? `: ${t}` : ""}`);
      }
      const json: any = await res.json().catch(() => null);
      const d: ComputeDecision = {
        sessionId,
        planId: String(json?.planId ?? body.planId ?? ""),
        action: String(json?.action ?? action),
        path: String(json?.path ?? ""),
        comment: body.comment,
        createdAt: new Date().toISOString(),
      };
      onDecision?.(d);
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-2">
      <div className="text-xs text-slate-600">
        MVP card: record an explicit compute decision (execute / degrade / skip). Planner/verifier will start producing compute plans in later tasks.
      </div>

      <div className="grid grid-cols-1 gap-2">
        <input
          value={planId}
          onChange={(e) => setPlanId(e.target.value)}
          placeholder="planId (optional, e.g. dag_consensus_v1)"
          className="w-full text-xs bg-white border border-slate-200 rounded px-2 py-1 text-slate-900 placeholder:text-slate-400"
          disabled={!connected || busy}
        />
        <input
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          placeholder="comment (optional)"
          className="w-full text-xs bg-white border border-slate-200 rounded px-2 py-1 text-slate-900 placeholder:text-slate-400"
          disabled={!connected || busy}
        />
      </div>

      <div className="flex items-center gap-2">
        <button
          onClick={() => void post("execute")}
          className="text-xs px-2 py-1 rounded bg-emerald-600 hover:bg-emerald-500 border border-emerald-600 text-white disabled:opacity-50"
          disabled={!connected || busy}
          title="Execute full compute"
        >
          Execute
        </button>
        <button
          onClick={() => void post("degrade")}
          className="text-xs px-2 py-1 rounded bg-amber-600 hover:bg-amber-500 border border-amber-600 text-white disabled:opacity-50"
          disabled={!connected || busy}
          title="Degrade to cheaper/safer compute"
        >
          Degrade
        </button>
        <button
          onClick={() => void post("skip")}
          className="text-xs px-2 py-1 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
          disabled={!connected || busy}
          title="Skip compute"
        >
          Skip
        </button>
        <div className="ml-auto text-[11px] text-slate-500">{busy ? "Saving…" : ""}</div>
      </div>

      {error && <div className="text-[11px] text-rose-600 break-words">{error}</div>}

      <details open className="border border-slate-200 rounded-lg bg-white overflow-hidden">
        <summary className="cursor-pointer select-none px-3 py-2 text-xs text-slate-700 bg-slate-50 border-b border-slate-200">
          Last decision
        </summary>
        <div className="p-3 text-xs text-slate-700 space-y-1">
          {last ? (
            <>
              <div>
                action: <span className="font-mono">{String(last.action ?? "")}</span>
              </div>
              <div>
                planId: <span className="font-mono">{String(last.planId ?? "")}</span>
              </div>
              {last.path && (
                <div className="break-all">
                  path: <span className="font-mono text-slate-500">{String(last.path)}</span>
                </div>
              )}
              {last.createdAt && <div className="text-[11px] text-slate-500">{String(last.createdAt)}</div>}
              {last.comment && <div className="whitespace-pre-wrap break-words">{String(last.comment)}</div>}
            </>
          ) : (
            <div className="text-xs text-slate-500">No compute decision yet.</div>
          )}
        </div>
      </details>
    </div>
  );
}


