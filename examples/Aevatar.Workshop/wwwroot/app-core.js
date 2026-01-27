// ============================================================
//  app-core.js
//
//  中文 + ASCII:
//  - UI 全局状态 + 基础工具函数
//  - Markdown 渲染 + 通用辅助方法
// ============================================================

const state = {
  sessions: [],
  currentSessionId: null,
  currentView: 'sessions',
  sse: null,
  messageMap: new Map(),
  streamBuffer: new Map(),
  streamFlushHandle: null,
  autoScroll: true,
  providers: [],
  defaultProvider: '',
  tools: [],
  dotnetFiles: [],
  mcpServers: [],
  toolFilter: 'all',
  toolSearch: '',
  selectedTools: new Set(),
  graph: {
    stage: null,
    scale: 1,
    offsetX: 0,
    offsetY: 0,
    isPanning: false,
    startX: 0,
    startY: 0,
    bound: false,
  },
};

const roleState = {
  roles: [],
  instances: [],
  edges: [],
  rootRole: 'sisyphus',
  sse: null,
  messageMap: new Map(),
  streamBuffer: new Map(),
  streamFlushHandle: null,
  autoScroll: true,
  selectedParent: '',
  selectedChild: '',
  graph: {
    stage: null,
    scale: 1,
    offsetX: 0,
    offsetY: 0,
    isPanning: false,
    startX: 0,
    startY: 0,
    bound: false,
  },
};

const el = {
  status: document.getElementById('status'),
  sessionList: document.getElementById('session-list'),
  currentSession: document.getElementById('current-session'),
  newSession: document.getElementById('new-session'),
  agentMode: document.getElementById('agent-mode'),
  messages: document.getElementById('messages'),
  composer: document.getElementById('composer'),
  input: document.getElementById('input'),
  streamChunkEvery: document.getElementById('stream-chunk-every'),
  events: document.getElementById('events'),
  historyList: document.getElementById('history-list'),
  historySummary: document.getElementById('history-summary'),
  contextKeys: document.getElementById('context-keys'),
  memoryAgent: document.getElementById('memory-agent'),
  memorySession: document.getElementById('memory-session'),
  memoryAgentId: document.getElementById('agent-memory-id'),
  memorySessionId: document.getElementById('session-memory-id'),
  handlersList: document.getElementById('handlers-list'),
  modulesList: document.getElementById('modules-list'),
  pingInput: document.getElementById('ping-input'),
  pingButton: document.getElementById('ping-button'),
  pingLog: document.getElementById('ping-log'),
  toggleHistory: document.getElementById('toggle-history'),
  toggleCompaction: document.getElementById('toggle-compaction'),
  toggleMemory: document.getElementById('toggle-memory'),
  toggleSessionMemory: document.getElementById('toggle-session-memory'),
  toggleVector: document.getElementById('toggle-vector'),
  toggleMcp: document.getElementById('toggle-mcp'),
  toggleSkills: document.getElementById('toggle-skills'),
  toggleDangerous: document.getElementById('toggle-dangerous'),
  toggleInternal: document.getElementById('toggle-internal'),
  settingsSave: document.getElementById('settings-save'),
  settingsStatus: document.getElementById('settings-status'),
  agentYaml: document.getElementById('agent-yaml'),
  agentYamlSave: document.getElementById('agent-yaml-save'),
  agentYamlLoad: document.getElementById('agent-yaml-load'),
  agentYamlRole: document.getElementById('agent-yaml-role'),
  agentYamlStatus: document.getElementById('agent-yaml-status'),
  workflowYaml: document.getElementById('workflow-yaml'),
  workflowName: document.getElementById('workflow-name'),
  workflowRun: document.getElementById('workflow-run'),
  workflowStatus: document.getElementById('workflow-status'),
  workflowGraph: document.getElementById('workflow-graph'),
  providerSelect: document.getElementById('provider-select'),
  providerDefault: document.getElementById('provider-default'),
  providerApiKey: document.getElementById('provider-api-key'),
  providerSave: document.getElementById('provider-save'),
  providerClear: document.getElementById('provider-clear'),
  providerStatus: document.getElementById('provider-status'),
  toolSearch: document.getElementById('tool-search'),
  toolFilters: document.getElementById('tool-filters'),
  toolRefresh: document.getElementById('tool-refresh'),
  toolList: document.getElementById('tool-list'),
  dotnetList: document.getElementById('dotnet-list'),
  mcpList: document.getElementById('mcp-list'),
  selectedTools: document.getElementById('selected-tools'),
  topNav: document.getElementById('top-nav'),
  views: document.querySelectorAll('.view'),
  navButtons: document.querySelectorAll('.nav-button'),
  roleSelect: document.getElementById('role-select'),
  roleRefresh: document.getElementById('role-refresh'),
  roleYaml: document.getElementById('role-yaml'),
  roleYamlSave: document.getElementById('role-yaml-save'),
  roleInstantiate: document.getElementById('role-instantiate'),
  roleLinkRoot: document.getElementById('role-link-root'),
  roleYamlStatus: document.getElementById('role-yaml-status'),
  roleList: document.getElementById('role-list'),
  roleParentLabel: document.getElementById('role-parent-label'),
  roleChildLabel: document.getElementById('role-child-label'),
  roleLink: document.getElementById('role-link'),
  roleUnlink: document.getElementById('role-unlink'),
  roleGraph: document.getElementById('role-graph'),
  roleMessages: document.getElementById('role-messages'),
  roleComposer: document.getElementById('role-composer'),
  roleInput: document.getElementById('role-input'),
  roleStreamChunkEvery: document.getElementById('role-stream-chunk-every'),
  delegationLog: document.getElementById('delegation-log'),
};

