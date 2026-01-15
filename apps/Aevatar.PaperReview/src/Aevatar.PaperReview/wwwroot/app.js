// ============================================================
//  Paper Review Frontend
//  Workers display LLM response streaming
// ============================================================

const API = {
  listSessions: () => fetchJson("/api/sessions"),
  createSession: (payload) => fetch("/api/sessions", { method: "POST", body: JSON.stringify(payload) }).then(r => r.json()),
  upload: (file) => {
    const fd = new FormData();
    fd.append("file", file);
    return fetch("/api/upload", { method: "POST", body: fd }).then(r => r.json());
  },
  start: (id) => fetch(`/api/sessions/${id}/review`, { method: "POST" }).then(r => r.json()),
  stop: (id) => fetch(`/api/sessions/${id}/stop`, { method: "POST" }).then(r => r.json()),
  result: (id) => fetchJson(`/api/sessions/${id}/result`),
};

// Pipeline steps
const STEPS = ["submit", "analyze", "decompose", "execute", "vote", "compose", "complete"];
const STEP_LABELS = {
  submit: "Submit",
  analyze: "Analyze",
  decompose: "Decompose",
  execute: "Execute",
  vote: "Vote",
  compose: "Compose",
  complete: "Complete",
};

// Global state
const state = {
  sessions: [],
  current: null,
  es: null,
  cache: {},
  // Modal state: allow live updates during streaming
  modal: { sessionId: null, workerId: null, headTs: 0 },
  ui: {
    // Pointer-based click fallback (streaming 期间 DOM 变化可能导致 click 被取消)
    pointerDownWorkerId: null,
    pointerDownAt: 0,
    pointerDownX: 0,
    pointerDownY: 0,
  },
};

// ─────────────────────────────────────────────────────────────
//  Persistence (Refresh Recovery)
//  - 页面刷新会清空内存 state/cache
//  - 这里把“关键 UI 状态”写入 localStorage（落盘）
//  - 注意：Streaming 事件极高频，所以用 debounce + 截断，避免写爆 localStorage
// ─────────────────────────────────────────────────────────────
const PERSIST = {
  version: 1,
  cacheKeyPrefix: "paperreview:cache:v1:",
  lastSessionKey: "paperreview:lastSession:v1",
};

let _persistTimer = 0;

function storageKeyForSession(sessionId) {
  return `${PERSIST.cacheKeyPrefix}${sessionId}`;
}

function saveLastSessionId(sessionId) {
  try {
    localStorage.setItem(PERSIST.lastSessionKey, sessionId);
  } catch {
    // Ignore storage errors (private mode / quota)
  }
}

function loadLastSessionId() {
  try {
    return localStorage.getItem(PERSIST.lastSessionKey);
  } catch {
    return null;
  }
}

function schedulePersistCache(sessionId = state.current) {
  if (!sessionId) return;
  if (_persistTimer) return;
  const target = sessionId;
  _persistTimer = setTimeout(() => {
    _persistTimer = 0;
    persistCacheNow(target);
  }, 800);
}

function persistCacheNow(sessionId, light = false) {
  const cache = state.cache?.[sessionId];
  if (!cache) return;

  // NOTE: 只落“可恢复 UI”所需字段，避免把 DOM/超大 prompt 写进去
  const snapshot = buildCacheSnapshot(cache, light);
  if (!snapshot) return;

  try {
    localStorage.setItem(storageKeyForSession(sessionId), JSON.stringify(snapshot));
  } catch {
    // Quota / private mode: fallback to lighter snapshot
    if (!light) {
      try {
        localStorage.setItem(storageKeyForSession(sessionId), JSON.stringify(buildCacheSnapshot(cache, true)));
      } catch {
        // Give up quietly
      }
    }
  }
}

function loadPersistedCache(sessionId) {
  try {
    const raw = localStorage.getItem(storageKeyForSession(sessionId));
    if (!raw) return null;
    const data = JSON.parse(raw);
    if (!data || data.v !== PERSIST.version) return null;
    return data;
  } catch {
    return null;
  }
}

function buildCacheSnapshot(cache, light = false) {
  const maxText = light ? 2000 : 12000;
  const maxPrompt = light ? 200 : 800;
  const maxLogs = light ? 40 : 120;
  const maxProposals = light ? 5 : 10;
  const maxCallIds = light ? 400 : 2000;

  const workers = Object.create(null);
  for (const [id, w] of Object.entries(cache.workers || {})) {
    if (!w) continue;
    workers[id] = {
      id: w.id || id,
      name: w.name || "",
      provider: w.provider || "",
      status: w.status || "pending",
      streaming: !!w.streaming,
      tokenIndex: w.tokenIndex || 0,
      streamContent: clampText(w.streamContent, maxText),
      lastResponse: clampText(w.lastResponse, maxText),
      topic: w.topic || "",
      topicId: w.topicId || "",
      history: Array.isArray(w.history)
        ? w.history.slice(0, 10).map(h => ({
            callId: h.callId || "",
            phase: h.phase || "",
            timestamp: h.timestamp || 0,
            pointId: h.pointId || "",
            pointTitle: h.pointTitle || "",
            // prompts 可能包含论文全文，必须截断
            system: clampText(h.system || "", maxPrompt),
            prompt: clampText(h.prompt || "", maxPrompt),
            response: clampText(h.response || "", maxText),
          }))
        : [],
    };
  }

  const points = cache.points || {};
  const byId = points.byId || {};
  const snapPoints = {
    rootId: points.rootId || "root",
    activeId: points.activeId || null,
    open: Array.from(points.open || []),
    byId: Object.create(null),
  };
  for (const [id, p] of Object.entries(byId)) {
    if (!p) continue;
    snapPoints.byId[id] = {
      id: p.id || id,
      title: p.title || "",
      status: p.status || "pending",
      consensus: clampText(p.consensus || "", maxText),
      consensusSource: p.consensusSource || "",
      consensusAt: p.consensusAt || 0,
      parentId: p.parentId || "",
      children: Array.isArray(p.children) ? p.children.slice(0, 128) : [],
      workers: Array.from(p.workers || []),
      proposals: Array.isArray(p.proposals)
        ? p.proposals.slice(0, maxProposals).map(x => ({
            workerId: x.workerId || "",
            displayName: x.displayName || "",
            timestamp: x.timestamp || 0,
            content: clampText(x.content || "", maxText),
          }))
        : [],
    };
  }

  const logs = Array.isArray(cache.logs) ? cache.logs.slice(0, maxLogs) : [];

  // Result 内容可以从 /result 重新拉取；这里只存 meta，避免爆 localStorage
  const result = cache.result
    ? {
        success: !!cache.result.success,
        error: cache.result.error || "",
        totalTokens: cache.result.totalTokens || 0,
        totalLlmCalls: cache.result.totalLlmCalls || 0,
      }
    : null;

  return {
    v: PERSIST.version,
    savedAt: Date.now(),
    progress: cache.progress || { phase: "-", percent: 0, message: "" },
    pipeline: cache.pipeline || Object.fromEntries(STEPS.map(s => [s, "pending"])),
    tokens: cache.tokens || 0,
    llmCalls: cache.llmCalls || 0,
    voting: cache.voting || null,
    logs,
    result,
    workers,
    points: snapPoints,
    callDedup: {
      callTokens: cache._callTokens || 0,
      callLlmCalls: cache._callLlmCalls || 0,
      callIds: Array.from(cache._countedCallIds || []).slice(-maxCallIds),
    },
  };
}

