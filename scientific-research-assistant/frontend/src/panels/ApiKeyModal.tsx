import { useEffect, useMemo, useState } from "react";
import { KeyRound, ChevronLeft, Search, X } from "lucide-react";

type ProviderItem = {
  id: string;
  displayName: string;
  category: "configured" | "popular" | "other" | string;
  description?: string;
  recommended?: boolean;
  connected?: boolean;
};

type ProviderPublic = {
  providerName: string;
  displayName: string;
  kind: string;
  apiKeyConfigured: boolean;
  endpoint: string;
  endpointSource: string; // secret | default | missing
};

type View = "list" | "connect" | "advanced";

export default function ApiKeyModal(props: {
  open: boolean;
  defaultProvider: string;
  providers: string[];
  onClose: () => void;
  onSaved?: () => void;
}) {
  const { open, onClose, onSaved } = props;

  const [view, setView] = useState<View>("list");
  const [busy, setBusy] = useState(false);
  const [search, setSearch] = useState("");
  const [providers, setProviders] = useState<ProviderItem[]>([]);

  // connect state
  const [providerName, setProviderName] = useState("");
  const [providerDisplayName, setProviderDisplayName] = useState("");
  const [endpoint, setEndpoint] = useState("");
  const [endpointOriginal, setEndpointOriginal] = useState("");
  const [endpointSource, setEndpointSource] = useState("");

  const [apiKey, setApiKey] = useState("");
  const [keyShown, setKeyShown] = useState(false);
  const [isNewKeyDraft, setIsNewKeyDraft] = useState(false);
  const [hasExistingKey, setHasExistingKey] = useState(false);
  const [existingKeyMasked, setExistingKeyMasked] = useState("");

  const [msg, setMsg] = useState<{ kind: "ok" | "err" | "info"; text: string } | null>(null);
  const [modelsBox, setModelsBox] = useState<string>("");

  // advanced state
  const [advKey, setAdvKey] = useState("");
  const [advValue, setAdvValue] = useState("");
  const [advShown, setAdvShown] = useState(false);

  useEffect(() => {
    if (!open) return;
    setView("list");
    setBusy(false);
    setSearch("");
    setProviders([]);
    setMsg(null);
    setModelsBox("");
    setProviderName("");
    setProviderDisplayName("");
    setEndpoint("");
    setEndpointOriginal("");
    setEndpointSource("");
    setApiKey("");
    setKeyShown(false);
    setIsNewKeyDraft(false);
    setHasExistingKey(false);
    setExistingKeyMasked("");
    setAdvKey("");
    setAdvValue("");
    setAdvShown(false);
    void refreshProviders();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  useEffect(() => {
    if (!open) return;

    function onKeyDown(ev: KeyboardEvent) {
      if (ev.key === "Escape") onClose();
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [open, onClose]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    const all = Array.isArray(providers) ? providers.slice() : [];
    const matches = (p: ProviderItem) => {
      const hay = `${p.displayName || ""} ${p.id || ""} ${p.description || ""}`.toLowerCase();
      return hay.includes(q);
    };
    const list = q ? all.filter(matches) : all;
    const order: Record<string, number> = { configured: 0, popular: 1, other: 2 };
    list.sort((a, b) => {
      const ra = order[a.category] ?? 9;
      const rb = order[b.category] ?? 9;
      if (ra !== rb) return ra - rb;
      return String(a.displayName || a.id).localeCompare(String(b.displayName || b.id), undefined, { sensitivity: "base" });
    });
    return {
      configured: list.filter((p) => p.category === "configured"),
      popular: list.filter((p) => p.category === "popular"),
      other: list.filter((p) => p.category === "other"),
    };
  }, [providers, search]);

  const canSave = useMemo(() => {
    return !busy && providerName.trim().length > 0 && apiKey.trim().length > 0 && isNewKeyDraft === true;
  }, [busy, providerName, apiKey, isNewKeyDraft]);

  const canTest = useMemo(() => {
    return !busy && providerName.trim().length > 0 && hasExistingKey === true;
  }, [busy, providerName, hasExistingKey]);

  if (!open) return null;

  async function refreshProviders() {
    try {
      const res = await fetch("/api/llm/providers");
      const json = await res.json().catch(() => null);
      const list = Array.isArray(json?.providers) ? (json.providers as ProviderItem[]) : [];
      setProviders(list);
    } catch {
      setProviders([]);
    }
  }

  function openConnect(p: ProviderItem) {
    setView("connect");
    setMsg(null);
    setModelsBox("");

    const id = String(p?.id ?? "").trim();
    setProviderName(id);
    setProviderDisplayName(String(p?.displayName ?? id));

    setEndpoint("");
    setEndpointOriginal("");
    setEndpointSource("");

    setApiKey("");
    setKeyShown(false);
    setIsNewKeyDraft(false);
    setHasExistingKey(false);
    setExistingKeyMasked("");

    void loadProviderDetails(id);
  }

  async function loadProviderDetails(pn: string) {
    const name = String(pn ?? "").trim();
    if (!name) return;

    try {
      const res = await fetch(`/api/llm/provider/${encodeURIComponent(name)}`);
      const json = await res.json().catch(() => null);
      const p = (json?.provider as ProviderPublic) || null;
      if (!p) return;

      setProviderDisplayName(String(p.displayName || name));
      setEndpoint(String(p.endpoint || ""));
      setEndpointOriginal(String(p.endpoint || ""));
      setEndpointSource(String(p.endpointSource || ""));

      setHasExistingKey(Boolean(p.apiKeyConfigured));
      await loadApiKeyMask(name);
    } catch {
      // best-effort
    }
  }

  async function loadApiKeyMask(pn: string) {
    if (isNewKeyDraft) return;
    const name = String(pn ?? "").trim();
    if (!name) return;

    try {
      const res = await fetch(`/api/llm/api-key/${encodeURIComponent(name)}`);
      const json = await res.json().catch(() => null);
      if (!json || json.ok !== true) return;

      const configured = Boolean(json.configured);
      const masked = String(json.masked || "");

      setHasExistingKey(configured);
      setExistingKeyMasked(masked);
      setKeyShown(false);

      if (configured && masked) {
        // existing-key mode: show masked in input (text) until user starts typing
        setApiKey(masked);
        setIsNewKeyDraft(false);
      } else {
        // no stored key: ready for draft
        setApiKey("");
        setIsNewKeyDraft(false);
      }
    } catch {
      // best-effort
    }
  }

  async function toggleKey() {
    const name = providerName.trim();
    if (!name) return;

    // Draft mode: classic password toggle (we just toggle display state)
    if (isNewKeyDraft || !hasExistingKey) {
      setKeyShown((v) => !v);
      return;
    }

    // Existing key mode: Show triggers reveal; Hide restores masked.
    if (!keyShown) {
      try {
        setBusy(true);
        setMsg(null);
        const res = await fetch(`/api/llm/api-key/${encodeURIComponent(name)}?reveal=true`);
        const json = await res.json().catch(() => null);
        if (!json || json.ok !== true || !json.value) {
          setMsg({ kind: "err", text: "Failed to reveal key (not configured)." });
          return;
        }
        const full = String(json.value || "");
        const masked = String(json.masked || existingKeyMasked || "");
        setExistingKeyMasked(masked);
        setKeyShown(true);
        setApiKey(full);
      } catch (e: any) {
        setMsg({ kind: "err", text: e?.message ?? String(e) });
      } finally {
        setBusy(false);
      }
      return;
    }

    // Hide
    setKeyShown(false);
    setApiKey(existingKeyMasked || "");
  }

  async function saveEndpointOverride(name: string) {
    const pn = String(name ?? "").trim();
    if (!pn) return;

    const key = `LLMProviders:Providers:${pn}:Endpoint`;
    const ep = endpoint.trim();

    const unchanged = ep === endpointOriginal;
    if (unchanged && endpointSource !== "secret") return;

    if (!ep) {
      await fetch("/api/secrets/remove", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ key }),
      });
      return;
    }

    await fetch("/api/secrets/set", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ key, value: ep }),
    });
  }

  async function submit() {
    const name = providerName.trim();
    const key = apiKey.trim();
    if (!name || !key || isNewKeyDraft !== true) return;

    setBusy(true);
    setMsg(null);
    setModelsBox("");
    try {
      await saveEndpointOverride(name);

      const res = await fetch("/api/llm/api-key", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ providerName: name, apiKey: key }),
      });
      const text = await res.text().catch(() => "");
      if (!res.ok) throw new Error(`HTTP ${res.status}${text ? `: ${text}` : ""}`);

      onSaved?.();
      setMsg({ kind: "ok", text: "Saved. Provider is now connected. Click Test to verify." });

      setApiKey("");
      setIsNewKeyDraft(false);
      setKeyShown(false);
      await refreshProviders();
      await loadProviderDetails(name);
    } catch (e: any) {
      setMsg({ kind: "err", text: e?.message ?? String(e) });
    } finally {
      setBusy(false);
    }
  }

  async function testConnection() {
    const name = providerName.trim();
    if (!name) return;

    setBusy(true);
    setMsg(null);
    setModelsBox("");
    try {
      const res = await fetch(`/api/llm/test/${encodeURIComponent(name)}`);
      const json = await res.json().catch(() => null);
      if (!json) throw new Error("bad response");
      if (json.ok === true) {
        const ms = typeof json.latencyMs === "number" ? json.latencyMs : null;
        const cnt = typeof json.modelsCount === "number" ? json.modelsCount : null;
        setMsg({ kind: "ok", text: `OK${ms != null ? ` · ${ms}ms` : ""}${cnt != null ? ` · models=${cnt}` : ""}` });
        const sample = Array.isArray(json.sampleModels) ? json.sampleModels : [];
        if (sample.length > 0) setModelsBox(sample.join("\n"));
      } else {
        setMsg({ kind: "err", text: `Test failed: ${String(json.error || "unknown error")}` });
      }
    } catch (e: any) {
      setMsg({ kind: "err", text: e?.message ?? String(e) });
    } finally {
      setBusy(false);
      await loadProviderDetails(name);
    }
  }

  async function fetchModels() {
    const name = providerName.trim();
    if (!name) return;

    setBusy(true);
    setMsg(null);
    setModelsBox("");
    try {
      const res = await fetch(`/api/llm/models/${encodeURIComponent(name)}?limit=200`);
      const json = await res.json().catch(() => null);
      if (!json) throw new Error("bad response");
      if (json.ok === true) {
        const arr = Array.isArray(json.models) ? json.models.map((x: any) => String(x)).filter(Boolean) : [];
        setMsg({ kind: "ok", text: `Fetched models: ${arr.length}` });
        setModelsBox(arr.join("\n"));
      } else {
        setMsg({ kind: "err", text: `Fetch models failed: ${String(json.error || "unknown error")}` });
      }
    } catch (e: any) {
      setMsg({ kind: "err", text: e?.message ?? String(e) });
    } finally {
      setBusy(false);
      await loadProviderDetails(name);
    }
  }

  async function disconnect() {
    const name = providerName.trim();
    if (!name) return;

    setBusy(true);
    setMsg(null);
    try {
      const res = await fetch(`/api/llm/api-key/${encodeURIComponent(name)}`, { method: "DELETE" });
      const text = await res.text().catch(() => "");
      if (!res.ok) throw new Error(`HTTP ${res.status}${text ? `: ${text}` : ""}`);
      setMsg({ kind: "ok", text: "Disconnected." });
      await refreshProviders();
      await loadProviderDetails(name);
    } catch (e: any) {
      setMsg({ kind: "err", text: e?.message ?? String(e) });
    } finally {
      setBusy(false);
    }
  }

  async function saveRaw() {
    const k = advKey.trim();
    const v = advValue.trim();
    if (!k || !v) return;

    setBusy(true);
    setMsg(null);
    try {
      const res = await fetch("/api/secrets/set", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ key: k, value: v }),
      });
      const text = await res.text().catch(() => "");
      if (!res.ok) throw new Error(`HTTP ${res.status}${text ? `: ${text}` : ""}`);
      setMsg({ kind: "ok", text: "Saved." });
      setAdvValue("");
      await refreshProviders();
    } catch (e: any) {
      setMsg({ kind: "err", text: e?.message ?? String(e) });
    } finally {
      setBusy(false);
    }
  }

  async function removeRaw() {
    const k = advKey.trim();
    if (!k) return;

    setBusy(true);
    setMsg(null);
    try {
      const res = await fetch("/api/secrets/remove", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ key: k }),
      });
      const text = await res.text().catch(() => "");
      if (!res.ok) throw new Error(`HTTP ${res.status}${text ? `: ${text}` : ""}`);
      setMsg({ kind: "ok", text: "Removed (if existed)." });
      await refreshProviders();
    } catch (e: any) {
      setMsg({ kind: "err", text: e?.message ?? String(e) });
    } finally {
      setBusy(false);
    }
  }

  const headerTitle =
    view === "list"
      ? "Connect provider"
      : view === "connect"
        ? `Connect ${providerDisplayName || providerName || ""}`.trim()
        : "Custom key/value";

  return (
    <div
      className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4"
      onMouseDown={(e) => {
        // Click outside the modal closes it (homepage click).
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="w-full max-w-2xl max-h-[90vh] bg-white border border-slate-200 rounded-2xl overflow-hidden shadow-xl flex flex-col">
        <div className="flex items-center gap-2 px-5 py-4 border-b border-slate-200">
          {view !== "list" ? (
            <button
              onClick={() => {
                setView("list");
                setMsg(null);
                setModelsBox("");
                void refreshProviders();
              }}
              className="p-2 rounded-lg hover:bg-slate-100 transition"
              title="Back"
            >
              <ChevronLeft size={18} className="text-slate-600" />
            </button>
          ) : (
            <KeyRound size={18} className="text-indigo-600" />
          )}
          <div className="text-base font-semibold text-slate-900">{headerTitle}</div>
          <div className="ml-auto" />
          <button onClick={onClose} className="p-2 rounded-lg hover:bg-slate-100 transition" aria-label="Close">
            <X size={18} />
          </button>
        </div>

        {/* Scrollable body (prevents modal overflow) */}
        <div className="min-h-0 flex-1 overflow-y-auto">
        {/* LIST */}
        {view === "list" && (
          <div className="p-5">
            <div className="flex items-center gap-2 px-3 py-2 rounded-xl border border-slate-200 bg-slate-50">
              <Search size={14} className="text-slate-400" />
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search providers"
                className="bg-transparent outline-none w-full text-sm text-slate-900 placeholder-slate-400"
              />
            </div>

            {filtered.configured.length > 0 && (
              <div className="mt-5">
                <div className="text-xs font-semibold text-slate-500 mb-2">Configured</div>
                <div className="border border-slate-200 rounded-xl overflow-hidden bg-white">
                  {filtered.configured.map((p) => (
                    <button
                      key={p.id}
                      onClick={() => openConnect(p)}
                      className="w-full text-left px-4 py-3 hover:bg-slate-50 border-b border-slate-200 last:border-b-0 flex items-center gap-3"
                    >
                      <div className="w-8 h-8 rounded-xl border border-slate-200 bg-white flex items-center justify-center font-bold text-slate-900">
                        {String(p.displayName || p.id).trim().slice(0, 1).toUpperCase()}
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2">
                          <div className="text-sm font-semibold text-slate-900 truncate">{p.displayName || p.id}</div>
                          <span className="text-[11px] px-2 py-0.5 rounded-full border border-emerald-200 bg-emerald-50 text-emerald-700 font-medium">
                            configured
                          </span>
                        </div>
                        <div className="text-xs text-slate-500 truncate">{p.description || ""}</div>
                      </div>
                      <div className="text-slate-400">›</div>
                    </button>
                  ))}
                </div>
              </div>
            )}

            <div className="mt-5">
              <div className="text-xs font-semibold text-slate-500 mb-2">Popular</div>
              <div className="border border-slate-200 rounded-xl overflow-hidden bg-white">
                {filtered.popular.map((p) => (
                  <button
                    key={p.id}
                    onClick={() => openConnect(p)}
                    className="w-full text-left px-4 py-3 hover:bg-slate-50 border-b border-slate-200 last:border-b-0 flex items-center gap-3"
                  >
                    <div className="w-8 h-8 rounded-xl border border-slate-200 bg-white flex items-center justify-center font-bold text-slate-900">
                      {String(p.displayName || p.id).trim().slice(0, 1).toUpperCase()}
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2">
                        <div className="text-sm font-semibold text-slate-900 truncate">{p.displayName || p.id}</div>
                        {p.recommended && (
                          <span className="text-[11px] px-2 py-0.5 rounded-full border border-indigo-200 bg-indigo-50 text-indigo-700 font-medium">
                            recommended
                          </span>
                        )}
                      </div>
                      <div className="text-xs text-slate-500 truncate">{p.description || ""}</div>
                    </div>
                    <div className="text-slate-400">›</div>
                  </button>
                ))}
              </div>
            </div>

            <div className="mt-5">
              <div className="text-xs font-semibold text-slate-500 mb-2">Other</div>
              <div className="border border-slate-200 rounded-xl overflow-hidden bg-white">
                {filtered.other.map((p) => (
                  <button
                    key={p.id}
                    onClick={() => openConnect(p)}
                    className="w-full text-left px-4 py-3 hover:bg-slate-50 border-b border-slate-200 last:border-b-0 flex items-center gap-3"
                  >
                    <div className="w-8 h-8 rounded-xl border border-slate-200 bg-white flex items-center justify-center font-bold text-slate-900">
                      {String(p.displayName || p.id).trim().slice(0, 1).toUpperCase()}
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="text-sm font-semibold text-slate-900 truncate">{p.displayName || p.id}</div>
                      <div className="text-xs text-slate-500 truncate">{p.description || ""}</div>
                    </div>
                    <div className="text-slate-400">›</div>
                  </button>
                ))}
              </div>
            </div>

            <div className="mt-5">
              <div className="text-xs font-semibold text-slate-500 mb-2">Advanced</div>
              <div className="border border-slate-200 rounded-xl overflow-hidden bg-white">
                <button
                  onClick={() => {
                    setView("advanced");
                    setMsg(null);
                    setModelsBox("");
                    setAdvKey("");
                    setAdvValue("");
                    setAdvShown(false);
                  }}
                  className="w-full text-left px-4 py-3 hover:bg-slate-50 flex items-center gap-3"
                >
                  <div className="w-8 h-8 rounded-xl border border-slate-200 bg-white flex items-center justify-center font-bold text-slate-900">
                    ⚙
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-semibold text-slate-900 truncate">Custom key/value</div>
                    <div className="text-xs text-slate-500 truncate">Set any IConfiguration key (values are never echoed)</div>
                  </div>
                  <div className="text-slate-400">›</div>
                </button>
              </div>
            </div>

            <div className="mt-5 text-xs text-slate-500 leading-relaxed">
              Saves into <span className="font-mono">~/.aevatar/secrets.json</span> (encrypted). All write APIs are{" "}
              <span className="font-semibold">localhost-only</span>.
            </div>
          </div>
        )}

        {/* CONNECT */}
        {view === "connect" && (
          <div className="p-5 space-y-4">
            <div className="text-xs text-slate-600 leading-relaxed">
              Enter your API key to connect your account and use it in Aevatar apps.
            </div>

            <div className="space-y-2">
              <div className="text-xs font-semibold text-slate-500">Provider name</div>
              <input
                value={providerName}
                onChange={(e) => setProviderName(e.target.value)}
                className="w-full text-sm bg-white border border-slate-200 rounded-xl px-3 py-2 text-slate-900"
                placeholder="e.g. openai"
                disabled={busy}
              />
            </div>

            <div className="space-y-2">
              <div className="text-xs font-semibold text-slate-500">Endpoint</div>
              <input
                value={endpoint}
                onChange={(e) => setEndpoint(e.target.value)}
                className="w-full text-sm bg-white border border-slate-200 rounded-xl px-3 py-2 text-slate-900"
                placeholder="e.g. https://api.openai.com"
                disabled={busy}
              />
              <div className="text-[11px] text-slate-500">
                Endpoint ({endpointSource || "unknown"}): {endpoint || "(empty)"}
              </div>
            </div>

            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <div className="text-xs font-semibold text-slate-500">API key</div>
                <button
                  onClick={() => void toggleKey()}
                  className="text-[11px] px-2 py-1 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700"
                  type="button"
                  disabled={busy}
                >
                  {keyShown ? "Hide" : "Show"}
                </button>
              </div>
              <input
                value={apiKey}
                onChange={(e) => {
                  const v = e.target.value;
                  // If showing an existing masked key, switching to draft mode should not keep the masked string.
                  if (!isNewKeyDraft && hasExistingKey && apiKey === existingKeyMasked) {
                    setIsNewKeyDraft(true);
                    setKeyShown(false);
                    setApiKey(v === existingKeyMasked ? "" : v);
                    return;
                  }
                  setApiKey(v);
                  if (!isNewKeyDraft) setIsNewKeyDraft(true);
                }}
                type={isNewKeyDraft || !hasExistingKey ? (keyShown ? "text" : "password") : "text"}
                className="w-full text-sm bg-white border border-slate-200 rounded-xl px-3 py-2 text-slate-900"
                placeholder="API key"
                disabled={busy}
              />
              <div className="text-[11px] text-slate-500">
                Writes <span className="font-mono">LLMProviders:Providers:&lt;providerName&gt;:ApiKey</span> into user secrets.
              </div>
            </div>

            <div className="flex flex-wrap gap-2">
              <button
                onClick={() => void submit()}
                className="text-xs px-3 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white disabled:opacity-50"
                disabled={!canSave}
              >
                {busy ? "Saving…" : "Save"}
              </button>
              <button
                onClick={() => void testConnection()}
                className="text-xs px-3 py-2 rounded-xl bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
                disabled={!canTest}
              >
                Test
              </button>
              <button
                onClick={() => void fetchModels()}
                className="text-xs px-3 py-2 rounded-xl bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
                disabled={!canTest}
              >
                Fetch models
              </button>
              <button
                onClick={() => void disconnect()}
                className="text-xs px-3 py-2 rounded-xl bg-white hover:bg-slate-50 border border-rose-200 text-rose-700 disabled:opacity-50"
                disabled={!canTest}
              >
                Disconnect
              </button>
            </div>

            {msg && (
              <div className={`text-xs whitespace-pre-wrap break-words ${
                msg.kind === "ok" ? "text-emerald-700" : msg.kind === "err" ? "text-rose-600" : "text-slate-600"
              }`}>
                {msg.text}
              </div>
            )}

            {modelsBox && (
              <pre className="text-xs text-slate-900 whitespace-pre max-h-[260px] overflow-auto bg-slate-50 border border-slate-200 rounded-xl p-3 font-mono">
                {modelsBox}
              </pre>
            )}
          </div>
        )}

        {/* ADVANCED */}
        {view === "advanced" && (
          <div className="p-5 space-y-4">
            <div className="text-xs text-slate-600 leading-relaxed">Set any IConfiguration key/value. Values are never echoed back.</div>

            <div className="space-y-2">
              <div className="text-xs font-semibold text-slate-500">Key</div>
              <input
                value={advKey}
                onChange={(e) => setAdvKey(e.target.value)}
                className="w-full text-sm bg-white border border-slate-200 rounded-xl px-3 py-2 text-slate-900"
                placeholder="e.g. LLMProviders:Providers:deepseek:ApiKey"
                disabled={busy}
              />
            </div>

            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <div className="text-xs font-semibold text-slate-500">Value</div>
                <button
                  onClick={() => setAdvShown((v) => !v)}
                  className="text-[11px] px-2 py-1 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700"
                  type="button"
                  disabled={busy}
                >
                  {advShown ? "Hide" : "Show"}
                </button>
              </div>
              <input
                value={advValue}
                onChange={(e) => setAdvValue(e.target.value)}
                type={advShown ? "text" : "password"}
                className="w-full text-sm bg-white border border-slate-200 rounded-xl px-3 py-2 text-slate-900"
                placeholder="value"
                disabled={busy}
              />
            </div>

            <div className="flex flex-wrap gap-2">
              <button
                onClick={() => void saveRaw()}
                className="text-xs px-3 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white disabled:opacity-50"
                disabled={busy || advKey.trim().length === 0 || advValue.trim().length === 0}
              >
                Save
              </button>
              <button
                onClick={() => void removeRaw()}
                className="text-xs px-3 py-2 rounded-xl bg-white hover:bg-slate-50 border border-rose-200 text-rose-700 disabled:opacity-50"
                disabled={busy || advKey.trim().length === 0}
              >
                Remove
              </button>
            </div>

            {msg && (
              <div className={`text-xs whitespace-pre-wrap break-words ${
                msg.kind === "ok" ? "text-emerald-700" : msg.kind === "err" ? "text-rose-600" : "text-slate-600"
              }`}>
                {msg.text}
              </div>
            )}
          </div>
        )}
        </div>
      </div>
    </div>
  );
}


