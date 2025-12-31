// ============================================================
//  Axiom Reasoning Frontend (Dashboard)
//  - PaperReview-like layout
//  - Human-readable cards (step/proposal/consensus), not token spam
// ============================================================

const $ = (id) => document.getElementById(id);

const API = {
  listSessions: () => fetchJson("/api/sessions"),
  listWorkflows: () => fetchJson("/api/workflows"),
  dagSnapshot: (id) => fetchJson(`/api/sessions/${id}/dag`),
  dagExplain: (id, nodeId) => fetchJson(`/api/sessions/${id}/dag/${encodeURIComponent(nodeId)}`),
  createSession: (payload) =>
    fetch("/api/sessions", { method: "POST", body: JSON.stringify(payload) }).then((r) => r.json()),
  run: (id) => fetch(`/api/sessions/${id}/run`, { method: "POST" }).then((r) => r.json()),
  stop: (id) => fetch(`/api/sessions/${id}/stop`, { method: "POST" }).then((r) => r.json()),
  result: (id) => fetchJson(`/api/sessions/${id}/result`),
  steps: (id) => fetch(`/api/sessions/${id}/artifacts/theorems`).then((r) => r.text()),
  state: (id) => fetch(`/api/sessions/${id}/artifacts/state`).then((r) => r.text()),
};

const PIPELINE = ["LOADING", "INITIALIZING", "EXECUTING", "COMPLETE"];

const state = {
  sessions: [],
  current: null,
  es: null,
  cache: {}, // sessionId -> { status, phase, tokens, llm, progress, steps: Map, workers: Map, lastVoteStepId }
  rawOpen: false,
  rawDropped: 0,
  renderScheduled: false,
  lastRenderAt: 0,
  modal: { sessionId: null, workerId: null, headTs: 0 },
  ui: {
    pointerDownWorkerId: null,
    pointerDownAt: 0,
    pointerDownX: 0,
    pointerDownY: 0,
  },
};

// ============================================================
//  Run Config (persisted in localStorage)
// ============================================================
const RUNCFG_KEY = "axiom_reasoning.run_config.v1";

function readInt(id, fallback) {
  const el = $(id);
  if (!el) return fallback;
  const n = parseInt(el.value, 10);
  return Number.isFinite(n) ? n : fallback;
}

function readBool(id, fallback) {
  const el = $(id);
  if (!el) return fallback;
  return !!el.checked;
}

function loadRunConfig() {
  try {
    const raw = localStorage.getItem(RUNCFG_KEY);
    if (!raw) return null;
    const obj = JSON.parse(raw);
    return obj && typeof obj === "object" ? obj : null;
  } catch {
    return null;
  }
}

function saveRunConfig(cfg) {
  try {
    localStorage.setItem(RUNCFG_KEY, JSON.stringify(cfg));
  } catch {
    // ignore
  }
}

function applyRunConfigToForm(cfg) {
  if (!cfg) return;
  if ($("input-max-duration-minutes") && typeof cfg.maxDurationMinutes === "number") $("input-max-duration-minutes").value = String(cfg.maxDurationMinutes);
  if ($("input-max-llm-calls") && typeof cfg.maxLlmCalls === "number") $("input-max-llm-calls").value = String(cfg.maxLlmCalls);
  if ($("input-max-tokens") && typeof cfg.maxTokens === "number") $("input-max-tokens").value = String(cfg.maxTokens);
  if ($("input-max-depth") && typeof cfg.maxDepth === "number") $("input-max-depth").value = String(cfg.maxDepth);
  if ($("input-continue-on-failure") && typeof cfg.continueOnFailure === "boolean") $("input-continue-on-failure").checked = cfg.continueOnFailure;
  if ($("input-workflow") && typeof cfg.workflow === "string") $("input-workflow").value = cfg.workflow;
  if ($("input-language") && typeof cfg.language === "string") $("input-language").value = cfg.language;
}

async function loadWorkflowsIntoSelect() {
  const sel = $("input-workflow");
  if (!sel) return;
  try {
    const list = await API.listWorkflows();
    const workflows = Array.isArray(list) ? list : [];
    sel.innerHTML = workflows.map((w) => `<option value="${escapeAttr(w)}">${escapeHtml(w)}</option>`).join("");
    if (!workflows.length) {
      sel.innerHTML = `<option value="hypothesis_promotion_loop">hypothesis_promotion_loop</option>`;
    }

    // Prefer HPL as default when present (unless user already has persisted selection).
    const hasHpl = workflows.includes("hypothesis_promotion_loop");
    if (hasHpl && (!sel.value || !workflows.includes(sel.value))) {
      sel.value = "hypothesis_promotion_loop";
    }
  } catch {
    sel.innerHTML = `<option value="hypothesis_promotion_loop">hypothesis_promotion_loop</option>`;
  }
}

function fetchJson(url) {
  return fetch(url).then((r) => r.json());
}

function safeText(s) {
  return s == null ? "" : String(s);
}