function applyPersistedCache(cache, saved) {
  if (!saved || typeof saved !== "object") return;

  // Basic fields
  if (saved.progress) cache.progress = saved.progress;
  if (saved.pipeline && typeof saved.pipeline === "object") {
    for (const step of STEPS) {
      if (saved.pipeline[step]) cache.pipeline[step] = saved.pipeline[step];
    }
  }
  if (typeof saved.tokens === "number") cache.tokens = Math.max(cache.tokens || 0, saved.tokens || 0);
  if (typeof saved.llmCalls === "number") cache.llmCalls = Math.max(cache.llmCalls || 0, saved.llmCalls || 0);
  if (Array.isArray(saved.logs)) cache.logs = saved.logs;
  if (saved.voting) cache.voting = saved.voting;

  // Restore workers
  if (saved.workers && typeof saved.workers === "object") {
    for (const [id, w] of Object.entries(saved.workers)) {
      if (!w) continue;
      upsertWorker(cache, id, {
        name: w.name,
        provider: w.provider,
        status: w.status,
        streaming: w.streaming,
        tokenIndex: w.tokenIndex,
        streamContent: w.streamContent,
        lastResponse: w.lastResponse,
        topic: w.topic,
        topicId: w.topicId,
      });
      if (Array.isArray(w.history)) {
        cache.workers[id].history = w.history;
      }
    }
  }

  // Restore points tree
  if (saved.points && typeof saved.points === "object") {
    const points = cache.points || (cache.points = { rootId: "root", byId: Object.create(null), activeId: null, open: new Set(["root"]) });
    if (typeof saved.points.rootId === "string") points.rootId = saved.points.rootId || "root";
    points.activeId = saved.points.activeId || null;
    points.open = new Set(Array.isArray(saved.points.open) ? saved.points.open : []);
    points.open.add(points.rootId || "root");

    if (saved.points.byId && typeof saved.points.byId === "object") {
      for (const [pid, p] of Object.entries(saved.points.byId)) {
        if (!p) continue;
        const node = ensurePoint(cache, pid, p.title || "", p.parentId || "");
        node.status = p.status || node.status;
        node.consensus = p.consensus || "";
        node.consensusSource = p.consensusSource || "";
        node.consensusAt = p.consensusAt || 0;
        node.parentId = p.parentId || node.parentId;
        if (Array.isArray(p.children)) node.children = p.children;
        node.workers = new Set(Array.isArray(p.workers) ? p.workers : []);
        node.proposals = Array.isArray(p.proposals) ? p.proposals : [];
      }
    }
  }

  // Restore call dedup (for non-engine counting fallback)
  if (saved.callDedup && typeof saved.callDedup === "object") {
    cache._callTokens = saved.callDedup.callTokens || cache._callTokens || 0;
    cache._callLlmCalls = saved.callDedup.callLlmCalls || cache._callLlmCalls || 0;
    cache._countedCallIds = new Set(Array.isArray(saved.callDedup.callIds) ? saved.callDedup.callIds : []);
  }
}

function clampText(str = "", max = 8000) {
  const s = String(str || "");
  if (s.length <= max) return s;
  return s.slice(0, max);
}

// 尽量在离开页面前落一个轻量快照（不保证一定执行，但能覆盖大多数刷新场景）
window.addEventListener("beforeunload", () => {
  if (state.current) persistCacheNow(state.current, true);
});

// ─────────────────────────────────────────────────────────────
//  Render Scheduling (avoid re-render per token)
// ─────────────────────────────────────────────────────────────
// Streaming 会产生高频 SSE 事件；如果每个 token 都全量重绘，会把主线程打爆，
// 导致“看起来像不 streaming”（实际是浏览器没机会 paint）。
let _workersRenderRaf = 0;
let _workersRenderCache = null;

function scheduleRenderWorkers(cache) {
  _workersRenderCache = cache;
  if (_workersRenderRaf) return;
  _workersRenderRaf = requestAnimationFrame(() => {
    _workersRenderRaf = 0;
    if (_workersRenderCache) {
      renderWorkers(_workersRenderCache);
      // 如果 modal 正在打开，保持内容随 streaming 实时刷新
      updateModalIfOpen(_workersRenderCache);
    }
  });
}

// ─────────────────────────────────────────────────────────────
//  DOM
// ─────────────────────────────────────────────────────────────
const $ = (id) => document.getElementById(id);

// ─────────────────────────────────────────────────────────────
//  Initialization
// ─────────────────────────────────────────────────────────────
window.addEventListener("DOMContentLoaded", () => {
  renderPipeline();
  bindActions();
  init();
});

async function init() {
  await refreshSessions();

  // 刷新恢复：
  // - 优先 URL: ?session=xxx
  // - 其次 localStorage 里记住的 last session
  const url = new URL(window.location.href);
  const sessionFromUrl = url.searchParams.get("session");
  const preferred = sessionFromUrl || loadLastSessionId();

  if (preferred && state.sessions.some(s => s.id === preferred)) {
    // NOTE: selectSession 会先 renderFromCache，再连 SSE
    // 因此如果有本地快照，可以做到“刷新后瞬间恢复 UI”
    selectSession(preferred);
  }
}

function bindActions() {
  $("btn-refresh").onclick = refreshSessions;
  $("btn-stop").onclick = () => {
    if (!state.current) return;
    API.stop(state.current).then(res => toast(res.success ? "Stopped" : res.error || "Failed to stop"));
  };
  $("btn-upload-create").onclick = () => $("file-input").click();
  $("file-input").onchange = async (e) => {
    const file = e.target.files?.[0];
    if (file) await createFromUpload(file);
    e.target.value = "";
  };
  $("modal-close").onclick = closeModal;
  $("modal-backdrop").onclick = closeModal;

  // Workers: use event delegation (cards are updated frequently during streaming)
  // NOTE: click 依赖 down/up 同一元素；streaming 时 DOM 可能轻微变动导致 click 丢失
  // 所以这里用 pointerdown/up 做更稳的“点击识别”，同时保留 click 兜底。
  const grid = $("worker-grid");
  grid.addEventListener("pointerdown", onWorkerPointerDown, { passive: true });
  grid.addEventListener("pointerup", onWorkerPointerUp);
  grid.addEventListener("click", onWorkerGridClick);
}

// ─────────────────────────────────────────────────────────────
//  Session Management
// ─────────────────────────────────────────────────────────────
async function refreshSessions() {
  const list = await API.listSessions();
  state.sessions = Array.isArray(list) ? list : [];
  renderSessionList();
}

