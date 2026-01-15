// AevatarKit Workspace MVP frontend (no build step)
// - Agent Manager UI (fetch from /api/agents/*)
// - Workflow Editor (reuse /api/runs + SSE)
// - Memory Manager (reuse /api/runs/{runId}/memory)

let currentRunId = null;
let es = null;

const state = {
  page: "agents",
  query: "",
  agents: [],
  categories: [],
  expanded: new Set(),
  graphs: [],
  runs: [],
  mcpServers: []
};

const $ = (id) => document.getElementById(id);

function setConn(connected, text) {
  const dot = $("connDot");
  const t = $("connText");
  if (!dot || !t) return;
  dot.classList.remove("ok", "bad");
  dot.classList.add(connected ? "ok" : "bad");
  t.textContent = text;
}

function escapeHtml(s) {
  return String(s ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;")
    .replaceAll("'", "&#39;");
}

function statusBadge(status) {
  const s = (status || "").toString().toLowerCase();
  if (s.includes("running")) return `<span class="badge running">RUNNING</span>`;
  if (s.includes("stream")) return `<span class="badge streaming">STREAMING</span>`;
  if (s.includes("complete")) return `<span class="badge completed">COMPLETED</span>`;
  if (s.includes("fail")) return `<span class="badge failed">FAILED</span>`;
  return `<span class="badge">${escapeHtml(status || "UNKNOWN")}</span>`;
}

async function apiGet(path) {
  const res = await fetch(path, { headers: { "accept": "application/json" } });
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`);
  return await res.json();
}

async function apiPost(path, body) {
  const res = await fetch(path, {
    method: "POST",
    headers: { "content-type": "application/json", "accept": "application/json" },
    body: JSON.stringify(body)
  });
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`);
  return await res.json();
}

