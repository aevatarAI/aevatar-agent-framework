// ============================================================================
//  Settings Panel - LLM Providers Tab
// ============================================================================

import React, { useEffect, useState, useMemo, useCallback } from 'react'
import { cn } from '@/lib/utils'
import {
  listLlmProviders,
  listLlmInstances,
  getLlmProvider,
  getApiKeyStatus,
  setLlmApiKey,
  deleteLlmApiKey,
  testLlmProvider,
  fetchLlmModels,
  saveProviderEndpoint,
  saveProviderModel,
  getDefaultProvider,
  setDefaultProvider,
  type ProviderPublic,
  type ProviderInstance,
} from '@/lib/axiom-client'
import type { LlmProvider } from '@/types'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { ProviderGroup } from './components'

interface ProvidersTabProps {
  defaultProvider?: string
}

export const ProvidersTab: React.FC<ProvidersTabProps> = ({
  defaultProvider: initialDefaultProvider,
}) => {
  const [providers, setProviders] = useState<LlmProvider[]>([])
  const [instances, setInstances] = useState<ProviderInstance[]>([])
  const [loading, setLoading] = useState(true)
  const [selected, setSelected] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  // Default provider state
  const [currentDefault, setCurrentDefault] = useState(initialDefaultProvider || '')
  const [settingDefault, setSettingDefault] = useState(false)
  const [defaultMsg, setDefaultMsg] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null)

  // Provider detail state
  const [detailLoading, setDetailLoading] = useState(false)
  const [providerDetail, setProviderDetail] = useState<ProviderPublic | null>(null)
  const [apiKey, setApiKey] = useState('')
  const [keyMasked, setKeyMasked] = useState('')
  const [keyShown, setKeyShown] = useState(false)
  const [hasKey, setHasKey] = useState(false)
  const [model, setModel] = useState('')
  const [originalModel, setOriginalModel] = useState('')
  const [models, setModels] = useState<string[]>([])
  const [endpoint, setEndpoint] = useState('')
  const [originalEndpoint, setOriginalEndpoint] = useState('')
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null)

  // Fetch providers, instances, and default
  useEffect(() => {
    const fetch = async () => {
      setLoading(true)
      try {
        const [providersRes, instancesRes, defaultRes] = await Promise.all([
          listLlmProviders(),
          listLlmInstances(),
          getDefaultProvider(),
        ])
        const arr = Array.isArray(providersRes?.providers) ? providersRes.providers : []
        setProviders(arr)
        const inst = Array.isArray(instancesRes?.instances) ? instancesRes.instances : []
        setInstances(inst)
        if (defaultRes?.providerName) {
          setCurrentDefault(defaultRes.providerName)
        }
      } catch {
        /* best-effort */
      }
      setLoading(false)
    }
    fetch()
  }, [])

  const handleSetDefault = useCallback(
    async (providerName: string) => {
      if (settingDefault || !providerName) return
      setSettingDefault(true)
      setDefaultMsg(null)
      try {
        const res = await setDefaultProvider(providerName)
        if (res?.ok) {
          setCurrentDefault(providerName)
          setDefaultMsg({ kind: 'ok', text: `Default provider set to ${providerName}` })
        } else {
          setDefaultMsg({ kind: 'err', text: res?.error || 'Failed to set default provider' })
        }
      } catch (e) {
        const err = e as Error
        setDefaultMsg({ kind: 'err', text: err?.message || 'Failed to set default provider' })
      }
      setSettingDefault(false)
    },
    [settingDefault]
  )

  const loadDetail = useCallback(async (name: string) => {
    setSelected(name)
    setDetailLoading(true)
    setMsg(null)
    setApiKey('')
    setKeyMasked('')
    setKeyShown(false)
    setModels([])
    try {
      const [providerRes, keyRes] = await Promise.all([getLlmProvider(name), getApiKeyStatus(name)])
      const p = providerRes?.provider
      setProviderDetail(p || null)
      setEndpoint(p?.endpoint || '')
      setOriginalEndpoint(p?.endpoint || '')
      setModel(p?.model || '')
      setOriginalModel(p?.model || '')
      setHasKey(!!keyRes?.configured)
      setKeyMasked(keyRes?.masked || '')
    } catch {
      /* best-effort */
    }
    setDetailLoading(false)
  }, [])

  const handleRevealKey = useCallback(async () => {
    if (!selected || busy) return
    setBusy(true)
    try {
      const res = await getApiKeyStatus(selected, true)
      if (res?.value) {
        setApiKey(res.value)
        setKeyShown(true)
      }
    } catch (e) {
      const err = e as Error
      setMsg({ kind: 'err', text: err?.message || 'Failed to reveal key' })
    }
    setBusy(false)
  }, [selected, busy])

  const handleFetchModels = useCallback(async () => {
    if (!selected || busy) return
    setBusy(true)
    try {
      const res = await fetchLlmModels(selected)
      setModels(res?.models || [])
    } catch (e) {
      const err = e as Error
      setMsg({ kind: 'err', text: err?.message || 'Failed to fetch models' })
    }
    setBusy(false)
  }, [selected, busy])

  const handleSave = useCallback(async () => {
    if (!selected || busy) return
    setBusy(true)
    setMsg(null)
    const saved: string[] = []
    try {
      if (apiKey.trim()) {
        await setLlmApiKey(selected, apiKey.trim())
        saved.push('API key')
        setHasKey(true)
        setApiKey('')
        setKeyShown(false)
        const keyRes = await getApiKeyStatus(selected)
        setKeyMasked(keyRes?.masked || '')
      }
      if (endpoint !== originalEndpoint) {
        await saveProviderEndpoint(selected, endpoint)
        saved.push('endpoint')
        setOriginalEndpoint(endpoint)
      }
      if (model !== originalModel) {
        await saveProviderModel(selected, model)
        saved.push('model')
        setOriginalModel(model)
      }
      if (saved.length > 0) {
        setMsg({ kind: 'ok', text: `Saved: ${saved.join(', ')}` })
      } else {
        setMsg({ kind: 'ok', text: 'No changes to save' })
      }
    } catch (e) {
      const err = e as Error
      setMsg({ kind: 'err', text: err?.message || 'Save failed' })
    }
    setBusy(false)
  }, [selected, apiKey, endpoint, originalEndpoint, model, originalModel, busy])

  const handleTest = useCallback(async () => {
    if (!selected || busy) return
    setBusy(true)
    setMsg(null)
    try {
      if (apiKey.trim()) {
        setMsg({ kind: 'ok', text: 'Saving key before test...' })
        await setLlmApiKey(selected, apiKey.trim())
        setHasKey(true)
        setApiKey('')
        setKeyShown(false)
        const keyRes = await getApiKeyStatus(selected)
        setKeyMasked(keyRes?.masked || '')
      }

      const res = await testLlmProvider(selected)
      const latency = res?.latencyMs ? ` (${res.latencyMs}ms)` : ''
      const modelsInfo = res?.modelsCount ? ` | ${res.modelsCount} models` : ''
      setMsg({
        kind: res?.ok ? 'ok' : 'err',
        text: res?.ok ? `Connection OK${latency}${modelsInfo}` : res?.error || 'Test failed',
      })
    } catch (e) {
      const err = e as Error
      setMsg({ kind: 'err', text: err?.message || 'Test failed' })
    }
    setBusy(false)
  }, [selected, apiKey, busy])

  const handleDelete = useCallback(async () => {
    if (!selected || busy) return
    setBusy(true)
    setMsg(null)
    try {
      await deleteLlmApiKey(selected)
      setMsg({ kind: 'ok', text: 'API key removed' })
      setHasKey(false)
      setKeyMasked('')
      setApiKey('')
    } catch (e) {
      const err = e as Error
      setMsg({ kind: 'err', text: err?.message || 'Delete failed' })
    }
    setBusy(false)
  }, [selected, busy])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return providers
    return providers.filter(
      (p) => p.id.toLowerCase().includes(q) || (p.displayName || '').toLowerCase().includes(q)
    )
  }, [providers, search])

  const grouped = useMemo(() => {
    const configured = filtered.filter((p) => p.apiKeyConfigured)
    const popular = filtered.filter((p) => !p.apiKeyConfigured && p.category === 'popular')
    const other = filtered.filter((p) => !p.apiKeyConfigured && p.category !== 'popular')
    return { configured, popular, other }
  }, [filtered])

  const configuredInstances = instances

  return (
    <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
      {/* Provider List */}
      <div className="lg:col-span-1 card overflow-hidden">
        {/* Default Provider Selector */}
        <div className="p-3 border-b border-border-subtle bg-surface-elevated/50">
          <label className="text-[10px] font-mono text-text-dimmed uppercase tracking-wider block mb-1.5">
            Default Provider
          </label>
          <div className="flex gap-2">
            <Select
              value={currentDefault || undefined}
              onValueChange={handleSetDefault}
              disabled={settingDefault || configuredInstances.length === 0}
            >
              <SelectTrigger className="flex-1 text-sm">
                <SelectValue
                  placeholder={
                    configuredInstances.length === 0 ? 'No configured providers' : 'Select default...'
                  }
                />
              </SelectTrigger>
              <SelectContent>
                {configuredInstances.map((inst) => (
                  <SelectItem key={inst.name} value={inst.name}>
                    {inst.providerDisplayName} ({inst.name})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <button
              onClick={() => handleSetDefault(currentDefault)}
              disabled={settingDefault || !currentDefault}
              className="btn-ghost px-3 text-xs"
            >
              {settingDefault ? '...' : 'Set'}
            </button>
          </div>
          {currentDefault && (
            <div className="mt-1 text-[10px] font-mono text-neon-cyan">Current: {currentDefault}</div>
          )}
          {configuredInstances.length === 0 && !loading && (
            <div className="mt-1 text-[10px] font-mono text-neon-gold">
              Configure a provider API key below first
            </div>
          )}
          {defaultMsg && (
            <div
              className={cn(
                'mt-2 text-[10px] font-mono px-2 py-1 rounded',
                defaultMsg.kind === 'ok' ? 'text-neon-green bg-neon-green/10' : 'text-neon-red bg-neon-red/10'
              )}
            >
              {defaultMsg.text}
            </div>
          )}
        </div>

        {/* Search */}
        <div className="p-3 border-b border-border-subtle">
          <input
            type="text"
            placeholder="Search providers..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="input w-full text-sm"
          />
        </div>
        <div className="max-h-[60vh] overflow-auto">
          {loading ? (
            <div className="p-4 text-center text-sm text-text-muted">Loading...</div>
          ) : (
            <>
              {grouped.configured.length > 0 && (
                <ProviderGroup
                  label="Configured"
                  providers={grouped.configured}
                  selected={selected}
                  onSelect={loadDetail}
                  defaultProvider={currentDefault}
                />
              )}
              {grouped.popular.length > 0 && (
                <ProviderGroup
                  label="Popular"
                  providers={grouped.popular}
                  selected={selected}
                  onSelect={loadDetail}
                />
              )}
              {grouped.other.length > 0 && (
                <ProviderGroup
                  label="Other"
                  providers={grouped.other}
                  selected={selected}
                  onSelect={loadDetail}
                />
              )}
            </>
          )}
        </div>
      </div>

      {/* Provider Detail */}
      <div className="lg:col-span-2 card p-6">
        {!selected ? (
          <div className="h-full flex items-center justify-center text-text-muted text-sm">
            Select a provider to configure
          </div>
        ) : detailLoading ? (
          <div className="h-full flex items-center justify-center text-text-muted text-sm">
            Loading...
          </div>
        ) : (
          <ProviderDetailForm
            selected={selected}
            providerDetail={providerDetail}
            hasKey={hasKey}
            keyMasked={keyMasked}
            apiKey={apiKey}
            setApiKey={setApiKey}
            keyShown={keyShown}
            setKeyShown={setKeyShown}
            endpoint={endpoint}
            setEndpoint={setEndpoint}
            originalEndpoint={originalEndpoint}
            model={model}
            setModel={setModel}
            originalModel={originalModel}
            models={models}
            busy={busy}
            msg={msg}
            onRevealKey={handleRevealKey}
            onFetchModels={handleFetchModels}
            onSave={handleSave}
            onTest={handleTest}
            onDelete={handleDelete}
          />
        )}
      </div>
    </div>
  )
}

// === Provider Detail Form (Internal Component) ===

interface ProviderDetailFormProps {
  selected: string
  providerDetail: ProviderPublic | null
  hasKey: boolean
  keyMasked: string
  apiKey: string
  setApiKey: (v: string) => void
  keyShown: boolean
  setKeyShown: (v: boolean) => void
  endpoint: string
  setEndpoint: (v: string) => void
  originalEndpoint: string
  model: string
  setModel: (v: string) => void
  originalModel: string
  models: string[]
  busy: boolean
  msg: { kind: 'ok' | 'err'; text: string } | null
  onRevealKey: () => void
  onFetchModels: () => void
  onSave: () => void
  onTest: () => void
  onDelete: () => void
}

const ProviderDetailForm: React.FC<ProviderDetailFormProps> = ({
  selected,
  providerDetail,
  hasKey,
  keyMasked,
  apiKey,
  setApiKey,
  keyShown,
  setKeyShown,
  endpoint,
  setEndpoint,
  originalEndpoint,
  model,
  setModel,
  originalModel,
  models,
  busy,
  msg,
  onRevealKey,
  onFetchModels,
  onSave,
  onTest,
  onDelete,
}) => (
  <div className="space-y-6">
    {/* Header */}
    <div>
      <h3 className="text-lg font-display font-semibold text-neon-cyan">
        {providerDetail?.displayName || selected}
      </h3>
      <div className="flex items-center gap-3 mt-2">
        <span
          className={cn(
            'text-[10px] font-mono px-2 py-0.5 rounded',
            hasKey ? 'bg-neon-green/10 text-neon-green' : 'bg-surface-elevated text-text-dimmed'
          )}
        >
          {hasKey ? '✓ API key configured' : '○ API key not set'}
        </span>
        {providerDetail?.kind && (
          <span className="text-[10px] font-mono px-2 py-0.5 rounded bg-surface-elevated text-text-muted">
            {providerDetail.kind}
          </span>
        )}
      </div>
    </div>

    {/* Endpoint */}
    <div>
      <div className="flex items-center gap-2 mb-1.5">
        <label className="text-xs font-mono text-text-muted">Endpoint</label>
        {providerDetail?.endpointSource && (
          <span
            className={cn(
              'text-[9px] font-mono px-1.5 py-0.5 rounded',
              providerDetail.endpointSource === 'secret'
                ? 'bg-neon-gold/10 text-neon-gold'
                : providerDetail.endpointSource === 'default'
                  ? 'bg-surface-elevated text-text-dimmed'
                  : 'bg-neon-red/10 text-neon-red'
            )}
          >
            {providerDetail.endpointSource}
          </span>
        )}
        {endpoint !== originalEndpoint && (
          <span className="text-[9px] font-mono text-neon-cyan">modified</span>
        )}
      </div>
      <input
        type="text"
        value={endpoint}
        onChange={(e) => setEndpoint(e.target.value)}
        placeholder="https://api.example.com/v1"
        className="input w-full"
      />
    </div>

    {/* Model */}
    <div>
      <div className="flex items-center gap-2 mb-1.5">
        <label className="text-xs font-mono text-text-muted">Model</label>
        {providerDetail?.modelSource && (
          <span
            className={cn(
              'text-[9px] font-mono px-1.5 py-0.5 rounded',
              providerDetail.modelSource === 'secret'
                ? 'bg-neon-gold/10 text-neon-gold'
                : providerDetail.modelSource === 'default'
                  ? 'bg-surface-elevated text-text-dimmed'
                  : 'bg-neon-red/10 text-neon-red'
            )}
          >
            {providerDetail.modelSource}
          </span>
        )}
        {model !== originalModel && (
          <span className="text-[9px] font-mono text-neon-cyan">modified</span>
        )}
      </div>
      <div className="flex gap-2">
        {models.length > 0 ? (
          <Select value={model || undefined} onValueChange={setModel}>
            <SelectTrigger className="flex-1">
              <SelectValue placeholder="Select model..." />
            </SelectTrigger>
            <SelectContent>
              {models.map((m) => (
                <SelectItem key={m} value={m}>
                  {m}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        ) : (
          <input
            type="text"
            value={model}
            onChange={(e) => setModel(e.target.value)}
            placeholder="e.g. gpt-4o, claude-3.5-sonnet"
            className="input flex-1"
          />
        )}
        <button onClick={onFetchModels} disabled={busy || !hasKey} className="btn-ghost px-3 text-xs">
          {models.length > 0 ? 'Refresh' : 'Fetch'}
        </button>
      </div>
    </div>

    {/* API Key */}
    <div>
      <div className="flex items-center gap-2 mb-1.5">
        <label className="text-xs font-mono text-text-muted">API Key</label>
        {hasKey && keyMasked && (
          <span className="text-[10px] font-mono text-text-dimmed">{keyMasked}</span>
        )}
      </div>
      <div className="flex gap-2">
        <input
          type={keyShown ? 'text' : 'password'}
          value={apiKey}
          onChange={(e) => setApiKey(e.target.value)}
          placeholder={hasKey ? 'Enter new key to replace...' : 'Enter API key...'}
          className="input flex-1 font-mono"
        />
        {hasKey && !apiKey && (
          <button onClick={onRevealKey} disabled={busy} className="btn-ghost px-3 text-xs">
            Reveal
          </button>
        )}
        {apiKey && (
          <button onClick={() => setKeyShown(!keyShown)} className="btn-ghost px-3 text-xs">
            {keyShown ? 'Hide' : 'Show'}
          </button>
        )}
      </div>
    </div>

    {/* Message */}
    {msg && (
      <div
        className={cn(
          'px-3 py-2 rounded-lg text-xs font-mono',
          msg.kind === 'ok'
            ? 'bg-neon-green/10 text-neon-green border border-neon-green/30'
            : 'bg-neon-red/10 text-neon-red border border-neon-red/30'
        )}
      >
        {msg.text}
      </div>
    )}

    {/* Actions */}
    <div className="flex flex-wrap gap-3 pt-2">
      <button
        onClick={onSave}
        disabled={busy || (!apiKey.trim() && endpoint === originalEndpoint && model === originalModel)}
        className="btn-primary px-6"
      >
        {busy ? 'Saving...' : 'Save'}
      </button>
      <button onClick={onTest} disabled={busy || !hasKey} className="btn-secondary px-4">
        Test Connection
      </button>
      {hasKey && (
        <button
          onClick={onDelete}
          disabled={busy}
          className="btn-ghost px-4 text-neon-red hover:bg-neon-red/10"
        >
          Remove Key
        </button>
      )}
    </div>

    {/* Hint */}
    <div className="pt-2 border-t border-border-subtle">
      <p className="text-[10px] text-text-dimmed font-mono">
        Endpoint and model overrides are saved as encrypted secrets. Leave blank to use provider
        defaults.
      </p>
    </div>
  </div>
)