const sampleWorkflowYaml = `dsl_version: "0.1"
goal:
  name: "Workshop Demo Workflow"
  success_metric: "balanced plan with critiques"
strategy: cot
budget:
  max_steps: 6
  token_limit: 4000
nodes:
  - id: planner
    type: workshop_default
  - id: critic
    type: workshop_default
  - id: synthesizer
    type: workshop_default
  - id: verifier
    type: workshop_default
edges:
  - from: planner
    to: critic
    channel: question
  - from: critic
    to: synthesizer
    channel: upstream_output
  - from: synthesizer
    to: verifier
    channel: upstream_output
constraints:
  - type: max_iterations
    value: 3
`;

const sampleAgentYaml = `id: "workshop_custom"
name: "Workshop Custom Agent"
version: "1.0"
provider: "default"
tools: []
system_prompt: |
  You are a focused assistant for Aevatar.Workshop.
extensions:
  event_modules: "workshop_chat_trace, workshop_ping"
  event_routes: |
    - when: event.type == "aevatar.agents.ai.core.ChatRequestEvent"
      to: workshop_chat_trace
    - when: event.type == "aevatar.agents.ai.core.ChatResponseEvent"
      to: workshop_chat_trace
    - when: event.type == "aevatar.workshop.WorkshopPingEvent"
      to: workshop_ping
`;

function setStatus(text, kind = 'info') {
  el.status.textContent = text;
  el.status.classList.toggle('error', kind === 'error');
}

function fetchJson(url, options) {
  return fetch(url, options).then(async (res) => {
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      const message = data.error || res.statusText;
      throw new Error(message);
    }
    return data;
  });
}

// ============================================================
//  AG-UI Event Stream (SSE with auto-reconnect)
//
//  中文 + ASCII:
//  - 对齐 VibeResearching 的 stream 体验：断线自动重连
//  - "connected" 以首个事件为准（避免 onopen 假阳性）
// ============================================================
function createAgUiStream(url, handlers = {}) {
  let es = null;
  let closed = false;
  let attempts = 0;
  let hasEvent = false;
  const maxAttempts = handlers.maxAttempts ?? 5;
  const baseDelay = handlers.reconnectDelayMs ?? 1200;

  const notifyStatus = (status) => {
    if (typeof handlers.onStatus === 'function') {
      handlers.onStatus(status);
    }
  };

  const connect = () => {
    if (closed) return;
    es = new EventSource(url);

    es.onopen = () => {
      // Wait for first event to mark connected.
      notifyStatus('connecting');
    };

    es.onmessage = (evt) => {
      if (!hasEvent) {
        hasEvent = true;
        attempts = 0;
        notifyStatus('connected');
      }
      if (typeof handlers.onMessage === 'function') {
        handlers.onMessage(evt);
      }
    };

    es.onerror = () => {
      if (closed) return;
      try { es.close(); } catch {}

      if (!hasEvent) {
        notifyStatus('error');
      } else {
        notifyStatus('reconnecting');
      }

      attempts += 1;
      if (maxAttempts > 0 && attempts > maxAttempts) {
        notifyStatus('error');
        return;
      }

      const delay = Math.min(8000, baseDelay * attempts);
      setTimeout(connect, delay);
    };
  };

  connect();

  return {
    close: () => {
      closed = true;
      try { es?.close(); } catch {}
    }
  };
}

function escapeHtml(value) {
  const div = document.createElement('div');
  div.textContent = value ?? '';
  return div.innerHTML;
}

