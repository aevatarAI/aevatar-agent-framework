import { ExternalLink, Search, X } from "lucide-react";
import { useEffect, useMemo, useState } from "react";

import type { SraTransport } from "../transport/SraTransport";
import { setSecretAsync } from "../panels/apiKey/api";

type SkillsMpItem = {
  id?: string;
  name?: string;
  description?: string;
  repoUrl?: string;
  url?: string;
  stars?: number;
};

export function SkillsMpModal(props: { open: boolean; onClose: () => void; transport: SraTransport }) {
  const { open, onClose, transport } = props;

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [note, setNote] = useState("");

  const [status, setStatus] = useState<{ configured: boolean; masked: string; keyPath?: string } | null>(null);
  const [apiKeyInput, setApiKeyInput] = useState("");

  const [q, setQ] = useState("");
  const [mode, setMode] = useState<"search" | "ai-search">("search");
  const [result, setResult] = useState<any>(null);

  const [packName, setPackName] = useState("");
  const [repoUrl, setRepoUrl] = useState("");
  const [ref, setRef] = useState("main");
  const [skillsSubDir, setSkillsSubDir] = useState("skills");
  const [syncNow, setSyncNow] = useState(true);

  const items: SkillsMpItem[] = useMemo(() => {
    const arr = Array.isArray((result as any)?.items) ? ((result as any).items as SkillsMpItem[]) : [];
    return arr;
  }, [result]);

  async function refreshStatus() {
    if (!transport.getJson) return;
    const json: any = await transport.getJson("/api/skillsmp/status");
    if (!json || json.ok !== true) throw new Error(String(json?.error ?? "bad response"));
    setStatus({ configured: Boolean(json.configured), masked: String(json.masked || ""), keyPath: String(json.keyPath || "") });
  }

  useEffect(() => {
    if (!open) return;
    setError("");
    setNote("");
    setResult(null);
    void refreshStatus().catch((e: any) => setError(`SkillsMP status failed: ${e?.message ?? String(e)}`));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  async function saveApiKey() {
    const key = apiKeyInput.trim();
    if (!key) return;
    setBusy(true);
    try {
      setError("");
      setNote("");
      await setSecretAsync(transport, "SkillsMP:ApiKey", key);
      setApiKeyInput("");
      await refreshStatus();
      setNote("Saved SkillsMP API key (stored in encrypted user secrets).");
    } catch (e: any) {
      setError(`Save failed: ${e?.message ?? String(e)}`);
    } finally {
      setBusy(false);
    }
  }

  async function runSearch() {
    const query = q.trim();
    if (!query) return;
    if (!transport.getJson) {
      setError("Search unavailable: transport lacks getJson.");
      return;
    }

    setBusy(true);
    try {
      setError("");
      setNote("");
      setResult(null);

      const path =
        mode === "ai-search"
          ? `/api/skillsmp/ai-search?q=${encodeURIComponent(query)}`
          : `/api/skillsmp/search?q=${encodeURIComponent(query)}&page=1&limit=20&sortBy=stars`;

      const json: any = await transport.getJson(path);
      if (!json || json.ok !== true) throw new Error(String(json?.error ?? "bad response"));
      setResult(json);
      setNote(`Found ${Array.isArray(json?.items) ? json.items.length : 0} item(s).`);
    } catch (e: any) {
      setError(`Search failed: ${e?.message ?? String(e)}`);
    } finally {
      setBusy(false);
    }
  }

  async function installPack() {
    const r = repoUrl.trim();
    if (!r) return;
    if (!transport.postJson) {
      setError("Install unavailable: transport lacks postJson.");
      return;
    }

    setBusy(true);
    try {
      setError("");
      setNote("");

      const body = {
        name: packName.trim() || undefined,
        repoUrl: r,
        ref: ref.trim() || "main",
        skillsSubDir: skillsSubDir.trim() || "skills",
        sync: syncNow,
      };

      const json: any = await transport.postJson("/api/skillsmp/install", body);
      if (!json || json.ok !== true) throw new Error(String(json?.error ?? "install failed"));

      setNote(syncNow ? "Installed and synced skill pack." : "Added skill pack config. Now click “Update Skills”.");
      setResult(json?.sync ?? json);
    } catch (e: any) {
      setError(`Install failed: ${e?.message ?? String(e)}`);
    } finally {
      setBusy(false);
    }
  }

  function onPickItem(it: SkillsMpItem) {
    const n = String(it?.name ?? "").trim();
    const r = String(it?.repoUrl ?? "").trim();
    if (n) setPackName(n);
    if (r) setRepoUrl(r);
  }

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="w-full max-w-4xl bg-white border border-slate-200 rounded-xl overflow-hidden">
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200">
          <div className="text-sm font-medium text-slate-900">SkillsMP Marketplace (Agent Skills)</div>
          <button onClick={onClose} className="p-2 rounded hover:bg-slate-100 transition" aria-label="Close">
            <X size={16} />
          </button>
        </div>

        <div className="p-4 border-b border-slate-200">
          <div className="text-xs text-slate-500 mb-2">Auth</div>
          <div className="flex items-center gap-2">
            <div className="text-xs text-slate-700">
              Status:{" "}
              <span className={`font-mono ${status?.configured ? "text-emerald-700" : "text-rose-700"}`}>
                {status?.configured ? `configured (${status.masked})` : "missing"}
              </span>
            </div>
            <div className="ml-auto text-[11px] text-slate-500 font-mono">{status?.keyPath || "SkillsMP:ApiKey"}</div>
          </div>

          {!status?.configured && (
            <div className="mt-3 flex items-center gap-2">
              <input
                value={apiKeyInput}
                onChange={(e) => setApiKeyInput(e.target.value)}
                placeholder="Paste SkillsMP API key (saved to encrypted user secrets)…"
                className="w-full bg-white border border-slate-200 rounded px-3 py-2 text-sm"
              />
              <button
                type="button"
                onClick={() => void saveApiKey()}
                disabled={busy || !apiKeyInput.trim()}
                className="text-sm px-3 py-2 rounded bg-indigo-600 hover:bg-indigo-500 text-white disabled:opacity-50"
              >
                Save
              </button>
            </div>
          )}

          <div className="mt-4 flex items-center gap-2">
            <div className="flex items-center gap-2 bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 w-full">
              <Search size={14} className="text-slate-400" />
              <input
                value={q}
                onChange={(e) => setQ(e.target.value)}
                placeholder="Search skills (e.g. literature review, bioinformatics, web scraping)…"
                className="w-full bg-transparent outline-none text-sm text-slate-900 placeholder:text-slate-400"
              />
            </div>
            <select
              value={mode}
              onChange={(e) => setMode(e.target.value as any)}
              className="text-sm border border-slate-200 rounded px-2 py-2 bg-white"
              title="Search mode"
            >
              <option value="search">keyword</option>
              <option value="ai-search">ai-search</option>
            </select>
            <button
              type="button"
              onClick={() => void runSearch()}
              disabled={busy || !q.trim()}
              className="text-sm px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 disabled:opacity-50"
            >
              Search
            </button>
          </div>

          {(error || note) && (
            <div className="mt-3 text-xs">
              {note && <div className="text-slate-600">{note}</div>}
              {error && <div className="text-rose-700">{error}</div>}
            </div>
          )}
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-0">
          <div className="p-4 border-b md:border-b-0 md:border-r border-slate-200">
            <div className="text-xs text-slate-500 mb-2">Results</div>
            <div className="space-y-2 max-h-[60vh] overflow-auto">
              {items.length === 0 ? (
                <div className="text-sm text-slate-500">No items yet. Run a search.</div>
              ) : (
                items.map((it, idx) => (
                  <div key={String(it.id || idx)} className="border border-slate-200 rounded-lg p-3 bg-white hover:border-slate-300 transition">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <div className="text-sm font-semibold text-slate-900 truncate">{String(it.name || "(unnamed)")}</div>
                        {it.description && <div className="text-xs text-slate-600 mt-1 line-clamp-3">{String(it.description)}</div>}
                        <div className="text-[11px] text-slate-500 mt-2 font-mono break-all">
                          {it.repoUrl ? `repo: ${it.repoUrl}` : it.url ? `url: ${it.url}` : ""}
                        </div>
                        {typeof it.stars === "number" && (
                          <div className="text-[11px] text-slate-500 mt-1">stars: {it.stars}</div>
                        )}
                      </div>
                      <div className="shrink-0 flex items-center gap-2">
                        {it.url && (
                          <a
                            href={String(it.url)}
                            target="_blank"
                            rel="noreferrer"
                            className="p-2 rounded border border-slate-200 hover:bg-slate-50"
                            title="Open in browser"
                          >
                            <ExternalLink size={14} className="text-slate-600" />
                          </a>
                        )}
                        <button
                          type="button"
                          onClick={() => onPickItem(it)}
                          className="text-xs px-2 py-2 rounded bg-indigo-600 hover:bg-indigo-500 text-white"
                          title="Use this item to prefill install form"
                          disabled={busy}
                        >
                          Pick
                        </button>
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>

          <div className="p-4">
            <div className="text-xs text-slate-500 mb-2">Install (Git Skill Pack)</div>
            <div className="text-xs text-slate-600 mb-3">
              SkillsMP API 当前只暴露搜索端点；这里以 “把 repo 写入 <span className="font-mono">skillpacks.json</span> 并触发同步” 作为安装方式。
            </div>

            <div className="space-y-2">
              <div className="text-xs text-slate-500">Repo URL</div>
              <input
                value={repoUrl}
                onChange={(e) => setRepoUrl(e.target.value)}
                placeholder="https://github.com/org/repo.git"
                className="w-full bg-white border border-slate-200 rounded px-3 py-2 text-sm font-mono"
              />

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                <div>
                  <div className="text-xs text-slate-500">Pack Name</div>
                  <input
                    value={packName}
                    onChange={(e) => setPackName(e.target.value)}
                    placeholder="(optional)"
                    className="w-full bg-white border border-slate-200 rounded px-3 py-2 text-sm"
                  />
                </div>
                <div>
                  <div className="text-xs text-slate-500">Ref</div>
                  <input
                    value={ref}
                    onChange={(e) => setRef(e.target.value)}
                    placeholder="main"
                    className="w-full bg-white border border-slate-200 rounded px-3 py-2 text-sm font-mono"
                  />
                </div>
              </div>

              <div>
                <div className="text-xs text-slate-500">Skills SubDir</div>
                <input
                  value={skillsSubDir}
                  onChange={(e) => setSkillsSubDir(e.target.value)}
                  placeholder="skills"
                  className="w-full bg-white border border-slate-200 rounded px-3 py-2 text-sm font-mono"
                />
                <div className="text-[11px] text-slate-500 mt-1">
                  Repo 内 skills 根目录；例如 K-Dense 是 <span className="font-mono">scientific-skills</span>
                </div>
              </div>

              <label className="flex items-center gap-2 text-xs text-slate-700">
                <input type="checkbox" checked={syncNow} onChange={(e) => setSyncNow(e.target.checked)} />
                Sync immediately (git clone/pull) after saving config
              </label>

              <button
                type="button"
                onClick={() => void installPack()}
                disabled={busy || !repoUrl.trim()}
                className="w-full text-sm px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 disabled:opacity-50"
              >
                Install
              </button>
            </div>

            {result && (
              <div className="mt-4">
                <div className="text-xs text-slate-500 mb-2">Last response (debug)</div>
                <pre className="text-[11px] text-slate-900 whitespace-pre-wrap break-words max-h-[35vh] overflow-auto bg-slate-50 border border-slate-200 rounded-lg p-3">
                  {JSON.stringify(result, null, 2)}
                </pre>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}


