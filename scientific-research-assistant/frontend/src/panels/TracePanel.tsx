import { useMemo } from "react";

type TraceItem = {
  runId?: string;
  roundIndex?: number;
  updatedAt?: string;
  summaryPath?: string;
  preview?: string;
  agents?: string[];
  dagChangesCount?: number;
  kind?: string;
  redFlags?: string[];
};

type TraceSnapshot = {
  sessionId?: string;
  items?: TraceItem[];
};

export default function TracePanel(props: { snapshot: TraceSnapshot | null }) {
  const { snapshot } = props;
  const items = useMemo(() => (Array.isArray(snapshot?.items) ? snapshot!.items! : []), [snapshot]);

  const ordered = useMemo(() => {
    // Already appended in order; keep as-is but bound.
    return items.slice(-200).reverse();
  }, [items]);

  return (
    <div className="space-y-2">
      <div className="text-[11px] text-slate-500">
        items: <span className="font-mono">{items.length}</span>
      </div>
      {ordered.length === 0 ? (
        <div className="text-xs text-slate-500">No trace yet (run vibe once).</div>
      ) : (
        <div className="space-y-2 max-h-[55vh] overflow-auto">
          {ordered.map((it, idx) => (
            <div key={`${it.runId || "x"}:${it.roundIndex ?? idx}:${idx}`} className="border border-slate-200 rounded-lg p-2 bg-slate-50">
              <div className="flex items-center gap-2 text-xs text-slate-900">
                <span className="font-mono">round {typeof it.roundIndex === "number" ? it.roundIndex : "?"}</span>
                {it.runId && <span className="text-[11px] text-slate-500 font-mono">run {it.runId}</span>}
                {it.kind && (
                  <span className="text-[10px] px-2 py-0.5 rounded bg-rose-50 text-rose-700 border border-rose-200">
                    {it.kind}
                  </span>
                )}
                <span className="ml-auto text-[11px] text-slate-500">{it.updatedAt || ""}</span>
              </div>

              <div className="mt-1 text-[11px] text-slate-500">
                agents: {Array.isArray(it.agents) ? it.agents.join(", ") : "-"} · dagChanges:{" "}
                <span className="font-mono">{typeof it.dagChangesCount === "number" ? it.dagChangesCount : "-"}</span>
              </div>

              {it.redFlags && it.redFlags.length > 0 && (
                <div className="mt-1 text-[11px] text-rose-600 break-words">redFlags: {it.redFlags.join(", ")}</div>
              )}

              {it.summaryPath && (
                <div className="mt-1 text-[11px] text-slate-600 font-mono break-all">summary: {it.summaryPath}</div>
              )}

              {it.preview && (
                <pre className="mt-2 text-xs text-slate-900 whitespace-pre-wrap break-words max-h-56 overflow-auto border border-slate-200 rounded p-2 bg-white">
                  {it.preview}
                </pre>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}


