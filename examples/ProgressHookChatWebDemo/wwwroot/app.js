const statusEl = document.getElementById("status");
const messagesEl = document.getElementById("messages");
const eventsEl = document.getElementById("events");
const composer = document.getElementById("composer");
const inputEl = document.getElementById("input");
const sessionListEl = document.getElementById("session-list");
const currentSessionEl = document.getElementById("current-session");
const newSessionBtn = document.getElementById("new-session");
const sessionInfoBodyEl = document.getElementById("session-info-body");
const sessionHistoryEl = document.getElementById("session-history");

let sessionId = null;
let eventSource = null;
const messageMap = new Map();
let sessionInfoTimer = null;
let sessionHistoryTimer = null;

function setStatus(text, color = "#8ad") {
  statusEl.textContent = text;
  statusEl.style.color = color;
}

function renderMessage(message) {
  let el = messageMap.get(message.id)?.el;
  if (!el) {
    el = document.createElement("div");
    el.className = `message ${message.role}`;
    el.innerHTML = `
      <div class="role">${message.role}</div>
      <div class="bubble"></div>
    `;
    messagesEl.appendChild(el);
    messageMap.set(message.id, { ...message, el });
    messagesEl.scrollTop = messagesEl.scrollHeight;
  }
  const bubble = el.querySelector(".bubble");
  bubble.textContent = message.content;
}

function formatTimestamp(value) {
  if (!value) return "-";
  const dt = new Date(value);
  if (Number.isNaN(dt.getTime())) return String(value);
  return dt.toLocaleString();
}

function truncate(text, maxLen) {
  if (!text) return "";
  if (text.length <= maxLen) return text;
  return `${text.slice(0, maxLen)}…`;
}

function appendDelta(messageId, role, delta) {
  const existing = messageMap.get(messageId);
  if (existing) {
    existing.content += delta;
    renderMessage(existing);
    return;
  }
  const msg = { id: messageId, role, content: delta };
  messageMap.set(messageId, msg);
  renderMessage(msg);
}

function addEventLine(type, meta, open = false) {
  const details = document.createElement("details");
  details.className = "event";
  details.open = open;

  const summary = document.createElement("summary");
  summary.className = "type";
  summary.textContent = type;

  const metaEl = document.createElement("div");
  metaEl.className = "meta";
  metaEl.textContent = meta;

  details.appendChild(summary);
  details.appendChild(metaEl);
  eventsEl.appendChild(details);
  eventsEl.scrollTop = eventsEl.scrollHeight;
}

function renderSessionInfo(info) {
  if (!sessionInfoBodyEl) return;
  if (!info) {
    sessionInfoBodyEl.textContent = "-";
    return;
  }

  const lines = [];
  lines.push(`created: ${formatTimestamp(info.createdAt)}`);
  lines.push(`updated: ${formatTimestamp(info.updatedAt)}`);
  lines.push(`messages: ${info.messageCount ?? 0}`);

  if (info.lastMessage && info.lastMessage.content) {
    const preview = truncate(info.lastMessage.content, 80);
    lines.push(`last: ${info.lastMessage.role}: ${preview}`);
  }

  if (info.memoryEnabled) {
    const count = info.memoryHasMore ? `${info.memoryEntries}+` : info.memoryEntries;
    lines.push(`session memory: ${count}`);
  } else {
    lines.push("session memory: off");
  }

  sessionInfoBodyEl.textContent = lines.join("\n");
}

function renderSessionHistory(list) {
  if (!sessionHistoryEl) return;
  sessionHistoryEl.innerHTML = "";
  if (!Array.isArray(list) || list.length === 0) {
    sessionHistoryEl.textContent = "(empty)";
    return;
  }

  list.forEach((item) => {
    const row = document.createElement("div");
    row.className = "history-item";

    const meta = document.createElement("div");
    meta.className = "meta";
    meta.textContent = `${item.role ?? "assistant"} | ${formatTimestamp(item.timestamp)}`;

    const body = document.createElement("div");
    body.textContent = item.content ?? "";

    row.appendChild(meta);
    row.appendChild(body);
    sessionHistoryEl.appendChild(row);
  });
}

function updateCurrentSession() {
  currentSessionEl.textContent = sessionId ? `session: ${sessionId}` : "session: -";
}

function renderSessions(list) {
  sessionListEl.innerHTML = "";
  list.forEach((id) => {
    const item = document.createElement("button");
    item.className = `session-item${id === sessionId ? " active" : ""}`;
    item.textContent = id;
    item.addEventListener("click", () => switchSession(id));
    sessionListEl.appendChild(item);
  });
}

async function createSession() {
  const res = await fetch("/api/sessions/new");
  const data = await res.json();
  return data.sessionId;
}

async function fetchSessions() {
  const res = await fetch("/api/sessions");
  if (!res.ok) return [];
  const data = await res.json();
  return Array.isArray(data.sessions) ? data.sessions : [];
}

