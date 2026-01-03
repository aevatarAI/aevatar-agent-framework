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

async function fetchForm(url, formData, options = {}) {
  const res = await fetch(url, {
    method: "POST",
    body: formData,
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

// ============================================================
//  UI State (theme / layout) - minimal, localStorage-backed
// ============================================================
const THEME_KEY = "aevatar.notebook.theme"; // light | dark | device

function getAppRoot() {
  return el("appRoot") || document.body;
}

function setActiveThemeButton(theme) {
  const buttons = document.querySelectorAll(".seg-btn");
  buttons.forEach((b) => {
    const t = b.getAttribute("data-theme") || "";
    b.classList.toggle("is-active", t === theme);
  });
}

function applyTheme(theme) {
  const t = theme || "light";
  document.documentElement.setAttribute("data-theme", t);
  localStorage.setItem(THEME_KEY, t);
  setActiveThemeButton(t);
}

function initTheme() {
  const saved = localStorage.getItem(THEME_KEY);
  applyTheme(saved || "light");

  document.querySelectorAll(".seg-btn").forEach((b) => {
    b.addEventListener("click", () => {
      const t = b.getAttribute("data-theme") || "light";
      applyTheme(t);
    });
  });
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
  div.className = `chat-item ${role}`;
  const label = role === "user" ? "YOU" : "ASSISTANT";
  div.innerHTML = `<div class="chat-meta"><span class="chat-role">${label}</span></div><div class="chat-content"></div>`;
  div.querySelector(".chat-content").textContent = content;
  log.appendChild(div);
  log.scrollTop = log.scrollHeight;
  return div;
}

function pretty(obj) {
  return JSON.stringify(obj, null, 2);
}

let lastInfo = null;
const selectedSources = new Set(); // empty => all sources
let cachedSources = [];

async function refreshInfo() {
  try {
    const info = await fetchJson("/api/info");
    lastInfo = info;
    if (info.isReady) {
      setBadge(true, `ready · ${info.llmDefaultProvider} · ${info.agentId.slice(0, 10)}…`);
    } else {
      setBadge(false, `not ready · ${info.lastError || "initializing..."}`);
    }
    el("chatMeta").textContent = pretty({
      persistence: info.persistence,
      selection: { selectedSourceIds: Array.from(selectedSources) },
    });
    return info;
  } catch (e) {
    setBadge(false, `error · ${e.message}`);
    el("chatMeta").textContent = e.message;
    return null;
  }
}

function updateSelectedSourcesInfo(total) {
  const n = selectedSources.size;
  el("selectedSourcesInfo").textContent =
    `selected: ${n} / ${total} (${n === 0 ? "empty = all" : "only selected"})`;

  const badge = el("chatSelectedBadge");
  if (badge) badge.textContent = n === 0 ? "All sources" : `${n} selected`;
}

function filterSourcesByQuery(list, q) {
  const query = (q || "").trim().toLowerCase();
  if (!query) return list;
  return list.filter((s) => {
    const id = (s.sourceId || "").toLowerCase();
    const name = (s.displayName || s.title || "").toLowerCase();
    const mem = (s.memoryId || "").toLowerCase();
    return id.includes(query) || name.includes(query) || mem.includes(query);
  });
}

function renderSourcesList(view, total) {
  const list = el("sourcesList");
  list.innerHTML = "";

  updateSelectedSourcesInfo(total);

  const fmtAt = (iso) => {
    if (!iso) return "";
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    const pad = (n) => String(n).padStart(2, "0");
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  };

  for (const s of view) {
    const div = document.createElement("div");
    div.className = "list-item source-item";
    div.title = s.sourceId || "";
    const latest = fmtAt(s.latestAt || "");
    const displayName = s.displayName || s.title || s.sourceId || "";
    div.innerHTML = `
      <div class="source-left">
        <input class="source-check" type="checkbox" />
        <div class="meta">
          <div class="k">${displayName}</div>
          <div class="m">${s.entryCount} entries${latest ? " · " + latest : ""}</div>
        </div>
      </div>
    `;

    const chk = div.querySelector(".source-check");
    chk.checked = selectedSources.has(s.sourceId);
    chk.onchange = (e) => {
      if (chk.checked) selectedSources.add(s.sourceId);
      else selectedSources.delete(s.sourceId);
      updateSelectedSourcesInfo(total);
      e.stopPropagation();
    };

    div.querySelector(".meta").onclick = async () => {
      const detail = await fetchJson(`/api/sources/${encodeURIComponent(s.sourceId)}`);
      el("sourceDetail").textContent = pretty(detail);
    };

    list.appendChild(div);
  }

  if (view.length === 0) {
    list.innerHTML = `<div class="list-item"><div class="m">(no sources)</div></div>`;
  }
}

async function refreshSources() {
  try {
    const data = await fetchJson("/api/sources");
    const sources = data.sources || [];
    cachedSources = sources;

    // Drop selections that no longer exist.
    const existing = new Set(sources.map((s) => s.sourceId));
    for (const id of Array.from(selectedSources)) {
      if (!existing.has(id)) selectedSources.delete(id);
    }

    const q = (el("globalSearch") && el("globalSearch").value) || "";
    const view = filterSourcesByQuery(sources, q);
    renderSourcesList(view, sources.length);
  } catch (e) {
    const list = el("sourcesList");
    list.innerHTML = `<div class="list-item"><div class="m">error: ${e.message}</div></div>`;
    cachedSources = [];
    updateSelectedSourcesInfo(0);
  }
}

async function addSource() {
  const title = (el("sourceTitle").value || "").trim();
  const text = (el("sourceText").value || "").trim();
  if (!text) return;

  try {
    const out = await fetchJson("/api/sources/text", {
      method: "POST",
      body: JSON.stringify({ title: title || null, text }),
    });
    appendMsg("assistant", `Source added: ${out.sourceId}`);
    el("sourceText").value = "";
    await refreshSources();
  } catch (e) {
    appendMsg("assistant", `Add source error: ${e.message}`);
  }
}

async function uploadSource() {
  const input = el("sourceFile");
  const file = input.files && input.files[0];
  if (!file) return;

  try {
    const fd = new FormData();
    fd.append("file", file);
    const out = await fetchForm("/api/sources/file", fd);
    appendMsg("assistant", `Source uploaded: ${out.sourceId}`);
    input.value = "";
    await refreshSources();
  } catch (e) {
    appendMsg("assistant", `Upload source error: ${e.message}`);
  }
}

function renderCitations(parent, citations) {
  if (!citations || citations.length === 0) return;

  const wrap = document.createElement("div");
  wrap.className = "chips";

  for (const c of citations) {
    const sourceId = c.sourceId || "";
    const chunkId = c.chunkId || "";
    const btn = document.createElement("button");
    btn.className = "chip";
    btn.textContent = chunkId ? `${sourceId}:${chunkId}` : `${sourceId}`;
    btn.onclick = async () => {
      await openCitation(sourceId, chunkId);
    };
    wrap.appendChild(btn);
  }

  parent.appendChild(wrap);
}

async function openCitation(sourceId, chunkId) {
  if (!sourceId) return;
  const detail = await fetchJson(`/api/sources/${encodeURIComponent(sourceId)}?limit=2000`);

  let hit = null;
  for (const e of detail.entries || []) {
    const tags = e.Tags || {};
    if (chunkId && tags.chunk_id === chunkId) {
      hit = e;
      break;
    }
  }

  el("sourceDetail").textContent = pretty({
    sourceId,
    chunkId: chunkId || null,
    hit,
    detail,
  });
}

async function sendChat() {
  const msg = (el("chatInput").value || "").trim();
  if (!msg) return;
  el("chatInput").value = "";
  appendMsg("user", msg);

  // Always allocate an assistant slot so errors don't create duplicate bubbles.
  const div = appendMsg("assistant", "");
  const contentEl = div.querySelector(".chat-content");
  const toolsEl = document.createElement("div");
  toolsEl.className = "chat-tools";
  const metaEl = div.querySelector(".chat-meta");
  if (metaEl && metaEl.insertAdjacentElement) metaEl.insertAdjacentElement("afterend", toolsEl);
  else div.appendChild(toolsEl);

  try {
    const payload = { message: msg };
    const selected = Array.from(selectedSources);
    if (selected.length > 0) payload.selectedSourceIds = selected;

    // Streaming (NDJSON): /api/chat/stream
    let meta = null;
    const toolMap = new Map(); // toolCallId -> element
    const toolByName = new Map(); // toolName -> element (merge)
    const minSpinnerMs = 320;

    // UX: show "thinking" immediately to avoid dead air.
    const thinkPill = document.createElement("div");
    thinkPill.className = "tool-pill is-running tool-phase";
    thinkPill.innerHTML = `<span class="tool-icon tool-spinner"></span><span class="tool-name">思考中…</span>`;
    toolsEl.appendChild(thinkPill);

    let genPill = null;
    let genTimer = null;

    function clearGenPill() {
      if (genTimer) {
        clearTimeout(genTimer);
        genTimer = null;
      }
      if (genPill && genPill.parentNode) {
        genPill.parentNode.removeChild(genPill);
      }
      genPill = null;
    }

    function ensureGenPill(delayMs = 200) {
      if (genPill) return;
      if (genTimer) return;
      genTimer = setTimeout(() => {
        genTimer = null;
        if (genPill) return;
        genPill = document.createElement("div");
        genPill.className = "tool-pill is-running tool-phase";
        genPill.innerHTML = `<span class="tool-icon tool-spinner"></span><span class="tool-name">生成中…</span>`;
        toolsEl.appendChild(genPill);
        const log = el("chatLog");
        log.scrollTop = log.scrollHeight;
      }, Math.max(0, delayMs));
    }

    const res = await fetch("/api/chat/stream", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });

    // Fallback: if streaming is not available, use non-streaming endpoint.
    if (!res.ok) {
      const text = await res.text();
      throw new Error(text || `HTTP ${res.status}`);
    }

    if (!res.body || !res.body.getReader) {
      const out = await fetchJson("/api/chat", { method: "POST", body: JSON.stringify(payload) });
      contentEl.textContent = out.content || "";
      renderCitations(div, out.citations || []);
      el("chatMeta").textContent = pretty({
        persistence: (lastInfo && lastInfo.persistence) || null,
        lastChat: {
          executionId: out.executionId,
          requestId: out.requestId,
          context: out.context || null,
          citations: (out.citations || []).length,
        },
        selection: { selectedSourceIds: selected },
      });
      return;
    }

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buf = "";
    let doneRendered = false;
    let hasAnyDelta = false;

    function applyEvent(evt) {
      if (!evt || !evt.type) return;

      if (evt.type === "tool_start") {
        const id = evt.toolCallId || `${Date.now()}-${Math.random()}`;
        const name = evt.toolName || "tool";

        // Remove initial thinking indicator once tools start (user already sees progress).
        if (thinkPill && thinkPill.parentNode) thinkPill.parentNode.removeChild(thinkPill);

        let row = toolByName.get(name);
        if (!row) {
          row = document.createElement("div");
          row.className = "tool-pill is-running";
          row.dataset.toolName = name;
          row.dataset.count = "0";
          row.dataset.running = "0";

          const icon = document.createElement("span");
          icon.className = "tool-icon tool-spinner";
          const label = document.createElement("span");
          label.className = "tool-name";
          label.textContent = name;

          row.appendChild(icon);
          row.appendChild(label);
          toolsEl.appendChild(row);
          toolByName.set(name, row);
        }

        const total = Math.max(0, parseInt(row.dataset.count || "0", 10)) + 1;
        const running = Math.max(0, parseInt(row.dataset.running || "0", 10)) + 1;
        row.dataset.count = String(total);
        row.dataset.running = String(running);
        row.dataset.lastStartAt = String(Date.now());

        const label = row.querySelector(".tool-name");
        if (label) label.textContent = total > 1 ? `${name} ×${total}` : name;

        row.classList.add("is-running");
        row.classList.remove("is-done");
        const icon = row.querySelector(".tool-icon");
        if (icon) {
          icon.classList.remove("tool-ok", "tool-err");
          icon.classList.add("tool-spinner");
          icon.textContent = "";
        }

        toolMap.set(id, row);

        const log = el("chatLog");
        log.scrollTop = log.scrollHeight;
        return;
      }

      if (evt.type === "tool_end") {
        const id = evt.toolCallId || "";
        const name = evt.toolName || "tool";
        const ok = !!evt.success;

        let row = id ? toolMap.get(id) : null;
        if (!row) row = toolByName.get(name) || null;
        if (!row) return;

        const running = Math.max(0, parseInt(row.dataset.running || "0", 10)) - 1;
        row.dataset.running = String(Math.max(0, running));

        // If other calls of same tool still running, keep spinner.
        if (running > 0) {
          return;
        }

        // Tool finished. The model usually needs another LLM round to synthesize the final answer.
        // Show "generating" indicator if we don't get new deltas soon.
        ensureGenPill(160);

        const lastStartAt = Math.max(0, parseInt(row.dataset.lastStartAt || "0", 10));
        const elapsed = lastStartAt ? Date.now() - lastStartAt : 0;
        const delay = Math.max(0, minSpinnerMs - elapsed);

        const applyDone = () => {
          row.classList.remove("is-running");
          row.classList.add("is-done");

          const icon = row.querySelector(".tool-icon");
          if (icon) {
            icon.classList.remove("tool-spinner");
            icon.classList.add(ok ? "tool-ok" : "tool-err");
            icon.textContent = ok ? "✓" : "✕";
          }

          const ms = typeof evt.durationMs === "number" ? Math.max(0, Math.round(evt.durationMs)) : null;
          if (!ok && evt.error) row.title = `${name}: ${evt.error}`;
          else if (ms !== null) row.title = `${name}: ${ms}ms`;
        };

        if (delay > 0) setTimeout(applyDone, delay);
        else applyDone();

        const log = el("chatLog");
        log.scrollTop = log.scrollHeight;
        return;
      }

      if (evt.type === "meta") {
        meta = evt;
        el("chatMeta").textContent = pretty({
          persistence: (lastInfo && lastInfo.persistence) || null,
          lastChat: {
            executionId: evt.executionId,
            requestId: evt.requestId,
            context: evt.context || null,
            citations: (evt.citations || []).length,
          },
          selection: { selectedSourceIds: selected },
        });
        return;
      }

      if (evt.type === "delta") {
        const t = evt.content || "";
        if (t) {
          hasAnyDelta = true;
          // Remove dead-air indicators once we get real tokens.
          if (thinkPill && thinkPill.parentNode) thinkPill.parentNode.removeChild(thinkPill);
          clearGenPill();

          contentEl.textContent += t;
          const log = el("chatLog");
          log.scrollTop = log.scrollHeight;
        }
        return;
      }

      if (evt.type === "error") {
        const err = evt.error || "unknown error";
        clearGenPill();
        contentEl.textContent += `\n\n[error] ${err}`;
        return;
      }

      if (evt.type === "done") {
        clearGenPill();
        if (!hasAnyDelta && thinkPill && thinkPill.parentNode) thinkPill.parentNode.removeChild(thinkPill);
        if (!doneRendered) {
          doneRendered = true;
          renderCitations(div, (meta && meta.citations) || []);
        }
      }
    }

    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      buf += decoder.decode(value, { stream: true });

      while (true) {
        const idx = buf.indexOf("\n");
        if (idx < 0) break;
        const line = buf.slice(0, idx).trim();
        buf = buf.slice(idx + 1);
        if (!line) continue;
        try {
          applyEvent(JSON.parse(line));
        } catch (e) {
          // Ignore malformed line; keep UI responsive.
        }
      }
    }

    // Flush remaining buffered line (best-effort)
    const tail = buf.trim();
    if (tail) {
      try {
        applyEvent(JSON.parse(tail));
      } catch (e) {
        // ignore
      }
    }
  } catch (e) {
    contentEl.textContent = `Chat error: ${e.message}`;
  }
}

async function refreshReports() {
  const list = el("reportsList");
  if (!list) return;
  list.innerHTML = "";
  try {
    const fmtAt = (iso) => {
      if (!iso) return "";
      const d = new Date(iso);
      if (Number.isNaN(d.getTime())) return iso;
      // Compact (local-ish): YYYY-MM-DD HH:mm
      const pad = (n) => String(n).padStart(2, "0");
      return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
    };

    const data = await fetchJson("/api/reports");
    for (const r of data.reports || []) {
      const div = document.createElement("div");
      div.className = "studio-output";
      const latest = fmtAt(r.latestAt || "");
      div.innerHTML = `
        <div class="studio-output-left">
          <div class="studio-output-ico" aria-hidden="true">📄</div>
          <div class="studio-output-text">
            <div class="studio-output-title">${r.reportId}</div>
            <div class="studio-output-sub">${r.entryCount} entries${latest ? " · " + latest : ""}</div>
          </div>
        </div>
        <div class="studio-output-meta">
          <a href="/report.html?reportId=${encodeURIComponent(r.reportId)}" target="_blank" rel="noreferrer" onclick="event.stopPropagation()">Open</a>
        </div>
      `;
      div.onclick = async () => {
        const detail = await fetchJson(`/api/reports/${encodeURIComponent(r.reportId)}`);

        const metaPre = el("reportMeta");
        const metaWrap = el("reportDebug");
        if (metaPre) metaPre.textContent = pretty(detail);
        if (metaWrap) metaWrap.hidden = !metaPre || !metaPre.textContent;

        const latest = (detail.entries || [])[0];
        el("reportBox").textContent = (latest && latest.Content) || "(empty)";
      };
      list.appendChild(div);
    }
    if ((data.reports || []).length === 0) {
      list.innerHTML = `<div class="studio-output"><div class="studio-output-sub">(no reports yet)</div></div>`;
    }
  } catch (e) {
    list.innerHTML = `<div class="studio-output"><div class="studio-output-sub">error: ${e.message}</div></div>`;
  }
}

async function genReport() {
  const topic = (el("reportTopic").value || "").trim();
  const status = el("reportStatus");
  const streamDetails = el("reportStreamDetails");
  const streamTools = el("reportStreamTools");
  const streamLog = el("reportStreamLog");

  if (status) status.textContent = "Generating…";
  el("reportBox").textContent = "";

  if (streamDetails) {
    streamDetails.hidden = false;
    streamDetails.open = true;
  }
  if (streamTools) streamTools.innerHTML = "";
  if (streamLog) streamLog.innerHTML = "";

  const selected = Array.from(selectedSources);
  const payload = { topic: topic || null };
  if (selected.length > 0) payload.selectedSourceIds = selected;

  // Stage UI
  const stageTitle = { outline: "Outline", draft: "Draft", refine: "Refine" };
  const stageMap = new Map(); // stage -> { details, summary, pre }
  let currentStage = "";

  function ensureStage(stage) {
    if (!streamLog) return null;
    const key = stage || "stage";
    if (stageMap.has(key)) return stageMap.get(key);

    const details = document.createElement("details");
    details.className = "meta meta-compact";
    details.open = true;
    details.dataset.stage = key;

    const summary = document.createElement("summary");
    summary.className = "meta-summary";
    summary.textContent = `${stageTitle[key] || key} · running…`;

    const pre = document.createElement("pre");
    pre.className = "pre pre-small";
    pre.textContent = "";

    details.appendChild(summary);
    details.appendChild(pre);
    streamLog.appendChild(details);

    const node = { details, summary, pre };
    stageMap.set(key, node);
    return node;
  }

  // Tool pills (same idea as chat: merge by tool name, ensure spinner is visible)
  const toolByName = new Map();
  const toolMap = new Map(); // toolCallId -> element
  const minSpinnerMs = 320;

  function toolStart(toolCallId, toolName) {
    if (!streamTools) return;
    const name = toolName || "tool";

    let row = toolByName.get(name);
    if (!row) {
      row = document.createElement("div");
      row.className = "tool-pill is-running";
      row.dataset.toolName = name;
      row.dataset.count = "0";
      row.dataset.running = "0";

      const icon = document.createElement("span");
      icon.className = "tool-icon tool-spinner";
      const label = document.createElement("span");
      label.className = "tool-name";
      label.textContent = name;

      row.appendChild(icon);
      row.appendChild(label);
      streamTools.appendChild(row);
      toolByName.set(name, row);
    }

    const total = Math.max(0, parseInt(row.dataset.count || "0", 10)) + 1;
    const running = Math.max(0, parseInt(row.dataset.running || "0", 10)) + 1;
    row.dataset.count = String(total);
    row.dataset.running = String(running);
    row.dataset.lastStartAt = String(Date.now());

    const label = row.querySelector(".tool-name");
    if (label) label.textContent = total > 1 ? `${name} ×${total}` : name;

    row.classList.add("is-running");
    row.classList.remove("is-done");
    const icon = row.querySelector(".tool-icon");
    if (icon) {
      icon.classList.remove("tool-ok", "tool-err");
      icon.classList.add("tool-spinner");
      icon.textContent = "";
    }

    toolMap.set(toolCallId || `${Date.now()}-${Math.random()}`, row);
  }

  function toolEnd(toolCallId, toolName, success, durationMs, error) {
    const name = toolName || "tool";
    let row = toolCallId ? toolMap.get(toolCallId) : null;
    if (!row) row = toolByName.get(name) || null;
    if (!row) return;

    const running = Math.max(0, parseInt(row.dataset.running || "0", 10)) - 1;
    row.dataset.running = String(Math.max(0, running));
    if (running > 0) return;

    const lastStartAt = Math.max(0, parseInt(row.dataset.lastStartAt || "0", 10));
    const elapsed = lastStartAt ? Date.now() - lastStartAt : 0;
    const delay = Math.max(0, minSpinnerMs - elapsed);

    const applyDone = () => {
      row.classList.remove("is-running");
      row.classList.add("is-done");

      const icon = row.querySelector(".tool-icon");
      if (icon) {
        icon.classList.remove("tool-spinner");
        icon.classList.add(success ? "tool-ok" : "tool-err");
        icon.textContent = success ? "✓" : "✕";
      }

      const ms = typeof durationMs === "number" ? Math.max(0, Math.round(durationMs)) : null;
      if (!success && error) row.title = `${name}: ${error}`;
      else if (ms !== null) row.title = `${name}: ${ms}ms`;
    };

    if (delay > 0) setTimeout(applyDone, delay);
    else applyDone();
  }

  try {
    const res = await fetch("/api/report/stream", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });

    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    if (!res.body) throw new Error("Streaming not supported by browser");

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buf = "";
    let meta = null;

    async function applyEvent(evt) {
      if (!evt || !evt.type) return;

      if (evt.type === "meta") {
        meta = evt;
        if (status) status.textContent = `Generating… (reportId=${evt.reportId || ""})`;
        return;
      }

      if (evt.type === "stage_start") {
        currentStage = evt.stage || "";
        const node = ensureStage(currentStage);
        if (node) {
          node.details.open = true;
          node.summary.textContent = `${stageTitle[currentStage] || currentStage} · running…`;
        }
        return;
      }

      if (evt.type === "stage_delta") {
        const stage = evt.stage || currentStage || "";
        const node = ensureStage(stage);
        if (!node) return;

        const t = evt.content || "";
        if (!t) return;

        node.pre.textContent += t;
        if (stage === "refine") {
          // Live preview for final stage (markdown viewer is on a separate page).
          el("reportBox").textContent = node.pre.textContent;
        }
        return;
      }

      if (evt.type === "stage_end") {
        const stage = evt.stage || currentStage || "";
        const node = ensureStage(stage);
        if (!node) return;

        const ms = typeof evt.durationMs === "number" ? Math.max(0, Math.round(evt.durationMs)) : null;
        const chars = typeof evt.chars === "number" ? Math.max(0, Math.round(evt.chars)) : null;
        const dur = ms !== null ? `${ms}ms` : "";
        const cc = chars !== null ? `${chars} chars` : "";
        const extra = [dur, cc].filter(Boolean).join(" · ");

        node.summary.textContent = `${stageTitle[stage] || stage} · done${extra ? " · " + extra : ""}`;

        // Auto-collapse completed stages to keep UI compact.
        node.details.open = false;
        return;
      }

      if (evt.type === "tool_start") {
        toolStart(evt.toolCallId, evt.toolName);
        return;
      }

      if (evt.type === "tool_end") {
        toolEnd(evt.toolCallId, evt.toolName, !!evt.success, evt.durationMs, evt.error);
        return;
      }

      if (evt.type === "saved") {
        const url = evt.viewerUrl || (evt.reportId ? `/report.html?reportId=${encodeURIComponent(evt.reportId)}` : "");
        if (status) {
          status.innerHTML = `Saved v${evt.version || "?"}${url ? ` · <a href="${url}" target="_blank" rel="noreferrer">Open</a>` : ""}`;
        }

        // Debug summary (collapsible)
        const metaPre = el("reportMeta");
        const metaWrap = el("reportDebug");
        if (metaPre) {
          metaPre.textContent = pretty({
            reportId: evt.reportId || (meta && meta.reportId) || "",
            version: evt.version || "",
            executionId: (meta && meta.executionId) || "",
            topic: (meta && meta.topic) || topic || "",
            context: (meta && meta.context) || null,
            citations: (meta && meta.citations ? meta.citations.length : 0) || 0,
            selection: { selectedSourceIds: selected },
          });
        }
        if (metaWrap) metaWrap.hidden = !metaPre || !metaPre.textContent;

        await refreshReports();
        return;
      }

      if (evt.type === "error") {
        const err = evt.error || "unknown error";
        if (status) status.textContent = `error: ${err}`;
        el("reportBox").textContent = `error: ${err}`;
        return;
      }

      if (evt.type === "done") {
        if (status && status.textContent === "Generating…") status.textContent = "";
        return;
      }
    }

    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      buf += decoder.decode(value, { stream: true });

      let idx;
      while ((idx = buf.indexOf("\n")) >= 0) {
        const line = buf.slice(0, idx).trim();
        buf = buf.slice(idx + 1);
        if (!line) continue;
        try {
          await applyEvent(JSON.parse(line));
        } catch (e) {
          // ignore malformed line
        }
      }
    }

    // Flush remaining buffered line (best-effort)
    const tail = buf.trim();
    if (tail) {
      try {
        await applyEvent(JSON.parse(tail));
      } catch (e) {
        // ignore
      }
    }
  } catch (e) {
    el("reportBox").textContent = `error: ${e.message}`;
    if (status) status.textContent = e.message;
  }
}