function renderSessionList() {
  const el = $("session-list");
  el.innerHTML = "";
  if (!state.sessions.length) {
    el.className = "session-list empty-state";
    el.textContent = "No sessions";
    return;
  }
  el.className = "session-list";
  state.sessions.forEach(s => {
    const item = document.createElement("div");
    item.className = "session-item" + (s.id === state.current ? " active" : "");
    item.innerHTML = `
      <div class="session-title">${escapeHtml(s.title || "Untitled")}</div>
      <div class="session-meta">${s.type || ""} · ${s.status} · ${formatTime(s.createdAt)}</div>
    `;
    item.onclick = () => selectSession(s.id);
    el.appendChild(item);
  });
}

async function selectSession(id) {
  state.current = id;
  saveLastSessionId(id);
  renderSessionList();
  ensureCache(id);
  renderFromCache(id);
  connectSse(id);
  // 切换 session 时关闭 worker modal，避免引用旧缓存
  closeModal();
  const res = await API.result(id);
  if (res) {
    const cache = state.cache[id];
    if (typeof res.totalTokens === "number") cache.tokens = Math.max(cache.tokens || 0, res.totalTokens || 0);
    if (typeof res.totalLlmCalls === "number") cache.llmCalls = Math.max(cache.llmCalls || 0, res.totalLlmCalls || 0);
    $("stat-tokens").textContent = formatNumber(cache.tokens);
    $("stat-llm").textContent = cache.llmCalls;
    if (res.content || res.error) {
      cache.result = res;
      renderResult(cache);
    }
  }

  // 选择 session 后也落一次盘（避免“刚点开就刷新”丢状态）
  schedulePersistCache(id);
}

function ensureCache(id) {
  if (!state.cache[id]) {
    state.cache[id] = {
      progress: { phase: "-", percent: 0, message: "" },
      pipeline: Object.fromEntries(STEPS.map(s => [s, "pending"])),
      workers: {},
      // Atomic points (subtasks) + per-point consensus
      points: {
        rootId: "root",
        byId: Object.create(null),
        activeId: null,
        open: new Set(["root"]),
      },
      logs: [],
      voting: null,
      result: null,
      tokens: 0,
      llmCalls: 0,
      // Internal counters (monotonic, dedup by callId)
      _countedCallIds: new Set(),
      _callTokens: 0,
      _callLlmCalls: 0,
      // DOM cache: keep worker card elements stable (so they are clickable while streaming)
      _workerDom: Object.create(null),
    };
    
    // Initialize root node for mind-map
    state.cache[id].points.byId.root = {
      id: "root",
      title: "Paper Review",
      status: "pending",
      consensus: "",
      consensusSource: "",
      consensusAt: 0,
      parentId: "",
      children: [],
      workers: new Set(),
      proposals: [],
    };

    // ─────────────────────────────────────────────
    //  Refresh Recovery
    //  - 尝试从 localStorage 恢复上一次 UI 快照
    //  - 只在“首次创建 cache”时做，避免覆盖当前内存态
    // ─────────────────────────────────────────────
    const saved = loadPersistedCache(id);
    if (saved) applyPersistedCache(state.cache[id], saved);
  }
}

async function createFromUpload(file) {
  toast("Uploading...");
  const uploadRes = await API.upload(file);
  if (!uploadRes.success) return toast(uploadRes.error || "Upload failed");

  const payload = {
    title: $("input-title").value || file.name,
    authors: $("input-authors").value || "",
    reviewType: $("input-review-type").value || "Standard",
    uploadId: uploadRes.uploadId,
  };
  const createRes = await API.createSession(payload);
  if (!createRes.success) return toast(createRes.error || "Create failed");
  toast("Created, starting review...");
  await refreshSessions();
  await API.start(createRes.sessionId);
  selectSession(createRes.sessionId);
}

// ─────────────────────────────────────────────────────────────
//  SSE Connection
// ─────────────────────────────────────────────────────────────
function connectSse(sessionId) {
  if (state.es) {
    state.es.close();
    state.es = null;
  }
  const es = new EventSource(`/api/sessions/${sessionId}/events`);
  es.onmessage = (e) => {
    try {
      handleEvent(JSON.parse(e.data));
    } catch (err) {
      console.error("SSE parse error", err);
    }
  };
  es.onerror = () => {
    es.close();
    setTimeout(() => {
      if (state.current === sessionId) connectSse(sessionId);
    }, 2000);
  };
  state.es = es;
}

// ─────────────────────────────────────────────────────────────
//  Event Handling
// ─────────────────────────────────────────────────────────────
function handleEvent(evt) {
  const cache = state.cache[state.current];
  if (!cache) return;

  switch (evt.type) {
    case "ProgressEvent":
      handleProgress(cache, evt);
      break;
    case "TaskDecomposedEvent":
      handleTaskDecomposed(cache, evt);
      break;
    case "AtomicPointConsensusEvent":
      handleAtomicPointConsensus(cache, evt);
      break;
    case "StageLogEvent":
      handleStageLog(cache, evt);
      break;
    case "VotingRoundEvent":
      cache.voting = evt;
      renderVoting(cache);
      break;
    case "WorkerStartedEvent":
      handleWorkerStarted(cache, evt);
      break;
    case "LlmCallStartEvent":
      handleLlmCallStart(cache, evt);
      break;
    case "LlmStreamingEvent":
      handleLlmStreaming(cache, evt);
      break;
    case "LlmCallCompleteEvent":
      handleLlmComplete(cache, evt);
      break;
    case "WorkerCompletedEvent":
      handleWorkerCompleted(cache, evt);
      break;
    case "ResultEvent":
      if (typeof evt.totalTokens === "number") cache.tokens = Math.max(cache.tokens || 0, evt.totalTokens || 0);
      if (typeof evt.totalLlmCalls === "number") cache.llmCalls = Math.max(cache.llmCalls || 0, evt.totalLlmCalls || 0);
      $("stat-tokens").textContent = formatNumber(cache.tokens);
      $("stat-llm").textContent = cache.llmCalls;
      cache.result = evt;
      renderResult(cache);
      break;
    case "ErrorEvent":
      toast(evt.message || "Error occurred");
      break;
  }

  // Refresh-safe: debounce persist to localStorage (drop huge fields)
  schedulePersistCache(state.current);
}

