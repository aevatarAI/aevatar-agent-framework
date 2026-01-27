// ============================================================
//  app-sessions.js
//
//  中文 + ASCII:
//  - Session 列表 + SSE + Chat 流
//  - History / Memory / Handlers / Settings
// ============================================================

function appendDeltaImmediate(messageId, role, delta) {
  const existing = ensureMessage(messageId, role);
  existing.text += delta;
  renderMessage(existing, false);
}

function flushMessageBuffer(messageId) {
  const entry = state.streamBuffer.get(messageId);
  if (!entry || !entry.delta) return;
  appendDeltaImmediate(messageId, entry.role, entry.delta);
  state.streamBuffer.delete(messageId);
}

function flushStreamBuffer() {
  state.streamFlushHandle = null;
  if (state.streamBuffer.size === 0) return;
  const shouldScroll = state.autoScroll;
  const pending = Array.from(state.streamBuffer.entries());
  state.streamBuffer.clear();
  pending.forEach(([messageId, entry]) => {
    if (!entry.delta) return;
    appendDeltaImmediate(messageId, entry.role, entry.delta);
  });
  if (shouldScroll) scrollMessagesToBottom();
}

function scheduleStreamFlush() {
  if (state.streamFlushHandle) return;
  state.streamFlushHandle = requestAnimationFrame(flushStreamBuffer);
}

function enqueueDelta(messageId, role, delta) {
  if (!delta) return;
  const entry = state.streamBuffer.get(messageId) || { delta: '', role };
  entry.delta += delta;
  if (role) entry.role = role;
  state.streamBuffer.set(messageId, entry);
  scheduleStreamFlush();
}

function finalizeMessage(messageId) {
  flushMessageBuffer(messageId);
  const existing = state.messageMap.get(messageId);
  if (!existing) return;
  renderMessage(existing, true);
}

function renderSessions(list) {
  state.sessions = list || [];
  el.sessionList.innerHTML = '';
  if (state.sessions.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'session-item';
    empty.textContent = 'No sessions yet';
    el.sessionList.appendChild(empty);
    return;
  }
  state.sessions.forEach((session) => {
    const btn = document.createElement('button');
    btn.className = 'session-item';
    if (session.sessionId === state.currentSessionId) {
      btn.classList.add('active');
    }
    btn.innerHTML = `<div><strong>${session.sessionId.slice(0, 8)}</strong></div>
      <div class="session-meta">${session.mode}${session.role ? ' · ' + session.role : ''}</div>`;
    btn.onclick = () => selectSession(session.sessionId);
    el.sessionList.appendChild(btn);
  });
}

async function fetchSessions() {
  const data = await fetchJson('/api/sessions');
  renderSessions(data.sessions || []);
}

async function createSession() {
  const mode = el.agentMode.value;
  const data = await fetchJson(`/api/sessions/new?mode=${encodeURIComponent(mode)}`);
  await fetchSessions();
  if (data.sessionId) {
    selectSession(data.sessionId);
  }
}

async function selectSession(sessionId) {
  state.currentSessionId = sessionId;
  el.currentSession.textContent = sessionId ? sessionId.slice(0, 12) : '-';
  renderSessions(state.sessions);
  resetStreams();
  if (!sessionId) return;
  connectSse(sessionId);
  await refreshAllPanels();
  refreshAgentYamlPanel();
}

function resetStreams() {
  state.messageMap.clear();
  state.streamBuffer.clear();
  if (state.streamFlushHandle) {
    cancelAnimationFrame(state.streamFlushHandle);
    state.streamFlushHandle = null;
  }
  state.autoScroll = true;
  el.messages.innerHTML = '';
  el.events.innerHTML = '';
  el.pingLog.innerHTML = '';
  if (state.sse) {
    state.sse.close();
    state.sse = null;
  }
}

function connectSse(sessionId) {
  const url = `/api/sessions/${sessionId}/agui/events`;
  state.sse = createAgUiStream(url, {
    onStatus: (status) => {
      if (status === 'connected') {
        setStatus('connected');
      } else if (status === 'reconnecting') {
        setStatus('reconnecting…', 'info');
      } else if (status === 'error') {
        setStatus('disconnected', 'error');
      } else {
        setStatus('connecting…');
      }
    },
    onMessage: (evt) => {
      try {
        const payload = JSON.parse(evt.data);
        handleAgUiEvent(payload);
      } catch (err) {
        console.warn('Bad SSE payload', err);
      }
    },
    reconnectDelayMs: 1200,
    maxAttempts: 8,
  });
}

