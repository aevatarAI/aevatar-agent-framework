import React, { useEffect, useState, useMemo, useCallback } from 'react';
import { cn } from '@/lib/utils';
import { useSisyphusStore } from '@/store/sisyphus-store';
import {
  listLlmProviders,
  getLlmProvider,
  getApiKeyStatus,
  setLlmApiKey,
  deleteLlmApiKey,
  testLlmProvider,
  fetchLlmModels,
  setSecret,
  removeSecret,
  saveProviderEndpoint,
  saveProviderModel,
  getToolsSnapshot,
  reconnectMcp,
  syncSkills,
  getSkillsSyncStatus,
  getDefaultProvider,
  setDefaultProvider,
  getAgentProviders,
  setAgentProvider,
  type ProviderPublic,
} from '@/lib/axiom-client';
import type { LlmProvider, ToolSummary } from '@/types';
import SkillsMpModal from './skills-mp-modal';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

// ============================================================
//  Settings Panel - Full-page settings in main content area
//  Integrates: Tools & MCP, LLM Providers, Agent Config
// ============================================================

interface SettingsPanelProps {
  sessionId: string | null;
  connected: boolean;
}

type TabKey = 'tools' | 'providers' | 'agents' | 'advanced';

const SettingsPanel: React.FC<SettingsPanelProps> = ({ sessionId, connected }) => {
  const { tools, setTools, apiInfo, agentRoster } = useSisyphusStore();
  
  const [activeTab, setActiveTab] = useState<TabKey>('tools');

  return (
    <div className="h-full flex flex-col overflow-hidden">
      {/* Header */}
      <header className="flex-shrink-0 px-6 py-4 border-b border-border-subtle bg-surface/50">
        <h1 className="text-xl font-display font-bold text-neon-cyan tracking-wider text-balance">
          Settings
        </h1>
        <p className="text-sm text-text-muted mt-1 text-pretty">
          Manage tools, MCP connections, LLM providers, and API keys
        </p>
      </header>

      {/* Tabs */}
      <nav className="flex-shrink-0 px-6 pt-4 flex gap-1 border-b border-border-subtle">
        {[
          { key: 'tools' as TabKey, label: 'Tools & MCP', icon: '⚙️' },
          { key: 'providers' as TabKey, label: 'LLM Providers', icon: '🔑' },
          { key: 'agents' as TabKey, label: 'Agents', icon: '🤖' },
          { key: 'advanced' as TabKey, label: 'Advanced', icon: '🛠️' },
        ].map((tab) => (
          <button
            key={tab.key}
            onClick={() => setActiveTab(tab.key)}
            className={cn(
              "px-4 py-2.5 text-sm font-mono rounded-t-lg transition-all duration-200",
              activeTab === tab.key
                ? "bg-surface border border-b-0 border-border-subtle text-neon-cyan"
                : "text-text-muted hover:text-text-primary hover:bg-surface-elevated"
            )}
          >
            <span className="mr-2">{tab.icon}</span>
            {tab.label}
          </button>
        ))}
      </nav>

      {/* Content */}
      <div className="flex-1 overflow-y-auto p-6">
        {activeTab === 'tools' && (
          <ToolsMcpTab sessionId={sessionId} connected={connected} tools={tools} setTools={setTools} />
        )}
        {activeTab === 'providers' && (
          <ProvidersTab defaultProvider={apiInfo?.llm?.default} />
        )}
        {activeTab === 'agents' && (
          <AgentsTab 
            sessionId={sessionId} 
            connected={connected} 
            agentRoster={agentRoster}
            availableProviders={apiInfo?.llm?.providers || []}
          />
        )}
        {activeTab === 'advanced' && (
          <AdvancedTab />
        )}
      </div>
    </div>
  );
};

// ============================================================
//  Tab: Tools & MCP
// ============================================================

interface ToolsMcpTabProps {
  sessionId: string | null;
  connected: boolean;
  tools: ToolSummary[];
  setTools: (tools: ToolSummary[]) => void;
}

