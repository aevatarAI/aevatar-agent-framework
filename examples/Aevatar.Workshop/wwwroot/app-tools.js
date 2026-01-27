// ============================================================
//  app-tools.js
//
//  中文 + ASCII:
//  - Tool Catalog / MCP / Provider 管理
//  - Agent YAML Builder
// ============================================================

function parseYamlList(yaml, key) {
  const lines = (yaml || '').replace(/\r/g, '').split('\n');
  const idx = lines.findIndex((line) => line.trim().startsWith(`${key}:`));
  if (idx < 0) return [];
  const items = [];
  for (let i = idx + 1; i < lines.length; i += 1) {
    const line = lines[i];
    if (!/^\s*-\s+/.test(line)) break;
    const raw = line.replace(/^\s*-\s+/, '').trim();
    items.push(raw.replace(/^["']|["']$/g, ''));
  }
  return items.filter(Boolean);
}

function updateYamlList(yaml, key, items) {
  const lines = (yaml || '').replace(/\r/g, '').split('\n');
  let idx = lines.findIndex((line) => line.trim().startsWith(`${key}:`));
  if (idx < 0) {
    lines.push(`${key}:`);
    idx = lines.length - 1;
  }
  let end = idx + 1;
  while (end < lines.length && /^\s*-\s+/.test(lines[end])) {
    end += 1;
  }
  const next = items.map((item) => `  - "${item}"`);
  lines.splice(idx + 1, end - idx - 1, ...next);
  return lines.join('\n').trimEnd();
}

function updateYamlScalar(yaml, key, value) {
  const lines = (yaml || '').replace(/\r/g, '').split('\n');
  const idx = lines.findIndex((line) => line.trim().startsWith(`${key}:`));
  const line = `${key}: "${value}"`;
  if (idx < 0) {
    lines.push(line);
  } else {
    lines[idx] = line;
  }
  return lines.join('\n').trimEnd();
}

function renderSelectedTools() {
  if (!el.selectedTools) return;
  el.selectedTools.innerHTML = '';
  const items = Array.from(state.selectedTools.values()).sort((a, b) => a.localeCompare(b));
  if (items.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'panel-subtitle';
    empty.textContent = 'Drop tools here to pin them into YAML';
    el.selectedTools.appendChild(empty);
    return;
  }
  items.forEach((name) => {
    const pill = document.createElement('div');
    pill.className = 'tool-pill';
    pill.innerHTML = `<span>${escapeHtml(name)}</span>`;
    const remove = document.createElement('button');
    remove.type = 'button';
    remove.textContent = '×';
    remove.addEventListener('click', () => {
      state.selectedTools.delete(name);
      el.agentYaml.value = updateYamlList(el.agentYaml.value, 'tools', Array.from(state.selectedTools));
      renderSelectedTools();
    });
    pill.appendChild(remove);
    el.selectedTools.appendChild(pill);
  });
}

function syncSelectedToolsFromYaml() {
  if (!el.agentYaml) return;
  const list = parseYamlList(el.agentYaml.value, 'tools');
  state.selectedTools = new Set(list);
  renderSelectedTools();
}

function syncProviderFromYaml() {
  if (!el.agentYaml || !el.providerSelect) return;
  const lines = el.agentYaml.value.split('\n');
  const match = lines.find((line) => line.trim().startsWith('provider:'));
  if (!match) return;
  const value = match.split(':').slice(1).join(':').trim().replace(/^["']|["']$/g, '');
  if (value) {
    el.providerSelect.value = value;
  }
}

function addToolToSelection(name) {
  if (!name) return;
  state.selectedTools.add(name);
  el.agentYaml.value = updateYamlList(el.agentYaml.value, 'tools', Array.from(state.selectedTools));
  renderSelectedTools();
}

function attachDragHandlers(node, payload) {
  node.setAttribute('draggable', 'true');
  node.addEventListener('dragstart', (e) => {
    e.dataTransfer.setData('application/json', JSON.stringify(payload));
  });
}

function enableDropZone(zone) {
  if (!zone) return;
  zone.addEventListener('dragover', (e) => {
    e.preventDefault();
    zone.classList.add('is-over');
  });
  zone.addEventListener('dragleave', () => zone.classList.remove('is-over'));
  zone.addEventListener('drop', async (e) => {
    e.preventDefault();
    zone.classList.remove('is-over');
    const raw = e.dataTransfer.getData('application/json');
    if (!raw) return;
    let payload;
    try {
      payload = JSON.parse(raw);
    } catch {
      return;
    }
    if (payload?.kind === 'dotnet-file' && payload.filePath) {
      const tool = await registerDotNetTool(payload.filePath);
      if (tool?.name) addToolToSelection(tool.name);
      return;
    }
    if (payload?.name) addToolToSelection(payload.name);
  });
}

function renderToolList(list) {
  if (!el.toolList) return;
  el.toolList.innerHTML = '';
  if (!list || list.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'panel-subtitle';
    empty.textContent = 'No tools available for this session.';
    el.toolList.appendChild(empty);
    return;
  }
  list.forEach((tool) => {
    const row = document.createElement('div');
    row.className = 'tool-item';
    const meta = document.createElement('div');
    meta.innerHTML = `
      <div class="tool-title">${escapeHtml(tool.name)}</div>
      <div class="tool-meta">${escapeHtml(tool.source || '')}${tool.isDangerous ? ' · dangerous' : ''}</div>
    `;
    const add = document.createElement('button');
    add.type = 'button';
    add.className = 'btn btn-outline';
    add.textContent = 'Add';
    add.addEventListener('click', () => addToolToSelection(tool.name));
    row.appendChild(meta);
    row.appendChild(add);
    attachDragHandlers(row, { kind: 'tool', name: tool.name });
    el.toolList.appendChild(row);
  });
}

function renderDotNetList(list) {
  if (!el.dotnetList) return;
  el.dotnetList.innerHTML = '';
  if (!list || list.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'panel-subtitle';
    empty.textContent = 'No dotnet file tools found in configured directories.';
    el.dotnetList.appendChild(empty);
    return;
  }
  list.forEach((tool) => {
    const row = document.createElement('div');
    row.className = 'tool-item';
    const meta = document.createElement('div');
    meta.innerHTML = `
      <div class="tool-title">${escapeHtml(tool.name)}</div>
      <div class="tool-meta">${escapeHtml(tool.filePath)}</div>
    `;
    const add = document.createElement('button');
    add.type = 'button';
    add.className = 'btn btn-outline';
    add.textContent = 'Add';
    add.addEventListener('click', async () => {
      const registered = await registerDotNetTool(tool.filePath);
      if (registered?.name) addToolToSelection(registered.name);
    });
    row.appendChild(meta);
    row.appendChild(add);
    attachDragHandlers(row, { kind: 'dotnet-file', name: tool.name, filePath: tool.filePath });
    el.dotnetList.appendChild(row);
  });
}

function renderMcpServers(list) {
  if (!el.mcpList) return;
  el.mcpList.innerHTML = '';
  if (!list || list.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'panel-subtitle';
    empty.textContent = 'No MCP servers configured.';
    el.mcpList.appendChild(empty);
    return;
  }
  list.forEach((server) => {
    const row = document.createElement('div');
    row.className = 'tool-item';
    row.innerHTML = `
      <div>
        <div class="tool-title">${escapeHtml(server.name || server.key)}</div>
        <div class="tool-meta">${escapeHtml(server.transport || '')}</div>
      </div>
      <span class="tool-tag">${escapeHtml(server.key)}</span>
    `;
    el.mcpList.appendChild(row);
  });
}

async function loadProviders() {
  if (!el.providerSelect) return;
  const data = await fetchJson('/api/llm/providers');
  state.providers = data.providers || [];
  state.defaultProvider = data.defaultProvider || '';
  el.providerSelect.innerHTML = '';
  state.providers.forEach((p) => {
    const opt = document.createElement('option');
    opt.value = p.name;
    opt.textContent = p.name + (p.name === state.defaultProvider ? ' (default)' : '');
    el.providerSelect.appendChild(opt);
  });
  if (state.providers.length === 0) {
    const opt = document.createElement('option');
    opt.value = '';
    opt.textContent = 'No providers configured';
    el.providerSelect.appendChild(opt);
  }
  updateProviderStatus();
}

async function saveProviderKey() {
  if (!el.providerSelect?.value) return;
  const apiKey = el.providerApiKey.value || '';
  await fetchJson('/api/llm/api-key', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ providerName: el.providerSelect.value, apiKey }),
  });
  el.providerApiKey.value = '';
  await loadProviders();
}

async function clearProviderKey() {
  if (!el.providerSelect?.value) return;
  await fetchJson('/api/llm/api-key', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ providerName: el.providerSelect.value, apiKey: '' }),
  });
  el.providerApiKey.value = '';
  await loadProviders();
}

