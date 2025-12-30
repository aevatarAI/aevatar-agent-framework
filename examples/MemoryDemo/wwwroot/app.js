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
      setBadge(true, `ready · ${info.llmDefaultProvider} · ${info.agentId.slice(0, 10)}…`);
    } else {
      setBadge(false, `not ready · ${info.lastError || "initializing..."}`);
    }

    // Settings + paths panel
    try {
      el("enableStoreChk").checked = !!info.settings?.enableMemoryStoreAppend;
      el("enableVectorChk").checked = !!info.settings?.enableMemoryVectorIndexAppend;
      el("pathsBox").textContent = pretty({
        agentId: info.agentId,
        defaultMemoryId: `privateagent::${info.agentId}`,
        ...info.paths,
        persistence: info.persistence,
        settings: info.settings,
      });

      // Fill default memoryId inputs
      el("searchMemoryId").value ||= "";
      el("memEntriesId").value ||= `privateagent::${info.agentId}`;
      el("vectorMemoryId").value ||= `privateagent::${info.agentId}`;
    } catch {
      // ignore
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

async function refreshCqrs() {
  const data = await fetchJson("/api/cqrs/state");
  el("cqrsBox").textContent = pretty(data);
}

async function searchMemory() {
  const query = el("searchInput").value.trim();
  if (!query) return;

  const memoryType = el("searchTypeSel")?.value || "all";
  const memoryId = (el("searchMemoryId")?.value || "").trim();

  const res = await fetch("/api/search_memory", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ query, maxResults: 10, memoryType, memoryId: memoryId || null }),
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

async function applySettings() {
  const enableMemoryStoreAppend = !!el("enableStoreChk").checked;
  const enableMemoryVectorIndexAppend = !!el("enableVectorChk").checked;

  const out = await fetchJson("/api/settings", {
    method: "POST",
    body: JSON.stringify({ enableMemoryStoreAppend, enableMemoryVectorIndexAppend }),
  });

  appendMsg("meta", `settings updated: store=${out.enableMemoryStoreAppend} vector=${out.enableMemoryVectorIndexAppend}`);
  await refreshInfo();
}

async function refreshMemoryResources() {
  const data = await fetchJson("/api/memory/resources");
  el("memBox").textContent = pretty(data);
}

async function loadMemoryEntries() {
  const memoryId = el("memEntriesId").value.trim();
  if (!memoryId) return;
  const data = await fetchJson(`/api/memory/entries?memoryId=${encodeURIComponent(memoryId)}&limit=80`);
  el("memBox").textContent = pretty(data);
}

async function memoryStats() {
  const memoryId = el("memEntriesId").value.trim();
  if (!memoryId) return;
  try {
    const data = await fetchJson(`/api/memory/stats?memoryId=${encodeURIComponent(memoryId)}`);
    el("memBox").textContent = pretty(data);
  } catch (e) {
    el("memBox").textContent = `error: ${e.message}`;
  }
}

async function vectorSearch() {
  const query = el("vectorQuery").value.trim();
  const memoryId = el("vectorMemoryId").value.trim();
  if (!query) return;

  const data = await fetchJson("/api/vector/search", {
    method: "POST",
    body: JSON.stringify({ query, memoryId: memoryId || null, limit: 10 }),
  });
  el("vectorBox").textContent = pretty(data);
}

async function vectorStats() {
  const memoryId = el("vectorMemoryId").value.trim();
  if (!memoryId) return;
  try {
    const data = await fetchJson(`/api/vector/stats?memoryId=${encodeURIComponent(memoryId)}`);
    el("vectorBox").textContent = pretty(data);
  } catch (e) {
    el("vectorBox").textContent = `error: ${e.message}`;
  }
}

async function seedTrace() {
  try {
    const out = await fetchJson("/api/trace/seed", { method: "POST", body: "{}" });
    appendMsg("meta", `seeded trace: ${out.executionId}\nexecution memoryId: ${out.memoryId}`);
    el("traceId").value = out.executionId;
    el("graphId").value = out.executionId;
    el("searchMemoryId").value = out.memoryId;
    await refreshTraceList();
  } catch (e) {
    appendMsg("meta", `seed trace error: ${e.message}`);
  }
}

async function refreshTraceList() {
  const data = await fetchJson("/api/trace/list?limit=50");
  el("traceBox").textContent = pretty(data);
}

async function loadTrace() {
  const id = el("traceId").value.trim();
  if (!id) return;
  const res = await fetch(`/api/trace/${encodeURIComponent(id)}`);
  const text = await res.text();
  el("traceBox").textContent = text;
}

async function loadGraph() {
  const id = el("graphId").value.trim();
  if (!id) return;
  const res = await fetch(`/api/graph/${encodeURIComponent(id)}`);
  const text = await res.text();
  el("graphBox").textContent = text;
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
    await refreshCqrs();
  } catch (e) {
    appendMsg("meta", `error: ${e.message}`);
  }
}