const ToolsMcpTab: React.FC<ToolsMcpTabProps> = ({ sessionId, connected, tools, setTools }) => {
  const [mcpBusy, setMcpBusy] = useState(false);
  const [skillsBusy, setSkillsBusy] = useState(false);
  const [skillsNote, setSkillsNote] = useState('');
  const [skillsLogs, setSkillsLogs] = useState<any[]>([]);
  const [skillsStatus, setSkillsStatus] = useState<any>(null);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<'all' | 'mcp' | 'skills'>('all');
  const [skillsMpOpen, setSkillsMpOpen] = useState(false);

  const mcpCount = useMemo(() => tools.filter(t => (t.source || '').toUpperCase() === 'MCP').length, [tools]);
  const skillsCount = tools.length - mcpCount;

  // Fetch tools on mount
  useEffect(() => {
    if (!sessionId || !connected) return;
    const fetch = async () => {
      try {
        const res = await getToolsSnapshot(sessionId);
        setTools(res.tools || []);
      } catch { /* best-effort */ }
    };
    fetch();
  }, [sessionId, connected, setTools]);

  const filtered = useMemo(() => {
    let list = tools;
    if (filter === 'mcp') list = list.filter(t => (t.source || '').toUpperCase() === 'MCP');
    else if (filter === 'skills') list = list.filter(t => (t.source || '').toUpperCase() !== 'MCP');
    const q = search.trim().toLowerCase();
    if (q) list = list.filter(t => t.name.toLowerCase().includes(q) || (t.description || '').toLowerCase().includes(q));
    return list.slice(0, 200);
  }, [tools, search, filter]);

  const handleReconnectMcp = useCallback(async () => {
    if (!sessionId || mcpBusy) return;
    setMcpBusy(true);
    setError('');
    try {
      await reconnectMcp(sessionId);
      const res = await getToolsSnapshot(sessionId);
      setTools(res.tools || []);
      setSkillsNote('MCP reconnected successfully');
    } catch (e: any) {
      setError(`MCP reconnect failed: ${e?.message || String(e)}`);
    } finally {
      setMcpBusy(false);
    }
  }, [sessionId, mcpBusy, setTools]);

  const handleSyncSkills = useCallback(async () => {
    if (skillsBusy) return;
    setSkillsBusy(true);
    setError('');
    setSkillsNote('');
    setSkillsLogs([]);
    setSkillsStatus(null);

    let alive = true;
    let timer: ReturnType<typeof setInterval> | null = null;

    const poll = async () => {
      try {
        const st = await getSkillsSyncStatus() as any;
        if (!alive) return;
        setSkillsStatus(st);
        setSkillsLogs((st?.logs || []).slice(-12));
        if (st?.running && st?.current?.repoUrl) {
          setSkillsNote(`Syncing: ${st.current.packName || ''} (${st.current.repoUrl})`);
        }
      } catch { /* best-effort */ }
    };

    try {
      await poll();
      timer = setInterval(poll, 800);
      const res = await syncSkills() as any;
      if (timer) clearInterval(timer);
      await poll();
      alive = false;

      const packs = res?.packs || [];
      const okCount = packs.filter((p: any) => p?.ok).length;
      setSkillsNote(res?.ok ? `Skills updated: ${okCount}/${packs.length} packs` : `Partial: ${okCount}/${packs.length}`);

      if (sessionId) {
        const toolsRes = await getToolsSnapshot(sessionId);
        setTools(toolsRes.tools || []);
      }
    } catch (e: any) {
      setError(`Skills sync failed: ${e?.message || String(e)}`);
    } finally {
      alive = false;
      if (timer) clearInterval(timer);
      setSkillsBusy(false);
    }
  }, [skillsBusy, sessionId, setTools]);

  return (
    <div className="space-y-6">
      {/* Stats + Actions */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* Stats Card */}
        <div className="card p-5">
          <h3 className="text-sm font-display font-semibold text-neon-cyan tracking-wider mb-4">Tool Statistics</h3>
          <div className="grid grid-cols-3 gap-4">
            <StatBox label="Total" value={tools.length} />
            <StatBox label="MCP" value={mcpCount} accent="cyan" />
            <StatBox label="Skills" value={skillsCount} accent="gold" />
          </div>
        </div>

        {/* Actions Card */}
        <div className="card p-5">
          <h3 className="text-sm font-display font-semibold text-neon-cyan tracking-wider mb-4">Actions</h3>
          <div className="grid grid-cols-2 gap-3">
            <button
              onClick={handleReconnectMcp}
              disabled={!connected || mcpBusy}
              className="btn-secondary py-3 text-sm"
            >
              {mcpBusy ? 'Reconnecting…' : 'Reconnect MCP'}
            </button>
            <button
              onClick={handleSyncSkills}
              disabled={skillsBusy}
              className="btn-secondary py-3 text-sm flex items-center justify-center gap-2"
            >
              <svg className={cn("size-4", skillsBusy && "animate-spin")} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
              </svg>
              {skillsBusy ? 'Updating…' : 'Update Skills'}
            </button>
          </div>
          {/* SkillsMP Marketplace Button */}
          <button
            onClick={() => setSkillsMpOpen(true)}
            className="w-full mt-3 py-3 text-sm rounded-lg border border-neon-gold/30 bg-neon-gold/5 text-neon-gold hover:bg-neon-gold/10 transition-colors flex items-center justify-center gap-2"
          >
            <svg className="size-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z" />
            </svg>
            SkillsMP Marketplace
          </button>
          {skillsNote && <p className="mt-3 text-xs font-mono text-text-muted">{skillsNote}</p>}
          {error && <p className="mt-3 text-xs font-mono text-neon-red">{error}</p>}
        </div>
      </div>

      {/* SkillsMP Modal */}
      <SkillsMpModal
        open={skillsMpOpen}
        onClose={() => setSkillsMpOpen(false)}
        onInstalled={async () => {
          if (sessionId) {
            const res = await getToolsSnapshot(sessionId);
            setTools(res.tools || []);
          }
        }}
      />

      {/* Sync Logs */}
      {(skillsBusy || skillsLogs.length > 0) && (
        <div className="card p-4">
          <h4 className="text-xs font-mono text-text-dimmed mb-2">Sync Logs</h4>
          <div className="max-h-32 overflow-auto rounded border border-border-subtle bg-bg-void p-2 font-mono text-[10px] text-text-muted">
            {skillsBusy && <div className="text-neon-cyan mb-1">{skillsStatus?.current?.step || 'initializing…'}</div>}
            {skillsLogs.map((l, i) => <div key={i} className="whitespace-pre-wrap">{l?.message || ''}</div>)}
          </div>
        </div>
      )}

      {/* Tools List */}
      <div className="card overflow-hidden">
        <div className="p-4 border-b border-border-subtle flex flex-col sm:flex-row gap-3">
          <div className="relative flex-1">
            <svg className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-text-dimmed" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            <input
              type="text"
              placeholder="Search tools..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="input w-full pl-10"
            />
          </div>
          <Select value={filter} onValueChange={(v) => setFilter(v as 'all' | 'mcp' | 'skills')}>
            <SelectTrigger className="w-full sm:w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All ({tools.length})</SelectItem>
              <SelectItem value="mcp">MCP ({mcpCount})</SelectItem>
              <SelectItem value="skills">Skills ({skillsCount})</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="max-h-[50vh] overflow-auto divide-y divide-border-subtle">
          {filtered.length === 0 ? (
            <div className="p-8 text-center text-sm text-text-muted">
              {tools.length === 0 ? 'No tools available. Try "Reconnect MCP" or "Update Skills".' : 'No tools match your search.'}
            </div>
          ) : (
            filtered.map((tool, idx) => (
              <div key={`${tool.name}-${idx}`} className="px-4 py-3 hover:bg-surface-elevated transition-colors">
                <div className="flex items-center gap-2">
                  <span className="font-mono text-sm text-text-primary truncate flex-1">{tool.name}</span>
                  {(tool.source || '').toUpperCase() === 'MCP' ? (
                    <span className="text-[9px] font-mono text-neon-cyan bg-neon-cyan/10 px-1.5 py-0.5 rounded">MCP</span>
                  ) : tool.source && (
                    <span className="text-[9px] font-mono text-text-dimmed bg-surface-accent px-1.5 py-0.5 rounded">{tool.source}</span>
                  )}
                </div>
                {tool.description && <div className="text-xs text-text-muted mt-1 line-clamp-1">{tool.description}</div>}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
};

// ============================================================
//  Tab: LLM Providers
// ============================================================

interface ProvidersTabProps {
  defaultProvider?: string;
}

const ProvidersTab: React.FC<ProvidersTabProps> = ({ defaultProvider: initialDefaultProvider }) => {
  const [providers, setProviders] = useState<LlmProvider[]>([]);
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  
  // Default provider state
  const [currentDefault, setCurrentDefault] = useState(initialDefaultProvider || '');
  const [settingDefault, setSettingDefault] = useState(false);
  const [defaultMsg, setDefaultMsg] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null);

  // Provider detail state
  const [detailLoading, setDetailLoading] = useState(false);
  const [providerDetail, setProviderDetail] = useState<ProviderPublic | null>(null);
  const [apiKey, setApiKey] = useState('');
  const [keyMasked, setKeyMasked] = useState('');
  const [keyShown, setKeyShown] = useState(false);
  const [hasKey, setHasKey] = useState(false);
  const [model, setModel] = useState('');
  const [originalModel, setOriginalModel] = useState('');
  const [models, setModels] = useState<string[]>([]);
  const [endpoint, setEndpoint] = useState('');
  const [originalEndpoint, setOriginalEndpoint] = useState('');
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null);

  // Fetch providers and default
  useEffect(() => {
    const fetch = async () => {
      setLoading(true);
      try {
        const [providersRes, defaultRes] = await Promise.all([
          listLlmProviders(),
          getDefaultProvider(),
        ]);
        const arr = Array.isArray(providersRes?.providers) ? providersRes.providers : [];
        setProviders(arr);
        if (defaultRes?.providerName) {
          setCurrentDefault(defaultRes.providerName);
        }
      } catch { /* best-effort */ }
      setLoading(false);
    };
    fetch();
  }, []);

  // Handle setting default provider
  const handleSetDefault = useCallback(async (providerName: string) => {
    if (settingDefault || !providerName) return;
    setSettingDefault(true);
    setDefaultMsg(null);
    try {
      const res = await setDefaultProvider(providerName);
      if (res?.ok) {
        setCurrentDefault(providerName);
        setDefaultMsg({ kind: 'ok', text: `Default provider set to ${providerName}` });
      } else {
        setDefaultMsg({ kind: 'err', text: res?.error || 'Failed to set default provider' });
      }
    } catch (e: any) {
      setDefaultMsg({ kind: 'err', text: e?.message || 'Failed to set default provider' });
    }
    setSettingDefault(false);
  }, [settingDefault]);

  // Load provider details
  const loadDetail = useCallback(async (name: string) => {
    setSelected(name);
    setDetailLoading(true);
    setMsg(null);
    setApiKey('');
    setKeyMasked('');
    setKeyShown(false);
    setModels([]);
    try {
      const [providerRes, keyRes] = await Promise.all([
        getLlmProvider(name),
        getApiKeyStatus(name),
      ]);
      const p = providerRes?.provider;
      setProviderDetail(p || null);
      setEndpoint(p?.endpoint || '');
      setOriginalEndpoint(p?.endpoint || '');
      setModel(p?.model || '');
      setOriginalModel(p?.model || '');
      setHasKey(!!keyRes?.configured);
      setKeyMasked(keyRes?.masked || '');
    } catch { /* best-effort */ }
    setDetailLoading(false);
  }, []);

  // Reveal API Key
  const handleRevealKey = useCallback(async () => {
    if (!selected || busy) return;
    setBusy(true);
    try {
      const res = await getApiKeyStatus(selected, true);
      if (res?.value) {
        setApiKey(res.value);
        setKeyShown(true);
      }
    } catch (e: any) {
      setMsg({ kind: 'err', text: e?.message || 'Failed to reveal key' });
    }
    setBusy(false);
  }, [selected, busy]);

  const handleFetchModels = useCallback(async () => {
    if (!selected || busy) return;
    setBusy(true);
    try {
      const res = await fetchLlmModels(selected);
      setModels(res?.models || []);
    } catch (e: any) {
      setMsg({ kind: 'err', text: e?.message || 'Failed to fetch models' });
    }
    setBusy(false);
  }, [selected, busy]);

  // Save all: API key, endpoint, model
  const handleSave = useCallback(async () => {
    if (!selected || busy) return;
    setBusy(true);
    setMsg(null);
    const saved: string[] = [];
    try {
      // Save API Key if provided
      if (apiKey.trim()) {
        await setLlmApiKey(selected, apiKey.trim());
        saved.push('API key');
        setHasKey(true);
        setApiKey('');
        setKeyShown(false);
        // Refresh masked key
        const keyRes = await getApiKeyStatus(selected);
        setKeyMasked(keyRes?.masked || '');
      }
      // Save endpoint if changed
      if (endpoint !== originalEndpoint) {
        await saveProviderEndpoint(selected, endpoint);
        saved.push('endpoint');
        setOriginalEndpoint(endpoint);
      }
      // Save model if changed
      if (model !== originalModel) {
        await saveProviderModel(selected, model);
        saved.push('model');
        setOriginalModel(model);
      }
      if (saved.length > 0) {
        setMsg({ kind: 'ok', text: `Saved: ${saved.join(', ')}` });
      } else {
        setMsg({ kind: 'ok', text: 'No changes to save' });
      }
    } catch (e: any) {
      setMsg({ kind: 'err', text: e?.message || 'Save failed' });
    }
    setBusy(false);
  }, [selected, apiKey, endpoint, originalEndpoint, model, originalModel, busy]);

  const handleTest = useCallback(async () => {
    if (!selected || busy) return;
    setBusy(true);
    setMsg(null);
    try {
      // If user has entered a new API key, save it first before testing
      if (apiKey.trim()) {
        setMsg({ kind: 'ok', text: 'Saving key before test...' });
        await setLlmApiKey(selected, apiKey.trim());
        setHasKey(true);
        setApiKey('');
        setKeyShown(false);
        // Refresh masked key
        const keyRes = await getApiKeyStatus(selected);
        setKeyMasked(keyRes?.masked || '');
      }
      
      // Now test with saved credentials
      const res = await testLlmProvider(selected);
      const latency = res?.latencyMs ? ` (${res.latencyMs}ms)` : '';
      const modelsInfo = res?.modelsCount ? ` | ${res.modelsCount} models` : '';
      setMsg({
        kind: res?.ok ? 'ok' : 'err',
        text: res?.ok ? `Connection OK${latency}${modelsInfo}` : (res?.error || 'Test failed')
      });
    } catch (e: any) {
      setMsg({ kind: 'err', text: e?.message || 'Test failed' });
    }
    setBusy(false);
  }, [selected, apiKey, busy]);

  const handleDelete = useCallback(async () => {
    if (!selected || busy) return;
    setBusy(true);
    setMsg(null);
    try {
      await deleteLlmApiKey(selected);
      setMsg({ kind: 'ok', text: 'API key removed' });
      setHasKey(false);
      setKeyMasked('');
      setApiKey('');
    } catch (e: any) {
      setMsg({ kind: 'err', text: e?.message || 'Delete failed' });
    }
    setBusy(false);
  }, [selected, busy]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return providers;
    return providers.filter(p => p.id.toLowerCase().includes(q) || (p.displayName || '').toLowerCase().includes(q));
  }, [providers, search]);

  const grouped = useMemo(() => {
    const configured = filtered.filter(p => p.apiKeyConfigured);
    const popular = filtered.filter(p => !p.apiKeyConfigured && p.category === 'popular');
    const other = filtered.filter(p => !p.apiKeyConfigured && p.category !== 'popular');
    return { configured, popular, other };
  }, [filtered]);

  // Get configured providers for default selection
  const configuredProviders = useMemo(() => {
    return providers.filter(p => p.apiKeyConfigured);
  }, [providers]);

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
              disabled={settingDefault || configuredProviders.length === 0}
            >
              <SelectTrigger className="flex-1 text-sm">
                <SelectValue placeholder={configuredProviders.length === 0 ? "No configured providers" : "Select default..."} />
              </SelectTrigger>
              <SelectContent>
                {configuredProviders.map(p => (
                  <SelectItem key={p.id} value={p.id}>{p.displayName || p.id}</SelectItem>
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
            <div className="mt-1 text-[10px] font-mono text-neon-cyan">
              Current: {currentDefault}
            </div>
          )}
          {configuredProviders.length === 0 && !loading && (
            <div className="mt-1 text-[10px] font-mono text-neon-gold">
              Configure a provider API key below first
            </div>
          )}
          {defaultMsg && (
            <div className={cn(
              "mt-2 text-[10px] font-mono px-2 py-1 rounded",
              defaultMsg.kind === 'ok' ? "text-neon-green bg-neon-green/10" : "text-neon-red bg-neon-red/10"
            )}>
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
                <ProviderGroup label="Configured" providers={grouped.configured} selected={selected} onSelect={loadDetail} defaultProvider={currentDefault} />
              )}
              {grouped.popular.length > 0 && (
                <ProviderGroup label="Popular" providers={grouped.popular} selected={selected} onSelect={loadDetail} />
              )}
              {grouped.other.length > 0 && (
                <ProviderGroup label="Other" providers={grouped.other} selected={selected} onSelect={loadDetail} />
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
          <div className="space-y-6">
            {/* Header */}
            <div>
              <h3 className="text-lg font-display font-semibold text-neon-cyan">{providerDetail?.displayName || selected}</h3>
              <div className="flex items-center gap-3 mt-2">
                <span className={cn(
                  "text-[10px] font-mono px-2 py-0.5 rounded",
                  hasKey ? "bg-neon-green/10 text-neon-green" : "bg-surface-elevated text-text-dimmed"
                )}>
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
                  <span className={cn(
                    "text-[9px] font-mono px-1.5 py-0.5 rounded",
                    providerDetail.endpointSource === 'secret' ? "bg-neon-gold/10 text-neon-gold" :
                    providerDetail.endpointSource === 'default' ? "bg-surface-elevated text-text-dimmed" :
                    "bg-neon-red/10 text-neon-red"
                  )}>
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
                  <span className={cn(
                    "text-[9px] font-mono px-1.5 py-0.5 rounded",
                    providerDetail.modelSource === 'secret' ? "bg-neon-gold/10 text-neon-gold" :
                    providerDetail.modelSource === 'default' ? "bg-surface-elevated text-text-dimmed" :
                    "bg-neon-red/10 text-neon-red"
                  )}>
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
                      {models.map(m => <SelectItem key={m} value={m}>{m}</SelectItem>)}
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
                <button onClick={handleFetchModels} disabled={busy || !hasKey} className="btn-ghost px-3 text-xs">
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
                  <button onClick={handleRevealKey} disabled={busy} className="btn-ghost px-3 text-xs">
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
              <div className={cn(
                "px-3 py-2 rounded-lg text-xs font-mono",
                msg.kind === 'ok' ? "bg-neon-green/10 text-neon-green border border-neon-green/30" : "bg-neon-red/10 text-neon-red border border-neon-red/30"
              )}>
                {msg.text}
              </div>
            )}

            {/* Actions */}
            <div className="flex flex-wrap gap-3 pt-2">
              <button
                onClick={handleSave}
                disabled={busy || (!apiKey.trim() && endpoint === originalEndpoint && model === originalModel)}
                className="btn-primary px-6"
              >
                {busy ? 'Saving...' : 'Save'}
              </button>
              <button onClick={handleTest} disabled={busy || !hasKey} className="btn-secondary px-4">
                Test Connection
              </button>
              {hasKey && (
                <button onClick={handleDelete} disabled={busy} className="btn-ghost px-4 text-neon-red hover:bg-neon-red/10">
                  Remove Key
                </button>
              )}
            </div>

            {/* Hint */}
            <div className="pt-2 border-t border-border-subtle">
              <p className="text-[10px] text-text-dimmed font-mono">
                Endpoint and model overrides are saved as encrypted secrets. Leave blank to use provider defaults.
              </p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

// ============================================================
//  Tab: Agents Configuration
// ============================================================

interface AgentsTabProps {
  sessionId: string | null;
  connected: boolean;
  agentRoster: Array<{ agent: string; agentId?: string }>;
  availableProviders: string[];
}

// Fallback roster for display when no roster received
const FALLBACK_AGENT_ROSTER = [
  'research_assistant', 'planner', 'reasoner', 'librarian', 'verifier', 'dag_builder', 'paper_editor'
];

const AgentsTab: React.FC<AgentsTabProps> = ({ sessionId, connected, agentRoster, availableProviders }) => {
  const [agentProviderMap, setAgentProviderMap] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [msg, setMsg] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null);

  // Effective roster: use agentRoster if available, otherwise fallback
  const effectiveRoster = useMemo(() => {
    if (agentRoster.length > 0) {
      return agentRoster;
    }
    return FALLBACK_AGENT_ROSTER.map(agent => ({ agent }));
  }, [agentRoster]);

  // Fetch current agent providers on mount
  useEffect(() => {
    if (!sessionId || !connected) return;
    const fetch = async () => {
      setLoading(true);
      try {
        const res = await getAgentProviders(sessionId) as { map?: Record<string, string> };
        if (res?.map) {
          setAgentProviderMap(res.map);
        }
      } catch { /* best-effort */ }
      setLoading(false);
    };
    fetch();
  }, [sessionId, connected]);

  // Handle setting agent provider
  const handleSetAgentProvider = useCallback(async (agent: string, providerName: string) => {
    if (!sessionId || busy) return;
    setBusy(agent);
    setMsg(null);
    try {
      const res = await setAgentProvider(sessionId, agent, providerName) as { ok?: boolean; map?: Record<string, string>; error?: string };
      if (res?.map) {
        setAgentProviderMap(res.map);
        setMsg({ kind: 'ok', text: `${agent} → ${providerName || 'default'}` });
      } else if (res?.error) {
        setMsg({ kind: 'err', text: res.error });
      }
    } catch (e: any) {
      setMsg({ kind: 'err', text: e?.message || 'Failed to set provider' });
    }
    setBusy(null);
  }, [sessionId, busy]);

  if (!sessionId) {
    return (
      <div className="card p-8 text-center">
        <div className="text-4xl mb-4">🔗</div>
        <h3 className="text-lg font-display font-semibold text-text-primary mb-2">No Session Connected</h3>
        <p className="text-sm text-text-muted">
          Connect to a session to configure per-agent LLM providers.
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Info Card */}
      <div className="card p-5">
        <h3 className="text-sm font-display font-semibold text-neon-cyan tracking-wider mb-2">
          Per-Agent Provider Configuration
        </h3>
        <p className="text-xs text-text-muted">
          Each agent can use a different LLM provider. Leave as "default" to use the global default provider.
        </p>
        {msg && (
          <div className={cn(
            "mt-3 px-3 py-2 rounded-lg text-xs font-mono",
            msg.kind === 'ok' ? "bg-neon-green/10 text-neon-green border border-neon-green/30" : "bg-neon-red/10 text-neon-red border border-neon-red/30"
          )}>
            {msg.text}
          </div>
        )}
      </div>

      {/* Agents List */}
      <div className="card overflow-hidden">
        <div className="px-4 py-3 border-b border-border-subtle bg-surface-elevated/50">
          <div className="flex items-center justify-between">
            <span className="text-xs font-mono text-text-dimmed uppercase tracking-wider">
              Agents ({effectiveRoster.length})
            </span>
            {loading && <span className="text-xs text-text-muted">Loading...</span>}
          </div>
        </div>

        <div className="divide-y divide-border-subtle">
          {effectiveRoster.map((item) => {
            const agent = item.agent;
            const currentProvider = agentProviderMap[agent] || '';
            const isUpdating = busy === agent;
            
            return (
              <div key={agent} className="px-4 py-3 hover:bg-surface-elevated/30 transition-colors">
                <div className="flex items-center justify-between gap-4">
                  {/* Agent Info */}
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <div className="size-8 rounded-lg bg-neon-cyan/10 border border-neon-cyan/30 flex items-center justify-center">
                        <span className="text-xs font-bold text-neon-cyan uppercase">
                          {agent.slice(0, 2)}
                        </span>
                      </div>
                      <div>
                        <div className="text-sm font-mono text-text-primary">{agent}</div>
                      </div>
                    </div>
                  </div>

                  {/* Provider Selector */}
                  <div className="flex items-center gap-2">
                    <Select
                      value={currentProvider || "__default__"}
                      onValueChange={(v) => handleSetAgentProvider(agent, v === "__default__" ? "" : v)}
                      disabled={isUpdating || !connected}
                    >
                      <SelectTrigger className={cn("w-48 text-sm", isUpdating && "opacity-50")}>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="__default__">default</SelectItem>
                        {availableProviders.map(p => (
                          <SelectItem key={p} value={p}>{p}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    {isUpdating && (
                      <svg className="size-4 animate-spin text-neon-cyan" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                      </svg>
                    )}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Hint */}
      <div className="text-[10px] text-text-dimmed font-mono px-1">
        Agent provider mappings are stored per-session in artifacts/ui/agent_providers.json
      </div>
    </div>
  );
};

// ============================================================
//  Tab: Advanced (Raw Secrets)
// ============================================================

const AdvancedTab: React.FC = () => {
  const [key, setKey] = useState('');
  const [value, setValue] = useState('');
  const [shown, setShown] = useState(false);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null);

  const handleSave = useCallback(async () => {
    if (!key.trim() || !value.trim() || busy) return;
    setBusy(true);
    setMsg(null);
    try {
      await setSecret(key.trim(), value.trim());
      setMsg({ kind: 'ok', text: `Saved: ${key}` });
      setKey('');
      setValue('');
    } catch (e: any) {
      setMsg({ kind: 'err', text: e?.message || 'Save failed' });
    }
    setBusy(false);
  }, [key, value, busy]);

  const handleRemove = useCallback(async () => {
    if (!key.trim() || busy) return;
    setBusy(true);
    setMsg(null);
    try {
      await removeSecret(key.trim());
      setMsg({ kind: 'ok', text: `Removed: ${key}` });
      setKey('');
      setValue('');
    } catch (e: any) {
      setMsg({ kind: 'err', text: e?.message || 'Remove failed' });
    }
    setBusy(false);
  }, [key, busy]);

  return (
    <div className="max-w-2xl">
      <div className="card p-6 space-y-6">
        <div>
          <h3 className="text-lg font-display font-semibold text-neon-gold">Advanced Settings</h3>
          <p className="text-xs text-text-muted mt-1">
            Directly manage encrypted user secrets. Use with caution.
          </p>
        </div>

        <div>
          <label className="text-xs font-mono text-text-muted block mb-1.5">Secret Key</label>
          <input
            type="text"
            value={key}
            onChange={(e) => setKey(e.target.value)}
            placeholder="e.g. OpenAI:ApiKey or SkillsMP:ApiKey"
            className="input w-full font-mono"
          />
        </div>

        <div>
          <label className="text-xs font-mono text-text-muted block mb-1.5">Secret Value</label>
          <div className="flex gap-2">
            <input
              type={shown ? 'text' : 'password'}
              value={value}
              onChange={(e) => setValue(e.target.value)}
              placeholder="Enter secret value..."
              className="input flex-1 font-mono"
            />
            <button onClick={() => setShown(!shown)} className="btn-ghost px-3 text-xs">
              {shown ? 'Hide' : 'Show'}
            </button>
          </div>
        </div>

        {msg && (
          <div className={cn(
            "px-3 py-2 rounded-lg text-xs font-mono",
            msg.kind === 'ok' ? "bg-neon-green/10 text-neon-green border border-neon-green/30" : "bg-neon-red/10 text-neon-red border border-neon-red/30"
          )}>
            {msg.text}
          </div>
        )}

        <div className="flex gap-3">
          <button onClick={handleSave} disabled={busy || !key.trim() || !value.trim()} className="btn-primary px-6">
            Save Secret
          </button>
          <button onClick={handleRemove} disabled={busy || !key.trim()} className="btn-ghost px-4 text-neon-red hover:bg-neon-red/10">
            Remove Secret
          </button>
        </div>

        <div className="pt-4 border-t border-border-subtle">
          <p className="text-[10px] text-text-dimmed font-mono">
            Secrets are stored encrypted in user-specific storage. Keys follow pattern: Provider:Type (e.g. OpenAI:ApiKey, Claude:Endpoint).
          </p>
        </div>
      </div>
    </div>
  );
};

// ============================================================
//  Helper Components
// ============================================================

const StatBox: React.FC<{ label: string; value: number; accent?: 'cyan' | 'gold' }> = ({ label, value, accent }) => (
  <div className="text-center">
    <div className={cn(
      "text-2xl font-mono font-bold tabular-nums",
      accent === 'cyan' ? 'text-neon-cyan' : accent === 'gold' ? 'text-neon-gold' : 'text-text-primary'
    )}>
      {value}
    </div>
    <div className="text-[10px] text-text-dimmed uppercase tracking-wider">{label}</div>
  </div>
);

const ProviderGroup: React.FC<{
  label: string;
  providers: LlmProvider[];
  selected: string | null;
  onSelect: (name: string) => void;
  defaultProvider?: string;
}> = ({ label, providers, selected, onSelect, defaultProvider }) => (
  <div>
    <div className="px-3 py-1.5 text-[10px] font-mono text-text-dimmed uppercase tracking-wider bg-surface-elevated">
      {label} ({providers.length})
    </div>
    {providers.map(p => (
      <button
        key={p.id}
        onClick={() => onSelect(p.id)}
        className={cn(
          "w-full px-3 py-2.5 text-left transition-colors",
          selected === p.id ? "bg-neon-cyan/10 border-l-2 border-neon-cyan" : "hover:bg-surface-elevated"
        )}
      >
        <div className="flex items-center gap-2">
          <span className="text-sm font-mono text-text-primary">{p.displayName || p.id}</span>
          {p.id === defaultProvider && (
            <span className="text-[9px] font-mono text-neon-gold bg-neon-gold/10 px-1.5 py-0.5 rounded">default</span>
          )}
          {p.apiKeyConfigured && (
            <span className="text-[9px] font-mono text-neon-green">✓</span>
          )}
        </div>
      </button>
    ))}
  </div>
);

export default SettingsPanel;
