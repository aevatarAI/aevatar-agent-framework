import { useMemo, useState } from "react";
import DagGraphView from "../components/DagGraphView";
import type { SraTransport } from "../transport/SraTransport";

type DagNode = {
  id: string;
  type?: string;
  label?: string;
  proof?: string;
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
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const nodes = useMemo(() => (Array.isArray(snapshot?.nodes) ? snapshot!.nodes! : []), [snapshot]);
  const edges = useMemo(() => (Array.isArray(snapshot?.edges) ? snapshot!.edges! : []), [snapshot]);

  const graphNodes = useMemo(() => nodes.slice(0, 200), [nodes]);
  const graphEdges = useMemo(() => edges.slice(0, 400), [edges]);

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
    } catch (e: any) {
      setError(e?.message ?? String(e));
      setExplain(null);
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
                </button>
              ))
            )}
          </div>
        </div>

        <div className="border border-slate-200 rounded-lg bg-slate-50 p-2">
          <div className="text-xs text-slate-500 mb-2">Explain</div>
          {explain ? (
            <pre className="text-xs text-slate-900 whitespace-pre-wrap break-words max-h-72 overflow-auto">{JSON.stringify(explain, null, 2)}</pre>
          ) : (
            <div className="text-xs text-slate-500">Select a node to explain dependencies.</div>
          )}
        </div>
      </div>
    </div>
  );
}