function handleProgress(cache, evt) {
  cache.progress.phase = evt.phase || "-";
  cache.progress.percent = (evt.progressPercent || 0) * 100;
  cache.progress.message = evt.message || "";
  if (typeof evt.totalTokens === "number") cache.tokens = Math.max(cache.tokens || 0, evt.totalTokens || 0);
  if (typeof evt.totalLlmCalls === "number") cache.llmCalls = Math.max(cache.llmCalls || 0, evt.totalLlmCalls || 0);

  $("status-text").textContent = cache.progress.message || "Running";
  $("progress-bar-inner").style.width = `${Math.min(100, cache.progress.percent)}%`;
  $("stat-phase").textContent = cache.progress.phase;
  $("stat-tokens").textContent = formatNumber(cache.tokens);
  $("stat-llm").textContent = cache.llmCalls;

  // Handle step failure - show toast and update worker status
  // Only mark as error for gen[N] worker steps, not for coordinator steps
  // (coordinator steps like check_atomic may fail temporarily but workflow continues)
  if (evt.stepStatus === "Failed" && evt.message) {
    const msg = evt.message;
    // Parse error type for user-friendly message
    let errorMsg = msg;
    if (msg.includes("redflag-length>")) {
      const limit = msg.match(/redflag-length>(\d+)/)?.[1] || "?";
      errorMsg = `Response too long (>${limit} chars). Red-flag triggered.`;
    } else if (msg.includes("redflag")) {
      errorMsg = `Quality check failed: ${msg}`;
    }
    
    // Only show toast for significant errors (not temporary red-flags during voting)
    if (!msg.includes("redflag")) {
      toast(errorMsg, "error");
    }
    
    // Update worker status only for gen[N] workers, not coordinator
    const workerId = normalizeWorkerId(evt.stepId);
    if (workerId && workerId !== "coordinator" && cache.workers[workerId]) {
      upsertWorker(cache, workerId, {
        status: "error",
        streaming: false,
        lastResponse: errorMsg,
      });
      renderWorkers(cache);
    }
  }

  const currentStep = mapPhaseToStep(cache.progress.phase);
  STEPS.forEach(s => {
    const idx = STEPS.indexOf(s);
    const currentIdx = STEPS.indexOf(currentStep);
    if (idx < currentIdx) cache.pipeline[s] = "done";
    else if (idx === currentIdx) cache.pipeline[s] = "active";
    else if (cache.pipeline[s] !== "done") cache.pipeline[s] = "pending";
  });
  renderPipeline(cache.pipeline);

  // Atomic points progress (execute_subtasks[i])
  updatePointsFromProgress(cache, evt);
}

function handleStageLog(cache, evt) {
  cache.logs.unshift({
    stage: evt.stage,
    status: evt.status,
    summary: evt.summary,
    stats: evt.stats,
    details: evt.details,
    time: evt.endTime || evt.timestamp,
  });
  if (cache.logs.length > 100) cache.logs.pop();
  renderLogs(cache);
  
  // Pre-create worker cards when Configuration stage reports WorkerCount
  if (evt.stage === "Configuration" && evt.stats?.workerCount > 0) {
    const n = evt.stats.workerCount;
    // Create coordinator card first
    upsertWorker(cache, "coordinator", { status: "pending" });
    // Create N worker cards (worker-0 to worker-(N-1))
    for (let i = 0; i < n; i++) {
      upsertWorker(cache, `worker-${i}`, { status: "pending" });
    }
    renderWorkers(cache);
  }
}

// ─────────────────────────────────────────────────────────────
//  Atomic Points (Subtasks) - UI
// ─────────────────────────────────────────────────────────────

function handleTaskDecomposed(cache, evt) {
  const parentId = evt.parentPointId || cache.points.rootId || "root";
  const parentTitle = evt.parentPointTitle || "";
  const parent = ensurePoint(cache, parentId, parentTitle);

  // Keep root title stable
  if (parentId !== (cache.points.rootId || "root") && parentTitle) {
    parent.title = parentTitle;
  }

  const subTasks = Array.isArray(evt.subTasks) ? evt.subTasks : [];
  const childIds = [];

  subTasks.forEach((t, idx) => {
    const id = t.taskId || `${parentId}/execute_subtasks[${idx}]`;
    const title = t.description || t.taskId || `Point ${idx + 1}`;
    const child = ensurePoint(cache, id, title, parentId);
    // Respect backend status if provided
    if (t.status) child.status = t.status;
    childIds.push(id);
  });

  // Overwrite children order for deterministic tree rendering
  parent.children = childIds;

  // Expand parent so users immediately see the new branch
  cache.points.open.add(parentId);
  openAncestors(cache, parentId);
  renderPoints(cache);
}

function handleAtomicPointConsensus(cache, evt) {
  const pointId = evt.pointId;
  if (!pointId) return;

  const p = ensurePoint(cache, pointId, evt.pointTitle || "");
  p.consensus = evt.conclusion || "";
  p.consensusSource = evt.sourceStepId || "";
  p.consensusAt = evt.timestamp || Date.now();

  // 如果 point 还在 running，让它在共识到来时更直观地显示为 completed（不抢占 ProgressEvent）
  if (p.status === "running" && p.consensus) p.status = "completed";
  openAncestors(cache, pointId);
  renderPoints(cache);
}

function ensurePoint(cache, pointId, title = "", parentHintId = "") {
  const points = cache.points || {};
  const rootId = points.rootId || "root";
  const byId = points.byId || (points.byId = Object.create(null));

  if (!byId[pointId]) {
    byId[pointId] = {
      id: pointId,
      title: title || pointId,
      status: "pending",
      consensus: "",
      consensusSource: "",
      consensusAt: 0,
      parentId: "",
      children: [],
      workers: new Set(),
      proposals: [],
    };
  } else if (title && (!byId[pointId].title || byId[pointId].title === pointId)) {
    byId[pointId].title = title;
  }

  // Establish parent relationship (best-effort)
  if (pointId !== rootId && !byId[pointId].parentId) {
    let parentId = parentHintId;
    if (!parentId) {
      const slash = pointId.lastIndexOf("/");
      parentId = slash > 0 ? pointId.slice(0, slash) : rootId;
    }
    if (parentId && parentId !== pointId) {
      byId[pointId].parentId = parentId;
      const parent = ensurePoint(cache, parentId, parentId === rootId ? "Paper Review" : "");
      if (!Array.isArray(parent.children)) parent.children = [];
      if (!parent.children.includes(pointId)) parent.children.push(pointId);
    }
  }

  return byId[pointId];
}

function updatePointsFromProgress(cache, evt) {
  const stepType = evt.stepType || "";
  const stepStatus = evt.stepStatus || "";
  if (!stepType || !stepStatus) return;

  // 只用 workflow_call（point 节点）更新树状态
  if (String(stepType).toLowerCase() !== "workflow_call") return;

  // 关键：只把 execute_subtasks[i] 视为“atomic point”
  // - workflow_call 原语内部也会产生 workflow_call 事件（随机 stepId），不能当成 point 节点，否则会污染树/状态
  const stepId = evt.stepId || "";
  if (!/^execute_subtasks\[\d+\]$/i.test(stepId)) return;

  const pointId = evt.pointId || "";
  if (!pointId) return;

  const p = ensurePoint(cache, pointId, evt.pointTitle || "");
  const prevStatus = p.status;

  if (stepStatus === "Running") p.status = "running";
  else if (stepStatus === "Completed") p.status = "completed";
  else if (stepStatus === "Failed") p.status = "error";
  else if (stepStatus === "Skipped") p.status = "pending";

  // Active point highlight
  if (p.status === "running") {
    // 同一父节点下，workflow_call 是串行的：如果刷新恢复导致多个 sibling 都是 running，
    // 这里用“最新 running”做裁决，把其它 running 置回 pending（若已有共识则置为 completed）。
    const parentId = p.parentId || (cache.points?.rootId || "root");
    const parent = cache.points?.byId?.[parentId];
    if (parent && Array.isArray(parent.children)) {
      parent.children.forEach(cid => {
        if (cid === pointId) return;
        const other = cache.points.byId?.[cid];
        if (other && other.status === "running") {
          other.status = other.consensus ? "completed" : "pending";
        }
      });
    }

    cache.points.activeId = pointId;
    openAncestors(cache, pointId);
  } else if (cache.points.activeId === pointId) {
    cache.points.activeId = null;
  }

  if (p.status !== prevStatus) renderPoints(cache);
}

