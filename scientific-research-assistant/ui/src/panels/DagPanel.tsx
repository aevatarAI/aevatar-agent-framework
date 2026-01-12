import { useMemo, useState } from "react";
import DagGraphView from "../components/DagGraphView";
import type { SraTransport } from "../transport/SraTransport";

type DagNode = {
  id: string;
  type?: string;
  kind?: string;
  label?: string;
  proof?: string;
  owner?: string;
  attestationsCount?: number;
  attestations?: { pubkey?: string; signature?: string }[];
  updatedAt?: string;
  tags?: Record<string, string>;
};

type DagEdge = {
  fromId: string;
  toId: string;
  type?: string;
};

type DagSnapshot = {
  sessionId?: string;
  updatedAt?: string;
  nodes?: DagNode[];
  edges?: DagEdge[];
  truncated?: any;
};

export default function DagPanel(props: {
  transport: SraTransport;
  sessionId: string;
  connected: boolean;
  snapshot: DagSnapshot | null;
  onSnapshot?: (snap: DagSnapshot) => void;
  onOpenPage?: () => void;
}) {
  const { transport, sessionId, connected, snapshot, onSnapshot, onOpenPage } = props;
  const [q, setQ] = useState("");
  const [selected, setSelected] = useState<string>("");
  const [explain, setExplain] = useState<any>(null);
  const [chainMd, setChainMd] = useState<string>("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const nodes = useMemo(() => (Array.isArray(snapshot?.nodes) ? snapshot!.nodes! : []), [snapshot]);
  const edges = useMemo(() => (Array.isArray(snapshot?.edges) ? snapshot!.edges! : []), [snapshot]);

  const graphNodes = useMemo(() => nodes.slice(0, 200), [nodes]);
  const graphEdges = useMemo(() => edges.slice(0, 400), [edges]);

  const selectedNode = useMemo(() => {
    if (!selected) return null;
    return nodes.find((n) => n?.id === selected) ?? null;
  }, [nodes, selected]);

  const shortKey = (s: string, head = 10, tail = 8) => {
    const t = (s || "").trim();
    if (!t) return "";
    if (t.length <= head + tail + 3) return t;
    return `${t.slice(0, head)}…${t.slice(-tail)}`;
  };

  const filtered = useMemo(() => {
    const qq = q.trim().toLowerCase();
    const list = qq ? nodes.filter((n) => (n.id || "").toLowerCase().includes(qq) || (n.label || "").toLowerCase().includes(qq)) : nodes;
    return list.slice(0, 80);
  }, [nodes, q]);

  async function refresh() {
    if (!connected || !sessionId) return;
    setBusy(true);
    setError("");
    try {
      const getJson = transport.getJson;
      if (!getJson) throw new Error("Transport does not support GET JSON.");

      const json: any = await getJson(`/api/sessions/${encodeURIComponent(sessionId)}/dag`);
      if (json?.dag) onSnapshot?.(json.dag);
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  async function loadExplain(nodeId: string) {
    if (!connected || !sessionId || !nodeId) return;
    setSelected(nodeId);
    setBusy(true);
    setError("");
    try {
      const getJson = transport.getJson;
      if (!getJson) throw new Error("Transport does not support GET JSON.");

      const json: any = await getJson(`/api/sessions/${encodeURIComponent(sessionId)}/dag/${encodeURIComponent(nodeId)}/explain`);
      setExplain(json?.explain ?? null);

      // Best-effort: also load Knowledge Graph chain markdown (uses KnowledgeGraph backend).
      try {
        const chainJson: any = await getJson(`/api/sessions/${encodeURIComponent(sessionId)}/graph/${encodeURIComponent(nodeId)}/chain`);
        setChainMd(String(chainJson?.markdown ?? ""));
      } catch {
        setChainMd("");
      }
    } catch (e: any) {
      setError(e?.message ?? String(e));
      setExplain(null);
      setChainMd("");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        <input
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder="Search node id / label…"
          className="flex-1 text-xs bg-white border border-slate-200 rounded px-2 py-1 text-slate-900 placeholder:text-slate-400"
        />
        <button
          onClick={() => void refresh()}
          className="text-xs px-2 py-1 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
          disabled={!connected || busy}
        >
          {busy ? "…" : "Refresh"}
        </button>
        {onOpenPage && (
          <button
            onClick={onOpenPage}
            className="text-xs px-2 py-1 rounded bg-indigo-600 hover:bg-indigo-500 border border-indigo-600 text-white disabled:opacity-50"
            disabled={!connected || !sessionId}
            title="Open DAG view"
          >
            Open
          </button>
        )}
        <div className="text-[10px] text-slate-500 font-mono">
          n={nodes.length} e={edges.length}
        </div>
      </div>

      {error && <div className="text-[11px] text-rose-600 break-words">{error}</div>}

      <DagGraphView nodes={graphNodes} edges={graphEdges} heightPx={240} selectedId={selected} onSelect={(id) => void loadExplain(id)} />

      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
        <div className="border border-slate-200 rounded-lg bg-slate-50 p-2">
          <div className="text-xs text-slate-500 mb-2">Nodes (top {filtered.length})</div>
          <div className="max-h-72 overflow-auto space-y-1">
            {filtered.length === 0 ? (
              <div className="text-xs text-slate-500">No nodes.</div>
            ) : (
              filtered.map((n) => (
                <button
                  key={n.id}
                  onClick={() => void loadExplain(n.id)}
                  className={`w-full text-left text-xs rounded px-2 py-1 border transition ${
                    selected === n.id ? "bg-indigo-50 border-indigo-200 text-indigo-700" : "bg-white border-slate-200 hover:border-slate-300 text-slate-900"
                  }`}
                >
                  <div className="font-mono break-all">{n.id}</div>
                  {n.label && <div className="text-[11px] text-slate-600 line-clamp-2">{n.label}</div>}
                  <div className="mt-1 flex flex-wrap items-center gap-1 text-[10px] text-slate-600">
                    {n.kind ? <span className="px-1.5 py-0.5 rounded bg-white border border-slate-200">kind:{n.kind}</span> : null}
                    {n.type ? <span className="px-1.5 py-0.5 rounded bg-white border border-slate-200">type:{n.type}</span> : null}
                    {n.owner ? <span className="px-1.5 py-0.5 rounded bg-white border border-slate-200">owner:{shortKey(n.owner)}</span> : null}
                    {(typeof n.attestationsCount === "number" ? n.attestationsCount : (Array.isArray(n.attestations) ? n.attestations.length : 0)) > 0 ? (
                      <span className="px-1.5 py-0.5 rounded bg-white border border-slate-200">
                        attest:{typeof n.attestationsCount === "number" ? n.attestationsCount : (n.attestations?.length ?? 0)}
                      </span>
                    ) : null}
                  </div>
                </button>
              ))
            )}
          </div>
        </div>

        <div className="border border-slate-200 rounded-lg bg-slate-50 p-2">
          <div className="text-xs text-slate-500 mb-2">Details</div>
          {!selectedNode ? (
            <div className="text-xs text-slate-500">Select a node to inspect.</div>
          ) : (
            <div className="space-y-2">
              <div className="text-xs font-mono break-all text-slate-900">{selectedNode.id}</div>
              <div className="flex items-center gap-2 text-[11px] text-slate-600">
                <span className="px-1.5 py-0.5 rounded bg-white border border-slate-200">{selectedNode.type || "unknown"}</span>
                {selectedNode.kind ? <span className="px-1.5 py-0.5 rounded bg-white border border-slate-200">kind:{selectedNode.kind}</span> : null}
                {explain ? (
                  <span
                    className={`px-1.5 py-0.5 rounded border ${
                      explain?.provable ? "bg-emerald-50 border-emerald-200 text-emerald-700" : "bg-amber-50 border-amber-200 text-amber-700"
                    }`}
                    title={explain?.hasCycle ? "Cycle detected" : explain?.provable ? "Provable" : "Missing dependencies"}
                  >
                    {explain?.hasCycle ? "cycle" : explain?.provable ? "provable" : "incomplete"}
                  </span>
                ) : null}
              </div>

              {selectedNode.owner ? (
                <div className="text-[11px] text-slate-700">
                  <span className="text-slate-500">owner:</span>{" "}
                  <span className="font-mono break-all">{selectedNode.owner}</span>
                </div>
              ) : null}

              {Array.isArray(selectedNode.attestations) && selectedNode.attestations.length > 0 ? (
                <details className="bg-white border border-slate-200 rounded-md p-2">
                  <summary className="cursor-pointer select-none text-[11px] text-slate-600">
                    Attestations ({typeof selectedNode.attestationsCount === "number" ? selectedNode.attestationsCount : selectedNode.attestations.length})
                  </summary>
                  <div className="mt-2 space-y-1">
                    {selectedNode.attestations.slice(0, 20).map((a, idx) => (
                      <div key={`${a?.pubkey ?? ""}:${idx}`} className="text-[11px] text-slate-800">
                        <div className="font-mono break-all">pubkey: {a?.pubkey ?? ""}</div>
                        <div className="font-mono break-all text-slate-500">sig: {a?.signature ?? ""}</div>
                      </div>
                    ))}
                  </div>
                </details>
              ) : null}

              {selectedNode.label ? <div className="text-xs text-slate-800">{selectedNode.label}</div> : null}

              {selectedNode.proof ? (
                <details className="bg-white border border-slate-200 rounded-md p-2">
                  <summary className="cursor-pointer select-none text-[11px] text-slate-600">Proof</summary>
                  <pre className="mt-2 text-xs text-slate-900 whitespace-pre-wrap break-words max-h-40 overflow-auto">{selectedNode.proof}</pre>
                </details>
              ) : null}

              {explain ? (
                <details className="bg-white border border-slate-200 rounded-md p-2">
                  <summary className="cursor-pointer select-none text-[11px] text-slate-600">Explain</summary>
                  <div className="mt-2 text-xs text-slate-800 space-y-1">
                    <div>
                      <span className="text-slate-500">directDeps:</span>{" "}
                      {Array.isArray(explain?.directDeps) ? explain.directDeps.length : 0}
                    </div>
                    <div>
                      <span className="text-slate-500">missing:</span>{" "}
                      {Array.isArray(explain?.missing) ? explain.missing.length : 0}
                    </div>
                    {Array.isArray(explain?.missing) && explain.missing.length > 0 ? (
                      <div className="mt-2 space-y-1">
                        {explain.missing.slice(0, 12).map((m: any, idx: number) => (
                          <div key={`${m?.id ?? ""}:${idx}`} className="text-[11px] text-rose-700 break-all">
                            - {String(m?.id ?? "")} {m?.type ? `(${String(m.type)})` : ""}
                          </div>
                        ))}
                      </div>
                    ) : null}
                  </div>
                </details>
              ) : null}

              {chainMd ? (
                <details className="bg-white border border-slate-200 rounded-md p-2">
                  <summary className="cursor-pointer select-none text-[11px] text-slate-600">Knowledge chain (Markdown)</summary>
                  <pre className="mt-2 text-xs text-slate-900 whitespace-pre-wrap break-words max-h-40 overflow-auto">{chainMd}</pre>
                </details>
              ) : null}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}