async function setDefaultProvider() {
  if (!el.providerSelect?.value) return;
  await fetchJson('/api/llm/default-provider', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ providerName: el.providerSelect.value }),
  });
  await loadProviders();
}

async function loadMcpServers() {
  const data = await fetchJson('/api/mcp/servers');
  state.mcpServers = data.servers || [];
  renderMcpServers(state.mcpServers);
}

async function loadToolCatalog() {
  if (!state.currentSessionId || !el.toolList) return;
  const data = await fetchJson(`/api/tools/catalog?sessionId=${encodeURIComponent(state.currentSessionId)}`);
  state.tools = data.tools || [];
  renderToolCatalog();
}

async function loadDotNetFiles() {
  if (!el.dotnetList) return;
  const data = await fetchJson('/api/tools/dotnet');
  state.dotnetFiles = data.tools || [];
  renderDotNetList(state.dotnetFiles);
}

async function registerDotNetTool(filePath) {
  if (!state.currentSessionId) return null;
  const data = await fetchJson('/api/tools/dotnet/register', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sessionId: state.currentSessionId, filePath }),
  });
  await loadToolCatalog();
  return data.tool || null;
}

function renderToolCatalog() {
  const filter = state.toolFilter;
  const search = state.toolSearch.trim().toLowerCase();
  let list = state.tools || [];
  if (filter && filter !== 'all') {
    list = list.filter((t) => String(t.source || '').toLowerCase() === filter.toLowerCase());
  }
  if (search) {
    list = list.filter((t) =>
      (t.name || '').toLowerCase().includes(search) ||
      (t.description || '').toLowerCase().includes(search));
  }
  renderToolList(list);
}