function openAncestors(cache, pointId) {
  const points = cache.points;
  if (!points?.open) points.open = new Set();
  const byId = points.byId || {};

  let cur = pointId;
  let guard = 0;
  while (cur && guard++ < 32) {
    points.open.add(cur);
    const parent = byId[cur]?.parentId;
    if (!parent || parent === cur) break;
    cur = parent;
  }
}

// ─────────────────────────────────────────────────────────────
//  Worker Logic
//  Use displayName from backend if available, otherwise normalize
//  Group by logical role: coordinator or worker-N
// ─────────────────────────────────────────────────────────────
function normalizeWorkerId(raw = "", displayName = "") {
  let result = "coordinator";
  
  // Debug: log inputs
  if (raw && raw.includes("gen[")) {
    console.log(`[FE] normalizeWorkerId: raw="${raw}", displayName="${displayName}"`);
  }
  
  // If backend provides displayName, use it to determine ID
  if (displayName) {
    const d = displayName.toLowerCase();
    if (d === "coordinator" || d.includes("coordinator")) {
      result = "coordinator";
    } else {
      const wm = d.match(/worker\s*(\d+)/i);
      if (wm) result = `worker-${wm[1]}`;
    }
    // Debug: log when displayName is used
    if (raw && raw.includes("gen[")) {
      console.log(`[FE] Used displayName: "${displayName}" -> "${result}"`);
    }
  } else if (raw) {
    const lower = raw.toLowerCase();
    
    // Coordinator patterns - anything that's NOT a worker gen[N]
    if (lower.includes("check_atomic") || 
        lower.includes("coordinator") || 
        lower === "main" ||
        lower === "coordinator" ||
        (lower.includes("compose") && !lower.includes("gen[")) ||
        lower.endsWith(".vote") ||
        lower.includes("vote")) {
      result = "coordinator";
    } else {
      // Worker patterns: gen[N] or worker-N
      // gen[N] is 1-indexed from backend, convert to 0-indexed for UI
      const genMatch = raw.match(/gen\[(\d+)\]/i);
      if (genMatch) {
        const genIndex = parseInt(genMatch[1], 10);
        result = `worker-${genIndex - 1}`; // gen[1] → worker-0, gen[2] → worker-1
        console.log(`[FE] gen[${genIndex}] -> ${result}`);
      } else {
        const workerMatch = raw.match(/worker[-_ ]?(\d+)/i);
        if (workerMatch) {
          result = `worker-${workerMatch[1]}`;
        } else if (raw.length > 20 && (raw.includes("-") || /^[a-f0-9]+$/i.test(raw))) {
          // UUID-like IDs -> coordinator
          result = "coordinator";
        }
      }
    }
  }
  
  return result;
}

function getDisplayName(normalizedId) {
  if (normalizedId === "coordinator") return "Coordinator";
  const m = normalizedId.match(/worker-(\d+)/);
  if (m) return `Worker ${m[1]}`;
  return normalizedId;
}

function handleWorkerStarted(cache, evt) {
  const workerId = normalizeWorkerId(evt.workerId, evt.displayName);
  upsertWorker(cache, workerId, {
    status: "running",
    provider: evt.providerName || "-",
  });
  scheduleRenderWorkers(cache);
}

function handleLlmCallStart(cache, evt) {
  const workerId = normalizeWorkerId(evt.workerId, evt.displayName);
  const pointId = evt.pointId || "";
  const pointTitle = evt.pointTitle || "";
  
  upsertWorker(cache, workerId, {
    status: "running",
    provider: evt.providerName || "-",
    streaming: true,
    streamContent: "",
    topic: pointTitle || undefined,
    topicId: pointId || undefined,
  });
    
  // Add to history
  const worker = cache.workers[workerId];
  worker.history.unshift({
    system: decodeUnicode(evt.systemPrompt || ""),
    user: decodeUnicode(evt.userPrompt || ""),
    response: "",
    phase: evt.phase || "",
    pointId: pointId || "",
    pointTitle: pointTitle || "",
    timestamp: Date.now(),
  });
  if (worker.history.length > 10) worker.history.pop();

  // Track worker participation for this point
  if (pointId) {
    const p = ensurePoint(cache, pointId, pointTitle);
    p.workers.add(workerId);
  }

  scheduleRenderWorkers(cache);
  renderPoints(cache);
}

function handleLlmStreaming(cache, evt) {
  const workerId = normalizeWorkerId(evt.workerId, evt.displayName);
  const content = decodeUnicode(evt.accumulatedContent || evt.token || "");
  const isLast = evt.isLastToken === true;
  
  upsertWorker(cache, workerId, {
    status: isLast ? "completed" : "running",
    streaming: !isLast,
    streamContent: content,
    lastResponse: isLast ? content : undefined,
    tokenIndex: evt.tokenIndex ?? 0,
  });
    
  // Update history
  const worker = cache.workers[workerId];
  if (worker && worker.history.length > 0) {
    worker.history[0].response = content;
  }
  
  scheduleRenderWorkers(cache);
}

function handleLlmComplete(cache, evt) {
  const workerId = normalizeWorkerId(evt.workerId, evt.displayName);
  const content = decodeUnicode(evt.content || "");
  const pointId = evt.pointId || "";
  const pointTitle = evt.pointTitle || "";
  
  upsertWorker(cache, workerId, {
    streaming: false,
    streamContent: content,
    lastResponse: content,
    status: evt.success !== false ? "completed" : "error",
    topic: pointTitle || undefined,
    topicId: pointId || undefined,
  });

  // Update global stats (tokens / llm calls)
  // - Use callId for idempotency
  // - Keep counters monotonic (never decrease)
  const callId = evt.callId || `${evt.workerId || workerId}:${evt.timestamp || Date.now()}`;
  if (!cache._countedCallIds) cache._countedCallIds = new Set();
  if (!cache._countedCallIds.has(callId)) {
    cache._countedCallIds.add(callId);
    const callTokens = (typeof evt.totalTokens === "number")
      ? evt.totalTokens
      : ((evt.promptTokens || 0) + (evt.completionTokens || 0));
    cache._callTokens = (cache._callTokens || 0) + (callTokens || 0);
    cache._callLlmCalls = (cache._callLlmCalls || 0) + 1;
  }
  cache.tokens = Math.max(cache.tokens || 0, cache._callTokens || 0);
  cache.llmCalls = Math.max(cache.llmCalls || 0, cache._callLlmCalls || 0);
  $("stat-tokens").textContent = formatNumber(cache.tokens);
  $("stat-llm").textContent = cache.llmCalls;
    
  // Update history
  const worker = cache.workers[workerId];
  if (worker && worker.history.length > 0) {
    worker.history[0].response = content;
  }

  // Attach final output to point (optional, for quick compare)
  if (pointId) {
    const p = ensurePoint(cache, pointId, pointTitle);
    p.workers.add(workerId);
    p.proposals.unshift({
      workerId,
      displayName: cache.workers[workerId]?.name || workerId,
      timestamp: Date.now(),
      content,
    });
    if (p.proposals.length > 10) p.proposals.pop();
    renderPoints(cache);
  }
  
  scheduleRenderWorkers(cache);
}

