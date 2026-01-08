import { useMemo } from "react";

type DeliveryUi = {
  delivery?: {
    version?: number;
    updatedAt?: string;
    changedSummary?: string;
    paperOutlinePath?: string;
    paperDraftPath?: string;
    conclusionsPath?: string;
    evidencePath?: string;
    tasksPath?: string;
  };
  conclusions?: any[];
  evidence?: any[];
  tasks?: any[];
};

export default function DeliveryCenterPanel(props: {
  sessionId: string;
  connected: boolean;
  snapshot: DeliveryUi | null;
  onRefresh?: () => void;
}) {
  const { connected, snapshot, onRefresh } = props;

  const delivery = useMemo(() => snapshot?.delivery ?? null, [snapshot]);
  const conclusions = useMemo(() => (Array.isArray(snapshot?.conclusions) ? snapshot!.conclusions! : []), [snapshot]);
  const evidence = useMemo(() => (Array.isArray(snapshot?.evidence) ? snapshot!.evidence! : []), [snapshot]);
  const tasks = useMemo(() => (Array.isArray(snapshot?.tasks) ? snapshot!.tasks! : []), [snapshot]);

  const v = typeof delivery?.version === "number" ? delivery!.version : 0;
  const updatedAt = String(delivery?.updatedAt ?? "");

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        <button
          onClick={onRefresh}
          className="text-xs px-2 py-1 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
          disabled={!connected}
          title="Refresh from server"
        >
          Refresh
        </button>
        <div className="ml-auto text-[10px] text-slate-500 font-mono">
          v{v}
          {updatedAt ? ` · ${updatedAt}` : ""}
        </div>
      </div>

      {v <= 0 ? (
        <div className="text-xs text-slate-500">No delivery center snapshot yet (needs accepted DAG change).</div>
      ) : (
        <div className="space-y-3">
          {delivery?.changedSummary && (
            <div className="border border-slate-200 rounded-lg p-2 bg-slate-50">
              <div className="text-[11px] text-slate-500 mb-1">Changed summary</div>
              <div className="text-xs text-slate-900 whitespace-pre-wrap break-words">{delivery.changedSummary}</div>
            </div>
          )}

          <details open className="border border-slate-200 rounded-lg bg-white overflow-hidden">
            <summary className="cursor-pointer select-none px-3 py-2 text-xs text-slate-700 bg-slate-50 border-b border-slate-200">
              Conclusions ({conclusions.length})
            </summary>
            <div className="p-3 space-y-2 max-h-[45vh] overflow-auto">
              {conclusions.length === 0 ? (
                <div className="text-xs text-slate-500">No conclusion cards.</div>
              ) : (
                conclusions.slice(0, 20).map((c: any, idx: number) => (
                  <div key={`${c?.cardId ?? idx}:${idx}`} className="border border-slate-200 rounded-lg p-2 bg-slate-50">
                    <div className="flex items-center gap-2">
                      <div className="text-xs font-mono text-slate-700">{String(c?.cardId ?? "")}</div>
                      <div className="ml-auto text-[10px] px-2 py-0.5 rounded border border-slate-200 bg-white text-slate-700">
                        {String(c?.confidence ?? "")}
                      </div>
                    </div>
                    <div className="mt-1 text-xs text-slate-900 whitespace-pre-wrap break-words">{String(c?.claim ?? "")}</div>
                    {c?.notes && (
                      <div className="mt-1 text-[11px] text-slate-600 whitespace-pre-wrap break-words">{String(c.notes)}</div>
                    )}
                    {Array.isArray(c?.evidencePaths) && c.evidencePaths.length > 0 && (
                      <div className="mt-2 text-[11px] text-slate-600 break-words">
                        evidence:{" "}
                        <span className="font-mono text-slate-500">{c.evidencePaths.slice(0, 6).join(", ")}</span>
                      </div>
                    )}
                  </div>
                ))
              )}
            </div>
          </details>

          <details className="border border-slate-200 rounded-lg bg-white overflow-hidden">
            <summary className="cursor-pointer select-none px-3 py-2 text-xs text-slate-700 bg-slate-50 border-b border-slate-200">
              Evidence ({evidence.length})
            </summary>
            <div className="p-3 space-y-2 max-h-[45vh] overflow-auto">
              {evidence.length === 0 ? (
                <div className="text-xs text-slate-500">No evidence items.</div>
              ) : (
                evidence.slice(0, 30).map((e: any, idx: number) => (
                  <div key={`${e?.evidenceId ?? idx}:${idx}`} className="border border-slate-200 rounded-lg p-2 bg-slate-50">
                    <div className="flex items-center gap-2">
                      <div className="text-xs font-mono text-slate-700 break-all">{String(e?.evidenceId ?? "")}</div>
                      {e?.path && <div className="ml-auto text-[10px] text-slate-500 font-mono break-all">{String(e.path)}</div>}
                    </div>
                    {e?.title && <div className="mt-1 text-xs text-slate-900 break-words">{String(e.title)}</div>}
                    {e?.excerpt && (
                      <div className="mt-1 text-[11px] text-slate-600 whitespace-pre-wrap break-words">{String(e.excerpt)}</div>
                    )}
                    {e?.relevance && (
                      <div className="mt-1 text-[11px] text-slate-500 whitespace-pre-wrap break-words">
                        relevance: {String(e.relevance)}
                      </div>
                    )}
                  </div>
                ))
              )}
            </div>
          </details>

          <details className="border border-slate-200 rounded-lg bg-white overflow-hidden">
            <summary className="cursor-pointer select-none px-3 py-2 text-xs text-slate-700 bg-slate-50 border-b border-slate-200">
              Next tasks ({tasks.length})
            </summary>
            <div className="p-3 space-y-2 max-h-[45vh] overflow-auto">
              {tasks.length === 0 ? (
                <div className="text-xs text-slate-500">No next tasks.</div>
              ) : (
                tasks
                  .slice(0, 30)
                  .sort((a: any, b: any) => (Number(a?.priority ?? 0) || 0) - (Number(b?.priority ?? 0) || 0))
                  .map((t: any, idx: number) => (
                    <div key={`${t?.taskId ?? idx}:${idx}`} className="border border-slate-200 rounded-lg p-2 bg-slate-50">
                      <div className="flex items-center gap-2">
                        <div className="text-xs font-mono text-slate-700">{String(t?.taskId ?? "")}</div>
                        <div className="ml-auto text-[10px] px-2 py-0.5 rounded bg-white border border-slate-200 text-slate-700">
                          p{Number(t?.priority ?? 0) || 0}
                        </div>
                      </div>
                      <div className="mt-1 text-xs text-slate-900 break-words">{String(t?.title ?? "")}</div>
                      {t?.detail && (
                        <div className="mt-1 text-[11px] text-slate-600 whitespace-pre-wrap break-words">{String(t.detail)}</div>
                      )}
                      {Array.isArray(t?.blockedBy) && t.blockedBy.length > 0 && (
                        <div className="mt-1 text-[11px] text-slate-500 break-words">
                          blockedBy: <span className="font-mono">{t.blockedBy.slice(0, 8).join(", ")}</span>
                        </div>
                      )}
                    </div>
                  ))
              )}
            </div>
          </details>

          <details className="border border-slate-200 rounded-lg bg-white overflow-hidden">
            <summary className="cursor-pointer select-none px-3 py-2 text-xs text-slate-700 bg-slate-50 border-b border-slate-200">
              Files
            </summary>
            <div className="p-3 space-y-1 text-[11px] text-slate-600 font-mono break-all">
              {delivery?.paperOutlinePath ? <div>outline: {String(delivery.paperOutlinePath)}</div> : null}
              {delivery?.paperDraftPath ? <div>draft: {String(delivery.paperDraftPath)}</div> : null}
              {delivery?.conclusionsPath ? <div>conclusions: {String(delivery.conclusionsPath)}</div> : null}
              {delivery?.evidencePath ? <div>evidence: {String(delivery.evidencePath)}</div> : null}
              {delivery?.tasksPath ? <div>tasks: {String(delivery.tasksPath)}</div> : null}
            </div>
          </details>
        </div>
      )}
    </div>
  );
}


