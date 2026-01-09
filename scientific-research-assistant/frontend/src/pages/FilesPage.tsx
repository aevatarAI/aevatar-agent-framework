import { useEffect, useMemo, useState } from "react";
import { FileText, Folder, Save } from "lucide-react";

type FileNode = {
  path: string; // relative to session root
  name: string;
  kind: "dir" | "file";
  sizeBytes: number;
  updatedAtMs: number;
  children?: FileNode[] | null;
};

function formatBytes(n: number) {
  if (!Number.isFinite(n) || n <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  let i = 0;
  let v = n;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

function isTextLike(path: string) {
  const p = String(path || "").toLowerCase();
  return (
    p.endsWith(".md") ||
    p.endsWith(".txt") ||
    p.endsWith(".json") ||
    p.endsWith(".jsonl") ||
    p.endsWith(".yaml") ||
    p.endsWith(".yml") ||
    p.endsWith(".proto") ||
    p.endsWith(".cs") ||
    p.endsWith(".csproj") ||
    p.endsWith(".sln") ||
    p.endsWith(".ts") ||
    p.endsWith(".tsx") ||
    p.endsWith(".js") ||
    p.endsWith(".css") ||
    p.endsWith(".html")
  );
}

export default function FilesPage(props: {
  sessionId: string;
  sessionFromQuery?: string;
}) {
  const sessionId = (props.sessionId || props.sessionFromQuery || "").trim();
  const pathFromQuery = useMemo(() => {
    try {
      const q = new URLSearchParams(window.location.search);
      return (q.get("path") || "").trim();
    } catch {
      return "";
    }
  }, []);

  const [tree, setTree] = useState<FileNode | null>(null);
  const [treeError, setTreeError] = useState<string>("");
  const [selectedPath, setSelectedPath] = useState<string>("");
  const [content, setContent] = useState<string>("");
  const [savedContent, setSavedContent] = useState<string>("");
  const [busy, setBusy] = useState<boolean>(false);
  const [note, setNote] = useState<string>("");
  const [error, setError] = useState<string>("");

  const dirty = useMemo(() => content !== savedContent, [content, savedContent]);

  useEffect(() => {
    if (!sessionId) return;
    void refreshTree();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionId]);

  useEffect(() => {
    if (!sessionId) return;
    if (!pathFromQuery) return;
    if (selectedPath === pathFromQuery) return;
    void openFile(pathFromQuery);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionId, pathFromQuery]);

  async function refreshTree() {
    if (!sessionId) return;
    setTreeError("");
    try {
      const res = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/files/tree?depth=8`);
      const json = await res.json().catch(() => null);
      if (!res.ok) throw new Error(json?.error ?? `HTTP ${res.status}`);
      setTree(json?.tree ?? null);
    } catch (e: any) {
      setTree(null);
      setTreeError(e?.message ?? String(e));
    }
  }

  async function openFile(path: string) {
    const p = String(path || "").trim();
    if (!p || !sessionId) return;
    setBusy(true);
    setError("");
    setNote("");
    try {
      const res = await fetch(
        `/api/sessions/${encodeURIComponent(sessionId)}/files?path=${encodeURIComponent(p)}`,
      );
      const json = await res.json().catch(() => null);
      if (!res.ok) throw new Error(json?.error ?? `HTTP ${res.status}`);
      const c = String(json?.file?.content ?? "");
      setSelectedPath(p);
      setContent(c);
      setSavedContent(c);
      setNote(`Opened ${p}`);
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  async function saveFile() {
    const p = selectedPath.trim();
    if (!p || !sessionId || busy) return;
    setBusy(true);
    setError("");
    setNote("");
    try {
      const res = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/files`, {
        method: "PUT",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ path: p, content }),
      });
      const json = await res.json().catch(() => null);
      if (!res.ok) throw new Error(json?.error ?? `HTTP ${res.status}`);
      setSavedContent(content);
      setNote(`Saved ${p}`);
      void refreshTree();
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  function renderNode(n: FileNode, depth: number) {
    const isDir = n.kind === "dir";
    const pad = Math.min(depth, 14) * 12;
    const label = n.path || "(session root)";
    const clickable = !isDir && isTextLike(n.path);

    return (
      <div key={label}>
        <button
          type="button"
          onClick={() => clickable && void openFile(n.path)}
          className={`w-full text-left px-2 py-1 rounded flex items-center gap-2 hover:bg-slate-50 ${
            n.path === selectedPath ? "bg-indigo-50 text-indigo-700" : "text-slate-700"
          } ${clickable ? "" : "opacity-80"}`}
          style={{ paddingLeft: 8 + pad }}
          title={label}
        >
          {isDir ? <Folder size={14} className="text-slate-500 shrink-0" /> : <FileText size={14} className="text-slate-500 shrink-0" />}
          <span className="font-mono text-xs truncate">{n.name || label}</span>
          {!isDir && (
            <span className="ml-auto text-[11px] text-slate-400">{formatBytes(n.sizeBytes)}</span>
          )}
        </button>
        {isDir && Array.isArray(n.children) && n.children.length > 0 && (
          <div>
            {n.children.map((c) => renderNode(c, depth + 1))}
          </div>
        )}
      </div>
    );
  }

  const directoryGuide = useMemo(() => {
    const root = "workspace/sessions/{sessionId}/";
    return [
      { dir: `${root}paper/`, desc: "论文协作产物：outline.md / draft.md" },
      { dir: `${root}facts_proposed/`, desc: "facts 的 proposal 队列（待评审/共识的素材）" },
      { dir: `${root}decisions/`, desc: "HITL 决策快照（例如 compute_decision.json）" },
      { dir: `${root}deliverables/`, desc: "交付中心：brief.json + delivery snapshots（结论/证据/下一步）" },
      { dir: `${root}runs/{runId}/`, desc: "每次 run 的工作痕迹（例如 ui_events.jsonl）" },
      { dir: `${root}mailbox/`, desc: "durable mailbox：agent/user 消息队列（含 _dead/ 死信）" },
      { dir: `${root}artifacts/ui/`, desc: "前端展示快照（刷新可恢复）：ui_snapshot.json" },
      { dir: `${root}artifacts/dag/`, desc: "DAG 知识库：snapshot.json + staged/ + consensus/" },
      { dir: `${root}artifacts/trace/`, desc: "每轮推导 trace：trace.jsonl / summary.md" },
      { dir: `${root}artifacts/uploads/`, desc: "用户上传附件（安全落盘）" },
      { dir: `${root}tmp/`, desc: "原子写临时目录（安全写入用）" },
    ];
  }, []);

  return (
    <div className="h-screen bg-slate-50 text-slate-900 font-sans overflow-hidden">
      <div className="h-14 flex items-center justify-between px-6 bg-white border-b border-slate-200">
        <div className="flex items-center gap-3 min-w-0">
          <div className="font-semibold text-slate-900">Files</div>
          <div className="text-xs text-slate-500 truncate">
            session: <span className="font-mono text-slate-900">{sessionId || "(none)"}</span>
          </div>
        </div>
        <a
          href="/"
          className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700"
          title="Back to main"
        >
          Back
        </a>
      </div>

      <div className="h-[calc(100vh-56px)] flex min-h-0">
        {/* Left: tree */}
        <div className="w-[420px] shrink-0 min-h-0 border-r border-slate-200 bg-white flex flex-col">
          <div className="p-4 border-b border-slate-200">
            <div className="text-sm font-semibold text-slate-900">Session workspace</div>
            <div className="text-xs text-slate-500 mt-1">
              Read/edit is limited to small text-like files under <span className="font-mono">workspace/sessions/{`{id}`}</span>.
            </div>
            <button
              onClick={() => void refreshTree()}
              className="mt-3 text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700"
              disabled={!sessionId}
            >
              Refresh tree
            </button>
            {treeError && <div className="mt-2 text-xs text-rose-600 break-words">{treeError}</div>}
          </div>

          <div className="flex-1 min-h-0 overflow-y-auto p-2">
            {!tree ? (
              <div className="p-4 text-xs text-slate-500">No tree (select a session first).</div>
            ) : (
              renderNode(tree, 0)
            )}
          </div>
        </div>

        {/* Right: editor + guide */}
        <div className="flex-1 min-w-0 min-h-0 overflow-hidden flex flex-col">
          <div className="p-4 border-b border-slate-200 bg-white">
            <div className="flex items-center gap-2 min-w-0">
              <div className="text-sm font-semibold text-slate-900 truncate">
                {selectedPath ? (
                  <>
                    Editing: <span className="font-mono">{selectedPath}</span>
                  </>
                ) : (
                  "Select a file to open"
                )}
              </div>
              <div className="ml-auto flex items-center gap-2">
                {dirty && <span className="text-xs text-amber-700 bg-amber-50 border border-amber-200 rounded px-2 py-1">unsaved</span>}
                <button
                  onClick={() => void saveFile()}
                  disabled={!selectedPath || !dirty || busy}
                  className="text-xs px-3 py-2 rounded bg-indigo-600 hover:bg-indigo-500 text-white disabled:opacity-50"
                >
                  <span className="inline-flex items-center gap-2">
                    <Save size={14} /> Save
                  </span>
                </button>
              </div>
            </div>
            {note && <div className="mt-2 text-xs text-emerald-700 break-words">{note}</div>}
            {error && <div className="mt-2 text-xs text-rose-600 break-words">{error}</div>}
          </div>

          <div className="flex-1 min-h-0 overflow-hidden flex">
            <div className="flex-1 min-w-0 min-h-0 p-4 overflow-hidden">
              {selectedPath ? (
                <textarea
                  value={content}
                  onChange={(e) => setContent(e.target.value)}
                  className="w-full h-full resize-none bg-white border border-slate-200 rounded-xl p-3 font-mono text-xs text-slate-900 outline-none focus:ring-2 focus:ring-indigo-500/30"
                  spellCheck={false}
                />
              ) : (
                <div className="h-full border border-slate-200 rounded-xl bg-white p-6 text-sm text-slate-600">
                  Select a file from the left tree to view/edit.
                  <div className="mt-3 text-xs text-slate-500">
                    Tip: start with <span className="font-mono">paper/outline.md</span> or <span className="font-mono">deliverables/brief.json</span>.
                  </div>
                </div>
              )}
            </div>

            <div className="w-[420px] shrink-0 min-h-0 border-l border-slate-200 bg-slate-50 p-4 overflow-y-auto">
              <div className="text-sm font-semibold text-slate-900">Directory guide</div>
              <div className="mt-2 text-xs text-slate-600 leading-relaxed">
                These directories are File-SSoT for the vibe researching platform. UI state is a projection; files are the source of truth.
              </div>

              <div className="mt-4 space-y-2">
                {directoryGuide.map((x) => (
                  <div key={x.dir} className="bg-white border border-slate-200 rounded-xl p-3">
                    <div className="text-xs font-mono text-slate-900 break-all">{x.dir}</div>
                    <div className="mt-1 text-xs text-slate-600">{x.desc}</div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}