async function apiPut(path, body) {
  const res = await fetch(path, {
    method: "PUT",
    headers: { "content-type": "application/json", "accept": "application/json" },
    body: JSON.stringify(body)
  });
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`);
  return await res.json();
}

function showPage(page) {
  state.page = page;

  document.querySelectorAll(".navItem").forEach((b) => {
    b.classList.toggle("active", b.dataset.page === page);
  });

  document.querySelectorAll(".page").forEach((p) => p.classList.remove("active"));
  const el = document.getElementById(`page_${page}`);
  if (el) el.classList.add("active");

  const titleMap = {
    agents: "Agent Manager",
    workflow: "Workflow Editor",
    models: "Model Manager",
    tools: "System Tools",
    mcp: "MCP Manager",
    prompts: "Prompt Manager",
    files: "File Manager",
    memory: "Memory Manager"
  };
  $("pageTitle").textContent = titleMap[page] || "Workspace";

  // Search only makes sense on agents page in MVP.
  $("searchWrap").style.display = page === "agents" ? "flex" : "none";
  $("primaryBtn").style.display = page === "agents" ? "inline-flex" : "none";

  // Pills only meaningful on agents page in MVP.
  const showPills = page === "agents";
  $("pillAgents").style.display = showPills ? "inline-flex" : "none";
  $("pillCategories").style.display = showPills ? "inline-flex" : "none";
}

// ------------------------------
// Agent Manager
// ------------------------------

function applyQuery(items) {
  const q = (state.query || "").trim().toLowerCase();
  if (!q) return items;
  return items.filter((a) => {
    const n = (a.name || "").toLowerCase();
    const c = (a.categoryId || "").toLowerCase();
    const d = (a.description || "").toLowerCase();
    return n.includes(q) || c.includes(q) || d.includes(q);
  });
}

function renderAgentManager() {
  const container = $("agentCategories");
  if (!container) return;

  const agents = applyQuery(state.agents);
  const byCat = new Map();
  for (const a of agents) {
    const cid = a.categoryId || "uncategorized";
    if (!byCat.has(cid)) byCat.set(cid, []);
    byCat.get(cid).push(a);
  }

  const cats = state.categories
    .filter((c) => byCat.has(c.categoryId))
    .map((c) => ({ ...c, agentCount: byCat.get(c.categoryId).length }))
    .sort((a, b) => (a.categoryId || "").localeCompare(b.categoryId || ""));

  // Keep expanded state stable.
  for (const c of cats) {
    if (!state.expanded.has(c.categoryId)) {
      state.expanded.add(c.categoryId);
    }
  }

  container.innerHTML = "";

  for (const c of cats) {
    const catId = c.categoryId;
    const open = state.expanded.has(catId);
    const box = document.createElement("div");
    box.className = "cat";
    box.innerHTML = `
      <div class="catHeader" data-cat="${escapeHtml(catId)}">
        <div class="catLeft">
          <div class="spark">✦</div>
          <div class="catName">${escapeHtml(c.displayName || catId)}</div>
        </div>
        <div class="catRight">
          <div class="countBadge">${c.agentCount}</div>
          <div class="chev">${open ? "▾" : "▸"}</div>
        </div>
      </div>
      <div class="catBody ${open ? "open" : ""}" id="cat_${escapeHtml(catId)}"></div>
    `;

    container.appendChild(box);

    const body = box.querySelector(".catBody");
    const list = (byCat.get(catId) || []).sort((a, b) => (a.name || "").localeCompare(b.name || ""));
    for (const a of list) {
      const row = document.createElement("div");
      row.className = "agentRow";
      row.innerHTML = `
        <div class="agentTop">
          <div class="agentName">${escapeHtml(a.name)}</div>
          <div class="agentMeta">tools: ${escapeHtml(a.toolCount ?? 0)}</div>
        </div>
        <div class="agentDesc">${escapeHtml(a.description || "")}</div>
      `;
      body.appendChild(row);
    }
  }

  $("pillAgents").textContent = `${agents.length} agents`;
  $("pillCategories").textContent = `${cats.length} categories`;

  container.querySelectorAll(".catHeader").forEach((h) => {
    h.addEventListener("click", () => {
      const cat = h.dataset.cat;
      if (!cat) return;
      if (state.expanded.has(cat)) state.expanded.delete(cat);
      else state.expanded.add(cat);
      renderAgentManager();
    });
  });
}

async function loadAgents() {
  const cats = await apiGet("/api/agents/categories");
  const agents = await apiGet("/api/agents");
  state.categories = cats.categories || [];
  state.agents = agents.agents || [];
  renderAgentManager();
}

function openModal() {
  $("modalBackdrop").classList.remove("hidden");
  $("agentName").value = "";
  $("agentDesc").value = "";
  $("agentCategory").value = "creator";
  $("agentName").focus();
}

function closeModal() {
  $("modalBackdrop").classList.add("hidden");
}

async function createAgent() {
  const name = $("agentName").value.trim();
  const categoryId = $("agentCategory").value.trim() || "creator";
  const description = $("agentDesc").value.trim();

  if (!name) return;

  await apiPost("/api/agents", { name, categoryId, description });
  closeModal();
  await loadAgents();
}

// ------------------------------
// MCP Manager (MVP)
// ------------------------------

function openMcpModal() {
  $("mcpModalBackdrop").classList.remove("hidden");
  $("mcpName").value = "";
  $("mcpTransport").value = "STDIO";
  $("mcpCommand").value = "";
  $("mcpArgs").value = "";
  $("mcpEndpoint").value = "";
  $("mcpEnabled").checked = false;
  $("mcpName").focus();
}

function closeMcpModal() {
  $("mcpModalBackdrop").classList.add("hidden");
}

function renderMcpServers(servers) {
  const box = $("mcpServers");
  if (!box) return;
  box.innerHTML = "";

  for (const s of servers) {
    const el = document.createElement("div");
    el.className = "evt";

    const name = s.name || "(unnamed)";
    const transport = s.transport || "";
    const enabled = s.enabled ? "enabled" : "disabled";
    const cmd = s.command || "";
    const args = (s.args || []).join(" ");
    const endpoint = s.endpoint || "";

    const detail = transport.toString().toUpperCase().includes("HTTP")
      ? endpoint
      : `${cmd} ${args}`.trim();

    el.innerHTML = `
      <div class="evtTop">
        <div class="evtStep">${escapeHtml(name)}</div>
        <div class="evtStatus"><span class="badge">${escapeHtml(enabled)}</span></div>
      </div>
      <div class="evtBody">
        <div class="mono">${escapeHtml(transport)}</div>
        <div>${escapeHtml(detail)}</div>
      </div>
    `;

    box.appendChild(el);
  }
}

async function loadMcpServers() {
  const res = await apiGet("/api/mcp/servers");
  state.mcpServers = res.servers || [];
  renderMcpServers(state.mcpServers);
}

async function createMcpServer() {
  const name = ($("mcpName").value || "").trim();
  const transportUi = ($("mcpTransport").value || "STDIO").trim();
  const command = ($("mcpCommand").value || "").trim();
  const args = ($("mcpArgs").value || "").trim().split(/\s+/).filter(Boolean);
  const endpoint = ($("mcpEndpoint").value || "").trim();
  const enabled = !!$("mcpEnabled").checked;

  if (!name) {
    alert("Name is required.");
    return;
  }

  const transportMap = {
    STDIO: "MCP_SERVER_TRANSPORT_STDIO",
    HTTP: "MCP_SERVER_TRANSPORT_HTTP",
    NPX: "MCP_SERVER_TRANSPORT_NPX",
    UVX: "MCP_SERVER_TRANSPORT_UVX"
  };
  const transport = transportMap[transportUi] || "MCP_SERVER_TRANSPORT_UNSPECIFIED";

  await apiPost("/api/mcp/servers", {
    name,
    transport,
    command,
    args,
    endpoint,
    enabled
  });

  closeMcpModal();
  await loadMcpServers();
}

// ------------------------------
// Graph Library (Workflow)
// ------------------------------

function setGraphSelectOptions(graphs) {
  const sel = $("graphSelect");
  if (!sel) return;

  const current = sel.value || "";
  sel.innerHTML = `<option value="">(unsaved)</option>`;

  for (const g of graphs) {
    const opt = document.createElement("option");
    opt.value = g.graphId;
    opt.textContent = `${g.name}  · v${g.version}`;
    sel.appendChild(opt);
  }

  // Keep selection if possible
  if (current && graphs.some((g) => g.graphId === current)) {
    sel.value = current;
  } else if (graphs.length > 0 && !sel.value) {
    sel.value = graphs[0].graphId;
  }
}

async function loadGraphs() {
  const res = await apiGet("/api/graphs");
  state.graphs = res.graphs || [];
  setGraphSelectOptions(state.graphs);

  // Auto-load selected graph if any
  const sel = $("graphSelect");
  if (sel && sel.value) {
    await loadGraph(sel.value);
  }
}

async function loadGraph(graphId) {
  if (!graphId) return;
  const res = await apiGet(`/api/graphs/${encodeURIComponent(graphId)}`);
  const g = res.graph;
  if (!g) return;

  $("graphNameInput").value = g.name || "";
  $("graph").value = g.source || "";
}

function newGraph() {
  const sel = $("graphSelect");
  if (sel) sel.value = "";
  $("graphNameInput").value = "";
  $("graph").value = "";
}

async function saveGraph() {
  const name = ($("graphNameInput").value || "").trim();
  const source = $("graph").value || "";
  const description = "";

  if (!name) {
    alert("Graph name is required.");
    return;
  }

  const sel = $("graphSelect");
  const graphId = sel?.value || "";

  if (graphId) {
    await apiPut(`/api/graphs/${encodeURIComponent(graphId)}`, { name, description, source });
  } else {
    const created = await apiPost("/api/graphs", { name, description, source });
    const newId = created.graph?.graphId;
    await loadGraphs();
    if (sel && newId) sel.value = newId;
  }

  await loadGraphs();
}

// ------------------------------
// Workflow (Run + SSE)
// ------------------------------

function appendEvent(evt) {
  const el = document.createElement("div");
  el.className = "evt";

  const step = evt.stepName || evt.step_id || "(step)";
  const st = evt.status || evt.status?.toString() || "UNKNOWN";
  const out = evt.outputChunk || evt.output_chunk || "";
  const err = evt.error || "";

  el.innerHTML = `
    <div class="evtTop">
      <div class="evtStep">${escapeHtml(step)}</div>
      <div class="evtStatus">${statusBadge(st)}</div>
    </div>
    <div class="evtBody">${escapeHtml(out || err)}</div>
  `;

  const box = $("events");
  box.appendChild(el);
  box.scrollTop = box.scrollHeight;
}

async function startRun() {
  $("events").innerHTML = "";
  const graph = $("graph").value || "";
  const input = $("input").value || "";

  const graphId = ($("graphSelect")?.value || "").trim();
  const graphName = ($("graphNameInput")?.value || "").trim();

  const payload = {
    graphSource: graph,
    graphId: graphId,
    graphName: graphName,
    input: input,
    sessionId: "demo-session"
  };

  const json = await apiPost("/api/runs", payload);
  currentRunId = json.runId || json.run_id;

  $("runId").textContent = currentRunId || "-";
  $("sseUrl").textContent = currentRunId ? `/api/runs/${currentRunId}/events` : "-";

  // Prefill Memory page input
  const mr = $("memoryRunId");
  if (mr) mr.value = currentRunId || "";

  connectSse();
  await loadRuns();
}

function connectSse() {
  if (!currentRunId) return;

  if (es) {
    es.close();
    es = null;
  }

  setConn(false, "Connecting…");
  es = new EventSource(`/api/runs/${currentRunId}/events`);

  es.addEventListener("hello", () => setConn(true, "Connected"));
  es.addEventListener("step", (e) => {
    try {
      const evt = JSON.parse(e.data);
      appendEvent(evt);
    } catch (err) {
      appendEvent({ stepName: "client", status: "FAILED", error: String(err), outputChunk: e.data });
    }
  });
  es.onerror = () => setConn(false, "Disconnected");
}

// ------------------------------
// Run History (Workflow)
// ------------------------------

function renderRuns(runs) {
  const box = $("runsList");
  if (!box) return;
  box.innerHTML = "";

  for (const r of runs) {
    const el = document.createElement("div");
    el.className = "evt";

    const name = r.graphName || "(no graph)";
    const runId = r.runId;
    const status = r.lastStatus || "";
    const preview = r.inputPreview || "";

    el.innerHTML = `
      <div class="evtTop">
        <div class="evtStep">${escapeHtml(name)}</div>
        <div class="evtStatus">${statusBadge(status)}</div>
      </div>
      <div class="evtBody"><span class="mono">${escapeHtml(runId)}</span> · ${escapeHtml(preview)}</div>
    `;

    el.addEventListener("click", async () => {
      await replayRun(runId);
    });

    box.appendChild(el);
  }
}

async function loadRuns() {
  const res = await apiGet("/api/runs");
  state.runs = res.runs || [];
  renderRuns(state.runs);
}

async function replayRun(runId) {
  if (!runId) return;

  // Stop SSE for replay.
  if (es) {
    es.close();
    es = null;
  }
  setConn(false, "Replay");

  const res = await apiGet(`/api/runs/${encodeURIComponent(runId)}`);

  currentRunId = res.runId || runId;
  $("runId").textContent = currentRunId || "-";
  $("sseUrl").textContent = currentRunId ? `/api/runs/${currentRunId}/events` : "-";

  const mr = $("memoryRunId");
  if (mr) mr.value = currentRunId || "";

  $("events").innerHTML = "";
  const events = res.events || [];
  for (const e of events) {
    appendEvent(e);
  }
}

// ------------------------------
// Memory
// ------------------------------

function renderMemoryResources(resources) {
  const box = $("memoryResources");
  if (!box) return;
  box.innerHTML = "";

  for (const r of resources) {
    const el = document.createElement("div");
    el.className = "evt";

    const memoryId = r.memoryId || "";
    const count = r.entryCount ?? 0;
    const scopeType = r.scope?.type || "";
    const scopeId = r.scope?.scopeId || "";

    el.innerHTML = `
      <div class="evtTop">
        <div class="evtStep">${escapeHtml(memoryId)}</div>
        <div class="evtStatus"><span class="badge">${escapeHtml(scopeType)}</span></div>
      </div>
      <div class="evtBody">${escapeHtml(scopeId)} · entries: ${escapeHtml(count)}</div>
    `;

    el.addEventListener("click", async () => {
      await loadMemoryEntries(memoryId);
    });

    box.appendChild(el);
  }
}

function renderMemory(entries) {
  const box = $("memoryList");
  box.innerHTML = "";
  for (const e of entries) {
    const el = document.createElement("div");
    el.className = "evt";
    const role = e.role || "unknown";
    const content = e.content || "";
    const scope = e.scope?.type || e.scope?.type?.toString() || "";
    el.innerHTML = `
      <div class="evtTop">
        <div class="evtStep">${escapeHtml(role)}</div>
        <div class="evtStatus"><span class="badge">${escapeHtml(scope)}</span></div>
      </div>
      <div class="evtBody">${escapeHtml(content)}</div>
    `;
    box.appendChild(el);
  }
  box.scrollTop = box.scrollHeight;
}

async function loadMemoryResources() {
  const res = await apiGet("/api/memory/resources");
  renderMemoryResources(res.resources || []);
}

async function loadMemoryEntries(memoryId) {
  if (!memoryId) return;
  const res = await apiGet(`/api/memory/${encodeURIComponent(memoryId)}/entries?limit=200`);
  renderMemory(res.entries || []);
}

async function searchMemory() {
  const q = ($("memorySearchQuery")?.value || "").trim();
  if (!q) return;
  const res = await apiPost("/api/memory/search", {
    query: q,
    limit: 50,
    scopeType: "MEMORY_SCOPE_TYPE_UNSPECIFIED"
  });
  renderMemory(res.entries || []);
}

async function refreshMemory() {
  const runId = ($("memoryRunId")?.value || "").trim();
  if (!runId) return;
  await loadMemoryEntries(`run:${runId}`);
}

// ------------------------------
// Boot
// ------------------------------

async function main() {
  setConn(false, "Disconnected");

  // Nav routing
  document.querySelectorAll(".navItem").forEach((b) => {
    b.addEventListener("click", () => {
      const page = b.dataset.page;
      showPage(page);
      if (page === "workflow") {
        loadGraphs().catch(() => {});
        loadRuns().catch(() => {});
      }
      if (page === "mcp") {
        loadMcpServers().catch(() => {});
      }
      if (page === "memory") {
        loadMemoryResources().catch(() => {});
      }
    });
  });

  $("searchInput").addEventListener("input", (e) => {
    state.query = e.target.value || "";
    renderAgentManager();
  });

  $("refreshBtn").addEventListener("click", async () => {
    if (state.page === "agents") {
      await loadAgents();
    }
    if (state.page === "workflow") {
      await loadGraphs();
      await loadRuns();
    }
    if (state.page === "mcp") {
      await loadMcpServers();
    }
    if (state.page === "memory") {
      await loadMemoryResources();
    }
  });

  $("primaryBtn").addEventListener("click", () => openModal());

  $("modalClose").addEventListener("click", closeModal);
  $("modalCancel").addEventListener("click", closeModal);
  $("modalBackdrop").addEventListener("click", (e) => {
    if (e.target && e.target.id === "modalBackdrop") closeModal();
  });
  $("modalCreate").addEventListener("click", async () => {
    try { await createAgent(); } catch (e) { alert(String(e)); }
  });

  $("startBtn").addEventListener("click", async () => {
    try {
      showPage("workflow");
      await startRun();
    } catch (e) {
      setConn(false, "Error");
      appendEvent({ stepName: "client", status: "FAILED", error: String(e), outputChunk: "" });
    }
  });

  $("graphSelect")?.addEventListener("change", async (e) => {
    const id = e.target.value;
    if (id) await loadGraph(id);
  });
  $("graphNewBtn")?.addEventListener("click", () => newGraph());
  $("graphSaveBtn")?.addEventListener("click", async () => {
    try { await saveGraph(); } catch (e) { alert(String(e)); }
  });
  $("refreshRunsBtn")?.addEventListener("click", async () => {
    await loadRuns();
  });

  $("mcpAddBtn")?.addEventListener("click", () => openMcpModal());
  $("mcpModalClose")?.addEventListener("click", closeMcpModal);
  $("mcpModalCancel")?.addEventListener("click", closeMcpModal);
  $("mcpModalBackdrop")?.addEventListener("click", (e) => {
    if (e.target && e.target.id === "mcpModalBackdrop") closeMcpModal();
  });
  $("mcpModalCreate")?.addEventListener("click", async () => {
    try { await createMcpServer(); } catch (e) { alert(String(e)); }
  });

  $("memorySearchBtn")?.addEventListener("click", async () => {
    try { await searchMemory(); } catch (e) { alert(String(e)); }
  });
  $("memoryRefreshResourcesBtn")?.addEventListener("click", async () => {
    await loadMemoryResources();
  });

  $("refreshMemoryBtn").addEventListener("click", async () => {
    await refreshMemory();
  });

  showPage("agents");
  await loadAgents();
}

main();