function handleWorkerCompleted(cache, evt) {
  const workerId = normalizeWorkerId(evt.workerId, evt.displayName);
  upsertWorker(cache, workerId, {
    status: evt.success !== false ? "completed" : "error",
    streaming: false,
  });
  scheduleRenderWorkers(cache);
}

function upsertWorker(cache, workerId, patch = {}) {
  if (!cache.workers[workerId]) {
    cache.workers[workerId] = {
      id: workerId,
      name: getDisplayName(workerId),
      provider: "-",
      status: "pending",
      streaming: false,
      streamContent: "",
      lastResponse: "",
      topic: "",
      topicId: "",
      history: [],
      tokenIndex: 0,
    };
  }
  // Only apply defined values from patch
  const worker = cache.workers[workerId];
  for (const [key, value] of Object.entries(patch)) {
    if (value !== undefined) {
      worker[key] = value;
    }
  }
}

// ─────────────────────────────────────────────────────────────
//  Rendering
// ─────────────────────────────────────────────────────────────
function renderFromCache(id) {
  const cache = state.cache[id];
  if (!cache) return;
  renderPipeline(cache.pipeline);
  renderWorkers(cache);
  renderLogs(cache);
  renderVoting(cache);
  renderResult(cache);
  renderPoints(cache);
  $("stat-phase").textContent = cache.progress.phase;
  $("stat-tokens").textContent = formatNumber(cache.tokens);
  $("stat-llm").textContent = cache.llmCalls;
  $("status-text").textContent = cache.progress.message || "Waiting";
  $("progress-bar-inner").style.width = `${cache.progress.percent}%`;
}

function renderPipeline(pipeline = {}) {
  const el = $("pipeline-steps");
  el.innerHTML = "";
  STEPS.forEach(step => {
    const status = pipeline[step] || "pending";
    const div = document.createElement("div");
    div.className = `step ${status}`;
    const icon = status === "done" ? "✓" : status === "active" ? "●" : "";
    div.innerHTML = `<span>${STEP_LABELS[step]}</span><span class="step-icon">${icon}</span>`;
    el.appendChild(div);
  });
}

function renderWorkers(cache) {
  const el = $("worker-grid");
  const workers = Object.values(cache.workers);
  $("worker-count").textContent = workers.length;
            
  if (!workers.length) {
    el.className = "worker-grid empty-state";
    el.textContent = "Waiting for workers";
    // 清空 DOM 缓存（避免 session 切换残留引用）
    cache._workerDom = Object.create(null);
    return;
  }
    
  el.className = "worker-grid";
  // 从 empty-state 切换过来时，grid 里可能残留纯文本节点（"Waiting for workers"），
  // 在 CSS Grid 下它会变成一个匿名 grid item，占一个格子。
  // 这里显式清理所有文本节点，避免占位不消失。
  for (const node of Array.from(el.childNodes)) {
    if (node.nodeType === Node.TEXT_NODE) node.remove();
  }
  // 不再全量 innerHTML 重建：保持卡片 DOM 稳定，避免 streaming 中点击丢失
  
  // Sort: coordinator first, then workers by number
  workers.sort((a, b) => {
    if (a.id === "coordinator") return -1;
    if (b.id === "coordinator") return 1;
    const aNum = parseInt(a.id.replace("worker-", "")) || 999;
    const bNum = parseInt(b.id.replace("worker-", "")) || 999;
    return aNum - bNum;
  });
  
  const domMap = cache._workerDom || (cache._workerDom = Object.create(null));
  const desiredOrder = [];
  const present = new Set();

  workers.forEach(w => {
    present.add(w.id);
    desiredOrder.push(w.id);

    let dom = domMap[w.id];
    if (!dom) {
      dom = domMap[w.id] = createWorkerCardDom(w.id);
    }

    updateWorkerCardDom(dom, w);
  });

  // Remove stale cards (if any)
  for (const id of Object.keys(domMap)) {
    if (!present.has(id)) {
      domMap[id].card.remove();
      delete domMap[id];
    }
  }

  // Reorder ONLY when necessary (避免每帧移动 DOM 节点导致 click 被取消)
  let needReorder = false;
  for (let i = 0; i < desiredOrder.length; i++) {
    const id = desiredOrder[i];
    const node = domMap[id]?.card;
    if (!node) { needReorder = true; break; }
    if (el.children[i] !== node) { needReorder = true; break; }
  }

  if (needReorder) {
    const frag = document.createDocumentFragment();
    for (const id of desiredOrder) {
      frag.appendChild(domMap[id].card);
    }
    el.appendChild(frag);
  }
}

// ============================================================
//  Workers - click handling / stable DOM
// ============================================================

function onWorkerGridClick(e) {
  const card = e.target?.closest?.(".worker-card");
  if (!card) return;
  const workerId = card.dataset?.workerId;
  if (!workerId) return;
  openWorkerModal(workerId);
}

function onWorkerPointerDown(e) {
  const card = e.target?.closest?.(".worker-card");
  if (!card) return;
  const workerId = card.dataset?.workerId;
  if (!workerId) return;

  state.ui.pointerDownWorkerId = workerId;
  state.ui.pointerDownAt = Date.now();
  state.ui.pointerDownX = e.clientX || 0;
  state.ui.pointerDownY = e.clientY || 0;
}

function onWorkerPointerUp(e) {
  const workerId = state.ui.pointerDownWorkerId;
  if (!workerId) return;

  const dt = Date.now() - (state.ui.pointerDownAt || 0);
  const dx = (e.clientX || 0) - (state.ui.pointerDownX || 0);
  const dy = (e.clientY || 0) - (state.ui.pointerDownY || 0);
  const dist2 = dx * dx + dy * dy;

  // Reset state first (avoid double-open if handler throws)
  state.ui.pointerDownWorkerId = null;
  state.ui.pointerDownAt = 0;

  // Treat as click only when it's a short, small movement gesture
  if (dt <= 700 && dist2 <= 64) {
    openWorkerModal(workerId);
  }
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
  const topic = w.topic ? ` · ${truncateText(w.topic, 48)}` : "";
  dom.metaEl.textContent = `${w.provider || "-"} · ${w.tokenIndex || 0} tokens${topic}`;

  // Status
  const statusClass = w.streaming ? "running" : (w.status || "pending");
  const statusText = w.streaming ? "streaming" : (w.status || "pending");

  dom.badgeEl.className = `badge ${statusClass}`;
  dom.badgeEl.textContent = statusText;

  dom.card.classList.toggle("streaming", !!w.streaming);

  // Content
  const displayContent = w.streamContent || w.lastResponse || "";
  if (displayContent) {
    dom.contentEl.style.display = "";
    dom.emptyEl.style.display = "none";
    dom.contentEl.textContent = displayContent;
    dom.contentEl.classList.toggle("streaming-text", !!w.streaming);
  } else {
    dom.contentEl.style.display = "none";
    dom.emptyEl.style.display = "";
  }
}

