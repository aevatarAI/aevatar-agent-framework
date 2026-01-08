import { useEffect, useMemo, useState } from "react";
import { KeyRound, X } from "lucide-react";

export default function ApiKeyModal(props: {
  open: boolean;
  defaultProvider: string;
  providers: string[];
  onClose: () => void;
  onSaved?: () => void;
}) {
  const { open, defaultProvider, providers, onClose, onSaved } = props;

  const providerOptions = useMemo(() => {
    const list = Array.isArray(providers) ? providers.filter(Boolean) : [];
    if (list.length > 0) return list;
    return defaultProvider ? [defaultProvider] : [];
  }, [providers, defaultProvider]);

  const [providerName, setProviderName] = useState<string>("");
  const [apiKey, setApiKey] = useState<string>("");
  const [showKey, setShowKey] = useState<boolean>(false);
  const [busy, setBusy] = useState<boolean>(false);
  const [error, setError] = useState<string>("");
  const [note, setNote] = useState<string>("");
  const [status, setStatus] = useState<{ providerType: string; endpoint: string; hasApiKey: boolean } | null>(null);
  const [test, setTest] = useState<any>(null);
  const [models, setModels] = useState<string[] | null>(null);

  useEffect(() => {
    if (!open) return;
    setProviderName(defaultProvider || providerOptions[0] || "");
    setApiKey("");
    setShowKey(false);
    setBusy(false);
    setError("");
    setNote("");
    setTest(null);
    setModels(null);
    setStatus(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, defaultProvider]);

  useEffect(() => {
    if (!open) return;
    void loadStatus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, providerName]);

  if (!open) return null;

  async function loadStatus() {
    const pn = providerName.trim() || defaultProvider || providerOptions[0] || "";
    if (!pn) return;

    try {
      const res = await fetch(`/api/llm/status?providerName=${encodeURIComponent(pn)}`);
      const json = await res.json().catch(() => null);
      if (!res.ok) throw new Error(json?.error ?? `HTTP ${res.status}`);
      setStatus({
        providerType: String(json?.providerType ?? ""),
        endpoint: String(json?.endpoint ?? ""),
        hasApiKey: Boolean(json?.hasApiKey),
      });
    } catch {
      // best-effort only
      setStatus(null);
    }
  }

  async function save() {
    if (busy) return;
    setBusy(true);
    setError("");
    setNote("");

    try {
      const body = {
        providerName: providerName.trim() || undefined,
        apiKey: apiKey.trim(),
      };

      const res = await fetch("/api/secrets/llm/api-key", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      });

      if (!res.ok) {
        const text = await res.text().catch(() => "");
        throw new Error(`HTTP ${res.status}${text ? `: ${text}` : ""}`);
      }

      onSaved?.();
      setApiKey("");
      setShowKey(false);
      setNote("Saved to user secrets. You can Test connection now.");
      await loadStatus();
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  async function testConnection() {
    if (busy) return;
    const pn = providerName.trim() || defaultProvider || providerOptions[0] || "";
    if (!pn) return;

    setBusy(true);
    setError("");
    setNote("");
    setTest(null);

    try {
      const res = await fetch(`/api/llm/test?providerName=${encodeURIComponent(pn)}`);
      const json = await res.json().catch(() => null);
      if (!json) throw new Error(`HTTP ${res.status}`);
      if (!res.ok) throw new Error(json?.error ?? `HTTP ${res.status}`);
      setTest(json);

      if (json?.ok === true) {
        const ms = typeof json?.latencyMs === "number" ? json.latencyMs : "";
        const cnt = typeof json?.modelsCount === "number" ? json.modelsCount : "";
        setNote(`Test OK${ms !== "" ? ` · ${ms}ms` : ""}${cnt !== "" ? ` · models=${cnt}` : ""}`);
      } else {
        setError(String(json?.error ?? "Test failed"));
      }
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
      await loadStatus();
    }
  }

  async function fetchModels() {
    if (busy) return;
    const pn = providerName.trim() || defaultProvider || providerOptions[0] || "";
    if (!pn) return;

    setBusy(true);
    setError("");
    setNote("");
    setModels(null);

    try {
      const res = await fetch(`/api/llm/models?providerName=${encodeURIComponent(pn)}&limit=200`);
      const json = await res.json().catch(() => null);
      if (!json) throw new Error(`HTTP ${res.status}`);
      if (!res.ok) throw new Error(json?.error ?? `HTTP ${res.status}`);

      if (json?.ok === true && Array.isArray(json?.models)) {
        const arr = json.models.map((x: any) => String(x)).filter(Boolean);
        setModels(arr);
        setNote(`Fetched models: ${arr.length}`);
      } else {
        setError(String(json?.error ?? "Fetch models failed"));
      }
    } catch (e: any) {
      setError(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  }

  const canSave = Boolean(providerName.trim()) && Boolean(apiKey.trim()) && !busy;
  const canTest = Boolean((providerName.trim() || defaultProvider).trim()) && !busy;

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="w-full max-w-lg bg-white border border-slate-200 rounded-xl overflow-hidden">
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200">
          <div className="flex items-center gap-2 text-sm font-medium text-slate-900">
            <KeyRound size={16} className="text-indigo-600" />
            Set API Key (User Secrets)
          </div>
          <button onClick={onClose} className="p-2 rounded hover:bg-slate-100 transition" aria-label="Close">
            <X size={16} />
          </button>
        </div>

        <div className="p-4 space-y-4">
          <div className="text-xs text-slate-600 leading-relaxed">
            This writes your key into encrypted user secrets (default: <span className="font-mono">~/.aevatar/secrets.json</span>).
            It is not committed to git and can be reused across demos/apps.
          </div>

          <div className="text-xs bg-slate-50 border border-slate-200 rounded-lg p-3 space-y-1">
            <div className="flex items-center justify-between">
              <span className="text-slate-600">Current (server-resolved)</span>
              <span
                className={`text-[11px] px-2 py-0.5 rounded border font-mono ${
                  status?.hasApiKey ? "bg-emerald-50 text-emerald-700 border-emerald-200" : "bg-rose-50 text-rose-700 border-rose-200"
                }`}
              >
                {status?.hasApiKey ? "API key: configured" : "API key: missing"}
              </span>
            </div>
            <div className="text-slate-600">
              ProviderType: <span className="font-mono text-slate-900">{status?.providerType ?? "-"}</span>
            </div>
            <div className="text-slate-600">
              Endpoint: <span className="font-mono text-slate-900 break-all">{status?.endpoint ?? "-"}</span>
            </div>
          </div>

          <div className="space-y-2">
            <div className="text-xs text-slate-500">Provider</div>
            {providerOptions.length > 0 ? (
              <select
                value={providerName}
                onChange={(e) => setProviderName(e.target.value)}
                className="w-full text-sm bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900"
              >
                {providerOptions.map((p) => (
                  <option key={p} value={p}>
                    {p}
                  </option>
                ))}
              </select>
            ) : (
              <input
                value={providerName}
                onChange={(e) => setProviderName(e.target.value)}
                placeholder="provider name (e.g. deepseek)"
                className="w-full text-sm bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900"
              />
            )}
          </div>

          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <div className="text-xs text-slate-500">API Key</div>
              <button
                onClick={() => setShowKey((v) => !v)}
                className="text-[11px] px-2 py-1 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700"
                type="button"
              >
                {showKey ? "Hide" : "Show"}
              </button>
            </div>
            <input
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              placeholder="paste your API key"
              type={showKey ? "text" : "password"}
              className="w-full text-sm bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900"
            />
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={() => void testConnection()}
              className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
              disabled={!canTest}
              title="Probe by calling list-models endpoint (best-effort)"
              type="button"
            >
              {busy ? "Working…" : "Test connection"}
            </button>
            <button
              onClick={() => void fetchModels()}
              className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
              disabled={!canTest}
              title="Fetch available models (best-effort)"
              type="button"
            >
              Fetch models
            </button>
          </div>

          {note && <div className="text-xs text-emerald-700 break-words">{note}</div>}
          {error && <div className="text-xs text-rose-600 break-words">{error}</div>}

          {Array.isArray(models) && (
            <pre className="text-xs text-slate-900 whitespace-pre-wrap break-words max-h-[240px] overflow-auto bg-slate-50 border border-slate-200 rounded-lg p-3 font-mono">
              {models.join("\n")}
            </pre>
          )}
        </div>

        <div className="px-4 py-3 border-t border-slate-200 flex items-center justify-end gap-2 bg-slate-50">
          <button
            onClick={onClose}
            className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700"
            disabled={busy}
          >
            Cancel
          </button>
          <button
            onClick={() => void save()}
            className="text-xs px-3 py-2 rounded bg-indigo-600 hover:bg-indigo-500 text-white disabled:opacity-50"
            disabled={!canSave}
          >
            {busy ? "Saving…" : "Save"}
          </button>
        </div>
      </div>
    </div>
  );
}