function ensureMessage(id, role) {
  if (state.messageMap.has(id)) {
    const existing = state.messageMap.get(id);
    if (role && existing.role !== role) {
      existing.role = role;
      existing.node.className = `message ${role}`;
    }
    return existing;
  }
  const node = document.createElement('div');
  node.className = `message ${role}`;
  node.dataset.id = id;
  node.innerHTML = '<div class="content"></div>';
  const content = node.querySelector('.content');
  state.messageMap.set(id, { node, content, text: '', role });
  el.messages.appendChild(node);
  return state.messageMap.get(id);
}

function renderMessagesSnapshot(messages) {
  state.messageMap.clear();
  state.streamBuffer.clear();
  el.messages.innerHTML = '';
  (messages || []).forEach((msg) => {
    const item = ensureMessage(msg.id, msg.role || 'assistant');
    item.text = msg.content || '';
    renderMessage(item, true);
  });
  state.autoScroll = true;
  scrollMessagesToBottom();
}

function handleAgUiEvent(evt) {
  if (!evt || !evt.type) return;

  if (evt.type === 'MESSAGES_SNAPSHOT') {
    renderMessagesSnapshot(evt.messages || []);
    return;
  }

  if (evt.type === 'TEXT_MESSAGE_START') {
    ensureMessage(evt.messageId, evt.role || 'assistant');
    return;
  }

  if (evt.type === 'TEXT_MESSAGE_CONTENT') {
    const existing = state.messageMap.get(evt.messageId);
    enqueueDelta(evt.messageId, existing?.role || 'assistant', evt.delta || '');
    return;
  }

  if (evt.type === 'TEXT_MESSAGE_END') {
    finalizeMessage(evt.messageId);
    refreshHistory();
    refreshState();
    return;
  }

  if (evt.type === 'CUSTOM' && evt.name === 'WORKSHOP_PONG') {
    const payload = evt.value || {};
    const line = document.createElement('div');
    line.className = 'event-item';
    line.textContent = `PONG ${payload.requestId || ''}: ${payload.content || ''}`;
    el.pingLog.prepend(line);
  }

  const time = formatTimestamp(evt.timestamp);
  const asJson = (value) => JSON.stringify(value ?? {}, null, 2);

  const addEventLine = (type, meta, open = false) => {
    const details = document.createElement('details');
    details.className = 'event';
    details.open = open;
    const summary = document.createElement('summary');
    summary.className = 'type';
    summary.textContent = type;
    const metaEl = document.createElement('div');
    metaEl.className = 'meta';
    metaEl.textContent = meta;
    details.appendChild(summary);
    details.appendChild(metaEl);
    el.events.prepend(details);
  };

  switch (evt.type) {
    case 'RUN_STARTED':
    case 'RUN_FINISHED':
    case 'RUN_ERROR':
    case 'STEP_STARTED':
    case 'STEP_FINISHED':
    case 'TOOL_CALL_START':
    case 'TOOL_CALL_RESULT':
    case 'TOOL_CALL_END':
      addEventLine(evt.type, `${time}\n${asJson(evt)}`, evt.type === 'RUN_ERROR');
      if (evt.type === 'RUN_FINISHED' || evt.type === 'RUN_ERROR') {
        refreshHistory();
        refreshState();
      }
      return;
    case 'CUSTOM':
      if (evt.name === 'aevatar.llm.trace') {
        const meta = `${time}\n${evt.value?.phase ?? ''} | ${evt.value?.status ?? ''}`;
        addEventLine('LLM_TRACE', meta, evt.value?.status === 'failed');
      } else {
        const open = typeof evt.name === 'string' && evt.name.includes('error');
        addEventLine(`CUSTOM:${evt.name}`, `${time}\n${asJson(evt.value)}`, open);
      }
      return;
    default:
      addEventLine(evt.type, `${time}\n${asJson(evt)}`);
  }
}

