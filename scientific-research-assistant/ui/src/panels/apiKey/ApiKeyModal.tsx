import { useEffect, useMemo, useState } from "react";
import { ChevronLeft, KeyRound, X } from "lucide-react";

import type { SraTransport } from "../../transport/SraTransport";
import {
  deleteLlmApiKeyAsync,
  fetchModelsAsync,
  getApiKeyStatusAsync,
  getProviderAsync,
  listProvidersAsync,
  removeSecretAsync,
  setLlmApiKeyAsync,
  setSecretAsync,
  testProviderAsync,
} from "./api";
import type { ProviderItem, ProviderPublic, View } from "./types";
import { ApiKeyAdvancedView, ApiKeyConnectView, ApiKeyListView, type ApiKeyModalMsg } from "./ApiKeyModalViews";

// ============================================================
//  ApiKeyModal (shared)
//
//  中文说明：
//  - 复用 SRA Web 版的 Provider 连接 UX
//  - 所有网络调用都通过 transport（Web: fetch + EventSource；Obsidian: requestUrl + Node SSE）
//  - 安全：SRA 后端的 /api/llm/* 与 /api/secrets/* 全部 localhost-only
//    这里同时用 capability 做 UI gating，避免远程 baseUrl 时诱导用户失败操作
// ============================================================