function renderLogs(cache) {
  const el = $("stage-log");
  if (!cache.logs.length) {
    el.className = "stage-log empty-state";
    el.textContent = "No logs yet";
    return;
  }
  
  el.className = "stage-log";
  el.innerHTML = "";
  
  cache.logs.slice(0, 20).forEach(l => {
    const item = document.createElement("div");
    item.className = "log-item";
    item.innerHTML = `
      <div class="log-header">
        <div class="log-title">${escapeHtml(l.stage || "")} · ${l.status || ""}</div>
        <div class="log-time">${l.time ? new Date(l.time).toLocaleTimeString() : ""}</div>
      </div>
      <div class="log-summary">${escapeHtml(l.summary || "")}</div>
    `;
    if (l.details) {
      const det = document.createElement("details");
      det.innerHTML = `<summary>View details</summary><pre>${escapeHtml(JSON.stringify(l.details, null, 2))}</pre>`;
      item.appendChild(det);
    }
    el.appendChild(item);
  });
}

function renderPoints(cache) {
  const el = $("points-body");
  if (!el) return;

  const points = cache.points;
  const rootId = points?.rootId || "root";
  const byId = points?.byId || {};
  const total = Object.keys(byId).filter(id => id !== rootId).length;
  $("points-count").textContent = total;

  const root = byId[rootId];
  const hasTree = !!root && Array.isArray(root.children) && root.children.length > 0;
  if (!hasTree) {
    el.className = "points-body empty-state";
    el.textContent = "Waiting for decomposition";
    return;
  }

  el.className = "points-body";
  el.innerHTML = "";

  // Root node first (mind-map)
  el.appendChild(buildPointNode(cache, rootId, 0, ""));
}

function buildPointNode(cache, id, level, labelPrefix) {
  const points = cache.points;
  const rootId = points.rootId || "root";
  const byId = points.byId || {};
  const p = byId[id];
  if (!p) return document.createElement("div");

  const active = points.activeId === id;
  const det = document.createElement("details");
  det.className = `point-item point-node${active ? " active" : ""}`;
  det.dataset.pointId = id;

  // Open state: keep user toggles, always open root, always open active
  const isRoot = id === rootId;
  const shouldOpen = isRoot || active || (points.open?.has?.(id) ?? false);
  det.open = !!shouldOpen;

  // Track user open/close to preserve in re-renders
  det.addEventListener("toggle", () => {
    if (!points.open) points.open = new Set();
    if (det.open) points.open.add(id);
    else points.open.delete(id);
  });

  const { cls: badgeCls, text: badgeText } = getPointBadge(p.status);
  const workersCount = p.workers?.size || 0;
  const childrenCount = Array.isArray(p.children) ? p.children.length : 0;
  const meta = [
    childrenCount ? `${childrenCount} children` : "",
    workersCount ? `${workersCount} workers` : "",
    p.consensusSource ? `source: ${p.consensusSource}` : "",
  ].filter(Boolean).join(" · ");

  const title = isRoot ? (p.title || "Paper Review") : (p.title || id);
  const displayTitle = labelPrefix ? `${labelPrefix} ${title}` : title;

  const summary = document.createElement("summary");
  summary.className = "point-summary";
  summary.innerHTML = `
    <div>
      <div class="point-title">${escapeHtml(displayTitle)}</div>
      <div class="point-meta">${escapeHtml(meta)}</div>
    </div>
    <span class="badge ${badgeCls}">${escapeHtml(badgeText)}</span>
  `;
  det.appendChild(summary);

  const body = document.createElement("div");
  body.className = "point-body";

  // Show consensus if available
  const consensusText = (p.consensus || "").trim();
  if (consensusText) {
    const sec = document.createElement("div");
    sec.innerHTML = `
      <div class="point-section-title">Consensus</div>
      <div class="point-consensus">${escapeHtml(consensusText)}</div>
    `;
    body.appendChild(sec);
  } else if (!isRoot) {
    const sec = document.createElement("div");
    const placeholder = p.status === "running" ? "Working..." : "Pending";
    sec.innerHTML = `
      <div class="point-section-title">Consensus</div>
      <div class="point-consensus">${escapeHtml(placeholder)}</div>
    `;
    body.appendChild(sec);
  }

  det.appendChild(body);

  const children = Array.isArray(p.children) ? p.children : [];
  if (children.length) {
    const childrenWrap = document.createElement("div");
    childrenWrap.className = "point-children";

    children.forEach((cid, idx) => {
      const childLabel = isRoot ? `#${idx + 1}` : `${labelPrefix ? labelPrefix + "." : ""}${idx + 1}`;
      childrenWrap.appendChild(buildPointNode(cache, cid, level + 1, childLabel));
    });

    det.appendChild(childrenWrap);
  }

  return det;
}

function getPointBadge(status) {
  const s = (status || "pending").toLowerCase();
  if (s === "running") return { cls: "running", text: "running" };
  if (s === "completed") return { cls: "completed", text: "done" };
  if (s === "error" || s === "failed") return { cls: "error", text: "error" };
  return { cls: "pending", text: "pending" };
}

function renderVoting(cache) {
  const el = $("voting-body");
  const v = cache.voting;
    
  if (!v) {
    el.className = "voting-body empty-state";
    el.textContent = "Waiting for voting events";
    $("vote-k").textContent = "-";
    return;
  }
    
  el.className = "voting-body";
  $("vote-k").textContent = v.votesNeeded ?? "-";
  
  const candidates = (v.candidates || [])
    .map(c => `<div class="pill">${escapeHtml(c.candidateId || "")} · ${c.votes} votes${c.isLeader ? " 👑" : ""}</div>`)
    .join("");
  
  // Check if voting is stuck (high round count without consensus)
  const maxRounds = 10;
  const isStuck = v.round >= maxRounds && !v.consensusReached;
  const stuckWarning = isStuck ? `<div class="pill negative">⚠️ Max rounds reached, selecting best candidate</div>` : "";
  
  el.innerHTML = `
    <div class="voting-row">
      <div class="pill ${v.round > 5 ? 'warning' : ''}">Round ${v.round || 1}/${maxRounds}</div>
      <div class="pill">Type: ${v.votingType || "-"}</div>
      ${stuckWarning}
      <div class="pill ${v.consensusReached ? "positive" : ""}">${v.consensusReached ? "✓ Consensus reached" : "Voting..."}</div>
    </div>
    <div class="voting-row">${candidates || '<div class="pill">No candidates</div>'}</div>
  `;
}

function renderResult(cache) {
  const el = $("result-body");
  
  if (!cache.result) {
    el.className = "result-body empty-state";
    el.textContent = "Result will appear here";
    return;
  }
  
  el.className = "result-body";
  if (cache.result.success === false) {
    el.innerHTML = `
      <div class="result-failed">
        <div class="result-icon">❌</div>
        <div class="result-status">Review Failed</div>
        <div class="result-error">${escapeHtml(cache.result.error || "Unknown error")}</div>
        <div class="result-actions">
          <button class="btn-view-result" onclick="openResultPage()">
            <span class="btn-icon">📄</span>
            <span class="btn-text">View Report</span>
            <span class="btn-arrow">→</span>
          </button>
          <button class="btn ghost" onclick="downloadArtifact('report')">Download report.md</button>
          <button class="btn ghost" onclick="downloadArtifact('details')">Download details.json</button>
        </div>
      </div>
    `;
  } else {
    el.innerHTML = `
      <div class="result-success">
        <div class="result-icon">✅</div>
        <div class="result-status">Review Completed</div>
        <div class="result-meta">${formatNumber(cache.llmCalls || 0)} LLM calls · ${formatNumber(cache.tokens || 0)} tokens</div>
        <div class="result-actions">
          <button class="btn-view-result" onclick="openResultPage()">
            <span class="btn-icon">📄</span>
            <span class="btn-text">View Full Report</span>
            <span class="btn-arrow">→</span>
          </button>
          <button class="btn ghost" onclick="downloadArtifact('report')">Download report.md</button>
          <button class="btn ghost" onclick="downloadArtifact('details')">Download details.json</button>
        </div>
      </div>
    `;
  }
}