async function reset() {
  try {
    await fetchJson("/api/reset", { method: "POST", body: "{}" });
    selectedSources.clear();
    await refreshInfo();
    await refreshSources();
    await refreshReports();
    el("reportBox").textContent = "";
    el("reportMeta").textContent = "";
    el("sourceDetail").textContent = "";
    el("chatLog").innerHTML = "";
    appendMsg("assistant", "Reset done.");
  } catch (e) {
    appendMsg("assistant", `Reset error: ${e.message}`);
  }
}

function updateFocusButton() {
  const root = getAppRoot();
  const btn = el("focusBtn");
  if (!btn) return;
  btn.textContent = root.classList.contains("is-focus") ? "退出专注" : "专注";
}

function enterFocus() {
  const root = getAppRoot();
  root.dataset.prevLeft = root.classList.contains("is-left-collapsed") ? "1" : "0";
  root.dataset.prevRight = root.classList.contains("is-right-collapsed") ? "1" : "0";
  root.classList.add("is-focus", "is-left-collapsed", "is-right-collapsed");
  updateFocusButton();
}

function exitFocus() {
  const root = getAppRoot();
  root.classList.remove("is-focus");
  const prevLeft = root.dataset.prevLeft === "1";
  const prevRight = root.dataset.prevRight === "1";
  root.classList.toggle("is-left-collapsed", prevLeft);
  root.classList.toggle("is-right-collapsed", prevRight);
  updateFocusButton();
}