function hexToRgba(hex, alpha) {
  const cleaned = (hex || '').replace('#', '');
  if (cleaned.length !== 6) return `rgba(125, 211, 252, ${alpha})`;
  const r = parseInt(cleaned.slice(0, 2), 16);
  const g = parseInt(cleaned.slice(2, 4), 16);
  const b = parseInt(cleaned.slice(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

function formatTimestamp(value) {
  if (!value) return '-';
  const dt = new Date(value);
  if (Number.isNaN(dt.getTime())) return String(value);
  return dt.toLocaleTimeString();
}

function clampNumber(value, min, max) {
  if (Number.isNaN(value)) return min;
  return Math.min(max, Math.max(min, value));
}

function getStreamChunkEveryN() {
  if (!el.streamChunkEvery) return 0;
  const raw = Number.parseInt(el.streamChunkEvery.value, 10);
  const next = clampNumber(raw || 0, 1, 64);
  if (String(next) !== el.streamChunkEvery.value) {
    el.streamChunkEvery.value = String(next);
  }
  return next;
}

function getRoleStreamChunkEveryN() {
  if (!el.roleStreamChunkEvery) return 0;
  const raw = Number.parseInt(el.roleStreamChunkEvery.value, 10);
  const next = clampNumber(raw || 0, 1, 64);
  if (String(next) !== el.roleStreamChunkEvery.value) {
    el.roleStreamChunkEvery.value = String(next);
  }
  return next;
}

function isNearBottom(container, threshold = 48) {
  if (!container) return true;
  const distance = container.scrollHeight - container.scrollTop - container.clientHeight;
  return distance <= threshold;
}

function scrollMessagesToBottom(container = el.messages) {
  if (!container) return;
  container.scrollTop = container.scrollHeight;
}

function renderInlineMarkdown(text) {
  let html = escapeHtml(text || '');
  html = html.replace(/`([^`]+)`/g, '<code>$1</code>');
  html = html.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
  html = html.replace(/(^|[^*])\*([^*]+)\*/g, '$1<em>$2</em>');
  html = html.replace(/\[([^\]]+)\]\(([^)]+)\)/g,
    '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');
  return html;
}

function renderMarkdownBlocks(text) {
  const lines = (text || '').replace(/\r/g, '').split('\n');
  let html = '';
  let inUl = false;
  let inOl = false;

  const closeLists = () => {
    if (inUl) {
      html += '</ul>';
      inUl = false;
    }
    if (inOl) {
      html += '</ol>';
      inOl = false;
    }
  };

  lines.forEach((line) => {
    if (!line || /^\s*$/.test(line)) {
      closeLists();
      html += '<br>';
      return;
    }

    if (/^\s*[-*]\s+/.test(line)) {
      if (!inUl) {
        closeLists();
        html += '<ul>';
        inUl = true;
      }
      html += `<li>${renderInlineMarkdown(line.replace(/^\s*[-*]\s+/, ''))}</li>`;
      return;
    }

    if (/^\s*\d+\.\s+/.test(line)) {
      if (!inOl) {
        closeLists();
        html += '<ol>';
        inOl = true;
      }
      html += `<li>${renderInlineMarkdown(line.replace(/^\s*\d+\.\s+/, ''))}</li>`;
      return;
    }

    closeLists();
    if (/^\s*###\s+/.test(line)) {
      html += `<h3>${renderInlineMarkdown(line.replace(/^\s*###\s+/, ''))}</h3>`;
      return;
    }
    if (/^\s*##\s+/.test(line)) {
      html += `<h2>${renderInlineMarkdown(line.replace(/^\s*##\s+/, ''))}</h2>`;
      return;
    }
    if (/^\s*#\s+/.test(line)) {
      html += `<h1>${renderInlineMarkdown(line.replace(/^\s*#\s+/, ''))}</h1>`;
      return;
    }
    if (/^\s*>\s+/.test(line)) {
      html += `<blockquote>${renderInlineMarkdown(line.replace(/^\s*>\s+/, ''))}</blockquote>`;
      return;
    }

    html += `<p>${renderInlineMarkdown(line)}</p>`;
  });

  closeLists();
  return html;
}

function renderMarkdown(text) {
  if (!text) return '';
  const parts = text.split(/```/);
  return parts.map((part, idx) => {
    if (idx % 2 === 1) {
      return `<pre><code>${escapeHtml(part)}</code></pre>`;
    }
    return renderMarkdownBlocks(part);
  }).join('');
}

function renderMessage(item, finalize = false) {
  if (!item) return;
  if (finalize) {
    item.content.classList.add('markdown');
    item.content.classList.remove('streaming');
    item.content.innerHTML = renderMarkdown(item.text);
  } else {
    item.content.classList.add('streaming');
    item.content.classList.remove('markdown');
    item.content.textContent = item.text;
  }
}

function debounce(fn, delay = 250) {
  let timer = null;
  return (...args) => {
    if (timer) clearTimeout(timer);
    timer = setTimeout(() => fn(...args), delay);
  };
}
