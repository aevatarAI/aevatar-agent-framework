// ============================================================
//  app-sessions.js
//
//  中文 + ASCII:
//  - Session 列表 + SSE + Chat 流
//  - History / Memory / Handlers / Settings
// ============================================================

function appendDelta(messageId, role, delta) {
  if (!delta) return;
  const existing = ensureMessage(messageId, role);
  existing.text += delta;
  renderMessage(existing, false);
  if (state.autoScroll) scrollMessagesToBottom();
}

function finalizeMessage(messageId) {
  const existing = state.messageMap.get(messageId);
  if (!existing) return;
  renderMessage(existing, true);
}

function appendBackendLog(message, isError = false) {
  if (!el.backendLog) return;
  const line = document.createElement('div');
  line.className = `backend-log-item${isError ? ' error' : ''}`;
  line.textContent = message;
  el.backendLog.prepend(line);

  const items = el.backendLog.querySelectorAll('.backend-log-item');
  if (items.length > 120) {
    for (let i = items.length - 1; i >= 120; i -= 1) {
      items[i].remove();
    }
  }
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
      <div class="session-meta">${session.workflowName || 'workflow'}${session.primaryRole ? ' · ' + session.primaryRole : ''}</div>`;
    btn.onclick = () => selectSession(session.sessionId);
    el.sessionList.appendChild(btn);
  });
}

function renderWorkflows(list, defaultWorkflow) {
  if (!el.workflowSelect) return;
  const previous = el.workflowSelect.value;
  el.workflowSelect.innerHTML = '';
  (list || []).forEach((wf) => {
    const opt = document.createElement('option');
    opt.value = wf.name;
    opt.textContent = wf.name;
    el.workflowSelect.appendChild(opt);
  });

  const next = previous || defaultWorkflow || (list?.[0]?.name || '');
  if (next) {
    el.workflowSelect.value = next;
  }
}

async function fetchWorkflows() {
  const data = await fetchJson('/api/chat/workflows');
  state.workflows = data.workflows || [];
  state.defaultWorkflow = data.defaultWorkflow || '';
  renderWorkflows(state.workflows, state.defaultWorkflow);
}

async function fetchSessions() {
  const data = await fetchJson('/api/chat/sessions');
  renderSessions(data.sessions || []);
}

async function createSession() {
  const workflow = el.workflowSelect?.value || '';
  const data = await fetchJson(`/api/chat/sessions/new?workflow=${encodeURIComponent(workflow)}`);
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
  updateInputState(); // Update input state immediately
  if (!sessionId) return;
  connectSse(sessionId);
  await refreshAllPanels();
}

function resetStreams() {
  state.messageMap.clear();
  state.autoScroll = true;
  state.sseConnected = false;
  el.messages.innerHTML = '';
  el.events.innerHTML = '';
  el.pingLog.innerHTML = '';
  if (el.backendLog) el.backendLog.innerHTML = '';
  if (state.sse) {
    state.sse.close();
    state.sse = null;
  }
  updateInputState(); // Update input disabled state
}

function connectSse(sessionId) {
  const url = `/api/chat/sessions/${sessionId}/agui/events`;
  state.sse = createAgUiStream(url, {
    onStatus: (status) => {
      if (status === 'connected') {
        state.sseConnected = true;
        setStatus('connected');
        updateInputState();
      } else if (status === 'reconnecting') {
        state.sseConnected = false;
        setStatus('reconnecting…', 'info');
        updateInputState();
      } else if (status === 'error') {
        state.sseConnected = false;
        setStatus('disconnected', 'error');
        updateInputState();
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

// ============================================================
//  Input State Management
//
//  中文 + ASCII:
//  - 只有 SSE 连接就绪后才启用输入
//  - 确保实时 streaming 不丢失事件
// ============================================================
function updateInputState() {
  const canSend = state.currentSessionId && state.sseConnected;
  if (el.input) {
    el.input.disabled = !canSend;
    el.input.placeholder = canSend 
      ? 'Type a message...' 
      : (state.currentSessionId ? 'Connecting...' : 'Select a session first');
  }
  if (el.composer) {
    el.composer.style.opacity = canSend ? '1' : '0.6';
  }
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
  state.messageMap.set(id, { node, content, text: '', role, renderedLength: 0 });
  el.messages.appendChild(node);
  return state.messageMap.get(id);
}

function renderMessagesSnapshot(messages) {
  state.messageMap.clear();
  el.messages.innerHTML = '';
  (messages || []).forEach((msg) => {
    const item = ensureMessage(msg.id, msg.role || 'assistant');
    item.text = msg.content || '';
    renderMessage(item, true);
  });
  state.autoScroll = true;
  scrollMessagesToBottom();
}

function truncateEventText(value, maxLen = 120) {
  if (!value) return '';
  const text = String(value).replace(/\s+/g, ' ').trim();
  if (!text) return '';
  if (text.length <= maxLen) return text;
  return `${text.slice(0, maxLen - 1)}…`;
}

function joinEventParts(parts) {
  return parts.filter((part) => Boolean(part && String(part).trim())).join(' · ');
}

function buildCustomSummary(evt) {
  const name = (evt?.name || 'custom').toString();
  const value = evt?.value || {};
  const parts = [name];

  if (name === 'aevatar.llm.trace') {
    const phase = value.phase || '';
    const status = value.status || '';
    return joinEventParts([name, joinEventParts([phase, status])]);
  }

  if (name === 'WORKSHOP_STATUS' || name === 'SESSION_STATUS') {
    const stage = value.stage || '';
    const message = truncateEventText(value.message || '', 120);
    return joinEventParts([name, stage, message]);
  }

  if (name === 'execution_trace_raw') {
    const phase = value.phase || '';
    const status = value.fields?.status || '';
    const nodeId = value.nodeId || '';
    return joinEventParts([name, phase, status, nodeId]);
  }

  if (name.startsWith('event.handler.')) {
    const handler = value.handlerName || '';
    const eventType = value.eventType || '';
    const status = value.status || '';
    return joinEventParts([name, handler, eventType, status]);
  }

  const message = truncateEventText(value.message || value.error || '', 120);
  return joinEventParts([name, message]);
}

function buildEventSummary(evt, time) {
  if (!evt) return time || '';
  const type = evt.type;
  const parts = [time];

  switch (type) {
    case 'RUN_STARTED':
      parts.push(joinEventParts([`run=${evt.runId || ''}`, `thread=${evt.threadId || ''}`]));
      break;
    case 'RUN_FINISHED': {
      const status = evt.result?.status || 'completed';
      const durationMs = evt.result?.duration_ms ?? evt.result?.durationMs;
      parts.push(joinEventParts([`run=${evt.runId || ''}`, `status=${status}`, durationMs ? `durationMs=${durationMs}` : '']));
      break;
    }
    case 'RUN_ERROR':
      parts.push(truncateEventText(evt.message || evt.code || 'run error', 140));
      break;
    case 'STEP_STARTED':
    case 'STEP_FINISHED':
      parts.push(truncateEventText(evt.stepName || '', 120));
      break;
    case 'TEXT_MESSAGE_START':
      parts.push(joinEventParts([`role=${evt.role || ''}`, `id=${evt.messageId || ''}`]));
      break;
    case 'TEXT_MESSAGE_CONTENT':
      parts.push(joinEventParts([`id=${evt.messageId || ''}`, `delta=${truncateEventText(evt.delta || '', 120)}`]));
      break;
    case 'TEXT_MESSAGE_END':
      parts.push(`id=${evt.messageId || ''}`);
      break;
    case 'MESSAGES_SNAPSHOT':
      parts.push(`messages=${Array.isArray(evt.messages) ? evt.messages.length : 0}`);
      break;
    case 'CUSTOM':
      if (evt.name) parts.push(`name=${evt.name}`);
      break;
    case 'TOOL_CALL_START':
      parts.push(joinEventParts([`tool=${evt.toolName || ''}`, `call=${evt.toolCallId || ''}`]));
      break;
    case 'TOOL_CALL_ARGS':
      parts.push(joinEventParts([`call=${evt.toolCallId || ''}`, `args=${truncateEventText(evt.argsDelta || '', 120)}`]));
      break;
    case 'TOOL_CALL_RESULT':
      parts.push(joinEventParts([`call=${evt.toolCallId || ''}`, `result=${truncateEventText(evt.result || '', 120)}`]));
      break;
    case 'TOOL_CALL_END':
      parts.push(`call=${evt.toolCallId || ''}`);
      break;
    case 'CUSTOM':
      parts.push(buildCustomSummary(evt));
      break;
    default:
      break;
  }

  return joinEventParts(parts);
}

function resolveEventDisplayType(evt) {
  if (!evt || !evt.type) return '';
  if (evt.type !== 'CUSTOM') return evt.type;
  const name = (evt.name || '').toString();
  if (name.startsWith('event.handler.')) {
    return `EVENT_HANDLER_${name.replace('event.handler.', '').toUpperCase()}`;
  }
  if (name === 'aevatar.llm.trace') return 'LLM_TRACE';
  if (name.length > 0) return `CUSTOM:${name}`;
  return 'CUSTOM';
}

function handleAgUiEvent(evt) {
  if (!evt || !evt.type) return;

  const time = formatTimestamp(evt.timestamp);
  const asJson = (value) => JSON.stringify(value ?? {}, null, 2);
  const summary = buildEventSummary(evt, time);
  const meta = `${time}\n${asJson(evt)}`;
  const displayType = resolveEventDisplayType(evt);

  const addEventLine = (type, summaryText, metaText, open = false) => {
    const details = document.createElement('details');
    details.className = 'event';
    details.open = open;
    const summary = document.createElement('summary');
    summary.className = 'type';
    summary.textContent = summaryText ? `${type} · ${summaryText}` : type;
    const metaEl = document.createElement('div');
    metaEl.className = 'meta';
    metaEl.textContent = metaText;
    details.appendChild(summary);
    details.appendChild(metaEl);
    el.events.prepend(details);
  };

  if (evt.type === 'MESSAGES_SNAPSHOT') {
    state.sseConnected = true;
    updateInputState();
    addEventLine(displayType, summary, meta);
    renderMessagesSnapshot(evt.messages || []);
    return;
  }

  // SSE_CONNECTED: legacy fallback
  if (evt.type === 'CUSTOM' && evt.name === 'SSE_CONNECTED') {
    state.sseConnected = true;
    updateInputState();
    addEventLine('SSE_CONNECTED', `session=${evt.value?.sessionId || ''}`, meta);
    return;
  }

  if (evt.type === 'TEXT_MESSAGE_START') {
    ensureMessage(evt.messageId, evt.role || 'assistant');
    addEventLine(displayType, summary, meta);
    return;
  }

  if (evt.type === 'TEXT_MESSAGE_CONTENT') {
    const existing = state.messageMap.get(evt.messageId);
    appendDelta(evt.messageId, existing?.role || 'assistant', evt.delta || '');
    addEventLine(displayType, summary, meta);
    return;
  }

  if (evt.type === 'TEXT_MESSAGE_END') {
    finalizeMessage(evt.messageId);
    addEventLine(displayType, summary, meta);
    refreshHistory();
    refreshState();
    return;
  }

  if (evt.type === 'CUSTOM' &&
    (evt.name === 'SESSION_PONG' || evt.name === 'WORKSPACE_PONG' || evt.name === 'WORKSHOP_PONG')) {
    const payload = evt.value || {};
    const line = document.createElement('div');
    line.className = 'event-item';
    line.textContent = `PONG ${payload.requestId || ''}: ${payload.content || ''}`;
    el.pingLog.prepend(line);
  }

  if (evt.type === 'CUSTOM' && (evt.name === 'WORKSHOP_STATUS' || evt.name === 'SESSION_STATUS')) {
    const payload = evt.value || {};
    const stage = payload.stage || 'status';
    const message = payload.message || '';
    const line = `[${time}] ${stage}: ${message}`.trim();
    appendBackendLog(line, String(stage).includes('error'));
  }

  switch (evt.type) {
    case 'RUN_STARTED':
    case 'RUN_FINISHED':
    case 'RUN_ERROR':
    case 'STEP_STARTED':
    case 'STEP_FINISHED':
    case 'TOOL_CALL_START':
    case 'TOOL_CALL_ARGS':
    case 'TOOL_CALL_RESULT':
    case 'TOOL_CALL_END':
      addEventLine(displayType, summary, meta, evt.type === 'RUN_ERROR');
      if (evt.type === 'RUN_FINISHED' || evt.type === 'RUN_ERROR') {
        refreshHistory();
        refreshState();
      }
      return;
    default:
      addEventLine(displayType, summary, meta);
  }
}

async function sendMessage(message) {
  if (!state.currentSessionId) return;
  
  // Must wait for SSE to be connected for real-time streaming
  if (!state.sseConnected) {
    console.warn('[sendMessage] SSE not connected yet, message will be queued');
    setStatus('waiting for connection...', 'info');
    // Wait up to 3 seconds for SSE to connect
    for (let i = 0; i < 30 && !state.sseConnected; i++) {
      await new Promise(r => setTimeout(r, 100));
    }
    if (!state.sseConnected) {
      setStatus('SSE connection timeout', 'error');
      return;
    }
  }
  
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
  await fetchJson(`/api/chat/sessions/${state.currentSessionId}/input`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
}

async function refreshHistory() {
  if (!state.currentSessionId) return;
  const data = await fetchJson(`/api/chat/sessions/${state.currentSessionId}/state/history`);
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
