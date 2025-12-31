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
    el("chatMeta").textContent = pretty({ persistence: info.persistence });
    return info;
  } catch (e) {
    setBadge(false, `error · ${e.message}`);
    el("chatMeta").textContent = e.message;
    return null;
  }
}

async function refreshSources() {
  const list = el("sourcesList");
  list.innerHTML = "";
  try {
    const data = await fetchJson("/api/sources");
    for (const s of data.sources || []) {
      const div = document.createElement("div");
      div.className = "list-item";
      div.innerHTML = `<div class="k">${s.sourceId}</div><div class="m">${s.entryCount} entries · ${s.latestAt || ""}</div>`;
      div.onclick = async () => {
        const detail = await fetchJson(`/api/sources/${encodeURIComponent(s.sourceId)}`);
        el("sourceDetail").textContent = pretty(detail);
      };
      list.appendChild(div);
    }
    if ((data.sources || []).length === 0) {
      list.innerHTML = `<div class="list-item"><div class="m">(no sources yet)</div></div>`;
    }
  } catch (e) {
    list.innerHTML = `<div class="list-item"><div class="m">error: ${e.message}</div></div>`;
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

async function sendChat() {
  const msg = (el("chatInput").value || "").trim();
  if (!msg) return;
  el("chatInput").value = "";
  appendMsg("user", msg);

  try {
    const out = await fetchJson("/api/chat", {
      method: "POST",
      body: JSON.stringify({ message: msg }),
    });
    appendMsg("assistant", out.content || "");
  } catch (e) {
    appendMsg("assistant", `Chat error: ${e.message}`);
  }
}

async function genReport() {
  const topic = (el("reportTopic").value || "").trim();
  el("reportBox").textContent = "Generating...";
  try {
    const out = await fetchJson("/api/report", {
      method: "POST",
      body: JSON.stringify({ topic: topic || null }),
    });
    el("reportBox").textContent = out.content || "(empty)";
  } catch (e) {
    el("reportBox").textContent = `error: ${e.message}`;
  }
}

async function reset() {
  try {
    await fetchJson("/api/reset", { method: "POST", body: "{}" });
    await refreshInfo();
    await refreshSources();
    el("reportBox").textContent = "";
    el("sourceDetail").textContent = "";
    el("chatLog").innerHTML = "";
    appendMsg("assistant", "Reset done.");
  } catch (e) {
    appendMsg("assistant", `Reset error: ${e.message}`);
  }
}

function wire() {
  el("refreshSourcesBtn").onclick = refreshSources;
  el("addSourceBtn").onclick = addSource;
  el("sendBtn").onclick = sendChat;
  el("genReportBtn").onclick = genReport;
  el("resetBtn").onclick = reset;

  el("chatInput").addEventListener("keydown", (e) => {
    if (e.key === "Enter") sendChat();
  });
}

async function main() {
  wire();
  await refreshInfo();
  await refreshSources();
}

main();