function escapeHtml(str) {
  return safeText(str)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

function escapeAttr(str) {
  return escapeHtml(str).replaceAll('"', "&quot;");
}

function stepParentId(stepId) {
  if (!stepId) return null;
  const idx = stepId.indexOf(".gen[");
  if (idx > 0) return stepId.slice(0, idx);
  return null;
}

function parseAgUiMessageId(messageId) {
  // messageId format (server-side): msg:{sessionId}:{workerId}:{stepName}
  // stepName may contain ':' in rare cases, so we join the rest.
  const parts = String(messageId || "").split(":");
  if (parts.length < 4) return { sessionId: "", workerId: "coordinator", stepId: "" };
  return {
    sessionId: parts[1] || "",
    workerId: parts[2] || "coordinator",
    stepId: parts.slice(3).join(":") || "",
  };
}

function ensureSessionCache(sessionId) {
  if (!state.cache[sessionId]) {
    state.cache[sessionId] = {
      status: "pending",
      phase: "-",
      tokens: 0,
      llm: 0,
      progress: 0,
      steps: new Map(), // stepId -> { last: evt, proposals: Map }
      // PaperReview-like worker cache (stable DOM + history)
      workers: Object.create(null), // workerId -> { ... }
      graph: { iteration: 0, axioms: [], assumptions: [], theorems: [] },
      graphIndex: { axiomsById: Object.create(null), assumptionsById: Object.create(null), theoremsById: Object.create(null) },
      graphSelectedId: null,
      dag: null,          // snapshot from /api/sessions/{id}/dag
      dagExplain: null,   // explain result from /api/sessions/{id}/dag/{nodeId}
      lastVoteStepId: null,
      // AG-UI: message meta index (messageId -> meta)
      msgMeta: Object.create(null),
      // AG-UI: state store for dependency graph (STATE_SNAPSHOT/STATE_DELTA)
      aguiGraphState: null, // { kind, sessionId, graph: { iteration, axioms, assumptions, theorems, ... } }
      _stepsDirty: false,
    };
  }
  return state.cache[sessionId];
}

function getWorkerDisplayName(workerId) {
  if (workerId === "coordinator") return "Coordinator";
  const m = String(workerId).match(/worker-(\d+)/);
  if (m) return `Worker ${m[1]}`;
  return workerId;
}

function ensureWorker(cache, workerId) {
  const id = workerId || "coordinator";
  if (!cache.workers[id]) {
    cache.workers[id] = {
      id,
      name: getWorkerDisplayName(id),
      provider: "",
      tokenIndex: 0,
      status: "pending",
      streaming: false,
      streamContent: "",
      lastResponse: "",
      pendingAppend: "",
      errorMessage: "",
      history: [],
      stepId: "",
      stepType: "",
      dom: null,
    };
  }
  return cache.workers[id];
}

// ============================================================
//  AG-UI State (Graph) helpers
// ============================================================

function inferNodeKind(id) {
  const s = String(id || "").trim();
  if (!s) return "Unknown";
  if (/^A\d+/i.test(s) || /^[A-Z]\w*$/i.test(s) && s.startsWith("A")) return "Axiom";
  if (/^T\d+/i.test(s)) return "Theorem";
  if (/^H\d+/i.test(s)) return "Hypothesis";
  if (/^S\d+/i.test(s)) return "Assumption";
  return "Unknown";
}

function buildDagFromGraph(graph) {
  const g = graph && typeof graph === "object" ? graph : {};
  const axioms = Array.isArray(g.axioms) ? g.axioms : [];
  const assumptions = Array.isArray(g.assumptions) ? g.assumptions : [];
  const theorems = Array.isArray(g.theorems) ? g.theorems : [];

  const nodes = [];
  const edges = [];
  const byId = Object.create(null);

  function addNode(id, kind, label, proof) {
    const nid = String(id || "").trim();
    if (!nid || byId[nid]) return;
    const n = { id: nid, kind: kind || inferNodeKind(nid), label: label || "", proof: proof || "" };
    byId[nid] = n;
    nodes.push(n);
  }

  // Axioms: use the same ID heuristic as renderGraph (A1: ... => "A1")
  const axId = (line, idx) => {
    const m = String(line || "").match(/^([A-Za-z]\w*)\s*:/);
    return m ? m[1] : `A${idx + 1}`;
  };
  for (let i = 0; i < axioms.length; i++) addNode(axId(axioms[i], i), "Axiom", axioms[i], "");

  // Assumptions
  for (const a of assumptions) {
    const id = a && a.id ? a.id : "";
    const label = a && (a.statement || a.id) ? (a.statement || a.id) : "";
    addNode(id, "Assumption", label, "");
  }

  // Theorems + edges
  for (const t of theorems) {
    if (!t) continue;
    const id = t.id || "";
    const label = t.statement || t.id || "";
    const proof = t.proof || "";
    addNode(id, "Theorem", label, proof);

    const deps = Array.isArray(t.dependsOn) ? t.dependsOn : (Array.isArray(t.depends_on) ? t.depends_on : []);
    for (const d of deps) {
      const depId = String(d || "").trim();
      if (!depId) continue;
      if (!byId[depId]) addNode(depId, inferNodeKind(depId), depId, "");
      edges.push({ fromId: depId, toId: id });
    }
  }

  return { nodes, edges };
}

function applyGraphSnapshot(cache, sessionId, graph) {
  cache.graph = {
    iteration: graph && typeof graph.iteration === "number" ? graph.iteration : (graph && graph.iteration ? graph.iteration : (cache.graph.iteration || 0)),
    axioms: Array.isArray(graph && graph.axioms) ? graph.axioms : [],
    assumptions: Array.isArray(graph && graph.assumptions) ? graph.assumptions : [],
    theorems: Array.isArray(graph && graph.theorems) ? graph.theorems : [],
  };

  $("graph-iter").textContent = String(cache.graph.iteration || 0);

  // Build index for inspector (axiomsById / theoremsById / assumptionsById)
  const axiomsById = Object.create(null);
  const axId = (line, idx) => {
    const m = String(line || "").match(/^([A-Za-z]\w*)\s*:/);
    return m ? m[1] : `A${idx + 1}`;
  };
  for (let i = 0; i < cache.graph.axioms.length; i++) {
    const line = cache.graph.axioms[i];
    axiomsById[axId(line, i)] = line;
  }
  const theoremsById = Object.create(null);
  for (const t of cache.graph.theorems) {
    if (t && t.id) theoremsById[t.id] = t;
  }
  const assumptionsById = Object.create(null);
  for (const a of cache.graph.assumptions) {
    if (a && a.id) assumptionsById[a.id] = a;
  }
  cache.graphIndex = { axiomsById, assumptionsById, theoremsById };

  // Build a local DAG snapshot (no extra HTTP request per update).
  cache.dag = buildDagFromGraph(cache.graph);
  renderDagGraph(cache.dag, cache.graphSelectedId);
  renderGraphInspector(cache);
}

function applyAgUiGraphDelta(cache, deltaOps) {
  if (!cache.aguiGraphState || !cache.aguiGraphState.graph) return false;
  const ops = Array.isArray(deltaOps) ? deltaOps : [];
  let changed = false;

  const g = cache.aguiGraphState.graph;
  g.axioms = Array.isArray(g.axioms) ? g.axioms : [];
  g.assumptions = Array.isArray(g.assumptions) ? g.assumptions : [];
  g.theorems = Array.isArray(g.theorems) ? g.theorems : [];

  for (const op of ops) {
    if (!op || typeof op !== "object") continue;
    const kind = op.op;
    const path = op.path;
    const val = op.value;
    if (kind === "replace") {
      if (path === "/graph/iteration") { g.iteration = val; changed = true; }
      else if (path === "/graph/axioms") { g.axioms = Array.isArray(val) ? val : []; changed = true; }
      else if (path === "/graph/assumptions") { g.assumptions = Array.isArray(val) ? val : []; changed = true; }
      else if (path === "/graph/theorems") { g.theorems = Array.isArray(val) ? val : []; changed = true; }
    } else if (kind === "add") {
      if (path === "/graph/axioms/-") { g.axioms.push(val); changed = true; }
      else if (path === "/graph/assumptions/-") { g.assumptions.push(val); changed = true; }
      else if (path === "/graph/theorems/-") { g.theorems.push(val); changed = true; }
    }
  }
  return changed;
}

function createWorkerCardDom(workerId) {
  const card = document.createElement("div");
  card.className = "worker-card";
  card.dataset.workerId = workerId;

  const header = document.createElement("div");
  header.className = "worker-header";

  const info = document.createElement("div");
  info.className = "worker-info";

  const nameEl = document.createElement("div");
  nameEl.className = "worker-name";

  const metaEl = document.createElement("div");
  metaEl.className = "worker-meta";

  info.appendChild(nameEl);
  info.appendChild(metaEl);

  const badgeEl = document.createElement("span");
  badgeEl.className = "badge pending";
  badgeEl.textContent = "pending";

  header.appendChild(info);
  header.appendChild(badgeEl);

  const body = document.createElement("div");
  body.className = "worker-body";

  const contentEl = document.createElement("div");
  contentEl.className = "worker-content";

  const emptyEl = document.createElement("div");
  emptyEl.className = "worker-empty";
  emptyEl.textContent = "Waiting for response...";

  body.appendChild(contentEl);
  body.appendChild(emptyEl);

  card.appendChild(header);
  card.appendChild(body);

  return { card, nameEl, metaEl, badgeEl, contentEl, emptyEl };
}

function updateWorkerCardDom(dom, w) {
  dom.nameEl.textContent = w.name || w.id;
  const provider = w.provider || "-";
  const tokens = w.tokenIndex || 0;
  const stepType = w.stepType || "-";
  const err = w.errorMessage ? ` · FAIL: ${String(w.errorMessage).slice(0, 80)}` : "";
  dom.metaEl.textContent = `${provider} · ${tokens} tokens · ${stepType}` + (w.streaming ? " · streaming" : "") + (w.status === "error" ? err : "");

  const statusClass = w.streaming ? "running" : (w.status || "pending");
  const statusText = w.streaming ? "streaming" : (w.status || "pending");
  dom.badgeEl.className = `badge ${statusClass}`;
  dom.badgeEl.textContent = statusText;
  dom.card.classList.toggle("streaming", !!w.streaming);

  // Content: show full content, keep scroll stable
  const displayContent = w.streamContent || w.lastResponse || "";
  if (displayContent) {
    dom.contentEl.style.display = "";
    dom.emptyEl.style.display = "none";

    const atBottom = dom.contentEl.scrollTop + dom.contentEl.clientHeight >= dom.contentEl.scrollHeight - 8;
    const oldTop = dom.contentEl.scrollTop;

    // Streaming perf:
    // - Prefer incremental append during streaming (PaperReview silky mode)
    // - Fallback to full replace when something is inconsistent (e.g. card recreated)
    const pending = w.pendingAppend || "";
    if (pending) {
      const current = dom.contentEl.textContent || "";
      const okToAppend = (current.length + pending.length === displayContent.length) && displayContent.endsWith(pending);
      if (okToAppend) {
        dom.contentEl.insertAdjacentText("beforeend", pending);
      } else {
        dom.contentEl.textContent = displayContent;
      }
      w.pendingAppend = "";
      dom.contentEl.scrollTop = atBottom ? dom.contentEl.scrollHeight : oldTop;
    } else if (dom.contentEl.textContent !== displayContent) {
      dom.contentEl.textContent = displayContent;
      dom.contentEl.scrollTop = atBottom ? dom.contentEl.scrollHeight : oldTop;
    }
    dom.contentEl.classList.toggle("streaming-text", !!w.streaming);
  } else {
    dom.contentEl.style.display = "none";
    dom.emptyEl.style.display = "";
  }
}

function scheduleRender(sessionId, force = false) {
  if (!state.current || state.current !== sessionId) return;

  const now = Date.now();
  if (!force && now - state.lastRenderAt < 120) {
    if (state.renderScheduled) return;
  }

  if (state.renderScheduled) return;
  state.renderScheduled = true;
  requestAnimationFrame(() => {
    state.renderScheduled = false;
    state.lastRenderAt = Date.now();
    const cache = ensureSessionCache(sessionId);
    renderTop(cache);
    renderVoting(cache);
    if (force || cache._stepsDirty) {
      cache._stepsDirty = false;
      renderSteps(cache);
    }
  });
}

// ─────────────────────────────────────────────────────────────
//  Render Scheduling (avoid re-render per token)
// ─────────────────────────────────────────────────────────────
let _workersRenderRaf = 0;
let _workersRenderCache = null;

function scheduleRenderWorkers(cache) {
  _workersRenderCache = cache;
  if (_workersRenderRaf) return;
  _workersRenderRaf = requestAnimationFrame(() => {
    _workersRenderRaf = 0;
    if (_workersRenderCache) {
      renderWorkers(_workersRenderCache);
      updateModalIfOpen(_workersRenderCache);
    }
  });
}

function setDownloads(sessionId, enabled) {
  const aState = $("dl-state");
  const aSteps = $("dl-steps");
  const aReview = $("dl-review");
  const aTranscript = $("dl-transcript");
  if (aState) aState.href = `/api/sessions/${sessionId}/artifacts/state`;
  if (aSteps) aSteps.href = `/api/sessions/${sessionId}/artifacts/theorems`;
  if (aReview) aReview.href = `/api/sessions/${sessionId}/llm/review`;
  if (aTranscript) aTranscript.href = `/api/sessions/${sessionId}/llm/transcript`;
  if (enabled) {
    if (aState) aState.classList.remove("disabled");
    if (aSteps) aSteps.classList.remove("disabled");
    if (aReview) aReview.classList.remove("disabled");
    if (aTranscript) aTranscript.classList.remove("disabled");
  } else {
    if (aState) aState.classList.add("disabled");
    if (aSteps) aSteps.classList.add("disabled");
    if (aReview) aReview.classList.add("disabled");
    if (aTranscript) aTranscript.classList.add("disabled");
  }
}

function renderSessions() {
  const el = $("session-list");
  if (!state.sessions || state.sessions.length === 0) {
    el.classList.add("empty-state");
    el.innerHTML = "No sessions";
    return;
  }

  el.classList.remove("empty-state");
  el.innerHTML = state.sessions
    .map((s) => {
      const active = s.id === state.current ? "active" : "";
      const sub = `progress=${s.progressPercent ?? 0}% · llm=${s.totalLlmCalls ?? 0} · tokens=${s.totalTokens ?? 0}`;
      return `
        <div class="session-item ${active}" data-id="${s.id}">
          <div class="line1">
            <div class="id">${escapeHtml(s.id)}</div>
            <div class="status">${escapeHtml(s.status)}</div>
          </div>
          <div class="line2">${escapeHtml(sub)}</div>
        </div>
      `;
    })
    .join("");

  for (const item of el.querySelectorAll(".session-item")) {
    item.addEventListener("click", () => selectSession(item.getAttribute("data-id")));
  }
}

function renderPipeline(phaseText) {
  const phase = (phaseText ?? "").toUpperCase();
  const active = PIPELINE.find((p) => phase.includes(p)) || (phase.includes("COMPLETE") ? "COMPLETE" : "EXECUTING");
  const activeIdx = PIPELINE.indexOf(active);
  $("pipeline-steps").innerHTML = PIPELINE.map((p, idx) => {
    const cls = idx < activeIdx ? "pipe-step done" : idx === activeIdx ? "pipe-step active" : "pipe-step";
    return `<div class="${cls}">${p}</div>`;
  }).join("");
}

function renderTop(cache) {
  $("status-text").textContent = cache.status;
  $("stat-phase").textContent = cache.phase || "-";
  $("stat-tokens").textContent = String(cache.tokens || 0);
  $("stat-llm").textContent = String(cache.llm || 0);
  $("progress-bar-inner").style.width = `${cache.progress || 0}%`;
  renderPipeline(cache.phase || "");
}

function renderVoting(cache) {
  const host = $("voting-body");
  const voteStepId = cache.lastVoteStepId;
  if (!voteStepId) {
    host.classList.add("empty-state");
    host.textContent = "Waiting for vote steps";
    $("vote-k").textContent = "-";
    return;
  }

  const step = cache.steps.get(voteStepId);
  if (!step || !step.last) return;
  const evt = step.last;

  $("vote-k").textContent = String(evt.voteK || "-");

  host.classList.remove("empty-state");
  const consensus = evt.assistantResponse ? `<div class="mono">${escapeHtml(evt.assistantResponse)}</div>` : "";
  host.innerHTML = `
    <div class="step" data-step-id="${escapeHtml(voteStepId)}">
      <div class="top">
        <div>
          <div class="sid">${escapeHtml(evt.stepId || voteStepId)}</div>
          <div class="tag">type=${escapeHtml(evt.stepType)} · status=${escapeHtml(evt.stepStatus)}</div>
        </div>
        <div class="kv">
          <div class="pill">round ${evt.voteRound ?? 0}/${evt.voteMaxRounds ?? 0}</div>
          <div class="pill">votes ${evt.voteCurrentVotes ?? 0}/${evt.voteK ?? 0}</div>
        </div>
      </div>
      <div class="msg">${escapeHtml(evt.message || "")}</div>
      ${consensus}
    </div>
  `;
}

function renderWorkers(cache) {
  const host = $("worker-grid");
  const workers = cache.workers || {};
  const ids = Object.keys(workers);

  $("worker-count").textContent = String(ids.length || 0);

  if (!ids.length) {
    host.classList.add("empty-state");
    host.textContent = "Waiting for workers";
    return;
  }

  // Remove empty placeholder text node without destroying existing cards
  host.classList.remove("empty-state");
  for (const n of Array.from(host.childNodes)) {
    if (n.nodeType === Node.TEXT_NODE && (n.textContent || "").trim().length > 0) {
      host.removeChild(n);
    }
  }

  for (const workerId of ids.sort()) {
    const w = workers[workerId];
    if (!w.dom) {
      w.dom = createWorkerCardDom(workerId);
      host.appendChild(w.dom.card);
    }
    updateWorkerCardDom(w.dom, w);
  }
}

function renderSteps(cache) {
  const host = $("steps");
  const entries = Array.from(cache.steps.entries());
  if (entries.length === 0) {
    host.classList.add("empty-state");
    host.textContent = "No steps yet";
    return;
  }
  host.classList.remove("empty-state");

  // Sort by depth then stepId
  entries.sort((a, b) => {
    const da = a[1].last?.depth ?? 0;
    const db = b[1].last?.depth ?? 0;
    if (da !== db) return da - db;
    return a[0].localeCompare(b[0]);
  });

  host.innerHTML = entries
    .map(([stepId, s]) => {
      const evt = s.last || {};
      const pills = [];
      if (evt.workerId) pills.push(`worker=${evt.workerId}`);
      if (evt.depth != null) pills.push(`depth=${evt.depth}`);
      if (evt.stepType) pills.push(`type=${evt.stepType}`);
      if (evt.stepStatus) pills.push(`status=${evt.stepStatus}`);
      if (evt.voteK) pills.push(`vote ${evt.voteCurrentVotes}/${evt.voteK}`);

      const preview = evt.assistantResponsePreview || "";
      const body = evt.assistantResponse || "";

      // proposals
      const proposals = s.proposals ? Array.from(s.proposals.values()) : [];
      const proposalHtml = proposals.length
        ? `<details><summary>Proposals (${proposals.length})</summary>${proposals
            .map((p) => {
              const pv = p.assistantResponsePreview || p.assistantResponse || p.message || "";
              return `<div class="mono">${escapeHtml(p.stepId || "")}\n${escapeHtml(pv)}</div>`;
            })
            .join("")}</details>`
        : "";

      return `
        <div class="step" data-step-id="${escapeHtml(stepId)}">
          <div class="top">
            <div>
              <div class="sid">${escapeHtml(stepId)}</div>
              <div class="tag">${escapeHtml(evt.message || "")}</div>
            </div>
            <div class="kv">${pills.map((x) => `<div class="pill">${escapeHtml(x)}</div>`).join("")}</div>
          </div>
          ${proposalHtml}
        </div>
      `;
    })
    .join("");
}

function renderResultBox(text, ok) {
  const host = $("result-body");
  host.classList.remove("empty-state");
  host.innerHTML = `<div class="mono">${escapeHtml(text || (ok ? "(empty)" : "(failed)"))}</div>`;
}

function appendRaw(evt) {
  if (!state.rawOpen) return;
  const el = $("raw");
  // Debug only: keep this lightweight to avoid killing streaming performance.
  // NOTE: This still does work; that's why it's collapsed by default.
  const s = JSON.stringify(evt);
  el.insertAdjacentText("beforeend", (el.textContent ? "\n" : "") + s);
  // Soft cap: if debug view gets too big, keep last ~200KB.
  if (el.textContent.length > 200_000) {
    el.textContent = el.textContent.slice(-180_000);
  }
  el.scrollTop = el.scrollHeight;
}

function openWorkerModal(workerId) {
  const cache = state.current ? ensureSessionCache(state.current) : null;
  const worker = cache?.workers?.[workerId];
  if (!worker) return;

  state.modal.sessionId = state.current;
  state.modal.workerId = workerId;
  state.modal.headTs = worker.history?.[0]?.timestamp || 0;

  renderModal(worker);
  $("modal").classList.remove("hidden");
  document.body.style.overflow = "hidden";
}

function renderModal(worker) {
  $("modal-title").textContent = worker.name || worker.id;
  const err = worker.status === "error" && worker.errorMessage ? ` · FAIL: ${String(worker.errorMessage).slice(0, 120)}` : "";
  $("modal-subtitle").textContent =
    `${worker.provider || "-"} · ${worker.tokenIndex || 0} tokens · ${worker.history.length} conversations` +
    (worker.streaming ? " · streaming..." : "") + err;

  const body = $("modal-body");
  body.innerHTML = "";

  if (!worker.history.length) {
    body.innerHTML = '<div class="empty-state">No conversation history</div>';
    return;
  }

  worker.history.forEach((h, idx) => {
    const item = document.createElement("details");
    item.className = "chat-item";
    item.open = idx === 0;

    const summary = document.createElement("summary");
    summary.className = "chat-header";
    summary.innerHTML = `
      <div class="chat-phase">${escapeHtml(h.phase || "LLM Call")}</div>
      <div class="chat-time">${new Date(h.timestamp).toLocaleTimeString()}</div>
    `;
    item.appendChild(summary);

    const bodyDiv = document.createElement("div");
    bodyDiv.className = "chat-body";

    if (h.system) bodyDiv.appendChild(buildChatSection("System Prompt", h.system, false));
    if (h.user) bodyDiv.appendChild(buildChatSection("User Prompt", h.user, true));

    const respSection = buildChatSection("Response", h.response || "", true);
    const respContent = respSection.querySelector(".chat-content");
    if (respContent) {
      respContent.dataset.role = "modal-response";
      respContent.dataset.idx = String(idx);
      setChatContentText(respContent, h.response || "", worker.streaming && idx === 0);
    }
    bodyDiv.appendChild(respSection);

    item.appendChild(bodyDiv);
    body.appendChild(item);
  });
}

function buildChatSection(label, text, open = true) {
  const section = document.createElement("details");
  section.className = "chat-section";
  section.open = !!open;

  const summary = document.createElement("summary");
  summary.className = "chat-label";
  summary.textContent = label;

  const content = document.createElement("div");
  content.className = "chat-content";
  setChatContentText(content, text, false);

  section.appendChild(summary);
  section.appendChild(content);
  return section;
}

function setChatContentText(el, text, showWaitingWhenEmpty) {
  const val = text || "";
  if (!val && showWaitingWhenEmpty) {
    el.textContent = "Waiting...";
    el.style.color = "var(--text-muted)";
    return;
  }
  el.textContent = val;
  el.style.color = "";
}

function updateModalIfOpen(cache) {
  const modalEl = $("modal");
  if (modalEl.classList.contains("hidden")) return;

  const { sessionId, workerId } = state.modal || {};
  if (!sessionId || !workerId) return;
  if (sessionId !== state.current) return;

  const worker = cache?.workers?.[workerId];
  if (!worker) return;

  const headTs = worker.history?.[0]?.timestamp || 0;
  if (headTs && headTs !== state.modal.headTs) {
    state.modal.headTs = headTs;
    renderModal(worker);
    return;
  }

  $("modal-title").textContent = worker.name || worker.id;
  const err = worker.status === "error" && worker.errorMessage ? ` · FAIL: ${String(worker.errorMessage).slice(0, 120)}` : "";
  $("modal-subtitle").textContent =
    `${worker.provider || "-"} · ${worker.tokenIndex || 0} tokens · ${worker.history.length} conversations` +
    (worker.streaming ? " · streaming..." : "") + err;

  const respEl = $("modal-body").querySelector('[data-role="modal-response"][data-idx="0"]');
  if (!respEl) return;
  const latest = worker.history?.[0]?.response || "";
  setChatContentText(respEl, latest, !!worker.streaming);
}

function closeModal() {
  $("modal").classList.add("hidden");
  document.body.style.overflow = "";
  state.modal.sessionId = null;
  state.modal.workerId = null;
  state.modal.headTs = 0;
}

function applyEvent(sessionId, evt) {
  // ============================================================
  //  AG-UI compatibility
  //
  //  Server may emit AG-UI events (RUN_*/STEP_*/TEXT_*/STATE_*).
  //  For backward-compatible UI rendering, we wrap the old domain events
  //  (ProgressEvent/GraphEvent/ResultEvent/ErrorEvent) inside:
  //    { type:"CUSTOM", name:"aevatar.axiom.*", value:{ type:"ProgressEvent", ... } }
  //
  //  Here we unwrap that payload so the rest of the UI code can stay unchanged.
  // ============================================================
  if (evt && evt.type === "CUSTOM" && evt.value && typeof evt.value === "object" && evt.value.type) {
    evt = evt.value;
  }

  const cache = ensureSessionCache(sessionId);

  // ============================================================
  //  AG-UI: status snapshot (no replay needed)
  // ============================================================
  if (evt && evt.type === "CUSTOM" && evt.name === "aevatar.axiom.status_snapshot") {
    const v = evt.value && typeof evt.value === "object" ? evt.value : null;
    if (!v) return;

    if (typeof v.status === "string") cache.status = v.status;
    if (typeof v.phase === "string") cache.phase = v.phase;
    if (typeof v.totalTokens === "number") cache.tokens = v.totalTokens;
    if (typeof v.totalLlmCalls === "number") cache.llm = v.totalLlmCalls;
    if (typeof v.progressPercent === "number") cache.progress = v.progressPercent;

    scheduleRender(sessionId, true);
    return;
  }

  // ============================================================
  //  AG-UI: messages snapshot (reconnect bootstrap)
  // ============================================================
  if (evt && evt.type === "MESSAGES_SNAPSHOT") {
    const msgs = Array.isArray(evt.messages) ? evt.messages : [];
    for (let i = 0; i < msgs.length; i++) {
      const m = msgs[i];
      if (!m || typeof m !== "object") continue;
      if (m.role !== "assistant") continue;

      const messageId = m.id || "";
      const content = m.content || "";
      if (!messageId || !content) continue;

      const p = parseAgUiMessageId(messageId);
      const wid = p.workerId || "coordinator";
      const stepId = p.stepId || "";
      const w = ensureWorker(cache, wid);

      // Seed history only (don't override an active stream).
      if (w.streaming) continue;

      const exists = w.history && w.history.some((h) => h.stepId === stepId && h.response === content);
      if (!exists) {
        w.history.unshift({
          timestamp: Date.now(),
          stepId,
          phase: "LLM Call",
          status: "Completed",
          system: "",
          user: "",
          response: content,
        });
        if (w.history.length > 12) w.history.pop();
      }

      w.status = w.status === "error" ? w.status : "completed";
      w.streaming = false;
      w.streamContent = "";
      w.pendingAppend = "";
      w.lastResponse = content;
      if (stepId) w.stepId = stepId;
      w.stepType = w.stepType || "llm_call";
    }

    scheduleRenderWorkers(cache);
    scheduleRender(sessionId, true);
    return;
  }

  // ============================================================
  //  AG-UI: message metadata (worker/provider/prompts)
  // ============================================================
  if (evt && evt.type === "CUSTOM" && evt.name === "aevatar.axiom.message_meta") {
    const v = evt.value && typeof evt.value === "object" ? evt.value : null;
    if (!v) return;
    const messageId = v.messageId || "";
    if (messageId) cache.msgMeta[messageId] = v;

    const parsed = messageId ? parseAgUiMessageId(messageId) : { workerId: "coordinator", stepId: "" };
    const wid = v.workerId || parsed.workerId || "coordinator";
    const w = ensureWorker(cache, wid);
    if (v.providerName) w.provider = v.providerName;
    if (typeof v.tokenIndex === "number") w.tokenIndex = v.tokenIndex;
    if (v.stepId) w.stepId = v.stepId;
    if (v.stepType) w.stepType = v.stepType;

    // Fill prompts for the matching history item (by stepId), not just the head.
    const stepId = v.stepId || parsed.stepId || "";
    if (w.history && w.history.length) {
      let target = w.history[0];
      if (stepId) {
        for (let i = 0; i < w.history.length; i++) {
          if (w.history[i] && w.history[i].stepId === stepId) {
            target = w.history[i];
            break;
          }
        }
      }
      if (target) {
        if (!target.system && v.systemPrompt) target.system = v.systemPrompt;
        if (!target.user && v.userPrompt) target.user = v.userPrompt;
      }
    }

    scheduleRenderWorkers(cache);
    return;
  }

  // ============================================================
  //  AG-UI: streaming text messages (primary channel for token deltas)
  // ============================================================
  if (evt && evt.type === "TEXT_MESSAGE_START") {
    const messageId = evt.messageId || "";
    const p = parseAgUiMessageId(messageId);
    const wid = p.workerId || "coordinator";
    const stepId = p.stepId || "";
    const w = ensureWorker(cache, wid);

    w.status = "running";
    w.streaming = true;
    w.stepType = w.stepType || "llm_call";
    if (stepId) w.stepId = stepId;

    // New message => reset card content so we don't append to previous step output.
    const head = w.history && w.history.length ? w.history[0] : null;
    const isSameStep = head && head.stepId === stepId;
    if (!isSameStep) {
      w.pendingAppend = "";
      w.streamContent = "";
      w.lastResponse = "";

      w.history.unshift({
        timestamp: Date.now(),
        stepId,
        phase: "LLM Call",
        status: "Running",
        system: "",
        user: "",
        response: "",
      });
      if (w.history.length > 12) w.history.pop();
    }

    // Apply any meta we already have.
    const meta = cache.msgMeta[messageId];
    if (meta) {
      if (meta.providerName) w.provider = meta.providerName;
      if (typeof meta.tokenIndex === "number") w.tokenIndex = meta.tokenIndex;
      if (w.history && w.history.length) {
        let target = w.history[0];
        if (stepId) {
          for (let i = 0; i < w.history.length; i++) {
            if (w.history[i] && w.history[i].stepId === stepId) {
              target = w.history[i];
              break;
            }
          }
        }
        if (target) {
          if (!target.system && meta.systemPrompt) target.system = meta.systemPrompt;
          if (!target.user && meta.userPrompt) target.user = meta.userPrompt;
        }
      }
    }

    scheduleRenderWorkers(cache);
    return;
  }

  if (evt && evt.type === "TEXT_MESSAGE_CONTENT") {
    const messageId = evt.messageId || "";
    const delta = evt.delta || "";
    if (!delta) return;

    const p = parseAgUiMessageId(messageId);
    const wid = p.workerId || "coordinator";
    const stepId = p.stepId || "";
    const w = ensureWorker(cache, wid);

    // Ensure we have a head entry for this step.
    const head = w.history && w.history.length ? w.history[0] : null;
    const isSameStep = head && head.stepId === stepId;
    if (!isSameStep) {
      w.pendingAppend = "";
      w.streamContent = "";
      w.lastResponse = "";

      w.history.unshift({
        timestamp: Date.now(),
        stepId,
        phase: "LLM Call",
        status: "Running",
        system: "",
        user: "",
        response: "",
      });
      if (w.history.length > 12) w.history.pop();
    }

    w.streaming = true;
    w.status = "running";
    w.stepType = w.stepType || "llm_call";
    if (stepId) w.stepId = stepId;

    // Append (DOM will append via pendingAppend in rAF)
    w.pendingAppend = (w.pendingAppend || "") + delta;
    w.streamContent = (w.streamContent || "") + delta;
    if (w.history.length) w.history[0].response = (w.history[0].response || "") + delta;

    scheduleRenderWorkers(cache);
    return;
  }

  if (evt && evt.type === "TEXT_MESSAGE_END") {
    const messageId = evt.messageId || "";
    const p = parseAgUiMessageId(messageId);
    const wid = p.workerId || "coordinator";
    const w = ensureWorker(cache, wid);

    w.streaming = false;
    // If the step later fails, ProgressEvent will override status to error.
    w.status = w.status === "error" ? w.status : "completed";
    if (w.streamContent) w.lastResponse = w.streamContent;

    scheduleRenderWorkers(cache);
    return;
  }

  // ============================================================
  //  AG-UI: state sync for dependency graph
  // ============================================================
  if (evt && evt.type === "STATE_SNAPSHOT") {
    const snap = evt.snapshot && typeof evt.snapshot === "object" ? evt.snapshot : null;
    // We currently only publish graph snapshots/deltas via AG-UI state events.
    if (snap && snap.graph && typeof snap.graph === "object") {
      cache.aguiGraphState = snap;
      applyGraphSnapshot(cache, sessionId, snap.graph);
      return;
    }
  }

  if (evt && evt.type === "STATE_DELTA") {
    const ops = evt.delta;
    if (applyAgUiGraphDelta(cache, ops)) {
      const g = cache.aguiGraphState && cache.aguiGraphState.graph ? cache.aguiGraphState.graph : null;
      if (g) applyGraphSnapshot(cache, sessionId, g);
    }
    return;
  }

  if (evt.type === "ProgressEvent") {
    cache.phase = evt.phase || cache.phase;
    cache.progress = evt.progressPercent ?? cache.progress;
    cache.tokens = evt.totalTokens ?? cache.tokens;
    cache.llm = evt.totalLlmCalls ?? cache.llm;
    cache.status = "running";

    const workerId = evt.workerId || "coordinator";
    if (!cache.workers[workerId]) {
      cache.workers[workerId] = {
        id: workerId,
        name: getWorkerDisplayName(workerId),
        provider: "",
        tokenIndex: 0,
        status: "pending",
        streaming: false,
        streamContent: "",
        lastResponse: "",
        pendingAppend: "",
        errorMessage: "",
        history: [],
        stepId: "",
        stepType: "",
        dom: null,
      };
    }
    const w = cache.workers[workerId];
    w.stepId = evt.stepId || "";
    w.stepType = evt.stepType || "";
    if (evt.providerName) w.provider = evt.providerName;
    if (typeof evt.tokenIndex === "number") w.tokenIndex = evt.tokenIndex;

    const st = String(evt.stepStatus || "").toLowerCase();
    w.status = st.includes("failed") ? "error" : st.includes("completed") ? "completed" : "running";
    if (w.status === "error") {
      w.errorMessage = evt.error || evt.message || w.errorMessage || "failed";
    } else if (w.status === "completed") {
      w.errorMessage = "";
    }

    const sid = evt.stepId || evt.phase || "main";
    const parent = stepParentId(sid);
    if (parent) {
      // proposal under vote
      if (!cache.steps.has(parent)) cache.steps.set(parent, { last: null, proposals: new Map() });
      const p = cache.steps.get(parent);
      p.proposals.set(sid, evt);
    }

    if (!cache.steps.has(sid)) cache.steps.set(sid, { last: null, proposals: new Map() });
    const step = cache.steps.get(sid);
    step.last = evt;

    if (String(evt.stepType || "").toLowerCase() === "vote") {
      cache.lastVoteStepId = sid;
    }

    // Worker history (PaperReview-like):
    // - We treat each completed llm_call as one history item.
    // - During streaming, we keep updating the head item to show live text.
    if (String(evt.stepType || "").toLowerCase() === "llm_call") {
      const now = Date.now();
      const isCompleted = st.includes("completed");
      const isFailed = st.includes("failed");
      const delta = evt.tokenDelta || "";
      const finalBody = evt.assistantResponse || "";
      const content = finalBody || evt.assistantResponsePreview || "";

      const head = w.history[0];
      const isSameStep = head && head.stepId === (evt.stepId || "");
      if (!isSameStep) {
        // New step: reset in-card streaming buffer, otherwise we'll append to last step's output.
        w.pendingAppend = "";
        w.streamContent = "";
        w.lastResponse = "";

        w.history.unshift({
          timestamp: now,
          stepId: evt.stepId || "",
          phase: evt.phase || "LLM Call",
          status: evt.stepStatus || "",
          system: evt.systemPrompt || "",
          user: evt.userPrompt || "",
          response: "",
        });
        if (w.history.length > 12) w.history.pop();
      }
      if (w.history.length) {
        if (delta) w.history[0].response = (w.history[0].response || "") + delta;
        else if (content) w.history[0].response = content;
        w.history[0].status = evt.stepStatus || w.history[0].status;
        // fill prompts if they arrive later
        if (!w.history[0].system && evt.systemPrompt) w.history[0].system = evt.systemPrompt;
        if (!w.history[0].user && evt.userPrompt) w.history[0].user = evt.userPrompt;
      }

      w.streaming = !(isCompleted || isFailed);
      if (delta) {
        w.pendingAppend = (w.pendingAppend || "") + delta;
        w.streamContent = (w.streamContent || "") + delta;
      } else if (content) {
        // Completed/fallback:
        // - Avoid overwriting a longer streamed buffer with a shorter (possibly truncated) completion body.
        const cur = w.streamContent || "";
        if (!cur || cur.length <= content.length) {
          w.pendingAppend = "";
          w.streamContent = content;
        }
      }
      if (isCompleted) w.lastResponse = w.streamContent || content || w.lastResponse;
    }

    // 高频流式事件：节流渲染，避免卡顿
    const isDone = st.includes("completed") || st.includes("failed");
    const stepType = String(evt.stepType || "").toLowerCase();
    // Only mark steps panel dirty for non-streaming / stateful transitions
    if (isDone || (stepType && stepType !== "llm_call")) {
      cache._stepsDirty = true;
    }
    scheduleRender(sessionId, isDone);

    // Keep workers/cards stable: update via rAF to avoid per-token DOM churn
    scheduleRenderWorkers(cache);
    return;
  }

  if (evt.type === "GraphEvent") {
    // Legacy compatibility:
    // - Server may still emit GraphEvent (wrapped in CUSTOM) for older clients.
    // - We now build a local DAG snapshot to avoid extra HTTP per update.
    applyGraphSnapshot(cache, sessionId, evt);
    return;
  }

  if (evt.type === "ResultEvent") {
    cache.status = evt.success ? "completed" : "failed";
    cache.phase = "COMPLETE";
    cache.progress = 100;
    cache.tokens = evt.totalTokens ?? cache.tokens;
    cache.llm = evt.totalLlmCalls ?? cache.llm;
    scheduleRender(sessionId, true);

    $("btn-stop").disabled = true;
    $("btn-run").disabled = false;
    setDownloads(sessionId, true);

    // Render result + try parse artifacts
    const summary = evt.success ? (evt.content || "(no content)") : (evt.error || "failed");
    renderResultBox(summary, evt.success);

    // Best-effort: fetch steps.json for nicer display
    void (async () => {
      try {
        const stepsText = await API.steps(sessionId);
        renderResultBox(stepsText, true);
      } catch {
        // ignore
      }
    })();
  }

  if (evt.type === "ErrorEvent") {
    // Session-level error (execution failed / stopped). Show it immediately.
    cache.status = "failed";
    cache.phase = "COMPLETE";
    cache.progress = 100;
    const msg = evt.message || evt.error || "Execution failed";
    $("status-text").textContent = `FAILED: ${msg}`;
    renderResultBox(msg, false);
    $("btn-stop").disabled = true;
    $("btn-run").disabled = false;
    // Even on failure/stop, transcript/review may exist.
    setDownloads(sessionId, true);
    return;
  }
}

function renderGraph(graph, selectedId) {
  const viewport = $("graph-viewport");
  if (!viewport) return;
  const svg = $("graph-svg");

  // SVG nodes have fixed size; text must be clipped/truncated to avoid overlap.
  const NODE_W = 220;
  const NODE_H = 54;
  const NODE_TOP_PAD = 22; // rect y = p.y - 22
  const FIT_PAD = 80;

  const axioms = Array.isArray(graph.axioms) ? graph.axioms : [];
  const assumptions = Array.isArray(graph.assumptions) ? graph.assumptions : [];
  const theorems = Array.isArray(graph.theorems) ? graph.theorems : [];

  function clipSafeId(id) {
    return String(id || "").replace(/[^\w\-]/g, "_");
  }

  function normalizeOneLine(s) {
    return String(s || "").replace(/\s+/g, " ").trim();
  }

  function shortLabel(s, maxChars = 34) {
    const t = normalizeOneLine(s);
    if (t.length <= maxChars) return t;
    return t.slice(0, Math.max(1, maxChars - 1)) + "…";
  }

  function axId(line, idx) {
    // NOTE: Regex literal must use single backslashes (\w, \s). Double backslashes would match literal "\w".
    const m = String(line || "").match(/^([A-Za-z]\w*)\s*:/);
    return m ? m[1] : `A${idx + 1}`;
  }

  const nodes = [];
  for (let i = 0; i < axioms.length; i++) nodes.push({ id: axId(axioms[i], i), label: axioms[i], kind: "axiom" });
  for (let i = 0; i < assumptions.length; i++) {
    const a = assumptions[i] || {};
    const id = String(a.id || "").trim() || `S${i + 1}`;
    nodes.push({
      id,
      label: a.statement || a.id || id,
      kind: "assumption",
      motivation: a.motivation || "",
    });
  }
  for (const t of theorems) nodes.push({
    id: t.id || "",
    label: t.statement || t.id || "",
    kind: "theorem",
    dependsOn: t.dependsOn || t.depends_on || [],
    proof: t.proof || "",
  });

  const byId = Object.create(null);
  for (const n of nodes) byId[n.id] = n;

  // layout
  const pos = Object.create(null);
  let y = 50;
  const sources = nodes
    .filter(n => n.kind !== "theorem")
    .sort((a, b) => String(a.id).localeCompare(String(b.id)));
  for (const n of sources) {
    pos[n.id] = { x: 40, y };
    y += 90;
  }
  const ths = nodes.filter(n => n.kind === "theorem").sort((a, b) => String(a.id).localeCompare(String(b.id)));
  const laneY = Object.create(null);
  for (let i = 0; i < ths.length; i++) {
    const id = ths[i].id;
    const m = id.match(/^T(\d+)$/i);
    const layer = m ? parseInt(m[1], 10) : (i + 1);
    if (!laneY[layer]) laneY[layer] = 50;
    pos[id] = { x: 320 + (layer - 1) * 280, y: laneY[layer] };
    laneY[layer] += 120;
  }

  // Auto-fit SVG canvas to include all nodes (fix: nodes could be rendered outside the initial 1200x520 viewBox).
  if (svg) {
    let maxX = 1200;
    let maxY = 520;
    for (const k in pos) {
      const p = pos[k];
      if (!p) continue;
      maxX = Math.max(maxX, p.x + NODE_W + FIT_PAD);
      maxY = Math.max(maxY, (p.y - NODE_TOP_PAD) + NODE_H + FIT_PAD);
    }
    maxX = Math.ceil(maxX);
    maxY = Math.ceil(maxY);
    svg.setAttribute("width", String(maxX));
    svg.setAttribute("height", String(maxY));
    svg.setAttribute("viewBox", `0 0 ${maxX} ${maxY}`);
  }

  // edges
  const edges = [];
  for (const t of ths) {
    const deps = Array.isArray(t.dependsOn) ? t.dependsOn : [];
    for (const d of deps) {
      const depId = String(d || "").trim();
      if (byId[depId]) edges.push({ from: depId, to: t.id });
    }
  }

  const defs = [];
  const parts = [];
  for (const e of edges) {
    const a = pos[e.from];
    const b = pos[e.to];
    if (!a || !b) continue;
    const x1 = a.x + NODE_W, y1 = a.y;
    const x2 = b.x, y2 = b.y;
    const mx = (x1 + x2) / 2;
    parts.push(`<path d="M ${x1} ${y1} C ${mx} ${y1}, ${mx} ${y2}, ${x2} ${y2}" stroke="#94a3b8" stroke-width="2" fill="none" />`);
  }
  for (const n of nodes) {
    const p = pos[n.id];
    if (!p) continue;
    const k = String(n.kind || "").toLowerCase();
    const fill = k === "axiom" ? "#eff6ff" : (k === "assumption" ? "#fff7ed" : "#f0fdf4");
    const stroke = k === "axiom" ? "#bfdbfe" : (k === "assumption" ? "#fdba74" : "#bbf7d0");
    const fullLabel = normalizeOneLine(n.label || n.id);
    const title = escapeHtml(shortLabel(fullLabel));
    const clipId = `clip_${clipSafeId(n.id)}`;
    const sel = selectedId && selectedId === n.id;

    // Clip to node rect so label never overflows into neighbors.
    defs.push(`<clipPath id="${clipId}"><rect x="${p.x}" y="${p.y - 22}" width="${NODE_W}" height="${NODE_H}" rx="10" ry="10"></rect></clipPath>`);
    parts.push(`
      <g class="graph-node ${sel ? "selected" : ""}" data-node-id="${escapeAttr(n.id)}" data-node-kind="${escapeAttr(n.kind)}" clip-path="url(#${clipId})">
        <title>${escapeHtml(fullLabel)}</title>
        <rect x="${p.x}" y="${p.y - 22}" rx="10" ry="10" width="${NODE_W}" height="${NODE_H}" fill="${fill}" stroke="${stroke}" stroke-width="2"></rect>
        <text x="${p.x + 10}" y="${p.y - 2}" font-family="ui-monospace, Menlo, Consolas" font-size="12" fill="#0f172a">${escapeHtml(n.id)}</text>
        <text x="${p.x + 10}" y="${p.y + 16}" font-family="ui-sans-serif, system-ui" font-size="12" fill="#334155">${title}</text>
      </g>
    `);
  }
  viewport.innerHTML = `${defs.length ? `<defs>${defs.join("")}</defs>` : ""}${parts.join("")}`;
}

function renderDagGraph(dag, selectedId) {
  const viewport = $("graph-viewport");
  if (!viewport) return;
  const svg = $("graph-svg");
  if (!dag || !Array.isArray(dag.nodes) || !Array.isArray(dag.edges)) {
    viewport.innerHTML = "";
    return;
  }

  // SVG nodes have fixed size; text must be clipped/truncated to avoid overlap.
  const NODE_W = 220;
  const NODE_H = 54;
  const NODE_TOP_PAD = 22; // rect y = p.y - 22
  const FIT_PAD = 80;

  function clipSafeId(id) {
    return String(id || "").replace(/[^\w\-]/g, "_");
  }

  function normalizeOneLine(s) {
    return String(s || "").replace(/\s+/g, " ").trim();
  }

  function shortLabel(s, maxChars = 34) {
    const t = normalizeOneLine(s);
    if (t.length <= maxChars) return t;
    return t.slice(0, Math.max(1, maxChars - 1)) + "…";
  }

  const nodes = dag.nodes.map((n) => ({
    id: n.id || "",
    kind: n.kind || "Unknown",
    label: n.label || "",
    proof: n.proof || "",
  })).filter((n) => n.id);

  const byId = Object.create(null);
  for (const n of nodes) byId[n.id] = n;

  // layout: sources (axiom/hypothesis/assumption/unknown) on the left, theorems on the right
  const sources = nodes.filter((n) => String(n.kind).toLowerCase() !== "theorem")
    .sort((a, b) => String(a.id).localeCompare(String(b.id)));
  const theorems = nodes.filter((n) => String(n.kind).toLowerCase() === "theorem")
    .sort((a, b) => String(a.id).localeCompare(String(b.id)));

  const pos = Object.create(null);
  let y = 50;
  for (const n of sources) {
    pos[n.id] = { x: 40, y };
    y += 90;
  }

  const laneY = Object.create(null);
  for (let i = 0; i < theorems.length; i++) {
    const id = theorems[i].id;
    const m = String(id).match(/^T(\d+)$/i);
    const layer = m ? parseInt(m[1], 10) : (i + 1);
    if (!laneY[layer]) laneY[layer] = 50;
    pos[id] = { x: 320 + (layer - 1) * 280, y: laneY[layer] };
    laneY[layer] += 120;
  }

  // Auto-fit SVG canvas to include all nodes (fix: large theorem indices push nodes outside the initial viewBox).
  if (svg) {
    let maxX = 1200;
    let maxY = 520;
    for (const k in pos) {
      const p = pos[k];
      if (!p) continue;
      maxX = Math.max(maxX, p.x + NODE_W + FIT_PAD);
      maxY = Math.max(maxY, (p.y - NODE_TOP_PAD) + NODE_H + FIT_PAD);
    }
    maxX = Math.ceil(maxX);
    maxY = Math.ceil(maxY);
    svg.setAttribute("width", String(maxX));
    svg.setAttribute("height", String(maxY));
    svg.setAttribute("viewBox", `0 0 ${maxX} ${maxY}`);
  }

  const edges = dag.edges
    .map((e) => ({ from: e.fromId, to: e.toId }))
    .filter((e) => e.from && e.to && pos[e.from] && pos[e.to]);

  const defs = [];
  const parts = [];
  for (const e of edges) {
    const a = pos[e.from];
    const b = pos[e.to];
    const x1 = a.x + NODE_W, y1 = a.y;
    const x2 = b.x, y2 = b.y;
    const mx = (x1 + x2) / 2;
    parts.push(`<path d="M ${x1} ${y1} C ${mx} ${y1}, ${mx} ${y2}, ${x2} ${y2}" stroke="#94a3b8" stroke-width="2" fill="none" />`);
  }

  function colors(kind) {
    const k = String(kind || "").toLowerCase();
    if (k === "axiom") return { fill: "#eff6ff", stroke: "#bfdbfe" };
    if (k === "theorem") return { fill: "#f0fdf4", stroke: "#bbf7d0" };
    if (k === "hypothesis") return { fill: "#fffbeb", stroke: "#fde68a" };
    if (k === "assumption") return { fill: "#fff7ed", stroke: "#fdba74" };
    return { fill: "#f8fafc", stroke: "#e2e8f0" };
  }

  for (const n of nodes) {
    const p = pos[n.id];
    if (!p) continue;
    const c = colors(n.kind);
    const fullLabel = normalizeOneLine(n.label || n.id);
    const title = escapeHtml(shortLabel(fullLabel));
    const clipId = `clip_${clipSafeId(n.id)}`;
    const sel = selectedId && selectedId === n.id;

    // Clip to node rect so label never overflows into neighbors.
    defs.push(`<clipPath id="${clipId}"><rect x="${p.x}" y="${p.y - 22}" width="${NODE_W}" height="${NODE_H}" rx="10" ry="10"></rect></clipPath>`);
    parts.push(`
      <g class="graph-node ${sel ? "selected" : ""}" data-node-id="${escapeAttr(n.id)}" data-node-kind="${escapeAttr(n.kind)}" clip-path="url(#${clipId})">
        <title>${escapeHtml(fullLabel)}</title>
        <rect x="${p.x}" y="${p.y - 22}" rx="10" ry="10" width="${NODE_W}" height="${NODE_H}" fill="${c.fill}" stroke="${c.stroke}" stroke-width="2"></rect>
        <text x="${p.x + 10}" y="${p.y - 2}" font-family="ui-monospace, Menlo, Consolas" font-size="12" fill="#0f172a">${escapeHtml(n.id)}</text>
        <text x="${p.x + 10}" y="${p.y + 16}" font-family="ui-sans-serif, system-ui" font-size="12" fill="#334155">${title}</text>
      </g>
    `);
  }

  viewport.innerHTML = `${defs.length ? `<defs>${defs.join("")}</defs>` : ""}${parts.join("")}`;
}

function renderGraphInspector(cache) {
  const host = $("graph-inspector");
  if (!host) return;

  // Prefer Graph DB explain data (DAG reasoning)
  if (cache?.dagExplain && cache?.graphSelectedId) {
    const ex = cache.dagExplain;
    const node = ex.node;
    if (!node) {
      host.classList.add("empty-state");
      host.textContent = "Node not found in DAG.";
      return;
    }

    host.classList.remove("empty-state");
    const deps = Array.isArray(ex.directDependencies) ? ex.directDependencies : [];
    const depPills = deps.map((d) => `<div class="pill">${escapeHtml(String(d))}</div>`).join("");
    const missing = Array.isArray(ex.missingDependencies) ? ex.missingDependencies : [];
    const missingHtml = missing.length
      ? missing.map((m) => `<div class="pill">${escapeHtml(m.id)} · ${escapeHtml(String(m.kind || "unknown"))}</div>`).join("")
      : '<div class="empty-state">none</div>';

    const topo = Array.isArray(ex.topologicalOrder) ? ex.topologicalOrder : [];
    const topoText = topo.length ? topo.join(" → ") : "";

    host.innerHTML = `
      <div class="title">${escapeHtml(String(node.kind || "Unknown"))} · ${escapeHtml(node.id || "")}</div>
      <div class="kv">
        <div class="pill">provable=${ex.provableFromAxioms ? "true" : "false"}</div>
        <div class="pill">cycle=${ex.hasCycle ? "true" : "false"}</div>
        <div class="pill">deps=${deps.length}</div>
      </div>
      <div class="mono">${escapeHtml(node.label || "")}</div>
      <div style="margin-top:10px; font-weight:800; color: var(--text);">Depends on</div>
      <div class="kv" style="margin-top:6px;">${depPills || '<div class="empty-state">none</div>'}</div>
      <div style="margin-top:10px; font-weight:800; color: var(--text);">Missing / Hypotheses</div>
      <div class="kv" style="margin-top:6px;">${missingHtml}</div>
      ${topoText ? `<details style="margin-top:8px;"><summary style="cursor:pointer; font-weight:800; color: var(--text-secondary);">Topological order</summary><div class="mono" style="margin-top:8px;">${escapeHtml(topoText)}</div></details>` : ""}
      ${node.proof ? `<details style="margin-top:8px;"><summary style="cursor:pointer; font-weight:800; color: var(--text-secondary);">Proof</summary><div class="mono" style="margin-top:8px;">${escapeHtml(node.proof)}</div></details>` : ""}
    `;
    return;
  }

  const graph = cache?.graph;
  const axioms = Array.isArray(graph?.axioms) ? graph.axioms : [];
  const assumptions = Array.isArray(graph?.assumptions) ? graph.assumptions : [];
  const theorems = Array.isArray(graph?.theorems) ? graph.theorems : [];

  if (!axioms.length && !assumptions.length && !theorems.length) {
    host.classList.add("empty-state");
    host.textContent = "Waiting for graph...";
    return;
  }

  const selectedId = cache.graphSelectedId;
  if (!selectedId) {
    host.classList.add("empty-state");
    host.textContent = "Click a node to inspect details.";
    return;
  }

  host.classList.remove("empty-state");
  const idx = cache.graphIndex || { axiomsById: {}, assumptionsById: {}, theoremsById: {} };
  const ax = idx.axiomsById ? idx.axiomsById[selectedId] : null;
  const asm = idx.assumptionsById ? idx.assumptionsById[selectedId] : null;
  const th = idx.theoremsById ? idx.theoremsById[selectedId] : null;

  if (ax) {
    host.innerHTML = `
      <div class="title">Axiom · ${escapeHtml(selectedId)}</div>
      <div class="kv"><div class="pill">kind=axiom</div></div>
      <div class="mono">${escapeHtml(ax)}</div>
    `;
    return;
  }

  if (asm) {
    host.innerHTML = `
      <div class="title">Assumption · ${escapeHtml(selectedId)}</div>
      <div class="kv"><div class="pill">kind=assumption</div></div>
      <div class="mono">${escapeHtml(String(asm.statement || asm.id || ""))}</div>
      ${asm.motivation ? `<details style="margin-top:8px;"><summary style="cursor:pointer; font-weight:800; color: var(--text-secondary);">Motivation</summary><div class="mono" style="margin-top:8px;">${escapeHtml(String(asm.motivation || ""))}</div></details>` : ""}
    `;
    return;
  }

  if (th) {
    const deps = Array.isArray(th.dependsOn) ? th.dependsOn : (Array.isArray(th.depends_on) ? th.depends_on : []);
    const depPills = deps.map((d) => `<div class="pill">${escapeHtml(String(d))}</div>`).join("");
    const proof = th.proof || "";
    host.innerHTML = `
      <div class="title">Theorem · ${escapeHtml(th.id || selectedId)}</div>
      <div class="kv">
        <div class="pill">kind=theorem</div>
        <div class="pill">deps=${deps.length}</div>
      </div>
      <div class="mono">${escapeHtml(th.statement || "")}</div>
      <div style="margin-top:10px; font-weight:800; color: var(--text);">Depends on</div>
      <div class="kv" style="margin-top:6px;">${depPills || '<div class="empty-state">none</div>'}</div>
      <details style="margin-top:8px;">
        <summary style="cursor:pointer; font-weight:800; color: var(--text-secondary);">Proof</summary>
        <div class="mono" style="margin-top:8px;">${escapeHtml(proof || "(no proof in graph event)")}</div>
      </details>
    `;
    return;
  }

  host.classList.add("empty-state");
  host.textContent = "Selected node not found in current graph.";
}

function connect(sessionId) {
  if (state.es) {
    state.es.close();
    state.es = null;
  }

  // Prefer AG-UI stream (standardized). Legacy stream is kept server-side as /events.
  state.es = new EventSource(`/api/sessions/${sessionId}/agui/events`);
  state.es.onmessage = (e) => {
    try {
      const evt = JSON.parse(e.data);
      if (state.rawOpen) {
        appendRaw(evt);
      } else {
        state.rawDropped++;
      }
      applyEvent(sessionId, evt);
    } catch {
      // ignore parse errors
    }
  };
}

async function refreshSessions(selectFirst = false) {
  state.sessions = await API.listSessions();
  renderSessions();
  if (selectFirst && !state.current && state.sessions.length) {
    selectSession(state.sessions[0].id);
  }
}

function selectSession(sessionId) {
  if (!sessionId) return;
  state.current = sessionId;
  renderSessions();
  setDownloads(sessionId, false);
  const cache = ensureSessionCache(sessionId);
  connect(sessionId);
  // Best-effort: fetch DAG snapshot for graph view (kinds + hypotheses)
  void (async () => {
    try {
      cache.dag = await API.dagSnapshot(sessionId);
    } catch {
      cache.dag = null;
    }
    if (cache.dag) renderDagGraph(cache.dag, cache.graphSelectedId);
    else renderGraph(cache.graph, cache.graphSelectedId);
    renderGraphInspector(cache);
  })();

  // Allow stop/run for current
  $("btn-stop").disabled = false;
  $("btn-run").disabled = false;
}

async function createSession() {
  const payload = {
    axioms: $("input-axioms").value.trim(),
    goal: $("input-goal").value.trim(),
    seedHypothesis: $("input-seed-hypothesis") ? $("input-seed-hypothesis").value.trim() : "",
    workflow: $("input-workflow") ? $("input-workflow").value : "hypothesis_promotion_loop",
    language: $("input-language") ? $("input-language").value : "English",
    k: parseInt($("input-k").value, 10) || 3,
    maxRounds: parseInt($("input-max-rounds").value, 10) || 10,
    maxDepth: parseInt($("input-max-depth").value, 10) || 10,

    // Long-run budgets (frontend-configurable)
    maxDurationMinutes: readInt("input-max-duration-minutes", 120),
    maxLlmCalls: readInt("input-max-llm-calls", 5000),
    maxTokens: readInt("input-max-tokens", 8000000),
    continueOnFailure: readBool("input-continue-on-failure", true),
  };

  // Persist run config for next page load
  saveRunConfig({
    maxDurationMinutes: payload.maxDurationMinutes,
    maxLlmCalls: payload.maxLlmCalls,
    maxTokens: payload.maxTokens,
    maxDepth: payload.maxDepth,
    continueOnFailure: payload.continueOnFailure,
    workflow: payload.workflow,
    language: payload.language,
  });

  const res = await API.createSession(payload);
  if (!res.success) {
    $("status-text").textContent = `Create failed: ${res.error || "unknown"}`;
    return;
  }
  await refreshSessions();
  selectSession(res.sessionId);
  $("btn-run").disabled = false;
}

async function runSession() {
  if (!state.current) return;
  $("btn-run").disabled = true;
  $("btn-stop").disabled = false;
  setDownloads(state.current, false);

  const res = await API.run(state.current);
  if (!res.success) {
    $("status-text").textContent = `Run failed: ${res.error || "unknown"}`;
    $("btn-run").disabled = false;
    $("btn-stop").disabled = true;
  }
}

async function stopSession() {
  if (!state.current) return;
  const res = await API.stop(state.current);
  $("status-text").textContent = res.success ? "Stopped" : `Stop failed: ${res.error || "unknown"}`;
  $("btn-stop").disabled = true;
  $("btn-run").disabled = false;
}

function initDefaults() {
  if (!$("input-axioms").value.trim()) {
    $("input-axioms").value = [
      "O1: |Ψ⟩ ∈ ℋ, ⟨Ψ|Ψ⟩=1, and ∂t|Ψ⟩=0 (no fundamental external time evolution).",
      "O2: For any bounded causally closed region with boundary area A: dim(ℋ_region) < ∞ and dim(ℋ_region) ~ exp(A/(4 l_P^2)).",
      "O3: ℋ = ⊗_{v∈V} ℋ_v on a countable graph G=(V,E), and U = ∏_k U_local^(k) where each U_local acts only on adjacent vertices / finite neighborhood.",
      "O4: There exists a holographic isometry Φ: ℋ_bulk → ℋ_{∂G} (e.g., Golden MERA / QECC encoding).",
    ].join("\n");
  }
  if (!$("input-goal").value.trim()) {
    $("input-goal").value =
      "Discover novel non-trivial implications and conjectures from O1–O4. Each step must be either (A) DEDUCTION strictly from O1–O4 or prior derived facts, or (B) INTERPRETATION clearly labeled. Prefer small, checkable steps; avoid repetition.";
  }
  if ($("input-seed-hypothesis") && !$("input-seed-hypothesis").value.trim()) {
    $("input-seed-hypothesis").value = "";
  }
}

window.addEventListener("load", async () => {
  initDefaults();
  await loadWorkflowsIntoSelect();
  applyRunConfigToForm(loadRunConfig());

  $("btn-refresh").addEventListener("click", () => refreshSessions());
  $("btn-create").addEventListener("click", createSession);
  $("btn-run").addEventListener("click", runSession);
  $("btn-stop").addEventListener("click", stopSession);

  $("btn-toggle-raw").addEventListener("click", () => {
    state.rawOpen = !state.rawOpen;
    $("raw").classList.toggle("hidden", !state.rawOpen);
    if (state.rawOpen) {
      const el = $("raw");
      if (state.rawDropped > 0) {
        el.textContent = `[raw disabled] dropped ${state.rawDropped} events before open\n`;
        state.rawDropped = 0;
      }
    }
  });

  // modal close
  $("modal-close").addEventListener("click", closeModal);
  $("modal-backdrop").addEventListener("click", closeModal);
  window.addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeModal();
  });

  // Workers: PaperReview-style pointerdown/up to survive DOM churn during streaming
  const grid = $("worker-grid");
  grid.addEventListener("pointerdown", (e) => {
    const card = e.target.closest(".worker-card");
    if (!card) return;
    state.ui.pointerDownWorkerId = card.dataset.workerId || null;
    state.ui.pointerDownAt = Date.now();
    state.ui.pointerDownX = e.clientX;
    state.ui.pointerDownY = e.clientY;
  }, { passive: true });
  grid.addEventListener("pointerup", (e) => {
    const wid = state.ui.pointerDownWorkerId;
    if (!wid || !state.current) return;
    const dt = Date.now() - (state.ui.pointerDownAt || 0);
    const dx = Math.abs(e.clientX - (state.ui.pointerDownX || 0));
    const dy = Math.abs(e.clientY - (state.ui.pointerDownY || 0));
    state.ui.pointerDownWorkerId = null;
    if (dt < 600 && dx < 8 && dy < 8) {
      openWorkerModal(wid);
    }
  });
  grid.addEventListener("click", (e) => {
    const card = e.target.closest(".worker-card");
    if (!card || !state.current) return;
    const wid = card.dataset.workerId;
    if (wid) openWorkerModal(wid);
  });

  // Graph: click nodes to inspect details (right inspector panel)
  const svg = $("graph-svg");
  if (svg) {
    svg.addEventListener("click", (e) => {
      if (!state.current) return;
      const t = e.target;
      if (!t || typeof t.closest !== "function") return;
      const node = t.closest(".graph-node");
      if (!node) return;
      const id = node.dataset.nodeId || node.dataset.id;
      if (!id) return;

      const cache = ensureSessionCache(state.current);
      cache.graphSelectedId = id;
      cache.dagExplain = null;
      if (cache.dag) renderDagGraph(cache.dag, cache.graphSelectedId);
      else renderGraph(cache.graph, cache.graphSelectedId);

      // Pull DAG reasoning detail (best-effort)
      void (async () => {
        try {
          cache.dagExplain = await API.dagExplain(state.current, id);
        } catch {
          cache.dagExplain = null;
        }
        renderGraphInspector(cache);
      })();
    });
  }

  await refreshSessions(true);
});