function toggleLeft() {
  const root = getAppRoot();
  if (root.classList.contains("is-focus")) exitFocus();
  root.classList.toggle("is-left-collapsed");
}

function toggleRight() {
  const root = getAppRoot();
  if (root.classList.contains("is-focus")) exitFocus();
  root.classList.toggle("is-right-collapsed");
}

function wire() {
  el("refreshSourcesBtn").onclick = refreshSources;
  el("addSourceBtn").onclick = addSource;
  el("uploadSourceBtn").onclick = uploadSource;
  el("clearSelectedSourcesBtn").onclick = async () => {
    selectedSources.clear();
    await refreshSources();
  };
  el("sendBtn").onclick = sendChat;
  el("genReportBtn").onclick = genReport;
  el("refreshReportsBtn").onclick = refreshReports;
  el("resetBtn").onclick = reset;

  const leftBtn = el("toggleLeftBtn");
  if (leftBtn) leftBtn.onclick = toggleLeft;

  const rightBtn = el("toggleRightBtn");
  if (rightBtn) rightBtn.onclick = toggleRight;

  const focusBtn = el("focusBtn");
  if (focusBtn) {
    focusBtn.onclick = () => {
      const root = getAppRoot();
      if (root.classList.contains("is-focus")) exitFocus();
      else enterFocus();
    };
  }
  updateFocusButton();

  const search = el("globalSearch");
  if (search) {
    search.addEventListener("input", () => {
      const view = filterSourcesByQuery(cachedSources, search.value || "");
      renderSourcesList(view, cachedSources.length);
    });
  }

  const chips = el("promptChips");
  if (chips) {
    chips.addEventListener("click", (e) => {
      const btn = e.target && e.target.closest && e.target.closest("button[data-prompt]");
      if (!btn) return;
      const prompt = btn.getAttribute("data-prompt") || "";
      if (!prompt) return;
      el("chatInput").value = prompt;
      el("chatInput").focus();
    });
  }

  el("chatInput").addEventListener("keydown", (e) => {
    if (e.key === "Enter") sendChat();
  });
}

async function main() {
  initTheme();
  wire();
  await refreshInfo();
  await refreshSources();
  await refreshReports();
}

main();


