import { useMemo } from "react";

type BriefSnapshot = {
  sessionId?: string;
  version?: number;
  updatedAt?: string;
  rewrittenQuestion?: string;
  scope?: string;
  successCriteria?: string;
  terms?: { term?: string; meaning?: string }[];
  assumptions?: string[];
  risks?: string[];
  uncertainties?: string[];
  milestones?: { roundIndex?: number; expectedOutput?: string }[];
};

export default function BriefPanel(props: {
  sessionId: string;
  connected: boolean;
  snapshot: BriefSnapshot | null;
  onRefresh?: () => void;
}) {
  const { connected, snapshot, onRefresh } = props;

  const v = useMemo(() => (typeof snapshot?.version === "number" ? snapshot!.version : 0), [snapshot?.version]);
  const updatedAt = useMemo(() => String(snapshot?.updatedAt ?? ""), [snapshot?.updatedAt]);

  const terms = useMemo(() => (Array.isArray(snapshot?.terms) ? snapshot!.terms! : []), [snapshot?.terms]);
  const milestones = useMemo(
    () => (Array.isArray(snapshot?.milestones) ? snapshot!.milestones! : []),
    [snapshot?.milestones],
  );

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
        <div className="text-xs text-slate-500">No brief yet (run vibe once).</div>
      ) : (
        <div className="space-y-3">
          {snapshot?.rewrittenQuestion && (
            <div className="border border-slate-200 rounded-lg p-2 bg-slate-50">
              <div className="text-[11px] text-slate-500 mb-1">Rewritten question</div>
              <div className="text-xs text-slate-900 whitespace-pre-wrap break-words">{snapshot.rewrittenQuestion}</div>
            </div>
          )}

          <details open className="border border-slate-200 rounded-lg bg-white overflow-hidden">
            <summary className="cursor-pointer select-none px-3 py-2 text-xs text-slate-700 bg-slate-50 border-b border-slate-200">
              Scope & success
            </summary>
            <div className="p-3 space-y-2">
              {snapshot?.scope ? (
                <div>
                  <div className="text-[11px] text-slate-500 mb-1">Scope</div>
                  <div className="text-xs text-slate-900 whitespace-pre-wrap break-words">{snapshot.scope}</div>
                </div>
              ) : (
                <div className="text-xs text-slate-500">No scope.</div>
              )}
              {snapshot?.successCriteria ? (
                <div>
                  <div className="text-[11px] text-slate-500 mb-1">Success criteria</div>
                  <div className="text-xs text-slate-900 whitespace-pre-wrap break-words">{snapshot.successCriteria}</div>
                </div>
              ) : null}
            </div>
          </details>

          <details className="border border-slate-200 rounded-lg bg-white overflow-hidden">
            <summary className="cursor-pointer select-none px-3 py-2 text-xs text-slate-700 bg-slate-50 border-b border-slate-200">
              Milestones ({milestones.length})
            </summary>
            <div className="p-3">
              {milestones.length === 0 ? (
                <div className="text-xs text-slate-500">No milestones.</div>
              ) : (
                <ol className="list-decimal pl-5 space-y-1 text-xs text-slate-900">
                  {milestones.slice(0, 12).map((m, idx) => (
                    <li key={`${m.roundIndex ?? idx}:${idx}`} className="break-words">
                      <span className="font-mono text-slate-600">
                        {typeof m.roundIndex === "number" ? `Round ${m.roundIndex}` : "Round ?"}:
                      </span>{" "}
                      {String(m.expectedOutput ?? "")}
                    </li>
                  ))}
                </ol>
              )}
            </div>
          </details>

          <details className="border border-slate-200 rounded-lg bg-white overflow-hidden">
            <summary className="cursor-pointer select-none px-3 py-2 text-xs text-slate-700 bg-slate-50 border-b border-slate-200">
              Assumptions / risks / uncertainties
            </summary>
            <div className="p-3 grid grid-cols-1 gap-3">
              <div>
                <div className="text-[11px] text-slate-500 mb-1">Assumptions</div>
                {Array.isArray(snapshot?.assumptions) && snapshot!.assumptions!.length > 0 ? (
                  <ul className="list-disc pl-5 text-xs text-slate-900 space-y-1">
                    {snapshot!.assumptions!.slice(0, 12).map((x, i) => (
                      <li key={`a:${i}`} className="break-words">
                        {x}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <div className="text-xs text-slate-500">-</div>
                )}
              </div>
              <div>
                <div className="text-[11px] text-slate-500 mb-1">Risks</div>
                {Array.isArray(snapshot?.risks) && snapshot!.risks!.length > 0 ? (
                  <ul className="list-disc pl-5 text-xs text-slate-900 space-y-1">
                    {snapshot!.risks!.slice(0, 12).map((x, i) => (
                      <li key={`r:${i}`} className="break-words">
                        {x}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <div className="text-xs text-slate-500">-</div>
                )}
              </div>
              <div>
                <div className="text-[11px] text-slate-500 mb-1">Uncertainties</div>
                {Array.isArray(snapshot?.uncertainties) && snapshot!.uncertainties!.length > 0 ? (
                  <ul className="list-disc pl-5 text-xs text-slate-900 space-y-1">
                    {snapshot!.uncertainties!.slice(0, 12).map((x, i) => (
                      <li key={`u:${i}`} className="break-words">
                        {x}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <div className="text-xs text-slate-500">-</div>
                )}
              </div>
            </div>
          </details>

          <details className="border border-slate-200 rounded-lg bg-white overflow-hidden">
            <summary className="cursor-pointer select-none px-3 py-2 text-xs text-slate-700 bg-slate-50 border-b border-slate-200">
              Terms ({terms.length})
            </summary>
            <div className="p-3">
              {terms.length === 0 ? (
                <div className="text-xs text-slate-500">No terms.</div>
              ) : (
                <div className="space-y-2 max-h-64 overflow-auto">
                  {terms.slice(0, 24).map((t, idx) => (
                    <div key={`${t.term || "t"}:${idx}`} className="border border-slate-200 rounded-lg p-2 bg-slate-50">
                      <div className="text-xs font-mono text-slate-900 break-all">{String(t.term ?? "")}</div>
                      {t.meaning && (
                        <div className="mt-1 text-[11px] text-slate-700 whitespace-pre-wrap break-words">
                          {String(t.meaning)}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          </details>
        </div>
      )}
    </div>
  );
}


