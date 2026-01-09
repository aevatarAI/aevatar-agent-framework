import { useEffect, useMemo, useRef, useState } from "react";

type DagNode = {
  id: string;
  type?: string;
  label?: string;
};

type DagEdge = {
  fromId: string;
  toId: string;
};

export default function DagGraphView(props: {
  nodes: DagNode[];
  edges: DagEdge[];
  heightPx?: number;
  selectedId?: string;
  onSelect?: (id: string) => void;
}) {
  const { nodes, edges, heightPx = 260, selectedId, onSelect } = props;
  const svgRef = useRef<SVGSVGElement | null>(null);
  const dragRef = useRef<{ x: number; y: number; view: ViewBox } | null>(null);

  type ViewBox = { x: number; y: number; w: number; h: number };

  function kindKey(kind: string | undefined) {
    const k = String(kind || "").toLowerCase();
    if (k.includes("axiom")) return "axiom";
    if (k.includes("theorem")) return "theorem";
    if (k.includes("assumption")) return "assumption";
    if (k.includes("hypothesis")) return "hypothesis";
    return "unknown";
  }

  function colors(kind: string | undefined) {
    const k = kindKey(kind);
    if (k === "axiom") return { fill: "#eff6ff", stroke: "#bfdbfe" };
    if (k === "theorem") return { fill: "#f0fdf4", stroke: "#bbf7d0" };
    if (k === "hypothesis") return { fill: "#fffbeb", stroke: "#fde68a" };
    if (k === "assumption") return { fill: "#fff7ed", stroke: "#fdba74" };
    return { fill: "#f8fafc", stroke: "#e2e8f0" };
  }

  const pos = useMemo(() => {
    const ids = nodes.map((n) => String(n.id || "").trim()).filter(Boolean);
    const out: Record<string, string[]> = {};
    const indeg: Record<string, number> = {};
    const byId: Record<string, DagNode> = {};

    for (const n of nodes) {
      const id = String(n.id || "").trim();
      if (!id) continue;
      byId[id] = n;
      out[id] = [];
      indeg[id] = 0;
    }

    const seenEdge = new Set<string>();
    for (const e of edges) {
      const from = String(e.fromId || "").trim();
      const to = String(e.toId || "").trim();
      if (!from || !to) continue;
      if (!out[from] || typeof indeg[to] !== "number") continue;
      const k = `${from}->${to}`;
      if (seenEdge.has(k)) continue;
      seenEdge.add(k);
      out[from].push(to);
      indeg[to] += 1;
    }

    const weight = (n: DagNode) => {
      const k = kindKey(n.type);
      if (k === "axiom") return 0;
      if (k === "assumption") return 1;
      if (k === "hypothesis") return 2;
      if (k === "theorem") return 3;
      return 4;
    };

    const sortKey = (id: string) => `${weight(byId[id] || { id })}:${id}`;

    const q = ids.filter((id) => indeg[id] === 0).sort((a, b) => sortKey(a).localeCompare(sortKey(b)));
    const order: string[] = [];
    while (q.length) {
      const id = q.shift()!;
      order.push(id);
      for (const to of out[id] || []) {
        indeg[to] -= 1;
        if (indeg[to] === 0) {
          q.push(to);
          q.sort((a, b) => sortKey(a).localeCompare(sortKey(b)));
        }
      }
    }

    if (order.length < ids.length) {
      const seen = new Set(order);
      const rest = ids.filter((id) => !seen.has(id)).sort((a, b) => sortKey(a).localeCompare(sortKey(b)));
      order.push(...rest);
    }

    const depth: Record<string, number> = {};
    for (const id of ids) depth[id] = 0;
    for (const id of order) {
      const d = depth[id] || 0;
      for (const to of out[id] || []) depth[to] = Math.max(depth[to] || 0, d + 1);
    }

    const X0 = 160;
    const Y0 = 90;
    const X_SP = 260;
    const Y_SP = 84;
    const m: Record<string, { x: number; y: number }> = {};
    for (let i = 0; i < order.length; i++) {
      const id = order[i];
      m[id] = { x: X0 + (depth[id] || 0) * X_SP, y: Y0 + i * Y_SP };
    }
    return m;
  }, [nodes, edges]);

  const fitView = useMemo(() => {
    let minX = Infinity;
    let minY = Infinity;
    let maxX = -Infinity;
    let maxY = -Infinity;
    for (const n of nodes) {
      const id = String(n.id || "").trim();
      const p = pos[id];
      if (!p) continue;
      minX = Math.min(minX, p.x);
      minY = Math.min(minY, p.y);
      maxX = Math.max(maxX, p.x);
      maxY = Math.max(maxY, p.y);
    }
    if (!Number.isFinite(minX)) return { x: 0, y: 0, w: 1200, h: 520 } as ViewBox;
    const PAD = 140;
    const w = Math.max(720, maxX - minX + PAD * 2);
    const h = Math.max(520, maxY - minY + PAD * 2);
    return { x: minX - PAD, y: minY - PAD, w, h } as ViewBox;
  }, [nodes, pos]);

  const [view, setView] = useState<ViewBox>(fitView);

  useEffect(() => {
    setView(fitView);
  }, [fitView]);

  function zoomAt(clientX: number, clientY: number, factor: number) {
    const svg = svgRef.current;
    if (!svg) return;
    const rect = svg.getBoundingClientRect();
    const px = (clientX - rect.left) / Math.max(1, rect.width);
    const py = (clientY - rect.top) / Math.max(1, rect.height);

    setView((cur) => {
      const nx = cur.x + cur.w * px - (cur.w * factor) * px;
      const ny = cur.y + cur.h * py - (cur.h * factor) * py;
      return { x: nx, y: ny, w: cur.w * factor, h: cur.h * factor };
    });
  }

  const R = 28;
  const markerId = "arrow";

  return (
    <div className="relative w-full" style={{ height: `${heightPx}px` }}>
      <div className="absolute right-2 top-2 z-10 flex items-center gap-2">
        <button
          onClick={() => setView(fitView)}
          className="text-[11px] px-2 py-1 rounded bg-white border border-slate-200 hover:bg-slate-50 text-slate-700"
          title="Fit"
        >
          Fit
        </button>
        <button
          onClick={() => {
            const r = svgRef.current?.getBoundingClientRect();
            const cx = (r?.left ?? 0) + (r?.width ?? 0) / 2;
            const cy = (r?.top ?? 0) + (r?.height ?? 0) / 2;
            zoomAt(cx, cy, 0.88);
          }}
          className="text-[11px] px-2 py-1 rounded bg-white border border-slate-200 hover:bg-slate-50 text-slate-700"
          title="Zoom in"
        >
          +
        </button>
        <button
          onClick={() => {
            const r = svgRef.current?.getBoundingClientRect();
            const cx = (r?.left ?? 0) + (r?.width ?? 0) / 2;
            const cy = (r?.top ?? 0) + (r?.height ?? 0) / 2;
            zoomAt(cx, cy, 1.12);
          }}
          className="text-[11px] px-2 py-1 rounded bg-white border border-slate-200 hover:bg-slate-50 text-slate-700"
          title="Zoom out"
        >
          -
        </button>
      </div>

      <svg
        ref={svgRef}
        viewBox={`${view.x} ${view.y} ${view.w} ${view.h}`}
        className="w-full h-full rounded-xl border border-slate-200 bg-white"
        style={{ touchAction: "none" }}
        onWheel={(e) => {
          e.preventDefault();
          const delta = e.deltaY;
          const factor = delta > 0 ? 1.12 : 0.88; // wheel down => zoom out
          zoomAt(e.clientX, e.clientY, factor);
        }}
        onPointerDown={(e) => {
          (e.currentTarget as any).setPointerCapture?.(e.pointerId);
          dragRef.current = { x: e.clientX, y: e.clientY, view };
        }}
        onPointerMove={(e) => {
          if (!dragRef.current) return;
          const dx = e.clientX - dragRef.current.x;
          const dy = e.clientY - dragRef.current.y;
          const svg = svgRef.current;
          if (!svg) return;
          const rect = svg.getBoundingClientRect();
          const sx = (dx / Math.max(1, rect.width)) * dragRef.current.view.w;
          const sy = (dy / Math.max(1, rect.height)) * dragRef.current.view.h;
          setView({ ...dragRef.current.view, x: dragRef.current.view.x - sx, y: dragRef.current.view.y - sy });
        }}
        onPointerUp={() => {
          dragRef.current = null;
        }}
      >
        <defs>
          <marker id={markerId} markerWidth="10" markerHeight="10" refX="9" refY="3" orient="auto" markerUnits="strokeWidth">
            <path d="M0,0 L0,6 L9,3 z" fill="rgba(148, 163, 184, 0.95)"></path>
          </marker>
        </defs>

        {/* edges */}
        {edges.map((e, idx) => {
          const a = pos[String(e.fromId || "").trim()];
          const b = pos[String(e.toId || "").trim()];
          if (!a || !b) return null;
          const dx = b.x - a.x;
          const dy = b.y - a.y;
          const dist = Math.hypot(dx, dy) || 1;
          const sx = a.x + (dx / dist) * R;
          const sy = a.y + (dy / dist) * R;
          const tx = b.x - (dx / dist) * R;
          const ty = b.y - (dy / dist) * R;
          return (
            <path
              key={`${e.fromId}->${e.toId}:${idx}`}
              d={`M ${sx} ${sy} L ${tx} ${ty}`}
              stroke="rgba(148, 163, 184, 0.95)"
              strokeWidth="2"
              fill="none"
              markerEnd={`url(#${markerId})`}
            />
          );
        })}

        {/* nodes */}
        {nodes.map((n) => {
          const id = String(n.id || "").trim();
          const p = pos[id];
          if (!p) return null;
          const c = colors(n.type);
          const selected = selectedId === id;
          return (
            <g key={id} onClick={() => onSelect?.(id)} style={{ cursor: onSelect ? "pointer" : "default" }}>
              <title>{`${id}${n.label ? `\n${String(n.label)}` : ""}`}</title>
              <circle cx={p.x} cy={p.y} r={R} fill={c.fill} stroke={selected ? "#4f46e5" : c.stroke} strokeWidth={selected ? 4 : 2} />
              <text x={p.x} y={p.y + 4} textAnchor="middle" fontSize="11" fill="#0f172a" style={{ userSelect: "none" }}>
                {id}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}


