// ============================================================
//  config.js
//
//  中文 + ASCII:
//  - LLM Provider Config UI (Workshop)
//  - Reuse layout semantics from VibeResearching providers tab
// ============================================================

const configState = {
  providers: [],
  defaultProvider: '',
  selected: '',
  maskedKey: '',
  revealedKey: '',
  keyShown: false,
};

const cfg = {
  status: document.getElementById('config-status'),
  list: document.getElementById('provider-list'),
  detail: document.getElementById('provider-detail'),
  search: document.getElementById('provider-search'),
  refresh: document.getElementById('provider-refresh'),
};

function setConfigStatus(text, kind = 'info') {
  if (!cfg.status) return;
  cfg.status.textContent = text || '';
  cfg.status.className = `config-status ${kind}`;
}

async function loadProviders() {
  try {
    setConfigStatus('Loading providers…');
    const data = await fetchJson('/api/llm/providers');
    configState.providers = data.providers || [];
    configState.defaultProvider = data.defaultProvider || '';
    if (!configState.selected || !configState.providers.find((p) => p.name === configState.selected)) {
      configState.selected = configState.defaultProvider || (configState.providers[0]?.name ?? '');
    }
    await loadApiKey(configState.selected, false);
    renderProviderList();
    renderProviderDetail();
    setConfigStatus('');
  } catch (err) {
    setConfigStatus(`Failed to load providers: ${err?.message || String(err)}`, 'error');
  }
}

function renderProviderList() {
  if (!cfg.list) return;
  cfg.list.innerHTML = '';
  const search = (cfg.search?.value || '').trim().toLowerCase();
  const list = configState.providers.filter((p) => !search || p.name.toLowerCase().includes(search));

  if (list.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'panel-subtitle';
    empty.textContent = 'No providers found.';
    cfg.list.appendChild(empty);
    return;
  }

  list.forEach((provider) => {
    const item = document.createElement('button');
    item.type = 'button';
    item.className = 'provider-item';
    if (provider.name === configState.selected) {
      item.classList.add('active');
    }

    const tags = [];
    if (provider.name === configState.defaultProvider) tags.push('default');
    tags.push(provider.hasApiKey ? 'key' : 'no-key');

    item.innerHTML = `
      <div class="provider-title">${escapeHtml(provider.name)}</div>
      <div class="provider-meta">
        <span>${escapeHtml(provider.providerType || '')}</span>
        <span>${tags.join(' · ')}</span>
      </div>
    `;
    item.addEventListener('click', async () => {
      configState.selected = provider.name;
      await loadApiKey(provider.name, false);
      renderProviderList();
      renderProviderDetail();
    });
    cfg.list.appendChild(item);
  });
}

async function loadApiKey(providerName, reveal) {
  if (!providerName) return;
  try {
    const url = reveal
      ? `/api/llm/api-key/${encodeURIComponent(providerName)}?reveal=true`
      : `/api/llm/api-key/${encodeURIComponent(providerName)}`;
    const res = await fetchJson(url);
    configState.maskedKey = res.masked || '';
    configState.revealedKey = res.value || '';
    configState.keyShown = Boolean(reveal && res.value);
  } catch (err) {
    configState.maskedKey = '';
    configState.revealedKey = '';
    configState.keyShown = false;
    setConfigStatus(`Failed to load api key: ${err?.message || String(err)}`, 'error');
  }
}

function renderProviderDetail() {
  if (!cfg.detail) return;
  const provider = configState.providers.find((p) => p.name === configState.selected);
  if (!provider) {
    cfg.detail.innerHTML = '<div class="panel-subtitle">Select a provider to configure.</div>';
    return;
  }

  const keyValue = configState.keyShown
    ? configState.revealedKey
    : configState.maskedKey || '(not set)';

  cfg.detail.innerHTML = `
    <div class="provider-header">
      <div>
        <div class="provider-title">${escapeHtml(provider.name)}</div>
        <div class="provider-meta">Type: ${escapeHtml(provider.providerType || '-')}</div>
      </div>
      <button id="provider-set-default" class="btn btn-primary" type="button" ${provider.name === configState.defaultProvider ? 'disabled' : ''}>
        ${provider.name === configState.defaultProvider ? 'Default' : 'Set Default'}
      </button>
    </div>

    <div class="provider-grid">
      <div class="provider-row"><span>Model</span><span>${escapeHtml(provider.model || '-')}</span></div>
      <div class="provider-row"><span>Endpoint</span><span>${escapeHtml(provider.endpoint || '-')}</span></div>
      <div class="provider-row"><span>Streaming</span><span>${provider.enableStreaming ? 'enabled' : 'disabled'}</span></div>
    </div>

    <div class="provider-section">
      <div class="section-title">API Key</div>
      <div class="inline-form">
        <input id="provider-key-input" class="input" placeholder="Paste API key" />
        <button id="provider-key-save" class="btn btn-primary" type="button">Save</button>
        <button id="provider-key-clear" class="btn btn-outline" type="button">Clear</button>
      </div>
      <div class="inline-form">
        <input class="input" readonly value="${escapeHtml(keyValue)}" />
        <button id="provider-key-toggle" class="btn btn-outline" type="button">${configState.keyShown ? 'Hide' : 'Show'}</button>
      </div>
    </div>
  `;

  const setDefaultBtn = document.getElementById('provider-set-default');
  const saveBtn = document.getElementById('provider-key-save');
  const clearBtn = document.getElementById('provider-key-clear');
  const toggleBtn = document.getElementById('provider-key-toggle');
  const input = document.getElementById('provider-key-input');

  if (setDefaultBtn) {
    setDefaultBtn.addEventListener('click', async () => {
      await setDefaultProvider(provider.name);
    });
  }
  if (saveBtn) {
    saveBtn.addEventListener('click', async () => {
      const value = input?.value?.trim() || '';
      if (!value) return;
      await saveApiKey(provider.name, value);
      if (input) input.value = '';
    });
  }
  if (clearBtn) {
    clearBtn.addEventListener('click', async () => {
      await clearApiKey(provider.name);
    });
  }
  if (toggleBtn) {
    toggleBtn.addEventListener('click', async () => {
      await loadApiKey(provider.name, !configState.keyShown);
      renderProviderDetail();
    });
  }
}

async function setDefaultProvider(providerName) {
  try {
    await fetchJson('/api/llm/default', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ providerName }),
    });
    configState.defaultProvider = providerName;
    renderProviderList();
    renderProviderDetail();
  } catch (err) {
    setConfigStatus(`Failed to set default: ${err?.message || String(err)}`, 'error');
  }
}

async function saveApiKey(providerName, apiKey) {
  try {
    await fetchJson('/api/llm/api-key', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ providerName, apiKey }),
    });
    await loadApiKey(providerName, false);
    await loadProviders();
  } catch (err) {
    setConfigStatus(`Failed to save api key: ${err?.message || String(err)}`, 'error');
  }
}

async function clearApiKey(providerName) {
  try {
    await fetchJson(`/api/llm/api-key/${encodeURIComponent(providerName)}`, {
      method: 'DELETE',
    });
    await loadApiKey(providerName, false);
    await loadProviders();
  } catch (err) {
    setConfigStatus(`Failed to clear api key: ${err?.message || String(err)}`, 'error');
  }
}

if (cfg.search) {
  cfg.search.addEventListener('input', () => renderProviderList());
}
if (cfg.refresh) {
  cfg.refresh.addEventListener('click', () => loadProviders());
}

loadProviders();