async function sendMessage(message) {
  if (!state.currentSessionId) return;
  const streamChunkEveryN = getStreamChunkEveryN();
  const requestId = `web:${state.currentSessionId}:${Date.now()}`;
  const payload = {
    requestId,
    userId: 'workshop-ui',
    message,
    context: {
      sessionId: state.currentSessionId,
    },
    timestamp: new Date().toISOString(),
    streamChunkEveryN,
  };
  await fetchJson(`/api/sessions/${state.currentSessionId}/input`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
}

async function refreshHistory() {
  if (!state.currentSessionId) return;
  const data = await fetchJson(`/api/sessions/${state.currentSessionId}/state/history`);
  el.historyList.innerHTML = '';
  (data.history || []).forEach((msg) => {
    const div = document.createElement('div');
    div.className = 'memory-item';
    div.innerHTML = `<strong>${msg.role}</strong>: ${escapeHtml(msg.content || '')}`;
    el.historyList.appendChild(div);
  });
}

async function refreshState() {
  if (!state.currentSessionId) return;
  const data = await fetchJson(`/api/agent/state?sessionId=${state.currentSessionId}`);
  el.historySummary.textContent = data.historySummary || '-';
  el.contextKeys.innerHTML = '';
  (data.contextKeys || []).forEach((key) => {
    const tag = document.createElement('div');
    tag.className = 'tag';
    tag.textContent = key;
    el.contextKeys.appendChild(tag);
  });
}

async function refreshMemory() {
  if (!state.currentSessionId) return;
  const data = await fetchJson(`/api/agent/memory?sessionId=${state.currentSessionId}`);
  el.memoryAgentId.textContent = data.agentMemoryId || '-';
  el.memorySessionId.textContent = data.sessionMemoryId || '-';

  el.memoryAgent.innerHTML = '';
  (data.agentEntries || []).forEach((entry) => {
    const div = document.createElement('div');
    div.className = 'memory-item';
    div.innerHTML = `<strong>${entry.role || 'assistant'}</strong>: ${escapeHtml(entry.content || '')}`;
    el.memoryAgent.appendChild(div);
  });

  el.memorySession.innerHTML = '';
  (data.sessionEntries || []).forEach((entry) => {
    const div = document.createElement('div');
    div.className = 'memory-item';
    div.innerHTML = `<strong>${entry.role || 'assistant'}</strong>: ${escapeHtml(entry.content || '')}`;
    el.memorySession.appendChild(div);
  });
}

async function refreshHandlers() {
  if (!state.currentSessionId) return;
  const data = await fetchJson(`/api/agent/handlers?sessionId=${state.currentSessionId}`);
  el.handlersList.innerHTML = '';
  (data.handlers || []).forEach((h) => {
    const div = document.createElement('div');
    div.className = 'handler-item';
    div.innerHTML = `<div><strong>${h.name}</strong> (${h.visibility})</div>
      <div class="panel-subtitle">${h.handlerType} · ${h.parameterType}</div>`;
    el.handlersList.appendChild(div);
  });

  el.modulesList.innerHTML = '';
  (data.modules || []).forEach((m) => {
    const div = document.createElement('div');
    div.className = 'module-item';
    div.textContent = `${m.name} (priority ${m.priority})`;
    el.modulesList.appendChild(div);
  });
}

async function refreshSettings() {
  if (!state.currentSessionId) return;
  const data = await fetchJson('/api/agent/settings', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sessionId: state.currentSessionId }),
  });

  el.toggleHistory.checked = data.enableHistory;
  el.toggleCompaction.checked = data.enableCompaction;
  el.toggleMemory.checked = data.enableMemoryStore;
  el.toggleSessionMemory.checked = data.enableSessionMemory;
  el.toggleVector.checked = data.enableVectorIndex;
  el.toggleMcp.checked = data.enableMcp;
  el.toggleSkills.checked = data.enableSkills;
  el.toggleDangerous.checked = data.allowDangerousTools;
  el.toggleInternal.checked = data.allowInternalTools;
}

async function applySettings() {
  if (!state.currentSessionId) return;
  const payload = {
    sessionId: state.currentSessionId,
    enableHistory: el.toggleHistory.checked,
    enableCompaction: el.toggleCompaction.checked,
    enableMemoryStore: el.toggleMemory.checked,
    enableSessionMemory: el.toggleSessionMemory.checked,
    enableVectorIndex: el.toggleVector.checked,
    enableMcp: el.toggleMcp.checked,
    enableSkills: el.toggleSkills.checked,
    allowDangerousTools: el.toggleDangerous.checked,
    allowInternalTools: el.toggleInternal.checked,
  };
  const data = await fetchJson('/api/agent/settings', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  el.settingsStatus.textContent = `Updated at ${new Date().toLocaleTimeString()}`;
  return data;
}

async function refreshAllPanels() {
  await Promise.all([
    refreshHistory(),
    refreshState(),
    refreshMemory(),
    refreshHandlers(),
    refreshSettings(),
  ]);
}