async function seedDemo() {
  const text = `seed-keyword: aevatar-cqrs · ts=${new Date().toISOString()}`;
  appendMsg("meta", `seeding: ${text}`);

  try {
    await fetchJson("/api/seed", {
      method: "POST",
      body: JSON.stringify({ text }),
    });

    await refreshState();
    await refreshCqrs();
  } catch (e) {
    appendMsg("meta", `seed error: ${e.message}`);
  }
}

async function resetAgent() {
  try {
    await fetchJson("/api/reset", { method: "POST", body: "{}" });
    el("chatLog").innerHTML = "";
    el("stateBox").textContent = "";
    el("summaryBox").textContent = "";
    el("cqrsBox").textContent = "";
    el("searchBox").textContent = "";
    el("memBox").textContent = "";
    el("vectorBox").textContent = "";
    el("traceBox").textContent = "";
    el("graphBox").textContent = "";
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

  el("seedBtn").addEventListener("click", seedDemo);
  el("seedTraceBtn").addEventListener("click", seedTrace);

  el("refreshStateBtn").addEventListener("click", async () => {
    try { await refreshState(); } catch (e) { appendMsg("meta", `state error: ${e.message}`); }
  });

  el("refreshCqrsBtn").addEventListener("click", async () => {
    try { await refreshCqrs(); } catch (e) { appendMsg("meta", `cqrs error: ${e.message}`); }
  });

  el("searchBtn").addEventListener("click", async () => {
    try { await searchMemory(); } catch (e) { appendMsg("meta", `search error: ${e.message}`); }
  });

  el("searchInput").addEventListener("keydown", (e) => {
    if (e.key === "Enter") searchMemory();
  });

  el("resetBtn").addEventListener("click", resetAgent);

  el("applySettingsBtn").addEventListener("click", async () => {
    try { await applySettings(); } catch (e) { appendMsg("meta", `settings error: ${e.message}`); }
  });

  el("refreshInfoBtn").addEventListener("click", async () => {
    try { await refreshInfo(); } catch (e) { appendMsg("meta", `info error: ${e.message}`); }
  });

  el("refreshMemResourcesBtn").addEventListener("click", async () => {
    try { await refreshMemoryResources(); } catch (e) { appendMsg("meta", `memory error: ${e.message}`); }
  });
  el("loadMemEntriesBtn").addEventListener("click", async () => {
    try { await loadMemoryEntries(); } catch (e) { appendMsg("meta", `memory error: ${e.message}`); }
  });
  el("memStatsBtn").addEventListener("click", async () => {
    try { await memoryStats(); } catch (e) { appendMsg("meta", `memory stats error: ${e.message}`); }
  });

  el("vectorSearchBtn").addEventListener("click", async () => {
    try { await vectorSearch(); } catch (e) { appendMsg("meta", `vector error: ${e.message}`); }
  });
  el("vectorStatsBtn").addEventListener("click", async () => {
    try { await vectorStats(); } catch (e) { appendMsg("meta", `vector stats error: ${e.message}`); }
  });

  el("traceListBtn").addEventListener("click", async () => {
    try { await refreshTraceList(); } catch (e) { appendMsg("meta", `trace list error: ${e.message}`); }
  });
  el("traceLoadBtn").addEventListener("click", async () => {
    try { await loadTrace(); } catch (e) { appendMsg("meta", `trace load error: ${e.message}`); }
  });
  el("graphLoadBtn").addEventListener("click", async () => {
    try { await loadGraph(); } catch (e) { appendMsg("meta", `graph load error: ${e.message}`); }
  });
}

async function boot() {
  wire();
  await refreshInfo();
  try {
    await refreshState();
    await refreshCqrs();
    await refreshTraceList();
  } catch {
    // ignore at startup
  }
}

boot();