// Open result in dedicated result page
function openResultPage() {
  if (!state.current) return;
  window.open(`/result.html?session=${state.current}`, "_blank");
}

// Download session deliverables (report/details)
function downloadArtifact(kind) {
  if (!state.current) return;
  const base = `/api/sessions/${state.current}/artifacts`;
  const url = kind === "details" ? `${base}/details` : `${base}/report`;
  const a = document.createElement("a");
  a.href = url;
  a.target = "_blank";
  a.rel = "noopener";
  document.body.appendChild(a);
  a.click();
  a.remove();
}

// ─────────────────────────────────────────────────────────────
//  Modal
// ─────────────────────────────────────────────────────────────
function openWorkerModal(workerId) {
  const cache = state.cache[state.current];
  const worker = cache?.workers?.[workerId];
  if (!worker) return;

  state.modal.sessionId = state.current;
  state.modal.workerId = workerId;
  state.modal.headTs = worker.history?.[0]?.timestamp || 0;

  renderModal(worker);
  $("modal").classList.remove("hidden");
  document.body.style.overflow = "hidden"; // Lock body scroll
}

function renderModal(worker) {
  $("modal-title").textContent = worker.name || worker.id;
  $("modal-subtitle").textContent =
    `${worker.provider || "-"} · ${worker.history.length} conversations` +
    (worker.streaming ? " · streaming..." : "");

  const body = $("modal-body");
  body.innerHTML = "";

  if (!worker.history.length) {
    body.innerHTML = '<div class="empty-state">No conversation history</div>';
    return;
  }

  worker.history.forEach((h, idx) => {
    const item = document.createElement("details");
    item.className = "chat-item";
    item.open = idx === 0; // First item open by default

    const summary = document.createElement("summary");
    summary.className = "chat-header";
    summary.innerHTML = `
      <div class="chat-phase">${escapeHtml(h.phase || "LLM Call")}</div>
      <div class="chat-time">${new Date(h.timestamp).toLocaleTimeString()}</div>
    `;
    item.appendChild(summary);

    const bodyDiv = document.createElement("div");
    bodyDiv.className = "chat-body";

    if (h.system) {
      bodyDiv.appendChild(buildChatSection("System Prompt", h.system, false));
    }
    if (h.user) {
      bodyDiv.appendChild(buildChatSection("User Prompt", h.user, true));
    }

    // Response (idx=0 will be live-updated)
    const responseText = h.response || "";
    const respSection = buildChatSection("Response", responseText, true);
    const respContent = respSection.querySelector(".chat-content");
    if (respContent) {
      respContent.dataset.role = "modal-response";
      respContent.dataset.idx = String(idx);
      setChatContentText(respContent, responseText, worker.streaming && idx === 0);
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
  el.style.color = ""; // reset
}

function updateModalIfOpen(cache) {
  const modalEl = $("modal");
  if (modalEl.classList.contains("hidden")) return;

  const { sessionId, workerId } = state.modal || {};
  if (!sessionId || !workerId) return;
  if (sessionId !== state.current) return;

  const worker = cache?.workers?.[workerId];
  if (!worker) return;

  // If a new call starts while modal is open, rebuild modal once (not per token)
  const headTs = worker.history?.[0]?.timestamp || 0;
  if (headTs && headTs !== state.modal.headTs) {
    state.modal.headTs = headTs;
    renderModal(worker);
    return;
  }

  // Update header meta
  $("modal-title").textContent = worker.name || worker.id;
  $("modal-subtitle").textContent =
    `${worker.provider || "-"} · ${worker.history.length} conversations` +
    (worker.streaming ? " · streaming..." : "");

  // Update latest response only (idx=0)
  const respEl = $("modal-body").querySelector('[data-role="modal-response"][data-idx="0"]');
  if (!respEl) return;
  const latest = worker.history?.[0]?.response || "";
  setChatContentText(respEl, latest, !!worker.streaming);
}

function closeModal() {
  $("modal").classList.add("hidden");
  document.body.style.overflow = ""; // Restore body scroll
  state.modal.sessionId = null;
  state.modal.workerId = null;
  state.modal.headTs = 0;
}

// ─────────────────────────────────────────────────────────────
//  Utilities
// ─────────────────────────────────────────────────────────────
function escapeHtml(str = "") {
  return String(str).replace(/[&<>"']/g, c => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
  }[c]));
}
    
function decodeUnicode(str = "") {
  if (!str) return "";
  // Decode Unicode escapes
  let result = str.replace(/\\u([0-9a-fA-F]{4})/g, (_, hex) =>
    String.fromCharCode(parseInt(hex, 16))
  );
  // Decode common escape sequences
  result = result
    .replace(/\\n/g, "\n")
    .replace(/\\r/g, "\r")
    .replace(/\\t/g, "\t")
    .replace(/\\\\/g, "\\");
  return result;
}

function formatTime(iso) {
  if (!iso) return "";
  return new Date(iso).toLocaleString("en-US", {
    month: "short", day: "numeric", hour: "2-digit", minute: "2-digit"
  });
}

function formatNumber(n) {
  if (n >= 1000000) return (n / 1000000).toFixed(1) + "M";
  if (n >= 1000) return (n / 1000).toFixed(1) + "K";
  return String(n);
}

function truncateText(str = "", max = 80) {
  const s = String(str || "");
  if (s.length <= max) return s;
  return s.slice(0, Math.max(0, max - 1)) + "…";
}

function mapPhaseToStep(phase = "") {
  const p = phase.toLowerCase();
  if (p.includes("start") || p === "starting" || p === "-") return "submit";
  if (p.includes("assess") || p.includes("analyze") || p.includes("check")) return "analyze";
  if (p.includes("decompos")) return "decompose";
  if (p.includes("solve") || p.includes("execut")) return "execute";
  if (p.includes("vote") || p.includes("consensus")) return "vote";
  if (p.includes("compose") || p.includes("synth")) return "compose";
  if (p.includes("complete") || p.includes("finish") || p.includes("done")) return "complete";
  return "submit";
}

async function fetchJson(url) {
  const res = await fetch(url);
  return res.json();
}

function toast(msg, type = "info") {
  const container = $("toast-container");
  const t = document.createElement("div");
  t.className = `toast ${type}`;
  t.textContent = msg;
  container.appendChild(t);
  setTimeout(() => t.remove(), type === "error" ? 5000 : 3000);
}