async function fetchSessionHistory() {
  if (!sessionId) return [];
  const res = await fetch(`/api/sessions/${sessionId}/state/history`);
  if (!res.ok) return [];
  const data = await res.json();
  return Array.isArray(data.history) ? data.history : [];
}

async function loadSessionHistory() {
  const list = await fetchSessionHistory();
  renderSessionHistory(list);
}

function scheduleHistoryRefresh(delay = 800) {
  if (sessionHistoryTimer) {
    clearTimeout(sessionHistoryTimer);
  }
  sessionHistoryTimer = setTimeout(loadSessionHistory, delay);
}

async function loadSessionInfo() {
  if (!sessionId) return;
  const res = await fetch(`/api/sessions/${sessionId}/info`);
  if (!res.ok) {
    renderSessionInfo(null);
    return;
  }
  const data = await res.json();
  renderSessionInfo(data);
}

function scheduleSessionInfoRefresh(delay = 500) {
  if (sessionInfoTimer) {
    clearTimeout(sessionInfoTimer);
  }
  sessionInfoTimer = setTimeout(loadSessionInfo, delay);
}

async function refreshSessions() {
  const sessions = await fetchSessions();
  renderSessions(sessions);
  return sessions;
}

async function switchSession(id) {
  if (!id || id === sessionId) return;
  sessionId = id;
  localStorage.setItem("progress-demo-session-id", sessionId);
  updateCurrentSession();

  messagesEl.innerHTML = "";
  eventsEl.innerHTML = "";
  messageMap.clear();

  if (eventSource) {
    eventSource.close();
  }
  connectEvents();
  await loadSessionInfo();
  await loadSessionHistory();
  await refreshSessions();
}

function connectEvents() {
  eventSource = new EventSource(`/api/sessions/${sessionId}/agui/events`);
  setStatus("connected");

  eventSource.onmessage = (evt) => {
    const payload = JSON.parse(evt.data);
    handleAgUiEvent(payload);
  };

  eventSource.onerror = () => {
    setStatus("disconnected", "#f88");
  };
}

function handleAgUiEvent(evt) {
  switch (evt.type) {
    case "MESSAGES_SNAPSHOT":
      messagesEl.innerHTML = "";
      messageMap.clear();
      evt.messages.forEach((m) => {
        renderMessage({ id: m.id, role: m.role, content: m.content });
      });
      return;
    case "TEXT_MESSAGE_START":
      appendDelta(evt.messageId, evt.role, "");
      return;
    case "TEXT_MESSAGE_CONTENT":
      {
        const existing = messageMap.get(evt.messageId);
        appendDelta(evt.messageId, existing?.role ?? "assistant", evt.delta);
      }
      return;
    case "TEXT_MESSAGE_END":
      scheduleSessionInfoRefresh();
      scheduleHistoryRefresh();
      return;
    case "RUN_STARTED":
    case "RUN_FINISHED":
    case "RUN_ERROR":
    case "STEP_STARTED":
    case "STEP_FINISHED":
    case "TOOL_CALL_START":
    case "TOOL_CALL_RESULT":
    case "TOOL_CALL_END":
      addEventLine(evt.type, JSON.stringify(evt, null, 2), evt.type === "RUN_ERROR");
      if (evt.type === "RUN_FINISHED" || evt.type === "RUN_ERROR") {
        scheduleSessionInfoRefresh();
        scheduleHistoryRefresh();
      }
      return;
    case "CUSTOM":
      if (evt.name === "aevatar.llm.trace") {
        const meta = `${evt.value?.phase ?? ""} | ${evt.value?.status ?? ""}`;
        addEventLine(`LLM_TRACE`, meta, evt.value?.status === "failed");
      } else {
        const open = typeof evt.name === "string" && evt.name.includes("error");
        addEventLine(`CUSTOM:${evt.name}`, JSON.stringify(evt.value ?? {}, null, 2), open);
      }
      return;
    default:
      addEventLine(evt.type, JSON.stringify(evt, null, 2));
  }
}

composer.addEventListener("submit", async (e) => {
  e.preventDefault();
  const message = inputEl.value.trim();
  if (!message) return;
  inputEl.value = "";

  await fetch(`/api/sessions/${sessionId}/input`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ message }),
  });
});

inputEl.addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !e.shiftKey) {
    e.preventDefault();
    composer.requestSubmit();
  }
});

(async function init() {
  setStatus("loading sessions…");
  const stored = localStorage.getItem("progress-demo-session-id");
  let sessions = await refreshSessions();

  if (stored && sessions.includes(stored)) {
    sessionId = stored;
  } else if (sessions.length > 0) {
    sessionId = sessions[0];
  } else {
    sessionId = await createSession();
    sessions = await refreshSessions();
  }

  updateCurrentSession();
  connectEvents();
  await loadSessionInfo();
  await loadSessionHistory();
})();

newSessionBtn.addEventListener("click", async () => {
  const id = await createSession();
  await refreshSessions();
  await switchSession(id);
});
