// ============================================================
//  Report viewer (standalone page)
//
//  URL:
//    /report.html?reportId=<id>
//
//  Features:
//  - Fetch latest report version via GET /api/reports/{reportId}
//  - Render Markdown (minimal, safe)
//  - Copy / Download
// ============================================================

function el(id) {
  return document.getElementById(id);
}

function qs(name) {
  const u = new URL(window.location.href);
  return u.searchParams.get(name);
}

function escapeHtml(s) {
  return (s || "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function renderInline(text) {
  // Inline code via backticks: split to keep it simple and safe.
  const parts = String(text || "").split("`");
  const out = [];
  for (let i = 0; i < parts.length; i++) {
    const seg = parts[i];
    if (i % 2 === 1) {
      out.push(`<code>${escapeHtml(seg)}</code>`);
    } else {
      let t = escapeHtml(seg);
      // Bold: **x**
      t = t.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");
      // Citations: [source:xxxx]
      t = t.replace(/\[(source:[^\]]+)\]/g, '<span class="citation">[$1]</span>');
      out.push(t);
    }
  }
  return out.join("");
}

function renderMarkdown(md) {
  const lines = String(md || "").replaceAll("\r\n", "\n").split("\n");
  const html = [];

  let inCode = false;
  let codeBuf = [];
  let listMode = null; // "ul" | "ol"

  function closeList() {
    if (listMode) {
      html.push(`</${listMode}>`);
      listMode = null;
    }
  }

  function flushCode() {
    if (!inCode) return;
    const code = codeBuf.join("\n");
    html.push(`<pre><code>${escapeHtml(code)}</code></pre>`);
    codeBuf = [];
    inCode = false;
  }

  for (const raw of lines) {
    const line = raw || "";

    if (line.trim().startsWith("```")) {
      closeList();
      if (inCode) flushCode();
      else inCode = true;
      continue;
    }

    if (inCode) {
      codeBuf.push(line);
      continue;
    }

    // Headings
    const mH = line.match(/^(#{1,3})\s+(.*)$/);
    if (mH) {
      closeList();
      const level = mH[1].length;
      html.push(`<h${level}>${renderInline(mH[2])}</h${level}>`);
      continue;
    }

    // Lists
    const mUl = line.match(/^\s*[-*+]\s+(.*)$/);
    if (mUl) {
      if (listMode !== "ul") {
        closeList();
        listMode = "ul";
        html.push("<ul>");
      }
      html.push(`<li>${renderInline(mUl[1])}</li>`);
      continue;
    }

    const mOl = line.match(/^\s*\d+\.\s+(.*)$/);
    if (mOl) {
      if (listMode !== "ol") {
        closeList();
        listMode = "ol";
        html.push("<ol>");
      }
      html.push(`<li>${renderInline(mOl[1])}</li>`);
      continue;
    }

    if (line.trim().length === 0) {
      closeList();
      continue;
    }

    closeList();
    html.push(`<p>${renderInline(line)}</p>`);
  }

  closeList();
  flushCode();

  return html.join("\n");
}

async function fetchJson(url) {
  const res = await fetch(url, { headers: { Accept: "application/json" } });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return await res.json();
}

function pick(obj, key) {
  if (!obj) return undefined;
  if (obj[key] !== undefined) return obj[key];
  const alt = key[0].toUpperCase() + key.slice(1);
  if (obj[alt] !== undefined) return obj[alt];
  return undefined;
}

async function load() {
  const reportId = (qs("reportId") || "").trim();
  if (!reportId) {
    el("reportError").textContent = "Missing reportId. Use /report.html?reportId=<id>";
    return;
  }

  el("reportTitle").textContent = `Report: ${reportId}`;
  el("reportSub").textContent = "loading…";
  el("reportError").textContent = "";

  const detail = await fetchJson(`/api/reports/${encodeURIComponent(reportId)}?limit=2000`);
  const entries = pick(detail, "entries") || [];
  const latest = entries[0] || null;

  const content = (latest && (pick(latest, "Content") || pick(latest, "content"))) || "";
  const tags = (latest && (pick(latest, "Tags") || pick(latest, "tags"))) || {};
  const topic = tags.topic || tags.Topic || "";
  const version = tags.version || tags.Version || "";

  el("reportSub").textContent = `${topic || "(no topic)"} · v${version || "?"}`;
  el("reportMarkdown").textContent = content || "";
  el("reportRender").innerHTML = renderMarkdown(content || "");

  // Actions
  el("copyBtn").onclick = async () => {
    try {
      await navigator.clipboard.writeText(content || "");
      el("reportSub").textContent = `${topic || "(no topic)"} · v${version || "?"} · copied`;
      setTimeout(() => {
        el("reportSub").textContent = `${topic || "(no topic)"} · v${version || "?"}`;
      }, 900);
    } catch (e) {
      el("reportError").textContent = `Copy failed: ${e.message}`;
    }
  };

  el("downloadBtn").onclick = () => {
    const blob = new Blob([content || ""], { type: "text/markdown;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${reportId}.md`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  };

  el("refreshBtn").onclick = () => load().catch((e) => (el("reportError").textContent = e.message));
}

load().catch((e) => {
  el("reportError").textContent = e.message || String(e);
  el("reportSub").textContent = "failed";
});


