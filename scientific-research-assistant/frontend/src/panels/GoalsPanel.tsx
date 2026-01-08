import { useEffect, useMemo, useState } from "react";

type GoalItem = {
  goalId: string;
  text: string;
  priority: number;
};

type GoalsSnapshot = {
  sessionId?: string;
  version?: number;
  updatedAt?: string;
  items?: GoalItem[];
};

export default function GoalsPanel(props: {
  sessionId: string;
  connected: boolean;
  snapshot: GoalsSnapshot | null;
  onSaved?: (snapshot: GoalsSnapshot) => void;
}) {
  const { sessionId, connected, snapshot, onSaved } = props;
  const [items, setItems] = useState<GoalItem[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>("");

  const version = useMemo(() => (typeof snapshot?.version === "number" ? snapshot!.version : 0), [snapshot]);

  useEffect(() => {
    const arr = Array.isArray(snapshot?.items) ? snapshot!.items! : [];
    setItems(
      arr.map((x) => ({
        goalId: String(x.goalId ?? "").trim(),
        text: String(x.text ?? ""),
        priority: typeof x.priority === "number" ? x.priority : 0,
      })),
    );
  }, [snapshot?.version]); // update on snapshot bump

  function addGoal() {
    setItems((prev) => [
      ...prev,
      {
        goalId: `g_${Math.random().toString(16).slice(2, 10)}`,
        text: "",
        priority: 0,
      },
    ]);
  }

  async function save() {
    if (!connected || !sessionId || saving) return;
    setSaving(true);
    setError("");
    try {
      const body = {
        version,
        items: items
          .map((x) => ({
            goalId: (x.goalId || "").trim(),
            text: (x.text || "").trim(),
            priority: Number.isFinite(x.priority) ? x.priority : 0,
          }))
          .filter((x) => x.text.length > 0),
      };

      const res = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/goals`, {
        method: "PUT",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      });
      if (!res.ok) {
        const t = await res.text().catch(() => "");
        throw new Error(`HTTP ${res.status}${t ? `: ${t}` : ""}`);
      }

      // Server emits goals_updated; still we optimistically refresh view by pulling GET /goals.
      const res2 = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/goals`);
      const json2: any = res2.ok ? await res2.json().catch(() => null) : null;
      const g = json2?.goals;
      if (g) {
        onSaved?.({ sessionId, ...g });
      }
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        <button
          onClick={addGoal}
          className="text-xs px-2 py-1 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
          disabled={!connected}
        >
          Add
        </button>
        <button
          onClick={() => void save()}
          className="text-xs px-2 py-1 rounded bg-indigo-600 hover:bg-indigo-500 border border-indigo-600 text-white disabled:opacity-50"
          disabled={!connected || saving}
        >
          {saving ? "Saving…" : "Save"}
        </button>
        <div className="ml-auto text-[10px] text-slate-500 font-mono">v{version}</div>
      </div>

      {error && <div className="text-[11px] text-rose-600 break-words">{error}</div>}

      {items.length === 0 ? (
        <div className="text-xs text-slate-500">No goals yet.</div>
      ) : (
        <div className="space-y-2">
          {items.map((g, idx) => (
            <div key={g.goalId || idx} className="border border-slate-200 rounded-lg p-2 bg-slate-50">
              <div className="flex items-center gap-2">
                <input
                  value={g.goalId}
                  onChange={(e) =>
                    setItems((prev) => prev.map((x, i) => (i === idx ? { ...x, goalId: e.target.value } : x)))
                  }
                  className="w-40 text-[11px] font-mono bg-white border border-slate-200 rounded px-2 py-1 text-slate-900 placeholder:text-slate-400"
                  placeholder="goalId"
                />
                <input
                  value={String(g.priority)}
                  onChange={(e) =>
                    setItems((prev) =>
                      prev.map((x, i) => (i === idx ? { ...x, priority: Number(e.target.value || 0) } : x)),
                    )
                  }
                  className="w-16 text-[11px] font-mono bg-white border border-slate-200 rounded px-2 py-1 text-slate-900 placeholder:text-slate-400"
                  placeholder="pri"
                />
                <button
                  onClick={() => setItems((prev) => prev.filter((_, i) => i !== idx))}
                  className="ml-auto text-[11px] px-2 py-1 rounded bg-white hover:bg-slate-100 border border-slate-200 text-slate-700"
                >
                  Delete
                </button>
              </div>
              <textarea
                value={g.text}
                onChange={(e) => setItems((prev) => prev.map((x, i) => (i === idx ? { ...x, text: e.target.value } : x)))}
                className="mt-2 w-full text-xs bg-white border border-slate-200 rounded px-2 py-2 text-slate-900 placeholder:text-slate-400"
                placeholder="Goal text…"
                rows={2}
              />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}


