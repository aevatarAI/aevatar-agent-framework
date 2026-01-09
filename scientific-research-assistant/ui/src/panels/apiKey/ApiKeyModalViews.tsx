import { Search } from "lucide-react";
import type { ProviderItem } from "./types";

export type ApiKeyModalMsg = { kind: "ok" | "err" | "info"; text: string };

function MsgBanner(props: { msg: ApiKeyModalMsg | null }) {
  const { msg } = props;
  if (!msg) return null;
  return (
    <div
      className={`text-xs rounded-xl border p-3 ${
        msg.kind === "ok"
          ? "border-emerald-200 bg-emerald-50 text-emerald-800"
          : msg.kind === "info"
            ? "border-slate-200 bg-slate-50 text-slate-700"
            : "border-rose-200 bg-rose-50 text-rose-800"
      }`}
    >
      {msg.text}
    </div>
  );
}

export function ApiKeyListView(props: {
  secretsEnabled: boolean;
  search: string;
  onSearchChange: (v: string) => void;
  filtered: { configured: ProviderItem[]; popular: ProviderItem[]; other: ProviderItem[] };
  onOpenConnect: (p: ProviderItem) => void;
  onOpenAdvanced: () => void;
}) {
  const { secretsEnabled, search, onSearchChange, filtered, onOpenConnect, onOpenAdvanced } = props;

  return (
    <div className="p-5">
      {!secretsEnabled && (
        <div className="mb-4 border border-amber-200 bg-amber-50 rounded-xl p-3 text-xs text-amber-900">
          Local-only: /api/llm/* and /api/secrets/* are <span className="font-mono">localhost-only</span>. Remote baseUrl is not supported for secrets management.
        </div>
      )}

      <div className="flex items-center gap-2 px-3 py-2 rounded-xl border border-slate-200 bg-slate-50">
        <Search size={14} className="text-slate-400" />
        <input
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
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
                onClick={() => onOpenConnect(p)}
                className="w-full text-left px-4 py-3 hover:bg-slate-50 border-b border-slate-200 last:border-b-0 flex items-center gap-3"
              >
                <div className="w-8 h-8 rounded-xl border border-slate-200 bg-white flex items-center justify-center font-bold text-slate-900">
                  {String(p.displayName || p.id).trim().slice(0, 1).toUpperCase()}
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <div className="text-sm font-semibold text-slate-900 truncate">{p.displayName || p.id}</div>
                    <span className="text-[10px] px-2 py-0.5 rounded bg-emerald-50 text-emerald-700 border border-emerald-200">
                      connected
                    </span>
                  </div>
                  {p.description && <div className="text-xs text-slate-500 mt-0.5 truncate">{p.description}</div>}
                </div>
                <div className="text-xs text-slate-500 font-mono">{p.id}</div>
              </button>
            ))}
          </div>
        </div>
      )}

      {filtered.popular.length > 0 && (
        <div className="mt-5">
          <div className="text-xs font-semibold text-slate-500 mb-2">Popular</div>
          <div className="border border-slate-200 rounded-xl overflow-hidden bg-white">
            {filtered.popular.map((p) => (
              <button
                key={p.id}
                onClick={() => onOpenConnect(p)}
                className="w-full text-left px-4 py-3 hover:bg-slate-50 border-b border-slate-200 last:border-b-0 flex items-center gap-3"
              >
                <div className="w-8 h-8 rounded-xl border border-slate-200 bg-white flex items-center justify-center font-bold text-slate-900">
                  {String(p.displayName || p.id).trim().slice(0, 1).toUpperCase()}
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <div className="text-sm font-semibold text-slate-900 truncate">{p.displayName || p.id}</div>
                    {p.recommended && (
                      <span className="text-[10px] px-2 py-0.5 rounded bg-indigo-50 text-indigo-700 border border-indigo-200">
                        recommended
                      </span>
                    )}
                  </div>
                  {p.description && <div className="text-xs text-slate-500 mt-0.5 truncate">{p.description}</div>}
                </div>
                <div className="text-xs text-slate-500 font-mono">{p.id}</div>
              </button>
            ))}
          </div>
        </div>
      )}

      {filtered.other.length > 0 && (
        <div className="mt-5">
          <div className="text-xs font-semibold text-slate-500 mb-2">Other</div>
          <div className="border border-slate-200 rounded-xl overflow-hidden bg-white">
            {filtered.other.map((p) => (
              <button
                key={p.id}
                onClick={() => onOpenConnect(p)}
                className="w-full text-left px-4 py-3 hover:bg-slate-50 border-b border-slate-200 last:border-b-0 flex items-center gap-3"
              >
                <div className="w-8 h-8 rounded-xl border border-slate-200 bg-white flex items-center justify-center font-bold text-slate-900">
                  {String(p.displayName || p.id).trim().slice(0, 1).toUpperCase()}
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <div className="text-sm font-semibold text-slate-900 truncate">{p.displayName || p.id}</div>
                  </div>
                  {p.description && <div className="text-xs text-slate-500 mt-0.5 truncate">{p.description}</div>}
                </div>
                <div className="text-xs text-slate-500 font-mono">{p.id}</div>
              </button>
            ))}
          </div>
        </div>
      )}

      <div className="mt-6">
        <button
          onClick={onOpenAdvanced}
          className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
          disabled={!secretsEnabled}
          title={secretsEnabled ? "Open advanced secrets editor" : "Local-only (loopback sidecar required)"}
        >
          Advanced: set/remove secret key
        </button>
      </div>
    </div>
  );
}

export function ApiKeyConnectView(props: {
  msg: ApiKeyModalMsg | null;
  busy: boolean;
  secretsEnabled: boolean;
  providerName: string;
  endpoint: string;
  endpointSource: string;
  onEndpointChange: (v: string) => void;
  model: string;
  modelSource: string;
  models: string[];
  onModelChange: (v: string) => void;
  apiKey: string;
  keyShown: boolean;
  isNewKeyDraft: boolean;
  hasExistingKey: boolean;
  existingKeyMasked: string;
  onApiKeyChange: (v: string) => void;
  onToggleKey: () => void;
  canSave: boolean;
  canTest: boolean;
  onSubmit: () => void;
  onTest: () => void;
  onFetchModels: () => void;
  onDisconnect: () => void;
}) {
  const {
    msg,
    busy,
    secretsEnabled,
    providerName,
    endpoint,
    endpointSource,
    onEndpointChange,
    model,
    modelSource,
    models,
    onModelChange,
    apiKey,
    keyShown,
    isNewKeyDraft,
    hasExistingKey,
    existingKeyMasked,
    onApiKeyChange,
    onToggleKey,
    canSave,
    canTest,
    onSubmit,
    onTest,
    onFetchModels,
    onDisconnect,
  } = props;

  return (
    <div className="p-5 space-y-4">
      <MsgBanner msg={msg} />

      <div className="grid grid-cols-1 gap-3">
        <div>
          <div className="text-[11px] text-slate-500 mb-1">Provider</div>
          <div className="text-sm font-mono text-slate-900 break-all">{providerName}</div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          <div>
            <div className="text-[11px] text-slate-500 mb-1">Endpoint</div>
            <input
              value={endpoint}
              onChange={(e) => onEndpointChange(e.target.value)}
              placeholder="https://..."
              className="w-full text-xs bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900 placeholder:text-slate-400"
              disabled={busy || !secretsEnabled}
            />
            <div className="mt-1 text-[10px] text-slate-500 font-mono">source: {endpointSource || "unknown"}</div>
          </div>

          <div>
            <div className="text-[11px] text-slate-500 mb-1">Model</div>
            {models.length > 0 ? (
              <select
                value={model}
                onChange={(e) => onModelChange(e.target.value)}
                className="w-full text-xs bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900"
                disabled={busy || !secretsEnabled}
              >
                <option value="">-- pick a model --</option>
                {models.map((m) => (
                  <option key={m} value={m}>
                    {m}
                  </option>
                ))}
              </select>
            ) : (
              <input
                value={model}
                onChange={(e) => onModelChange(e.target.value)}
                placeholder="model-id"
                className="w-full text-xs bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900 placeholder:text-slate-400"
                disabled={busy || !secretsEnabled}
              />
            )}
            <div className="mt-1 text-[10px] text-slate-500 font-mono">source: {modelSource || "unknown"}</div>
          </div>
        </div>

        <div>
          <div className="text-[11px] text-slate-500 mb-1">API Key</div>
          <div className="flex items-center gap-2">
            <input
              type={keyShown ? "text" : "password"}
              value={apiKey}
              onChange={(e) => onApiKeyChange(e.target.value)}
              placeholder={hasExistingKey ? "masked (click Show to reveal)" : "paste api key..."}
              className="flex-1 text-xs bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900 placeholder:text-slate-400"
              disabled={busy || !secretsEnabled}
            />
            <button
              type="button"
              onClick={onToggleKey}
              className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
              disabled={busy || !secretsEnabled}
              title={hasExistingKey && !isNewKeyDraft ? "Reveal stored key (localhost-only)" : "Show/hide"}
            >
              {keyShown ? "Hide" : "Show"}
            </button>
          </div>
          <div className="mt-1 text-[10px] text-slate-500 font-mono">
            {hasExistingKey ? `stored: yes · masked: ${existingKeyMasked || "-"}` : "stored: no"}
            {isNewKeyDraft ? " · draft: yes" : ""}
          </div>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <button
          onClick={onSubmit}
          className="text-xs px-3 py-2 rounded bg-indigo-600 hover:bg-indigo-500 border border-indigo-600 text-white disabled:opacity-50"
          disabled={!canSave}
        >
          {busy ? "Saving…" : "Save"}
        </button>
        <button
          onClick={onTest}
          className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
          disabled={!canTest}
        >
          Test
        </button>
        <button
          onClick={onFetchModels}
          className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
          disabled={busy || !secretsEnabled}
        >
          Fetch models
        </button>
        <button
          onClick={onDisconnect}
          className="ml-auto text-xs px-3 py-2 rounded bg-rose-600 hover:bg-rose-500 border border-rose-600 text-white disabled:opacity-50"
          disabled={busy || !secretsEnabled || !hasExistingKey}
          title="Remove stored API key"
        >
          Disconnect
        </button>
      </div>
    </div>
  );
}

export function ApiKeyAdvancedView(props: {
  msg: ApiKeyModalMsg | null;
  busy: boolean;
  secretsEnabled: boolean;
  advKey: string;
  advValue: string;
  advShown: boolean;
  onAdvKeyChange: (v: string) => void;
  onAdvValueChange: (v: string) => void;
  onToggleAdvShown: () => void;
  onSave: () => void;
  onRemove: () => void;
}) {
  const {
    msg,
    busy,
    secretsEnabled,
    advKey,
    advValue,
    advShown,
    onAdvKeyChange,
    onAdvValueChange,
    onToggleAdvShown,
    onSave,
    onRemove,
  } = props;

  return (
    <div className="p-5 space-y-4">
      <MsgBanner msg={msg} />

      <div className="text-xs text-slate-600">
        Advanced writes directly into Aevatar user secrets. Values are never stored in the UI; they are written to the sidecar secrets store.
      </div>

      <div className="grid grid-cols-1 gap-3">
        <div>
          <div className="text-[11px] text-slate-500 mb-1">Key</div>
          <input
            value={advKey}
            onChange={(e) => onAdvKeyChange(e.target.value)}
            placeholder="e.g. LLMProviders:Providers:openai:Model"
            className="w-full text-xs bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900 placeholder:text-slate-400 font-mono"
            disabled={busy || !secretsEnabled}
          />
        </div>
        <div>
          <div className="text-[11px] text-slate-500 mb-1">Value</div>
          <input
            type={advShown ? "text" : "password"}
            value={advValue}
            onChange={(e) => onAdvValueChange(e.target.value)}
            placeholder="value"
            className="w-full text-xs bg-white border border-slate-200 rounded-lg px-3 py-2 text-slate-900 placeholder:text-slate-400 font-mono"
            disabled={busy || !secretsEnabled}
          />
          <div className="mt-1 text-[10px] text-slate-500">
            <button
              type="button"
              className="underline hover:text-slate-700 disabled:opacity-50"
              onClick={onToggleAdvShown}
              disabled={busy}
            >
              {advShown ? "Hide" : "Show"}
            </button>
          </div>
        </div>
      </div>

      <div className="flex items-center gap-2">
        <button
          onClick={onSave}
          className="text-xs px-3 py-2 rounded bg-indigo-600 hover:bg-indigo-500 border border-indigo-600 text-white disabled:opacity-50"
          disabled={busy || !secretsEnabled || advKey.trim().length === 0 || advValue.trim().length === 0}
        >
          Save
        </button>
        <button
          onClick={onRemove}
          className="text-xs px-3 py-2 rounded bg-white hover:bg-slate-50 border border-slate-200 text-slate-700 disabled:opacity-50"
          disabled={busy || !secretsEnabled || advKey.trim().length === 0}
        >
          Remove
        </button>
        <div className="ml-auto text-[11px] text-slate-500">{busy ? "Working…" : ""}</div>
      </div>
    </div>
  );
}


