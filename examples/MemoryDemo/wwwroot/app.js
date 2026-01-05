async function fetchJson(url, options = {}) {
  const res = await fetch(url, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  const text = await res.text();
  if (!res.ok) {
    throw new Error(text || `HTTP ${res.status}`);
  }
  return text ? JSON.parse(text) : null;
}

function el(id) {
  return document.getElementById(id);
}

function setBadge(ok, text) {
  const b = el("statusBadge");
  b.textContent = text;
  b.classList.remove("ok", "err");
  b.classList.add(ok ? "ok" : "err");
}

function appendMsg(role, content) {
  const log = el("chatLog");
  const div = document.createElement("div");
  div.className = `msg ${role}`;
  div.innerHTML = `<div class="role">${role}</div><div class="content"></div>`;
  div.querySelector(".content").textContent = content;
  log.appendChild(div);
  log.scrollTop = log.scrollHeight;
}

function pretty(obj) {
  return JSON.stringify(obj, null, 2);
}

async function refreshInfo() {
  try {
    const info = await fetchJson("/api/info");
    if (info.isReady) {
      setBadge(true, `ready · ${info.memoryStore} · ${info.llmDefaultProvider} · ${info.agentId.slice(0, 10)}…`);
    } else {
      setBadge(false, `not ready · ${info.lastError || "initializing..."}`);
    }
    return info;
  } catch (e) {
    setBadge(false, `error · ${e.message}`);
    return null;
  }
}

async function refreshState() {
  const state = await fetchJson("/api/state");
  el("summaryBox").textContent = state.summary || "(empty)";

  const short = {
    agentId: state.agentId,
    historyCount: state.historyCount,
    messages: state.messages.map((m) => ({
      role: m.role,
      content: (m.content || "").slice(0, 240),
      timestamp: m.timestamp,
    })),
  };
  el("stateBox").textContent = pretty(short);
}

async function loadLongterm() {
  const data = await fetchJson("/api/longterm/history?limit=200");
  el("longtermBox").textContent = pretty(data);
}

async function searchMemory() {
  const query = el("searchInput").value.trim();
  if (!query) return;

  const res = await fetch("/api/search_memory", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ query, maxResults: 10, memoryType: "all" }),
  });

  const text = await res.text();
  if (!res.ok) {
    el("searchBox").textContent = text || `HTTP ${res.status}`;
    return;
  }

  try {
    el("searchBox").textContent = pretty(JSON.parse(text));
  } catch {
    el("searchBox").textContent = text;
  }
}

async function sendChat() {
  const msg = el("chatInput").value.trim();
  if (!msg) return;

  el("chatInput").value = "";
  appendMsg("user", msg);

  try {
    const res = await fetchJson("/api/chat", {
      method: "POST",
      body: JSON.stringify({ message: msg }),
    });

    appendMsg("assistant", res.content || "");
    if (res.toolCalled) {
      appendMsg("meta", `tool: ${res.toolCall?.name}\nresult: ${res.toolCall?.result || ""}`);
    }

    // Auto refresh memory panels after each message
    await refreshState();
  } catch (e) {
    appendMsg("meta", `error: ${e.message}`);
  }
}

async function resetAgent() {
  try {
    await fetchJson("/api/reset", { method: "POST", body: "{}" });
    el("chatLog").innerHTML = "";
    el("stateBox").textContent = "";
    el("summaryBox").textContent = "";
    el("longtermBox").textContent = "";
    el("searchBox").textContent = "";
    await refreshInfo();
  } catch (e) {
    appendMsg("meta", `reset error: ${e.message}`);
  }
}

function wire() {
  el("sendBtn").addEventListener("click", sendChat);
  el("chatInput").addEventListener("keydown", (e) => {
    if (e.key === "Enter") sendChat();
  });

  el("refreshStateBtn").addEventListener("click", async () => {
    try { await refreshState(); } catch (e) { appendMsg("meta", `state error: ${e.message}`); }
  });

  el("loadLongtermBtn").addEventListener("click", async () => {
    try { await loadLongterm(); } catch (e) { appendMsg("meta", `longterm error: ${e.message}`); }
  });

  el("searchBtn").addEventListener("click", async () => {
    try { await searchMemory(); } catch (e) { appendMsg("meta", `search error: ${e.message}`); }
  });

  el("searchInput").addEventListener("keydown", (e) => {
    if (e.key === "Enter") searchMemory();
  });

  el("resetBtn").addEventListener("click", resetAgent);
}

async function boot() {
  wire();
  await refreshInfo();
  try {
    await refreshState();
  } catch {
    // ignore at startup
  }
}

boot();


