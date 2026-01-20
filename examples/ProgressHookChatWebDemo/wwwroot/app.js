const statusEl = document.getElementById("status");
const messagesEl = document.getElementById("messages");
const eventsEl = document.getElementById("events");
const composer = document.getElementById("composer");
const inputEl = document.getElementById("input");

let sessionId = null;
const messageMap = new Map();

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

async function createSession() {
  const res = await fetch("/api/sessions/new");
  const data = await res.json();
  sessionId = data.sessionId;
  return sessionId;
}

function connectEvents() {
  const es = new EventSource(`/api/sessions/${sessionId}/agui/events`);
  setStatus("connected");

  es.onmessage = (evt) => {
    const payload = JSON.parse(evt.data);
    handleAgUiEvent(payload);
  };

  es.onerror = () => {
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
  setStatus("connecting…");
  await createSession();
  connectEvents();
})();