export default function ApiKeyModal(props: {
  transport: SraTransport;
  open: boolean;
  defaultProvider: string;
  providers: string[];
  onClose: () => void;
  onSaved?: () => void;
}) {
  const { transport, open, onClose, onSaved } = props;

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

  const [model, setModel] = useState("");
  const [modelOriginal, setModelOriginal] = useState("");
  const [modelSource, setModelSource] = useState("");

  const [apiKey, setApiKey] = useState("");
  const [keyShown, setKeyShown] = useState(false);
  const [isNewKeyDraft, setIsNewKeyDraft] = useState(false);
  const [hasExistingKey, setHasExistingKey] = useState(false);
  const [existingKeyMasked, setExistingKeyMasked] = useState("");

  const [msg, setMsg] = useState<ApiKeyModalMsg | null>(null);
  const [models, setModels] = useState<string[]>([]);

  // advanced state
  const [advKey, setAdvKey] = useState("");
  const [advValue, setAdvValue] = useState("");
  const [advShown, setAdvShown] = useState(false);

  const secretsEnabled = transport.capabilities.revealApiKey === true;

  useEffect(() => {
    if (!open) return;
    setView("list");
    setBusy(false);
    setSearch("");
    setProviders([]);
    setMsg(null);
    setProviderName("");
    setProviderDisplayName("");
    setEndpoint("");
    setEndpointOriginal("");
    setEndpointSource("");
    setModel("");
    setModelOriginal("");
    setModelSource("");
    setApiKey("");
    setKeyShown(false);
    setIsNewKeyDraft(false);
    setHasExistingKey(false);
    setExistingKeyMasked("");
    setAdvKey("");
    setAdvValue("");
    setAdvShown(false);
    setModels([]);

    if (!secretsEnabled) {
      setMsg({
        kind: "info",
        text: "Secrets/LLM provider configuration is local-only. Configure baseUrl to a loopback sidecar (e.g. http://localhost:5678).",
      });
      return;
    }

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
    if (busy) return false;
    if (!secretsEnabled) return false;
    if (providerName.trim().length === 0) return false;
    if (model.trim().length === 0) return false;

    // Never write API key unless user is drafting a new key.
    if (isNewKeyDraft && apiKey.trim().length === 0) return false;

    const endpointChanged = endpoint.trim() !== endpointOriginal.trim();
    const modelChanged = model.trim() !== modelOriginal.trim();
    // "default" values are only UI suggestions unless persisted into secrets.
    const needsPersistEndpoint = endpoint.trim().length > 0 && endpointSource !== "secret";
    const needsPersistModel = model.trim().length > 0 && modelSource !== "secret";
    const hasChanges = isNewKeyDraft || endpointChanged || modelChanged || needsPersistEndpoint || needsPersistModel;
    return hasChanges;
  }, [
    busy,
    secretsEnabled,
    providerName,
    isNewKeyDraft,
    apiKey,
    endpoint,
    endpointOriginal,
    endpointSource,
    model,
    modelOriginal,
    modelSource,
  ]);

  const canTest = useMemo(() => {
    return !busy && secretsEnabled && providerName.trim().length > 0 && hasExistingKey === true;
  }, [busy, secretsEnabled, providerName, hasExistingKey]);

  if (!open) return null;

  async function refreshProviders() {
    if (!secretsEnabled) return;
    try {
      const list = await listProvidersAsync(transport);
      setProviders(list);
    } catch {
      setProviders([]);
    }
  }

  function openConnect(p: ProviderItem) {
    setView("connect");
    setMsg(null);
    setModels([]);

    const id = String(p?.id ?? "").trim();
    setProviderName(id);
    setProviderDisplayName(String(p?.displayName ?? id));

    setEndpoint("");
    setEndpointOriginal("");
    setEndpointSource("");
    setModel("");
    setModelOriginal("");
    setModelSource("");

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
    if (!secretsEnabled) return;

    try {
      const p = (await getProviderAsync(transport, name)) as ProviderPublic | null;
      if (!p) return;

      setProviderDisplayName(String(p.displayName || name));
      setEndpoint(String(p.endpoint || ""));
      setEndpointOriginal(String(p.endpoint || ""));
      setEndpointSource(String(p.endpointSource || ""));
      const nextModel = String(p.model || "");
      const nextModelSource = String(p.modelSource || "");
      setModelSource(nextModelSource);

      // UX:
      // - If model is already configured (secret), show it.
      // - Otherwise, keep the field empty ("--") until user fetches models and selects one.
      const effectiveModel = nextModelSource === "secret" ? nextModel : "";
      setModel(effectiveModel);
      setModelOriginal(effectiveModel);

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
    if (!secretsEnabled) return;

    try {
      const st = await getApiKeyStatusAsync(transport, name);
      const configured = Boolean(st.configured);
      const masked = String(st.masked || "");

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

  function onApiKeyChange(v: string) {
    setApiKey(v);
    // If user edits while existing key is masked, treat as draft.
    if (!isNewKeyDraft && hasExistingKey && v !== existingKeyMasked) {
      setIsNewKeyDraft(true);
      setKeyShown(true);
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
      if (!secretsEnabled) {
        setMsg({ kind: "err", text: "Key reveal is disabled in remote mode." });
        return;
      }

      try {
        setBusy(true);
        setMsg(null);
        const st = await getApiKeyStatusAsync(transport, name, { reveal: true });
        if (!st.value) {
          setMsg({ kind: "err", text: "Failed to reveal key (not configured)." });
          return;
        }

        const full = String(st.value || "");
        const masked = String(st.masked || existingKeyMasked || "");
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
    if (!secretsEnabled) throw new Error("Secrets API is disabled in remote mode.");

    const key = `LLMProviders:Providers:${pn}:Endpoint`;
    const ep = endpoint.trim();

    if (!ep) {
      await removeSecretAsync(transport, key);
      return;
    }

    await setSecretAsync(transport, key, ep);
  }

  async function saveModelOverride(name: string) {
    const pn = String(name ?? "").trim();
    if (!pn) return;
    if (!secretsEnabled) throw new Error("Secrets API is disabled in remote mode.");

    const key = `LLMProviders:Providers:${pn}:Model`;
    const m = model.trim();

    if (!m) {
      await removeSecretAsync(transport, key);
      return;
    }

    await setSecretAsync(transport, key, m);
  }

  async function submit() {
    const name = providerName.trim();
    const key = apiKey.trim();
    const m = model.trim();
    if (!name) return;

    if (!m) {
      setMsg({ kind: "err", text: "Model is required. Click Fetch models and pick one (or type it)." });
      return;
    }

    if (!secretsEnabled) {
      setMsg({ kind: "err", text: "Secrets/LLM configuration is local-only (loopback sidecar required)." });
      return;
    }

    setBusy(true);
    setMsg(null);
    try {
      await saveEndpointOverride(name);
      await saveModelOverride(name);

      if (isNewKeyDraft === true) {
        if (!key) throw new Error("API key is required when saving a new key.");
        await setLlmApiKeyAsync(transport, name, key);

        setMsg({ kind: "ok", text: "Saved. Provider is now connected. Click Test to verify." });

        setApiKey("");
        setIsNewKeyDraft(false);
        setKeyShown(false);
      } else {
        setMsg({ kind: "ok", text: "Saved settings. Click Test to verify." });
      }

      onSaved?.();
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
    if (!secretsEnabled) return;

    setBusy(true);
    setMsg(null);
    try {
      const json: any = await testProviderAsync(transport, name);
      if (!json) throw new Error("bad response");
      if (json.ok === true) {
        const ms = typeof json.latencyMs === "number" ? json.latencyMs : null;
        const cnt = typeof json.modelsCount === "number" ? json.modelsCount : null;
        setMsg({ kind: "ok", text: `OK${ms != null ? ` · ${ms}ms` : ""}${cnt != null ? ` · models=${cnt}` : ""}` });
        // Keep UI minimal: model selection is driven by explicit Fetch models.
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
    if (!secretsEnabled) return;

    setBusy(true);
    setMsg(null);
    try {
      const res = await fetchModelsAsync(transport, name, 200);
      setModels(res.models);
      setMsg({ kind: "ok", text: `Fetched models: ${res.models.length}` });
      // Keep model empty until user explicitly selects from dropdown (unless already configured).
    } catch (e: any) {
      setMsg({ kind: "err", text: e?.message ?? String(e) });
    } finally {
      setBusy(false);
    }
  }

  async function disconnect() {
    const name = providerName.trim();
    if (!name) return;
    if (!secretsEnabled) return;

    setBusy(true);
    setMsg(null);
    try {
      await deleteLlmApiKeyAsync(transport, name);
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
    if (!secretsEnabled) return;

    setBusy(true);
    setMsg(null);
    try {
      await setSecretAsync(transport, k, v);
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
    if (!secretsEnabled) return;

    setBusy(true);
    setMsg(null);
    try {
      await removeSecretAsync(transport, k);
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
                setModels([]);
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
          {view === "list" && (
            <ApiKeyListView
              secretsEnabled={secretsEnabled}
              search={search}
              onSearchChange={setSearch}
              filtered={filtered}
              onOpenConnect={openConnect}
              onOpenAdvanced={() => {
                setView("advanced");
                setMsg(null);
              }}
            />
          )}

          {view === "connect" && (
            <ApiKeyConnectView
              msg={msg}
              busy={busy}
              secretsEnabled={secretsEnabled}
              providerName={providerName}
              endpoint={endpoint}
              endpointSource={endpointSource}
              onEndpointChange={setEndpoint}
              model={model}
              modelSource={modelSource}
              models={models}
              onModelChange={setModel}
              apiKey={apiKey}
              keyShown={keyShown}
              isNewKeyDraft={isNewKeyDraft}
              hasExistingKey={hasExistingKey}
              existingKeyMasked={existingKeyMasked}
              onApiKeyChange={onApiKeyChange}
              onToggleKey={() => void toggleKey()}
              canSave={canSave}
              canTest={canTest}
              onSubmit={() => void submit()}
              onTest={() => void testConnection()}
              onFetchModels={() => void fetchModels()}
              onDisconnect={() => void disconnect()}
            />
          )}

          {view === "advanced" && (
            <ApiKeyAdvancedView
              msg={msg}
              busy={busy}
              secretsEnabled={secretsEnabled}
              advKey={advKey}
              advValue={advValue}
              advShown={advShown}
              onAdvKeyChange={setAdvKey}
              onAdvValueChange={setAdvValue}
              onToggleAdvShown={() => setAdvShown((v) => !v)}
              onSave={() => void saveRaw()}
              onRemove={() => void removeRaw()}
            />
          )}
        </div>

        {/* Footer */}
        <div className="px-5 py-4 border-t border-slate-200 bg-white flex items-center gap-2">
          <div className="text-[11px] text-slate-500">
            Local-only: <span className="font-mono">/api/llm/*</span>, <span className="font-mono">/api/secrets/*</span>
          </div>
          <div className="ml-auto" />
          <button
            onClick={onClose}
            className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}