function refreshAgentYamlPanel() {
  if (!el.providerSelect) return;
  loadProviders();
  loadMcpServers();
  loadToolCatalog();
  loadDotNetFiles();
  syncSelectedToolsFromYaml();
}

function updateProviderStatus() {
  if (!el.providerStatus) return;
  const provider = el.providerSelect?.value || '';
  if (!provider) {
    el.providerStatus.textContent = 'No provider configured.';
    return;
  }
  if (provider === state.defaultProvider) {
    el.providerStatus.textContent = `Default provider: ${provider}`;
    return;
  }
  el.providerStatus.textContent = `Selected provider: ${provider}`;
}

async function loadAgentYaml(role) {
  if (!role) return;
  const data = await fetchJson(`/api/agent/yaml?role=${encodeURIComponent(role)}`);
  el.agentYaml.value = data.yaml || '';
  el.agentYamlStatus.textContent = data.path ? `Loaded from ${data.path}` : 'No YAML found.';
  syncSelectedToolsFromYaml();
  syncProviderFromYaml();
}

async function saveAgentYaml() {
  const yaml = el.agentYaml.value.trim();
  if (!yaml) return;
  const data = await fetchJson('/api/agent/yaml', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ yaml, createSession: true }),
  });
  el.agentYamlStatus.textContent = `Saved ${data.role}`;
  await fetchSessions();
  if (data.sessionId) {
    selectSession(data.sessionId);
  }
}
