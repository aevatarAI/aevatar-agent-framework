"use strict";
var __create = Object.create;
var __defProp = Object.defineProperty;
var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __getProtoOf = Object.getPrototypeOf;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __export = (target, all) => {
  for (var name in all)
    __defProp(target, name, { get: all[name], enumerable: true });
};
var __copyProps = (to, from, except, desc) => {
  if (from && typeof from === "object" || typeof from === "function") {
    for (let key of __getOwnPropNames(from))
      if (!__hasOwnProp.call(to, key) && key !== except)
        __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
  }
  return to;
};
var __toESM = (mod, isNodeMode, target) => (target = mod != null ? __create(__getProtoOf(mod)) : {}, __copyProps(
  // If the importer is in node compatibility mode or this is not an ESM
  // file that has been converted to a CommonJS file using a Babel-
  // compatible transform (i.e. "__esModule" has not been set), then set
  // "default" to the CommonJS "module.exports" for node compatibility.
  isNodeMode || !mod || !mod.__esModule ? __defProp(target, "default", { value: mod, enumerable: true }) : target,
  mod
));
var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

// src/main.ts
var main_exports = {};
__export(main_exports, {
  default: () => AevatarSraPlugin
});
module.exports = __toCommonJS(main_exports);
var import_obsidian4 = require("obsidian");

// src/settings.ts
var import_obsidian = require("obsidian");
var DEFAULT_SRA_SETTINGS = {
  baseUrl: "http://localhost:5678",
  vaultRoot: "SRA",
  secretsUiUrl: "http://localhost:6677",
  requestTimeoutMs: 15e3
};
var SraSettingsTab = class extends import_obsidian.PluginSettingTab {
  constructor(app, plugin) {
    super(app, plugin);
    this.plugin = plugin;
  }
  display() {
    const { containerEl } = this;
    containerEl.empty();
    containerEl.createEl("h2", { text: "Aevatar SRA" });
    new import_obsidian.Setting(containerEl).setName("Backend baseUrl").setDesc("Default: http://localhost:5678 (repo policy: never use :5000 in examples)").addText((t) => {
      t.setPlaceholder(DEFAULT_SRA_SETTINGS.baseUrl).setValue(this.plugin.settings.baseUrl).onChange(async (value) => {
        this.plugin.settings.baseUrl = (value != null ? value : "").trim();
        await this.plugin.saveSettings();
      });
    });
    new import_obsidian.Setting(containerEl).setName("Vault root folder").setDesc("All plugin artifacts will be written under this folder in the current vault. Default: SRA").addText((t) => {
      t.setPlaceholder(DEFAULT_SRA_SETTINGS.vaultRoot).setValue(this.plugin.settings.vaultRoot).onChange(async (value) => {
        this.plugin.settings.vaultRoot = (value != null ? value : "").trim() || DEFAULT_SRA_SETTINGS.vaultRoot;
        await this.plugin.saveSettings();
      });
    });
    new import_obsidian.Setting(containerEl).setName("Secrets UI URL (optional)").setDesc("Best-effort helper link. Keys should be configured outside the plugin.").addText((t) => {
      t.setPlaceholder(DEFAULT_SRA_SETTINGS.secretsUiUrl).setValue(this.plugin.settings.secretsUiUrl).onChange(async (value) => {
        this.plugin.settings.secretsUiUrl = (value != null ? value : "").trim();
        await this.plugin.saveSettings();
      });
    });
    new import_obsidian.Setting(containerEl).setName("Open Secrets UI").setDesc("Opens the configured Secrets UI URL in a browser (best-effort).").addButton((b) => {
      b.setButtonText("Open").onClick(() => {
        var _a;
        const url = ((_a = this.plugin.settings.secretsUiUrl) != null ? _a : "").trim();
        if (!url) {
          new import_obsidian.Notice("Secrets UI URL is empty.");
          return;
        }
        try {
          window.open(url, "_blank");
        } catch {
          new import_obsidian.Notice("Failed to open Secrets UI URL.");
        }
      });
    });
    new import_obsidian.Setting(containerEl).setName("Request timeout (ms)").setDesc("Default: 15000").addText((t) => {
      var _a;
      t.setPlaceholder(String(DEFAULT_SRA_SETTINGS.requestTimeoutMs)).setValue(String((_a = this.plugin.settings.requestTimeoutMs) != null ? _a : DEFAULT_SRA_SETTINGS.requestTimeoutMs)).onChange(async (value) => {
        const n = Number.parseInt((value != null ? value : "").trim(), 10);
        this.plugin.settings.requestTimeoutMs = Number.isFinite(n) && n > 0 ? n : DEFAULT_SRA_SETTINGS.requestTimeoutMs;
        await this.plugin.saveSettings();
      });
    });
    new import_obsidian.Setting(containerEl).setName("Test Connection").setDesc("Calls /health (then /api/info) to verify the backend is reachable.").addButton((b) => {
      b.setButtonText("Test Connection").onClick(async () => {
        const baseUrl = normalizeBaseUrl(this.plugin.settings.baseUrl);
        const timeoutMs = clampTimeout(this.plugin.settings.requestTimeoutMs);
        if (!baseUrl) {
          new import_obsidian.Notice("Invalid baseUrl.");
          return;
        }
        const res = await testConnectionAsync(baseUrl, timeoutMs);
        if (res.ok) {
          new import_obsidian.Notice(`SRA backend OK (${res.endpoint}, HTTP ${res.status})`);
        } else {
          new import_obsidian.Notice(`SRA backend NOT reachable (${res.endpoint}): ${res.error}`);
        }
      });
    });
  }
};
function normalizeBaseUrl(input) {
  const s = (input != null ? input : "").trim();
  if (!s)
    return "";
  return s.replace(/\/+$/, "");
}
function clampTimeout(ms) {
  const n = Number.isFinite(ms) ? ms : DEFAULT_SRA_SETTINGS.requestTimeoutMs;
  return Math.max(1e3, Math.min(12e4, n));
}
async function testConnectionAsync(baseUrl, timeoutMs) {
  var _a;
  const healthUrl = `${baseUrl}/health`;
  try {
    const r = await (0, import_obsidian.requestUrl)({
      url: healthUrl,
      method: "GET",
      timeout: timeoutMs,
      throw: false
    });
    if (r.status >= 200 && r.status < 300) {
      return { ok: true, endpoint: "/health", status: r.status, error: "" };
    }
  } catch (e) {
  }
  const infoUrl = `${baseUrl}/api/info`;
  try {
    const r = await (0, import_obsidian.requestUrl)({
      url: infoUrl,
      method: "GET",
      timeout: timeoutMs,
      throw: false
    });
    if (r.status >= 200 && r.status < 300) {
      return { ok: true, endpoint: "/api/info", status: r.status, error: "" };
    }
    return { ok: false, endpoint: "/api/info", status: r.status, error: `HTTP ${r.status}` };
  } catch (e) {
    const msg = ((_a = e == null ? void 0 : e.message) != null ? _a : String(e != null ? e : "unknown error")).trim();
    return { ok: false, endpoint: "/api/info", status: 0, error: msg || "request failed" };
  }
}

// src/ui/SraView.ts
var import_obsidian3 = require("obsidian");

// src/api/SraApiClient.ts
var import_obsidian2 = require("obsidian");

// src/api/types.ts
var SraApiError = class extends Error {
  constructor(message, args) {
    super(message);
    this.name = "SraApiError";
    this.status = args.status;
    this.url = args.url;
    this.responseText = args.responseText;
  }
};

// src/api/multipart.ts
function buildMultipartBody(files, boundary) {
  var _a, _b, _c;
  const list = Array.isArray(files) ? files : [];
  if (list.length === 0)
    throw new Error("no files");
  const b = boundary && boundary.trim() ? boundary.trim() : randomBoundary();
  const enc = new TextEncoder();
  const chunks = [];
  for (const f of list) {
    const fieldName = ((_a = f == null ? void 0 : f.fieldName) != null ? _a : "file").trim() || "file";
    const filename = sanitizeFilename(((_b = f == null ? void 0 : f.filename) != null ? _b : "file").trim() || "file");
    const contentType = ((_c = f == null ? void 0 : f.contentType) != null ? _c : "application/octet-stream").trim() || "application/octet-stream";
    const data = (f == null ? void 0 : f.data) instanceof Uint8Array ? f.data : new Uint8Array();
    chunks.push(enc.encode(`--${b}\r
`));
    chunks.push(
      enc.encode(
        `Content-Disposition: form-data; name="${escapeQuotes(fieldName)}"; filename="${escapeQuotes(filename)}"\r
`
      )
    );
    chunks.push(enc.encode(`Content-Type: ${contentType}\r
\r
`));
    chunks.push(data);
    chunks.push(enc.encode("\r\n"));
  }
  chunks.push(enc.encode(`--${b}--\r
`));
  const merged = concatBytes(chunks);
  return {
    contentType: `multipart/form-data; boundary=${b}`,
    body: toArrayBuffer(merged)
  };
}
function randomBoundary() {
  const rnd = Math.floor(Math.random() * 1e9).toString(16);
  return `----aevatar-sra-${Date.now().toString(16)}-${rnd}`;
}
function concatBytes(chunks) {
  let total = 0;
  for (const c of chunks)
    total += c.byteLength;
  const out = new Uint8Array(total);
  let offset = 0;
  for (const c of chunks) {
    out.set(c, offset);
    offset += c.byteLength;
  }
  return out;
}
function toArrayBuffer(u8) {
  return u8.buffer.slice(u8.byteOffset, u8.byteOffset + u8.byteLength);
}
function escapeQuotes(s) {
  return (s != null ? s : "").replace(/"/g, '\\"');
}
function sanitizeFilename(name) {
  const s = (name != null ? name : "").trim();
  if (!s)
    return "file";
  return s.replace(/[\\/]/g, "_");
}

// src/api/SraApiClient.ts
var SraApiClient = class {
  constructor(options) {
    this.baseUrl = normalizeBaseUrl2(options.baseUrl);
    this.timeoutMs = clampTimeout2(options.timeoutMs);
    this.headers = options.headers;
  }
  setBaseUrl(baseUrl) {
    this.baseUrl = normalizeBaseUrl2(baseUrl);
  }
  setTimeoutMs(timeoutMs) {
    this.timeoutMs = clampTimeout2(timeoutMs);
  }
  async health() {
    const url = this.url("/health");
    const res = await this.requestText("GET", url);
    return { ok: res.status >= 200 && res.status < 300, status: res.status, body: res.text };
  }
  async info() {
    const url = this.url("/api/info");
    return await this.requestJson("GET", url);
  }
  async createSession(providerName) {
    const url = this.url("/api/sessions");
    const body = providerName ? { providerName } : {};
    const json = await this.requestJson("POST", url, body);
    if (!(json == null ? void 0 : json.sessionId))
      throw new SraApiError("Invalid response: missing sessionId", { status: 0, url, responseText: "" });
    return { sessionId: String(json.sessionId) };
  }
  async listSessions() {
    var _a;
    const url = this.url("/api/sessions");
    const json = await this.requestJson("GET", url);
    return {
      count: Number((_a = json == null ? void 0 : json.count) != null ? _a : 0) || 0,
      sessions: Array.isArray(json == null ? void 0 : json.sessions) ? json.sessions : []
    };
  }
  async sendInput(sessionId, input) {
    const sid = String(sessionId != null ? sessionId : "").trim();
    if (!sid)
      throw new Error("sessionId is required");
    if (!(input == null ? void 0 : input.message) || !String(input.message).trim())
      throw new Error("message is required");
    const url = this.url(`/api/sessions/${encodeURIComponent(sid)}/input`);
    const json = await this.requestJson("POST", url, input);
    if (!(json == null ? void 0 : json.runId))
      throw new SraApiError("Invalid response: missing runId", { status: 0, url, responseText: "" });
    return { runId: String(json.runId) };
  }
  async getDeliverables(sessionId) {
    const sid = String(sessionId != null ? sessionId : "").trim();
    if (!sid)
      throw new Error("sessionId is required");
    const url = this.url(`/api/sessions/${encodeURIComponent(sid)}/deliverables`);
    return await this.requestJson("GET", url);
  }
  async uploadAttachments(sessionId, files) {
    var _a, _b;
    const sid = String(sessionId != null ? sessionId : "").trim();
    if (!sid)
      throw new Error("sessionId is required");
    const url = this.url(`/api/sessions/${encodeURIComponent(sid)}/uploads`);
    const parts = (files != null ? files : []).map((f) => ({
      fieldName: "file",
      filename: f.filename,
      contentType: f.contentType,
      data: f.data
    }));
    const mp = buildMultipartBody(parts);
    const res = await (0, import_obsidian2.requestUrl)({
      url,
      method: "POST",
      headers: this.resolveHeaders(mp.contentType),
      body: mp.body,
      timeout: this.timeoutMs,
      throw: false
    });
    const status = Number((_a = res == null ? void 0 : res.status) != null ? _a : 0) || 0;
    const text = typeof (res == null ? void 0 : res.text) === "string" ? res.text : "";
    if (status < 200 || status >= 300) {
      throw new SraApiError(`HTTP ${status}`, { status, url, responseText: trimForUi(text) });
    }
    let json = null;
    try {
      json = (_b = res == null ? void 0 : res.json) != null ? _b : JSON.parse(text);
    } catch {
    }
    const paths = Array.isArray(json == null ? void 0 : json.attachmentPaths) ? json.attachmentPaths : [];
    return { attachmentPaths: paths.map((x) => String(x != null ? x : "").trim()).filter((x) => x.length > 0) };
  }
  // ============================================================
  //  Internals
  // ============================================================
  url(path) {
    const p = String(path != null ? path : "").trim();
    if (!p.startsWith("/"))
      return `${this.baseUrl}/${p}`;
    return `${this.baseUrl}${p}`;
  }
  resolveHeaders(contentType) {
    const h = typeof this.headers === "function" ? this.headers() : this.headers;
    const out = { ...h != null ? h : {} };
    if (contentType)
      out["Content-Type"] = contentType;
    return out;
  }
  async requestText(method, url, body) {
    var _a;
    const m = String(method != null ? method : "GET").toUpperCase();
    const timeout = this.timeoutMs;
    const res = await (0, import_obsidian2.requestUrl)({
      url,
      method: m,
      headers: this.resolveHeaders(typeof body === "string" ? "application/json" : void 0),
      body: body ? typeof body === "string" ? body : JSON.stringify(body) : void 0,
      timeout,
      throw: false
    });
    const text = typeof (res == null ? void 0 : res.text) === "string" ? res.text : "";
    const status = Number((_a = res == null ? void 0 : res.status) != null ? _a : 0) || 0;
    if (status < 200 || status >= 300) {
      throw new SraApiError(`HTTP ${status}`, { status, url, responseText: trimForUi(text) });
    }
    return { status, text };
  }
  async requestJson(method, url, body) {
    var _a;
    const m = String(method != null ? method : "GET").toUpperCase();
    const timeout = this.timeoutMs;
    const res = await (0, import_obsidian2.requestUrl)({
      url,
      method: m,
      headers: this.resolveHeaders(body !== void 0 ? "application/json" : void 0),
      body: body !== void 0 ? JSON.stringify(body) : void 0,
      timeout,
      throw: false
    });
    const status = Number((_a = res == null ? void 0 : res.status) != null ? _a : 0) || 0;
    const text = typeof (res == null ? void 0 : res.text) === "string" ? res.text : "";
    if (status < 200 || status >= 300) {
      throw new SraApiError(`HTTP ${status}`, { status, url, responseText: trimForUi(text) });
    }
    try {
      const json = res == null ? void 0 : res.json;
      if (json && typeof json === "object")
        return json;
    } catch {
    }
    try {
      return JSON.parse(text);
    } catch {
      throw new SraApiError("Invalid JSON response", { status, url, responseText: trimForUi(text) });
    }
  }
};
function normalizeBaseUrl2(input) {
  const s = (input != null ? input : "").trim();
  return s.replace(/\/+$/, "");
}
function clampTimeout2(ms) {
  const n = Number.isFinite(ms) ? ms : 15e3;
  return Math.max(1e3, Math.min(12e4, n));
}
function trimForUi(text, max = 800) {
  const s = (text != null ? text : "").trim();
  return s.length <= max ? s : s.slice(0, max) + "\u2026";
}

// src/sse/sseParser.ts
var SseParser = class {
  constructor() {
    this.buffer = "";
  }
  push(chunk) {
    if (!chunk)
      return [];
    this.buffer += chunk;
    this.buffer = this.buffer.replace(/\r\n/g, "\n");
    const out = [];
    while (true) {
      const idx = this.buffer.indexOf("\n\n");
      if (idx < 0)
        break;
      const frame = this.buffer.slice(0, idx);
      this.buffer = this.buffer.slice(idx + 2);
      const data = extractData(frame);
      if (data.length > 0)
        out.push(data);
    }
    return out;
  }
  flush() {
    if (!this.buffer)
      return [];
    const rest = this.buffer;
    this.buffer = "";
    const data = extractData(rest.replace(/\r\n/g, "\n"));
    return data.length > 0 ? [data] : [];
  }
};
function extractData(frame) {
  const lines = (frame != null ? frame : "").split("\n");
  const parts = [];
  for (const line of lines) {
    if (!line)
      continue;
    if (line.startsWith("data:")) {
      const raw = line.slice("data:".length);
      parts.push(raw.startsWith(" ") ? raw.slice(1) : raw);
    }
  }
  return parts.join("\n").trim();
}

// src/sse/SraSseClient.ts
var import_node_http = __toESM(require("http"));
var import_node_https = __toESM(require("https"));
var SraSseClient = class {
  constructor(options) {
    this.stopped = true;
    this.retryTimer = null;
    this.attempt = 0;
    this.req = null;
    this.baseUrl = normalizeBaseUrl3(options.baseUrl);
    this.headers = options.headers;
  }
  setBaseUrl(baseUrl) {
    this.baseUrl = normalizeBaseUrl3(baseUrl);
  }
  connect(sessionId, handlers) {
    const sid = String(sessionId != null ? sessionId : "").trim();
    if (!sid)
      throw new Error("sessionId is required");
    this.disconnect();
    this.stopped = false;
    this.attempt = 0;
    const url = `${this.baseUrl}/api/sessions/${encodeURIComponent(sid)}/agui/events`;
    this.open(url, handlers);
  }
  disconnect() {
    var _a;
    this.stopped = true;
    this.attempt = 0;
    if (this.retryTimer) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
    try {
      (_a = this.req) == null ? void 0 : _a.destroy();
    } catch {
    }
    this.req = null;
  }
  // ============================================================
  //  Internals
  // ============================================================
  open(url, handlers) {
    if (this.stopped)
      return;
    handlers.onStatus("Connecting");
    const u = new URL(url);
    const isHttps = u.protocol === "https:";
    const mod = isHttps ? import_node_https.default : import_node_http.default;
    const headers = this.resolveHeaders();
    headers["Accept"] = "text/event-stream";
    headers["Cache-Control"] = "no-cache";
    const req = mod.request(
      {
        method: "GET",
        protocol: u.protocol,
        hostname: u.hostname,
        port: u.port ? Number(u.port) : void 0,
        path: u.pathname + u.search,
        headers
      },
      (res) => {
        var _a;
        const status = Number((_a = res.statusCode) != null ? _a : 0) || 0;
        if (status < 200 || status >= 300) {
          handlers.onStatus("Error", `HTTP ${status}`);
          res.resume();
          this.scheduleReconnect(url, handlers, `HTTP ${status}`);
          return;
        }
        handlers.onStatus("Connected");
        this.attempt = 0;
        const parser = new SseParser();
        res.setEncoding("utf8");
        res.on("data", (chunk) => {
          if (this.stopped)
            return;
          const frames = parser.push(chunk);
          for (const data of frames) {
            const evt = safeJsonParse(data);
            if (!evt)
              continue;
            handlers.onEvent(evt);
          }
        });
        res.on("end", () => {
          if (this.stopped)
            return;
          const rest = parser.flush();
          for (const data of rest) {
            const evt = safeJsonParse(data);
            if (!evt)
              continue;
            handlers.onEvent(evt);
          }
          handlers.onStatus("Disconnected", "stream ended");
          this.scheduleReconnect(url, handlers, "stream ended");
        });
        res.on("error", (err) => {
          var _a2, _b;
          if (this.stopped)
            return;
          handlers.onStatus("Error", (_a2 = err == null ? void 0 : err.message) != null ? _a2 : "stream error");
          this.scheduleReconnect(url, handlers, (_b = err == null ? void 0 : err.message) != null ? _b : "stream error");
        });
      }
    );
    req.on("error", (err) => {
      var _a, _b;
      if (this.stopped)
        return;
      handlers.onStatus("Error", (_a = err == null ? void 0 : err.message) != null ? _a : "request error");
      this.scheduleReconnect(url, handlers, (_b = err == null ? void 0 : err.message) != null ? _b : "request error");
    });
    req.end();
    this.req = req;
  }
  scheduleReconnect(url, handlers, reason) {
    if (this.stopped)
      return;
    if (this.retryTimer)
      return;
    this.attempt++;
    const delayMs = backoffMs(this.attempt);
    handlers.onStatus("Disconnected", `reconnecting in ${delayMs}ms (${reason})`);
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      this.open(url, handlers);
    }, delayMs);
  }
  resolveHeaders() {
    const h = typeof this.headers === "function" ? this.headers() : this.headers;
    return { ...h != null ? h : {} };
  }
};
function normalizeBaseUrl3(input) {
  return (input != null ? input : "").trim().replace(/\/+$/, "");
}
function backoffMs(attempt) {
  const base = Math.min(3e4, 500 * Math.pow(2, Math.max(0, attempt - 1)));
  const jitter = Math.floor(Math.random() * 200);
  return base + jitter;
}
function safeJsonParse(input) {
  if (typeof input !== "string")
    return null;
  const s = input.trim();
  if (!s)
    return null;
  try {
    const obj = JSON.parse(s);
    return obj && typeof obj === "object" ? obj : null;
  } catch {
    return null;
  }
}

// src/vault/paths.ts
function normalizeVaultRoot(input) {
  const raw = (input != null ? input : "").replace(/\\/g, "/").trim();
  if (!raw)
    return "SRA";
  if (raw.startsWith("/"))
    throw new Error("vaultRoot must be a vault-relative path");
  const parts = raw.split("/").filter(Boolean);
  if (parts.length === 0)
    return "SRA";
  for (const p of parts) {
    if (p === "." || p === "..")
      throw new Error("vaultRoot contains invalid path segments");
  }
  return parts.join("/");
}
function safeIdForPath(id) {
  const s = (id != null ? id : "").trim();
  if (!s)
    return "unknown";
  return s.replace(/[^a-zA-Z0-9._-]+/g, "-").replace(/-+/g, "-").replace(/^-+/, "").replace(/-+$/, "") || "unknown";
}
function joinVaultPath(...parts) {
  const segs = [];
  for (const part of parts) {
    const s = (part != null ? part : "").replace(/\\/g, "/").trim();
    if (!s)
      continue;
    if (s.startsWith("/"))
      throw new Error("absolute path is not allowed");
    for (const seg of s.split("/")) {
      const t = seg.trim();
      if (!t)
        continue;
      if (t === "." || t === "..")
        throw new Error("path traversal is not allowed");
      segs.push(t);
    }
  }
  return segs.join("/");
}
function sessionDir(vaultRoot, sessionId) {
  return joinVaultPath(normalizeVaultRoot(vaultRoot), "sessions", safeIdForPath(sessionId));
}
function runDir(vaultRoot, sessionId, runId) {
  return joinVaultPath(sessionDir(vaultRoot, sessionId), "runs", safeIdForPath(runId));
}
function runEventsPath(vaultRoot, sessionId, runId) {
  return joinVaultPath(runDir(vaultRoot, sessionId, runId), "events.jsonl");
}
function runMetaPath(vaultRoot, sessionId, runId) {
  return joinVaultPath(runDir(vaultRoot, sessionId, runId), "run.json");
}
function deliverablesDir(vaultRoot, sessionId) {
  return joinVaultPath(sessionDir(vaultRoot, sessionId), "deliverables");
}
function deliverablesJsonPath(vaultRoot, sessionId) {
  return joinVaultPath(deliverablesDir(vaultRoot, sessionId), "deliverables.json");
}
function deliverablesBriefPath(vaultRoot, sessionId) {
  return joinVaultPath(deliverablesDir(vaultRoot, sessionId), "brief.md");
}
function deliverablesDeliveryPath(vaultRoot, sessionId) {
  return joinVaultPath(deliverablesDir(vaultRoot, sessionId), "delivery.md");
}

// src/vault/VaultStore.ts
var VaultStore = class {
  constructor(app, vaultRoot) {
    this.flushTimer = null;
    this.pendingAppends = /* @__PURE__ */ new Map();
    this.app = app;
    this.vaultRoot = normalizeVaultRoot(vaultRoot);
  }
  setVaultRoot(vaultRoot) {
    this.vaultRoot = normalizeVaultRoot(vaultRoot);
  }
  async ensureRoot() {
    await this.ensureFolder(this.vaultRoot);
    await this.ensureFolder(`${this.vaultRoot}/sessions`);
  }
  async appendRunEvent(sessionId, runId, evt) {
    try {
      await this.ensureRoot();
      await this.ensureFolder(sessionDir(this.vaultRoot, sessionId));
      await this.ensureFolder(runDir(this.vaultRoot, sessionId, runId));
      const path = runEventsPath(this.vaultRoot, sessionId, runId);
      const line = JSON.stringify(evt) + "\n";
      this.enqueueAppend(path, line);
    } catch {
    }
  }
  async writeRunMeta(sessionId, runId, meta) {
    try {
      await this.ensureRoot();
      await this.ensureFolder(sessionDir(this.vaultRoot, sessionId));
      await this.ensureFolder(runDir(this.vaultRoot, sessionId, runId));
      const path = runMetaPath(this.vaultRoot, sessionId, runId);
      await this.safeWriteText(path, JSON.stringify(meta, null, 2) + "\n");
    } catch {
    }
  }
  async writeDeliverablesJson(sessionId, snapshot) {
    try {
      await this.ensureRoot();
      await this.ensureFolder(sessionDir(this.vaultRoot, sessionId));
      await this.ensureFolder(deliverablesDir(this.vaultRoot, sessionId));
      const path = deliverablesJsonPath(this.vaultRoot, sessionId);
      await this.safeWriteText(path, JSON.stringify(snapshot, null, 2) + "\n");
    } catch {
    }
  }
  async writeDeliverablesMarkdown(sessionId, briefMd, deliveryMd) {
    try {
      await this.ensureRoot();
      await this.ensureFolder(sessionDir(this.vaultRoot, sessionId));
      await this.ensureFolder(deliverablesDir(this.vaultRoot, sessionId));
      await this.safeWriteText(deliverablesBriefPath(this.vaultRoot, sessionId), briefMd);
      await this.safeWriteText(deliverablesDeliveryPath(this.vaultRoot, sessionId), deliveryMd);
    } catch {
    }
  }
  // ============================================================
  //  Append buffering (avoid too many small writes)
  // ============================================================
  enqueueAppend(path, text) {
    var _a;
    const list = (_a = this.pendingAppends.get(path)) != null ? _a : [];
    list.push(text);
    this.pendingAppends.set(path, list);
    if (!this.flushTimer) {
      this.flushTimer = setTimeout(() => {
        this.flushTimer = null;
        void this.flushAppendsAsync();
      }, 200);
    }
  }
  async flushAppendsAsync() {
    const entries = Array.from(this.pendingAppends.entries());
    this.pendingAppends.clear();
    for (const [path, chunks] of entries) {
      const content = chunks.join("");
      try {
        await this.safeAppendText(path, content);
      } catch {
      }
    }
  }
  // ============================================================
  //  Adapter helpers
  // ============================================================
  async ensureFolder(path) {
    var _a, _b;
    const adapter = (_b = (_a = this.app) == null ? void 0 : _a.vault) == null ? void 0 : _b.adapter;
    if (!adapter)
      return;
    const exists = await adapter.exists(path).catch(() => false);
    if (exists)
      return;
    await adapter.mkdir(path).catch(() => void 0);
  }
  async safeWriteText(path, content) {
    var _a, _b;
    const adapter = (_b = (_a = this.app) == null ? void 0 : _a.vault) == null ? void 0 : _b.adapter;
    if (!adapter)
      return;
    await adapter.write(path, content).catch(() => void 0);
  }
  async safeAppendText(path, content) {
    var _a, _b;
    const adapter = (_b = (_a = this.app) == null ? void 0 : _a.vault) == null ? void 0 : _b.adapter;
    if (!adapter)
      return;
    if (typeof adapter.append === "function") {
      await adapter.append(path, content).catch(() => void 0);
      return;
    }
    const prev = await adapter.read(path).catch(() => "");
    await adapter.write(path, String(prev != null ? prev : "") + content).catch(() => void 0);
  }
};

// src/vault/render.ts
function renderBriefMarkdown(brief) {
  var _a, _b, _c;
  const b = brief != null ? brief : {};
  const lines = [];
  lines.push("# Research Brief");
  lines.push("");
  pushField(lines, "UpdatedAt", b.updatedAt);
  pushField(lines, "Version", b.version);
  pushField(lines, "RewrittenQuestion", b.rewrittenQuestion);
  pushField(lines, "Scope", b.scope);
  pushField(lines, "SuccessCriteria", b.successCriteria);
  pushList(lines, "Assumptions", b.assumptions);
  pushList(lines, "Risks", b.risks);
  pushList(lines, "Uncertainties", b.uncertainties);
  const terms = Array.isArray(b.terms) ? b.terms : [];
  if (terms.length > 0) {
    lines.push("");
    lines.push("## Terms");
    for (const t of terms.slice(0, 50)) {
      const term = String((_a = t == null ? void 0 : t.term) != null ? _a : "").trim();
      const meaning = String((_b = t == null ? void 0 : t.meaning) != null ? _b : "").trim();
      if (!term && !meaning)
        continue;
      lines.push(`- **${escapeMd(term || "term")}**: ${escapeMd(meaning || "")}`);
    }
  }
  const milestones = Array.isArray(b.milestones) ? b.milestones : [];
  if (milestones.length > 0) {
    lines.push("");
    lines.push("## Milestones");
    for (const m of milestones.slice(0, 50)) {
      const idx = m == null ? void 0 : m.roundIndex;
      const out = String((_c = m == null ? void 0 : m.expectedOutput) != null ? _c : "").trim();
      lines.push(`- round ${idx != null ? idx : ""}: ${escapeMd(out)}`);
    }
  }
  lines.push("");
  return lines.join("\n");
}
function renderDeliveryMarkdown(delivery) {
  const lines = [];
  lines.push("# Delivery Snapshot");
  lines.push("");
  const json = safePrettyJson(delivery);
  lines.push("```json");
  lines.push(trimForNote(json, 2e4));
  lines.push("```");
  lines.push("");
  return lines.join("\n");
}
function pushField(lines, name, value) {
  const v = String(value != null ? value : "").trim();
  if (!v)
    return;
  lines.push(`- **${name}**: ${escapeMd(v)}`);
}
function pushList(lines, title, value) {
  const arr = Array.isArray(value) ? value : [];
  if (arr.length === 0)
    return;
  lines.push("");
  lines.push(`## ${title}`);
  for (const x of arr.slice(0, 100)) {
    const s = String(x != null ? x : "").trim();
    if (!s)
      continue;
    lines.push(`- ${escapeMd(s)}`);
  }
}
function safePrettyJson(obj) {
  var _a;
  try {
    return (_a = JSON.stringify(obj != null ? obj : null, null, 2)) != null ? _a : "null";
  } catch {
    return "null";
  }
}
function trimForNote(text, maxChars) {
  const s = String(text != null ? text : "");
  if (s.length <= maxChars)
    return s;
  return s.slice(0, maxChars) + "\n... (truncated)\n";
}
function escapeMd(s) {
  return (s != null ? s : "").replace(/\r/g, "").replace(/\n/g, " ").trim();
}

// src/ui/SraView.ts
var VIEW_TYPE_SRA = "aevatar-sra-view";
var SraView = class extends import_obsidian3.ItemView {
  constructor(leaf, plugin) {
    super(leaf);
    this.sessionId = "";
    this.runId = "";
    this.status = "Disconnected";
    this.lastError = "";
    this.attachments = [];
    this.plugin = plugin;
    this.api = new SraApiClient({ baseUrl: plugin.settings.baseUrl, timeoutMs: plugin.settings.requestTimeoutMs });
    this.sse = new SraSseClient({ baseUrl: plugin.settings.baseUrl });
    this.vault = new VaultStore(plugin.app, plugin.settings.vaultRoot);
  }
  getViewType() {
    return VIEW_TYPE_SRA;
  }
  getDisplayText() {
    return "SRA";
  }
  async onOpen() {
    this.render();
    await this.vault.ensureRoot();
  }
  async onClose() {
    this.sse.disconnect();
  }
  // ------------------------------------------------------------
  //  Public actions (used by commands)
  // ------------------------------------------------------------
  async createNewSession() {
    var _a;
    this.refreshRuntime();
    try {
      const res = await this.api.createSession();
      this.connectToSession(res.sessionId);
      new import_obsidian3.Notice(`SRA session created: ${res.sessionId}`);
    } catch (e) {
      this.setError((_a = e == null ? void 0 : e.message) != null ? _a : "create session failed");
      new import_obsidian3.Notice(`Create session failed: ${this.lastError}`);
    }
  }
  connectToSession(sessionId) {
    const sid = String(sessionId != null ? sessionId : "").trim();
    if (!sid) {
      this.setError("sessionId is required");
      return;
    }
    this.sessionId = sid;
    this.runId = "";
    this.elSession.value = sid;
    this.elRun.setText("run: (none)");
    this.setError("");
    this.refreshRuntime();
    this.sse.connect(sid, {
      onStatus: (status, detail) => {
        this.status = status;
        const tail = detail ? ` \u2014 ${detail}` : "";
        this.elStatus.setText(`SSE: ${status}${tail}`);
      },
      onEvent: (evt) => {
        const rid = extractRunId(evt);
        if (rid && rid !== this.runId) {
          this.runId = rid;
          this.elRun.setText(`run: ${rid}`);
        }
        void this.vault.appendRunEvent(this.sessionId, this.runId || "stream", evt);
      }
    });
  }
  async sendCurrentMessage() {
    var _a, _b;
    const msg = ((_a = this.elMessage.value) != null ? _a : "").trim();
    if (!msg)
      return;
    if (!this.sessionId) {
      new import_obsidian3.Notice("No active session. Create or connect first.");
      return;
    }
    this.refreshRuntime();
    try {
      const mode = this.elMode.value;
      let attachmentPaths = void 0;
      if (this.attachments.length > 0) {
        const files = await this.readAttachmentsAsync();
        const up = await this.api.uploadAttachments(this.sessionId, files);
        attachmentPaths = up.attachmentPaths;
      }
      const input = { message: msg, mode, attachmentPaths };
      const res = await this.api.sendInput(this.sessionId, input);
      this.runId = res.runId;
      this.elRun.setText(`run: ${res.runId}`);
      this.elMessage.value = "";
      this.attachments = [];
      this.renderAttachments();
      await this.vault.writeRunMeta(this.sessionId, res.runId, {
        sessionId: this.sessionId,
        runId: res.runId,
        createdAt: (/* @__PURE__ */ new Date()).toISOString(),
        mode
      });
    } catch (e) {
      this.setError((_b = e == null ? void 0 : e.message) != null ? _b : "send failed");
      new import_obsidian3.Notice(`Send failed: ${this.lastError}`);
    }
  }
  // ------------------------------------------------------------
  //  Rendering
  // ------------------------------------------------------------
  render() {
    const { containerEl } = this;
    containerEl.empty();
    containerEl.addClass("aevatar-sra-view");
    containerEl.createEl("h2", { text: "Aevatar SRA" });
    this.elStatus = containerEl.createEl("div", { text: "SSE: Disconnected" });
    this.elRun = containerEl.createEl("div", { text: "run: (none)" });
    const row = containerEl.createEl("div");
    row.style.display = "flex";
    row.style.gap = "8px";
    row.style.alignItems = "center";
    this.elSession = row.createEl("input");
    this.elSession.type = "text";
    this.elSession.placeholder = "sessionId";
    this.elSession.style.flex = "1";
    const btnConnect = row.createEl("button", { text: "Connect" });
    btnConnect.onclick = () => this.connectToSession(this.elSession.value);
    const btnNew = row.createEl("button", { text: "New Session" });
    btnNew.onclick = () => void this.createNewSession();
    const controls = containerEl.createEl("div");
    controls.style.display = "flex";
    controls.style.gap = "8px";
    controls.style.marginTop = "8px";
    this.elMode = controls.createEl("select");
    for (const m of ["chat", "vibe", "vibe_loop"]) {
      const opt = this.elMode.createEl("option");
      opt.value = m;
      opt.text = m;
    }
    const btnSend = controls.createEl("button", { text: "Send" });
    btnSend.onclick = () => void this.sendCurrentMessage();
    const btnPull = controls.createEl("button", { text: "Pull Deliverables" });
    btnPull.onclick = () => void this.pullDeliverablesAsync();
    const attachRow = containerEl.createEl("div");
    attachRow.style.display = "flex";
    attachRow.style.gap = "8px";
    attachRow.style.marginTop = "8px";
    attachRow.style.alignItems = "center";
    const btnAttach = attachRow.createEl("button", { text: "Attach current note" });
    btnAttach.onclick = () => void this.attachCurrentNoteAsync();
    const btnClear = attachRow.createEl("button", { text: "Clear attachments" });
    btnClear.onclick = () => {
      this.attachments = [];
      this.renderAttachments();
    };
    this.elAttachments = attachRow.createEl("div", { text: "" });
    this.elAttachments.style.opacity = "0.8";
    this.elMessage = containerEl.createEl("textarea");
    this.elMessage.placeholder = "Ask a research question\u2026";
    this.elMessage.rows = 4;
    this.elMessage.style.width = "100%";
    this.elMessage.style.marginTop = "8px";
    this.elError = containerEl.createEl("div", { text: "" });
    this.elError.style.marginTop = "8px";
    this.elError.style.color = "var(--text-error)";
    this.renderAttachments();
  }
  refreshRuntime() {
    this.api.setBaseUrl(this.plugin.settings.baseUrl);
    this.api.setTimeoutMs(this.plugin.settings.requestTimeoutMs);
    this.sse.setBaseUrl(this.plugin.settings.baseUrl);
    this.vault.setVaultRoot(this.plugin.settings.vaultRoot);
  }
  setError(msg) {
    this.lastError = (msg != null ? msg : "").trim();
    this.elError.setText(this.lastError);
  }
  renderAttachments() {
    const n = this.attachments.length;
    this.elAttachments.setText(n === 0 ? "Attachments: (none)" : `Attachments: ${n}`);
  }
  async attachCurrentNoteAsync() {
    const f = this.app.workspace.getActiveFile();
    if (!f) {
      new import_obsidian3.Notice("No active file.");
      return;
    }
    if (!this.attachments.some((x) => (x == null ? void 0 : x.path) === f.path)) {
      this.attachments.push(f);
      this.renderAttachments();
      new import_obsidian3.Notice(`Attached: ${f.name}`);
    }
  }
  async readAttachmentsAsync() {
    var _a, _b;
    const out = [];
    for (const f of this.attachments) {
      const name = String((_a = f == null ? void 0 : f.name) != null ? _a : "file").trim() || "file";
      const ext = (_b = name.toLowerCase().split(".").pop()) != null ? _b : "";
      const contentType = guessContentType(ext);
      const ab = await this.app.vault.readBinary(f);
      out.push({ filename: name, contentType, data: new Uint8Array(ab) });
    }
    return out;
  }
  async pullDeliverablesAsync() {
    var _a;
    if (!this.sessionId) {
      new import_obsidian3.Notice("No active session.");
      return;
    }
    this.refreshRuntime();
    try {
      const snap = await this.api.getDeliverables(this.sessionId);
      await this.vault.writeDeliverablesJson(this.sessionId, snap);
      const briefMd = renderBriefMarkdown(snap == null ? void 0 : snap.brief);
      const deliveryMd = renderDeliveryMarkdown(snap == null ? void 0 : snap.delivery);
      await this.vault.writeDeliverablesMarkdown(this.sessionId, briefMd, deliveryMd);
      new import_obsidian3.Notice("Deliverables written to Vault/SRA.");
    } catch (e) {
      this.setError((_a = e == null ? void 0 : e.message) != null ? _a : "pull deliverables failed");
      new import_obsidian3.Notice(`Pull deliverables failed: ${this.lastError}`);
    }
  }
};
function extractRunId(evt) {
  var _a;
  const rid = evt == null ? void 0 : evt.runId;
  if (typeof rid === "string" && rid.trim())
    return rid.trim();
  const raw = (_a = evt == null ? void 0 : evt.value) == null ? void 0 : _a.runId;
  if (typeof raw === "string" && raw.trim())
    return raw.trim();
  return "";
}
function guessContentType(ext) {
  const e = (ext != null ? ext : "").trim().toLowerCase();
  if (e === "md")
    return "text/markdown";
  if (e === "txt")
    return "text/plain";
  if (e === "json")
    return "application/json";
  if (e === "pdf")
    return "application/pdf";
  if (e === "png")
    return "image/png";
  if (e === "jpg" || e === "jpeg")
    return "image/jpeg";
  if (e === "gif")
    return "image/gif";
  if (e === "webp")
    return "image/webp";
  return "application/octet-stream";
}

// src/main.ts
var AevatarSraPlugin = class extends import_obsidian4.Plugin {
  constructor() {
    super(...arguments);
    this.settings = { ...DEFAULT_SRA_SETTINGS };
  }
  async onload() {
    await this.loadSettings();
    this.addSettingTab(new SraSettingsTab(this.app, this));
    this.registerView(VIEW_TYPE_SRA, (leaf) => new SraView(leaf, this));
    this.addCommand({
      id: "aevatar-sra-open-panel",
      name: "SRA: Open Panel",
      callback: () => void this.activateView()
    });
    this.addCommand({
      id: "aevatar-sra-new-session",
      name: "SRA: New Session",
      callback: async () => {
        const view = await this.activateView();
        await view.createNewSession();
      }
    });
    this.addCommand({
      id: "aevatar-sra-connect-session",
      name: "SRA: Connect to Session\u2026",
      callback: async () => {
        const view = await this.activateView();
        const sid = await promptForTextAsync("Connect to sessionId", "sessionId");
        if (!sid)
          return;
        view.connectToSession(sid);
      }
    });
    this.addCommand({
      id: "aevatar-sra-send-message",
      name: "SRA: Send Message",
      callback: async () => {
        const view = await this.activateView();
        await view.sendCurrentMessage();
      }
    });
    this.addCommand({
      id: "aevatar-sra-hello",
      name: "SRA: Hello",
      callback: () => {
        new import_obsidian4.Notice("Aevatar SRA plugin loaded (skeleton).");
      }
    });
  }
  async loadSettings() {
    const data = await this.loadData().catch(() => null);
    this.settings = { ...DEFAULT_SRA_SETTINGS, ...data != null ? data : {} };
  }
  async saveSettings() {
    await this.saveData(this.settings);
  }
  async activateView() {
    var _a;
    const existing = this.app.workspace.getLeavesOfType(VIEW_TYPE_SRA);
    const leaf = (_a = existing[0]) != null ? _a : this.app.workspace.getRightLeaf(false);
    if (!leaf)
      throw new Error("No workspace leaf available");
    await leaf.setViewState({ type: VIEW_TYPE_SRA, active: true });
    this.app.workspace.revealLeaf(leaf);
    return leaf.view;
  }
};
async function promptForTextAsync(title, placeholder) {
  var _a;
  const v = (_a = window.prompt(title, placeholder)) != null ? _a : "";
  return v.trim();
}
//# sourceMappingURL=data:application/json;base64,ewogICJ2ZXJzaW9uIjogMywKICAic291cmNlcyI6IFsiLi4vc3JjL21haW4udHMiLCAiLi4vc3JjL3NldHRpbmdzLnRzIiwgIi4uL3NyYy91aS9TcmFWaWV3LnRzIiwgIi4uL3NyYy9hcGkvU3JhQXBpQ2xpZW50LnRzIiwgIi4uL3NyYy9hcGkvdHlwZXMudHMiLCAiLi4vc3JjL2FwaS9tdWx0aXBhcnQudHMiLCAiLi4vc3JjL3NzZS9zc2VQYXJzZXIudHMiLCAiLi4vc3JjL3NzZS9TcmFTc2VDbGllbnQudHMiLCAiLi4vc3JjL3ZhdWx0L3BhdGhzLnRzIiwgIi4uL3NyYy92YXVsdC9WYXVsdFN0b3JlLnRzIiwgIi4uL3NyYy92YXVsdC9yZW5kZXIudHMiXSwKICAic291cmNlc0NvbnRlbnQiOiBbImltcG9ydCB7IE5vdGljZSwgUGx1Z2luIH0gZnJvbSBcIm9ic2lkaWFuXCI7XG5pbXBvcnQgeyBERUZBVUxUX1NSQV9TRVRUSU5HUywgU3JhU2V0dGluZ3NUYWIsIHR5cGUgU3JhUGx1Z2luU2V0dGluZ3MgfSBmcm9tIFwiLi9zZXR0aW5nc1wiO1xuaW1wb3J0IHsgU3JhVmlldywgVklFV19UWVBFX1NSQSB9IGZyb20gXCIuL3VpL1NyYVZpZXdcIjtcblxuLy8gPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09XG4vLyAgQWV2YXRhciBTUkEgKE9ic2lkaWFuIERlc2t0b3AgUGx1Z2luKVxuLy9cbi8vICBNVlAgZ29hbDpcbi8vICAtIFByb3ZpZGUgYSB0aGluIFVJIHNoZWxsIHRvIGNvbm5lY3QgdG8gU1JBIGJhY2tlbmQgKEhUVFAgKyBTU0UpXG4vLyAgLSBQZXJzaXN0IGFydGlmYWN0cyB0byBWYXVsdC9TUkEvXG4vL1xuLy8gIE5PVEU6XG4vLyAgLSBEZXNrdG9wLWZpcnN0IChFbGVjdHJvbikuIEZ1dHVyZSByZW1vdGUgaXMgc3VwcG9ydGVkIGJ5IGNvbmZpZ3VyYWJsZSBiYXNlVXJsLlxuLy8gIC0gTmV2ZXIgdXNlIHBvcnQgOjUwMDAgaW4gZGVmYXVsdHMvZXhhbXBsZXMgKHJlcG8gcG9saWN5KS5cbi8vID09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PVxuXG5leHBvcnQgZGVmYXVsdCBjbGFzcyBBZXZhdGFyU3JhUGx1Z2luIGV4dGVuZHMgUGx1Z2luIHtcbiAgc2V0dGluZ3M6IFNyYVBsdWdpblNldHRpbmdzID0geyAuLi5ERUZBVUxUX1NSQV9TRVRUSU5HUyB9O1xuXG4gIGFzeW5jIG9ubG9hZCgpOiBQcm9taXNlPHZvaWQ+IHtcbiAgICBhd2FpdCB0aGlzLmxvYWRTZXR0aW5ncygpO1xuXG4gICAgdGhpcy5hZGRTZXR0aW5nVGFiKG5ldyBTcmFTZXR0aW5nc1RhYih0aGlzLmFwcCwgdGhpcykpO1xuXG4gICAgdGhpcy5yZWdpc3RlclZpZXcoVklFV19UWVBFX1NSQSwgKGxlYWYpID0+IG5ldyBTcmFWaWV3KGxlYWYsIHRoaXMgYXMgYW55KSk7XG5cbiAgICB0aGlzLmFkZENvbW1hbmQoe1xuICAgICAgaWQ6IFwiYWV2YXRhci1zcmEtb3Blbi1wYW5lbFwiLFxuICAgICAgbmFtZTogXCJTUkE6IE9wZW4gUGFuZWxcIixcbiAgICAgIGNhbGxiYWNrOiAoKSA9PiB2b2lkIHRoaXMuYWN0aXZhdGVWaWV3KCksXG4gICAgfSk7XG5cbiAgICB0aGlzLmFkZENvbW1hbmQoe1xuICAgICAgaWQ6IFwiYWV2YXRhci1zcmEtbmV3LXNlc3Npb25cIixcbiAgICAgIG5hbWU6IFwiU1JBOiBOZXcgU2Vzc2lvblwiLFxuICAgICAgY2FsbGJhY2s6IGFzeW5jICgpID0+IHtcbiAgICAgICAgY29uc3QgdmlldyA9IGF3YWl0IHRoaXMuYWN0aXZhdGVWaWV3KCk7XG4gICAgICAgIGF3YWl0IHZpZXcuY3JlYXRlTmV3U2Vzc2lvbigpO1xuICAgICAgfSxcbiAgICB9KTtcblxuICAgIHRoaXMuYWRkQ29tbWFuZCh7XG4gICAgICBpZDogXCJhZXZhdGFyLXNyYS1jb25uZWN0LXNlc3Npb25cIixcbiAgICAgIG5hbWU6IFwiU1JBOiBDb25uZWN0IHRvIFNlc3Npb25cdTIwMjZcIixcbiAgICAgIGNhbGxiYWNrOiBhc3luYyAoKSA9PiB7XG4gICAgICAgIGNvbnN0IHZpZXcgPSBhd2FpdCB0aGlzLmFjdGl2YXRlVmlldygpO1xuICAgICAgICBjb25zdCBzaWQgPSBhd2FpdCBwcm9tcHRGb3JUZXh0QXN5bmMoXCJDb25uZWN0IHRvIHNlc3Npb25JZFwiLCBcInNlc3Npb25JZFwiKTtcbiAgICAgICAgaWYgKCFzaWQpIHJldHVybjtcbiAgICAgICAgdmlldy5jb25uZWN0VG9TZXNzaW9uKHNpZCk7XG4gICAgICB9LFxuICAgIH0pO1xuXG4gICAgdGhpcy5hZGRDb21tYW5kKHtcbiAgICAgIGlkOiBcImFldmF0YXItc3JhLXNlbmQtbWVzc2FnZVwiLFxuICAgICAgbmFtZTogXCJTUkE6IFNlbmQgTWVzc2FnZVwiLFxuICAgICAgY2FsbGJhY2s6IGFzeW5jICgpID0+IHtcbiAgICAgICAgY29uc3QgdmlldyA9IGF3YWl0IHRoaXMuYWN0aXZhdGVWaWV3KCk7XG4gICAgICAgIGF3YWl0IHZpZXcuc2VuZEN1cnJlbnRNZXNzYWdlKCk7XG4gICAgICB9LFxuICAgIH0pO1xuXG4gICAgdGhpcy5hZGRDb21tYW5kKHtcbiAgICAgIGlkOiBcImFldmF0YXItc3JhLWhlbGxvXCIsXG4gICAgICBuYW1lOiBcIlNSQTogSGVsbG9cIixcbiAgICAgIGNhbGxiYWNrOiAoKSA9PiB7XG4gICAgICAgIG5ldyBOb3RpY2UoXCJBZXZhdGFyIFNSQSBwbHVnaW4gbG9hZGVkIChza2VsZXRvbikuXCIpO1xuICAgICAgfSxcbiAgICB9KTtcbiAgfVxuXG4gIGFzeW5jIGxvYWRTZXR0aW5ncygpOiBQcm9taXNlPHZvaWQ+IHtcbiAgICBjb25zdCBkYXRhID0gKGF3YWl0IHRoaXMubG9hZERhdGEoKS5jYXRjaCgoKSA9PiBudWxsKSkgYXMgUGFydGlhbDxTcmFQbHVnaW5TZXR0aW5ncz4gfCBudWxsO1xuICAgIHRoaXMuc2V0dGluZ3MgPSB7IC4uLkRFRkFVTFRfU1JBX1NFVFRJTkdTLCAuLi4oZGF0YSA/PyB7fSkgfTtcbiAgfVxuXG4gIGFzeW5jIHNhdmVTZXR0aW5ncygpOiBQcm9taXNlPHZvaWQ+IHtcbiAgICBhd2FpdCB0aGlzLnNhdmVEYXRhKHRoaXMuc2V0dGluZ3MpO1xuICB9XG5cbiAgcHJpdmF0ZSBhc3luYyBhY3RpdmF0ZVZpZXcoKTogUHJvbWlzZTxTcmFWaWV3PiB7XG4gICAgY29uc3QgZXhpc3RpbmcgPSB0aGlzLmFwcC53b3Jrc3BhY2UuZ2V0TGVhdmVzT2ZUeXBlKFZJRVdfVFlQRV9TUkEpO1xuICAgIGNvbnN0IGxlYWYgPSBleGlzdGluZ1swXSA/PyB0aGlzLmFwcC53b3Jrc3BhY2UuZ2V0UmlnaHRMZWFmKGZhbHNlKTtcbiAgICBpZiAoIWxlYWYpIHRocm93IG5ldyBFcnJvcihcIk5vIHdvcmtzcGFjZSBsZWFmIGF2YWlsYWJsZVwiKTtcblxuICAgIGF3YWl0IGxlYWYuc2V0Vmlld1N0YXRlKHsgdHlwZTogVklFV19UWVBFX1NSQSwgYWN0aXZlOiB0cnVlIH0pO1xuICAgIHRoaXMuYXBwLndvcmtzcGFjZS5yZXZlYWxMZWFmKGxlYWYpO1xuICAgIHJldHVybiBsZWFmLnZpZXcgYXMgU3JhVmlldztcbiAgfVxufVxuXG5hc3luYyBmdW5jdGlvbiBwcm9tcHRGb3JUZXh0QXN5bmModGl0bGU6IHN0cmluZywgcGxhY2Vob2xkZXI6IHN0cmluZyk6IFByb21pc2U8c3RyaW5nPiB7XG4gIC8vIE1WUDogdXNlIGJyb3dzZXIgcHJvbXB0ICh3b3JrcyBpbiBkZXNrdG9wIEVsZWN0cm9uKS5cbiAgLy8gSWYgT2JzaWRpYW4gaW50cm9kdWNlcyBhIGJldHRlciBBUEksIHN3YXAgdGhpcyBpbXBsZW1lbnRhdGlvbi5cbiAgY29uc3QgdiA9IHdpbmRvdy5wcm9tcHQodGl0bGUsIHBsYWNlaG9sZGVyKSA/PyBcIlwiO1xuICByZXR1cm4gdi50cmltKCk7XG59XG5cblxuIiwgImltcG9ydCB7IEFwcCwgTm90aWNlLCBQbHVnaW5TZXR0aW5nVGFiLCBTZXR0aW5nLCByZXF1ZXN0VXJsIH0gZnJvbSBcIm9ic2lkaWFuXCI7XG5cbi8vID09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PVxuLy8gIFNldHRpbmdzIChNVlApXG4vL1xuLy8gIFNlY3VyaXR5IHN0YW5jZTpcbi8vICAtIERvIE5PVCBzdG9yZSBBUEkga2V5cyBpbiBwbHVnaW4gc2V0dGluZ3MuXG4vLyAgLSBLZXlzIHNob3VsZCBiZSBjb25maWd1cmVkIHZpYSBleGlzdGluZyBBZXZhdGFyL1NSQSBzZWNyZXRzIHdvcmtmbG93cy5cbi8vID09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PVxuXG5leHBvcnQgaW50ZXJmYWNlIFNyYVBsdWdpblNldHRpbmdzIHtcbiAgYmFzZVVybDogc3RyaW5nOyAvLyBlLmcuIGh0dHA6Ly9sb2NhbGhvc3Q6NTY3OFxuICB2YXVsdFJvb3Q6IHN0cmluZzsgLy8gZGVmYXVsdDogU1JBXG4gIHNlY3JldHNVaVVybDogc3RyaW5nOyAvLyBkZWZhdWx0OiBodHRwOi8vbG9jYWxob3N0OjY2NzdcbiAgcmVxdWVzdFRpbWVvdXRNczogbnVtYmVyOyAvLyBkZWZhdWx0OiAxNTAwMFxufVxuXG5leHBvcnQgY29uc3QgREVGQVVMVF9TUkFfU0VUVElOR1M6IFNyYVBsdWdpblNldHRpbmdzID0ge1xuICBiYXNlVXJsOiBcImh0dHA6Ly9sb2NhbGhvc3Q6NTY3OFwiLFxuICB2YXVsdFJvb3Q6IFwiU1JBXCIsXG4gIHNlY3JldHNVaVVybDogXCJodHRwOi8vbG9jYWxob3N0OjY2NzdcIixcbiAgcmVxdWVzdFRpbWVvdXRNczogMTUwMDAsXG59O1xuXG5leHBvcnQgY2xhc3MgU3JhU2V0dGluZ3NUYWIgZXh0ZW5kcyBQbHVnaW5TZXR0aW5nVGFiIHtcbiAgcHJpdmF0ZSByZWFkb25seSBwbHVnaW46IHsgc2V0dGluZ3M6IFNyYVBsdWdpblNldHRpbmdzOyBzYXZlU2V0dGluZ3M6ICgpID0+IFByb21pc2U8dm9pZD4gfTtcblxuICBjb25zdHJ1Y3RvcihhcHA6IEFwcCwgcGx1Z2luOiB7IHNldHRpbmdzOiBTcmFQbHVnaW5TZXR0aW5nczsgc2F2ZVNldHRpbmdzOiAoKSA9PiBQcm9taXNlPHZvaWQ+IH0pIHtcbiAgICBzdXBlcihhcHAsIHBsdWdpbiBhcyBhbnkpO1xuICAgIHRoaXMucGx1Z2luID0gcGx1Z2luO1xuICB9XG5cbiAgZGlzcGxheSgpOiB2b2lkIHtcbiAgICBjb25zdCB7IGNvbnRhaW5lckVsIH0gPSB0aGlzO1xuICAgIGNvbnRhaW5lckVsLmVtcHR5KCk7XG5cbiAgICBjb250YWluZXJFbC5jcmVhdGVFbChcImgyXCIsIHsgdGV4dDogXCJBZXZhdGFyIFNSQVwiIH0pO1xuXG4gICAgbmV3IFNldHRpbmcoY29udGFpbmVyRWwpXG4gICAgICAuc2V0TmFtZShcIkJhY2tlbmQgYmFzZVVybFwiKVxuICAgICAgLnNldERlc2MoXCJEZWZhdWx0OiBodHRwOi8vbG9jYWxob3N0OjU2NzggKHJlcG8gcG9saWN5OiBuZXZlciB1c2UgOjUwMDAgaW4gZXhhbXBsZXMpXCIpXG4gICAgICAuYWRkVGV4dCgodCkgPT4ge1xuICAgICAgICB0LnNldFBsYWNlaG9sZGVyKERFRkFVTFRfU1JBX1NFVFRJTkdTLmJhc2VVcmwpXG4gICAgICAgICAgLnNldFZhbHVlKHRoaXMucGx1Z2luLnNldHRpbmdzLmJhc2VVcmwpXG4gICAgICAgICAgLm9uQ2hhbmdlKGFzeW5jICh2YWx1ZSkgPT4ge1xuICAgICAgICAgICAgdGhpcy5wbHVnaW4uc2V0dGluZ3MuYmFzZVVybCA9ICh2YWx1ZSA/PyBcIlwiKS50cmltKCk7XG4gICAgICAgICAgICBhd2FpdCB0aGlzLnBsdWdpbi5zYXZlU2V0dGluZ3MoKTtcbiAgICAgICAgICB9KTtcbiAgICAgIH0pO1xuXG4gICAgbmV3IFNldHRpbmcoY29udGFpbmVyRWwpXG4gICAgICAuc2V0TmFtZShcIlZhdWx0IHJvb3QgZm9sZGVyXCIpXG4gICAgICAuc2V0RGVzYyhcIkFsbCBwbHVnaW4gYXJ0aWZhY3RzIHdpbGwgYmUgd3JpdHRlbiB1bmRlciB0aGlzIGZvbGRlciBpbiB0aGUgY3VycmVudCB2YXVsdC4gRGVmYXVsdDogU1JBXCIpXG4gICAgICAuYWRkVGV4dCgodCkgPT4ge1xuICAgICAgICB0LnNldFBsYWNlaG9sZGVyKERFRkFVTFRfU1JBX1NFVFRJTkdTLnZhdWx0Um9vdClcbiAgICAgICAgICAuc2V0VmFsdWUodGhpcy5wbHVnaW4uc2V0dGluZ3MudmF1bHRSb290KVxuICAgICAgICAgIC5vbkNoYW5nZShhc3luYyAodmFsdWUpID0+IHtcbiAgICAgICAgICAgIHRoaXMucGx1Z2luLnNldHRpbmdzLnZhdWx0Um9vdCA9ICh2YWx1ZSA/PyBcIlwiKS50cmltKCkgfHwgREVGQVVMVF9TUkFfU0VUVElOR1MudmF1bHRSb290O1xuICAgICAgICAgICAgYXdhaXQgdGhpcy5wbHVnaW4uc2F2ZVNldHRpbmdzKCk7XG4gICAgICAgICAgfSk7XG4gICAgICB9KTtcblxuICAgIG5ldyBTZXR0aW5nKGNvbnRhaW5lckVsKVxuICAgICAgLnNldE5hbWUoXCJTZWNyZXRzIFVJIFVSTCAob3B0aW9uYWwpXCIpXG4gICAgICAuc2V0RGVzYyhcIkJlc3QtZWZmb3J0IGhlbHBlciBsaW5rLiBLZXlzIHNob3VsZCBiZSBjb25maWd1cmVkIG91dHNpZGUgdGhlIHBsdWdpbi5cIilcbiAgICAgIC5hZGRUZXh0KCh0KSA9PiB7XG4gICAgICAgIHQuc2V0UGxhY2Vob2xkZXIoREVGQVVMVF9TUkFfU0VUVElOR1Muc2VjcmV0c1VpVXJsKVxuICAgICAgICAgIC5zZXRWYWx1ZSh0aGlzLnBsdWdpbi5zZXR0aW5ncy5zZWNyZXRzVWlVcmwpXG4gICAgICAgICAgLm9uQ2hhbmdlKGFzeW5jICh2YWx1ZSkgPT4ge1xuICAgICAgICAgICAgdGhpcy5wbHVnaW4uc2V0dGluZ3Muc2VjcmV0c1VpVXJsID0gKHZhbHVlID8/IFwiXCIpLnRyaW0oKTtcbiAgICAgICAgICAgIGF3YWl0IHRoaXMucGx1Z2luLnNhdmVTZXR0aW5ncygpO1xuICAgICAgICAgIH0pO1xuICAgICAgfSk7XG5cbiAgICBuZXcgU2V0dGluZyhjb250YWluZXJFbClcbiAgICAgIC5zZXROYW1lKFwiT3BlbiBTZWNyZXRzIFVJXCIpXG4gICAgICAuc2V0RGVzYyhcIk9wZW5zIHRoZSBjb25maWd1cmVkIFNlY3JldHMgVUkgVVJMIGluIGEgYnJvd3NlciAoYmVzdC1lZmZvcnQpLlwiKVxuICAgICAgLmFkZEJ1dHRvbigoYikgPT4ge1xuICAgICAgICBiLnNldEJ1dHRvblRleHQoXCJPcGVuXCIpLm9uQ2xpY2soKCkgPT4ge1xuICAgICAgICAgIGNvbnN0IHVybCA9ICh0aGlzLnBsdWdpbi5zZXR0aW5ncy5zZWNyZXRzVWlVcmwgPz8gXCJcIikudHJpbSgpO1xuICAgICAgICAgIGlmICghdXJsKSB7XG4gICAgICAgICAgICBuZXcgTm90aWNlKFwiU2VjcmV0cyBVSSBVUkwgaXMgZW1wdHkuXCIpO1xuICAgICAgICAgICAgcmV0dXJuO1xuICAgICAgICAgIH1cbiAgICAgICAgICB0cnkge1xuICAgICAgICAgICAgd2luZG93Lm9wZW4odXJsLCBcIl9ibGFua1wiKTtcbiAgICAgICAgICB9IGNhdGNoIHtcbiAgICAgICAgICAgIG5ldyBOb3RpY2UoXCJGYWlsZWQgdG8gb3BlbiBTZWNyZXRzIFVJIFVSTC5cIik7XG4gICAgICAgICAgfVxuICAgICAgICB9KTtcbiAgICAgIH0pO1xuXG4gICAgbmV3IFNldHRpbmcoY29udGFpbmVyRWwpXG4gICAgICAuc2V0TmFtZShcIlJlcXVlc3QgdGltZW91dCAobXMpXCIpXG4gICAgICAuc2V0RGVzYyhcIkRlZmF1bHQ6IDE1MDAwXCIpXG4gICAgICAuYWRkVGV4dCgodCkgPT4ge1xuICAgICAgICB0LnNldFBsYWNlaG9sZGVyKFN0cmluZyhERUZBVUxUX1NSQV9TRVRUSU5HUy5yZXF1ZXN0VGltZW91dE1zKSlcbiAgICAgICAgICAuc2V0VmFsdWUoU3RyaW5nKHRoaXMucGx1Z2luLnNldHRpbmdzLnJlcXVlc3RUaW1lb3V0TXMgPz8gREVGQVVMVF9TUkFfU0VUVElOR1MucmVxdWVzdFRpbWVvdXRNcykpXG4gICAgICAgICAgLm9uQ2hhbmdlKGFzeW5jICh2YWx1ZSkgPT4ge1xuICAgICAgICAgICAgY29uc3QgbiA9IE51bWJlci5wYXJzZUludCgodmFsdWUgPz8gXCJcIikudHJpbSgpLCAxMCk7XG4gICAgICAgICAgICB0aGlzLnBsdWdpbi5zZXR0aW5ncy5yZXF1ZXN0VGltZW91dE1zID0gTnVtYmVyLmlzRmluaXRlKG4pICYmIG4gPiAwID8gbiA6IERFRkFVTFRfU1JBX1NFVFRJTkdTLnJlcXVlc3RUaW1lb3V0TXM7XG4gICAgICAgICAgICBhd2FpdCB0aGlzLnBsdWdpbi5zYXZlU2V0dGluZ3MoKTtcbiAgICAgICAgICB9KTtcbiAgICAgIH0pO1xuXG4gICAgbmV3IFNldHRpbmcoY29udGFpbmVyRWwpXG4gICAgICAuc2V0TmFtZShcIlRlc3QgQ29ubmVjdGlvblwiKVxuICAgICAgLnNldERlc2MoXCJDYWxscyAvaGVhbHRoICh0aGVuIC9hcGkvaW5mbykgdG8gdmVyaWZ5IHRoZSBiYWNrZW5kIGlzIHJlYWNoYWJsZS5cIilcbiAgICAgIC5hZGRCdXR0b24oKGIpID0+IHtcbiAgICAgICAgYi5zZXRCdXR0b25UZXh0KFwiVGVzdCBDb25uZWN0aW9uXCIpLm9uQ2xpY2soYXN5bmMgKCkgPT4ge1xuICAgICAgICAgIGNvbnN0IGJhc2VVcmwgPSBub3JtYWxpemVCYXNlVXJsKHRoaXMucGx1Z2luLnNldHRpbmdzLmJhc2VVcmwpO1xuICAgICAgICAgIGNvbnN0IHRpbWVvdXRNcyA9IGNsYW1wVGltZW91dCh0aGlzLnBsdWdpbi5zZXR0aW5ncy5yZXF1ZXN0VGltZW91dE1zKTtcblxuICAgICAgICAgIGlmICghYmFzZVVybCkge1xuICAgICAgICAgICAgbmV3IE5vdGljZShcIkludmFsaWQgYmFzZVVybC5cIik7XG4gICAgICAgICAgICByZXR1cm47XG4gICAgICAgICAgfVxuXG4gICAgICAgICAgY29uc3QgcmVzID0gYXdhaXQgdGVzdENvbm5lY3Rpb25Bc3luYyhiYXNlVXJsLCB0aW1lb3V0TXMpO1xuICAgICAgICAgIGlmIChyZXMub2spIHtcbiAgICAgICAgICAgIG5ldyBOb3RpY2UoYFNSQSBiYWNrZW5kIE9LICgke3Jlcy5lbmRwb2ludH0sIEhUVFAgJHtyZXMuc3RhdHVzfSlgKTtcbiAgICAgICAgICB9IGVsc2Uge1xuICAgICAgICAgICAgbmV3IE5vdGljZShgU1JBIGJhY2tlbmQgTk9UIHJlYWNoYWJsZSAoJHtyZXMuZW5kcG9pbnR9KTogJHtyZXMuZXJyb3J9YCk7XG4gICAgICAgICAgfVxuICAgICAgICB9KTtcbiAgICAgIH0pO1xuICB9XG59XG5cbmZ1bmN0aW9uIG5vcm1hbGl6ZUJhc2VVcmwoaW5wdXQ6IHN0cmluZyk6IHN0cmluZyB7XG4gIGNvbnN0IHMgPSAoaW5wdXQgPz8gXCJcIikudHJpbSgpO1xuICBpZiAoIXMpIHJldHVybiBcIlwiO1xuICByZXR1cm4gcy5yZXBsYWNlKC9cXC8rJC8sIFwiXCIpO1xufVxuXG5mdW5jdGlvbiBjbGFtcFRpbWVvdXQobXM6IG51bWJlcik6IG51bWJlciB7XG4gIGNvbnN0IG4gPSBOdW1iZXIuaXNGaW5pdGUobXMpID8gbXMgOiBERUZBVUxUX1NSQV9TRVRUSU5HUy5yZXF1ZXN0VGltZW91dE1zO1xuICByZXR1cm4gTWF0aC5tYXgoMTAwMCwgTWF0aC5taW4oMTIwXzAwMCwgbikpO1xufVxuXG5hc3luYyBmdW5jdGlvbiB0ZXN0Q29ubmVjdGlvbkFzeW5jKFxuICBiYXNlVXJsOiBzdHJpbmcsXG4gIHRpbWVvdXRNczogbnVtYmVyLFxuKTogUHJvbWlzZTx7IG9rOiBib29sZWFuOyBlbmRwb2ludDogc3RyaW5nOyBzdGF0dXM6IG51bWJlcjsgZXJyb3I6IHN0cmluZyB9PiB7XG4gIGNvbnN0IGhlYWx0aFVybCA9IGAke2Jhc2VVcmx9L2hlYWx0aGA7XG4gIHRyeSB7XG4gICAgY29uc3QgciA9IGF3YWl0IHJlcXVlc3RVcmwoe1xuICAgICAgdXJsOiBoZWFsdGhVcmwsXG4gICAgICBtZXRob2Q6IFwiR0VUXCIsXG4gICAgICB0aW1lb3V0OiB0aW1lb3V0TXMgYXMgYW55LFxuICAgICAgdGhyb3c6IGZhbHNlLFxuICAgIH0gYXMgYW55KTtcblxuICAgIGlmIChyLnN0YXR1cyA+PSAyMDAgJiYgci5zdGF0dXMgPCAzMDApIHtcbiAgICAgIHJldHVybiB7IG9rOiB0cnVlLCBlbmRwb2ludDogXCIvaGVhbHRoXCIsIHN0YXR1czogci5zdGF0dXMsIGVycm9yOiBcIlwiIH07XG4gICAgfVxuICB9IGNhdGNoIChlOiBhbnkpIHtcbiAgICAvLyBmYWxsdGhyb3VnaCB0byAvYXBpL2luZm9cbiAgfVxuXG4gIGNvbnN0IGluZm9VcmwgPSBgJHtiYXNlVXJsfS9hcGkvaW5mb2A7XG4gIHRyeSB7XG4gICAgY29uc3QgciA9IGF3YWl0IHJlcXVlc3RVcmwoe1xuICAgICAgdXJsOiBpbmZvVXJsLFxuICAgICAgbWV0aG9kOiBcIkdFVFwiLFxuICAgICAgdGltZW91dDogdGltZW91dE1zIGFzIGFueSxcbiAgICAgIHRocm93OiBmYWxzZSxcbiAgICB9IGFzIGFueSk7XG5cbiAgICBpZiAoci5zdGF0dXMgPj0gMjAwICYmIHIuc3RhdHVzIDwgMzAwKSB7XG4gICAgICByZXR1cm4geyBvazogdHJ1ZSwgZW5kcG9pbnQ6IFwiL2FwaS9pbmZvXCIsIHN0YXR1czogci5zdGF0dXMsIGVycm9yOiBcIlwiIH07XG4gICAgfVxuXG4gICAgcmV0dXJuIHsgb2s6IGZhbHNlLCBlbmRwb2ludDogXCIvYXBpL2luZm9cIiwgc3RhdHVzOiByLnN0YXR1cywgZXJyb3I6IGBIVFRQICR7ci5zdGF0dXN9YCB9O1xuICB9IGNhdGNoIChlOiBhbnkpIHtcbiAgICBjb25zdCBtc2cgPSAoZT8ubWVzc2FnZSA/PyBTdHJpbmcoZSA/PyBcInVua25vd24gZXJyb3JcIikpLnRyaW0oKTtcbiAgICByZXR1cm4geyBvazogZmFsc2UsIGVuZHBvaW50OiBcIi9hcGkvaW5mb1wiLCBzdGF0dXM6IDAsIGVycm9yOiBtc2cgfHwgXCJyZXF1ZXN0IGZhaWxlZFwiIH07XG4gIH1cbn1cblxuXG4iLCAiaW1wb3J0IHsgSXRlbVZpZXcsIE5vdGljZSwgV29ya3NwYWNlTGVhZiB9IGZyb20gXCJvYnNpZGlhblwiO1xuXG5pbXBvcnQgeyBTcmFBcGlDbGllbnQgfSBmcm9tIFwiLi4vYXBpL1NyYUFwaUNsaWVudFwiO1xuaW1wb3J0IHR5cGUgeyBTcmFTZW5kSW5wdXRSZXF1ZXN0IH0gZnJvbSBcIi4uL2FwaS90eXBlc1wiO1xuaW1wb3J0IHsgU3JhU3NlQ2xpZW50LCB0eXBlIFNzZVN0YXR1cyB9IGZyb20gXCIuLi9zc2UvU3JhU3NlQ2xpZW50XCI7XG5pbXBvcnQgeyBWYXVsdFN0b3JlIH0gZnJvbSBcIi4uL3ZhdWx0L1ZhdWx0U3RvcmVcIjtcbmltcG9ydCB7IHJlbmRlckJyaWVmTWFya2Rvd24sIHJlbmRlckRlbGl2ZXJ5TWFya2Rvd24gfSBmcm9tIFwiLi4vdmF1bHQvcmVuZGVyXCI7XG5cbmV4cG9ydCBjb25zdCBWSUVXX1RZUEVfU1JBID0gXCJhZXZhdGFyLXNyYS12aWV3XCI7XG5cbnR5cGUgUGx1Z2luTGlrZSA9IHtcbiAgYXBwOiBhbnk7XG4gIHNldHRpbmdzOiB7XG4gICAgYmFzZVVybDogc3RyaW5nO1xuICAgIHZhdWx0Um9vdDogc3RyaW5nO1xuICAgIHJlcXVlc3RUaW1lb3V0TXM6IG51bWJlcjtcbiAgfTtcbn07XG5cbmV4cG9ydCBjbGFzcyBTcmFWaWV3IGV4dGVuZHMgSXRlbVZpZXcge1xuICBwcml2YXRlIHJlYWRvbmx5IHBsdWdpbjogUGx1Z2luTGlrZTtcblxuICBwcml2YXRlIHJlYWRvbmx5IGFwaTogU3JhQXBpQ2xpZW50O1xuICBwcml2YXRlIHJlYWRvbmx5IHNzZTogU3JhU3NlQ2xpZW50PGFueT47XG4gIHByaXZhdGUgcmVhZG9ubHkgdmF1bHQ6IFZhdWx0U3RvcmU7XG5cbiAgcHJpdmF0ZSBzZXNzaW9uSWQgPSBcIlwiO1xuICBwcml2YXRlIHJ1bklkID0gXCJcIjtcblxuICBwcml2YXRlIHN0YXR1czogU3NlU3RhdHVzID0gXCJEaXNjb25uZWN0ZWRcIjtcbiAgcHJpdmF0ZSBsYXN0RXJyb3IgPSBcIlwiO1xuXG4gIHByaXZhdGUgZWxTdGF0dXMhOiBIVE1MRWxlbWVudDtcbiAgcHJpdmF0ZSBlbFNlc3Npb24hOiBIVE1MSW5wdXRFbGVtZW50O1xuICBwcml2YXRlIGVsUnVuITogSFRNTEVsZW1lbnQ7XG4gIHByaXZhdGUgZWxNb2RlITogSFRNTFNlbGVjdEVsZW1lbnQ7XG4gIHByaXZhdGUgZWxNZXNzYWdlITogSFRNTFRleHRBcmVhRWxlbWVudDtcbiAgcHJpdmF0ZSBlbEVycm9yITogSFRNTEVsZW1lbnQ7XG4gIHByaXZhdGUgZWxBdHRhY2htZW50cyE6IEhUTUxFbGVtZW50O1xuXG4gIHByaXZhdGUgYXR0YWNobWVudHM6IGFueVtdID0gW107XG5cbiAgY29uc3RydWN0b3IobGVhZjogV29ya3NwYWNlTGVhZiwgcGx1Z2luOiBQbHVnaW5MaWtlKSB7XG4gICAgc3VwZXIobGVhZik7XG4gICAgdGhpcy5wbHVnaW4gPSBwbHVnaW47XG5cbiAgICB0aGlzLmFwaSA9IG5ldyBTcmFBcGlDbGllbnQoeyBiYXNlVXJsOiBwbHVnaW4uc2V0dGluZ3MuYmFzZVVybCwgdGltZW91dE1zOiBwbHVnaW4uc2V0dGluZ3MucmVxdWVzdFRpbWVvdXRNcyB9KTtcbiAgICB0aGlzLnNzZSA9IG5ldyBTcmFTc2VDbGllbnQoeyBiYXNlVXJsOiBwbHVnaW4uc2V0dGluZ3MuYmFzZVVybCB9KTtcbiAgICB0aGlzLnZhdWx0ID0gbmV3IFZhdWx0U3RvcmUocGx1Z2luLmFwcCwgcGx1Z2luLnNldHRpbmdzLnZhdWx0Um9vdCk7XG4gIH1cblxuICBnZXRWaWV3VHlwZSgpOiBzdHJpbmcge1xuICAgIHJldHVybiBWSUVXX1RZUEVfU1JBO1xuICB9XG5cbiAgZ2V0RGlzcGxheVRleHQoKTogc3RyaW5nIHtcbiAgICByZXR1cm4gXCJTUkFcIjtcbiAgfVxuXG4gIGFzeW5jIG9uT3BlbigpOiBQcm9taXNlPHZvaWQ+IHtcbiAgICB0aGlzLnJlbmRlcigpO1xuICAgIGF3YWl0IHRoaXMudmF1bHQuZW5zdXJlUm9vdCgpO1xuICB9XG5cbiAgYXN5bmMgb25DbG9zZSgpOiBQcm9taXNlPHZvaWQ+IHtcbiAgICB0aGlzLnNzZS5kaXNjb25uZWN0KCk7XG4gIH1cblxuICAvLyAtLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS1cbiAgLy8gIFB1YmxpYyBhY3Rpb25zICh1c2VkIGJ5IGNvbW1hbmRzKVxuICAvLyAtLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS1cblxuICBhc3luYyBjcmVhdGVOZXdTZXNzaW9uKCk6IFByb21pc2U8dm9pZD4ge1xuICAgIHRoaXMucmVmcmVzaFJ1bnRpbWUoKTtcbiAgICB0cnkge1xuICAgICAgY29uc3QgcmVzID0gYXdhaXQgdGhpcy5hcGkuY3JlYXRlU2Vzc2lvbigpO1xuICAgICAgdGhpcy5jb25uZWN0VG9TZXNzaW9uKHJlcy5zZXNzaW9uSWQpO1xuICAgICAgbmV3IE5vdGljZShgU1JBIHNlc3Npb24gY3JlYXRlZDogJHtyZXMuc2Vzc2lvbklkfWApO1xuICAgIH0gY2F0Y2ggKGU6IGFueSkge1xuICAgICAgdGhpcy5zZXRFcnJvcihlPy5tZXNzYWdlID8/IFwiY3JlYXRlIHNlc3Npb24gZmFpbGVkXCIpO1xuICAgICAgbmV3IE5vdGljZShgQ3JlYXRlIHNlc3Npb24gZmFpbGVkOiAke3RoaXMubGFzdEVycm9yfWApO1xuICAgIH1cbiAgfVxuXG4gIGNvbm5lY3RUb1Nlc3Npb24oc2Vzc2lvbklkOiBzdHJpbmcpOiB2b2lkIHtcbiAgICBjb25zdCBzaWQgPSBTdHJpbmcoc2Vzc2lvbklkID8/IFwiXCIpLnRyaW0oKTtcbiAgICBpZiAoIXNpZCkge1xuICAgICAgdGhpcy5zZXRFcnJvcihcInNlc3Npb25JZCBpcyByZXF1aXJlZFwiKTtcbiAgICAgIHJldHVybjtcbiAgICB9XG5cbiAgICB0aGlzLnNlc3Npb25JZCA9IHNpZDtcbiAgICB0aGlzLnJ1bklkID0gXCJcIjtcbiAgICB0aGlzLmVsU2Vzc2lvbi52YWx1ZSA9IHNpZDtcbiAgICB0aGlzLmVsUnVuLnNldFRleHQoXCJydW46IChub25lKVwiKTtcbiAgICB0aGlzLnNldEVycm9yKFwiXCIpO1xuXG4gICAgdGhpcy5yZWZyZXNoUnVudGltZSgpO1xuICAgIHRoaXMuc3NlLmNvbm5lY3Qoc2lkLCB7XG4gICAgICBvblN0YXR1czogKHN0YXR1cywgZGV0YWlsKSA9PiB7XG4gICAgICAgIHRoaXMuc3RhdHVzID0gc3RhdHVzO1xuICAgICAgICBjb25zdCB0YWlsID0gZGV0YWlsID8gYCBcdTIwMTQgJHtkZXRhaWx9YCA6IFwiXCI7XG4gICAgICAgIHRoaXMuZWxTdGF0dXMuc2V0VGV4dChgU1NFOiAke3N0YXR1c30ke3RhaWx9YCk7XG4gICAgICB9LFxuICAgICAgb25FdmVudDogKGV2dCkgPT4ge1xuICAgICAgICAvLyBNaW5pbWFsIFVYOiB1cGRhdGUgcnVuSWQgd2hlbiBwcmVzZW50LCBhbmQgcGVyc2lzdCByYXcgZXZlbnRzIHRvIHZhdWx0LlxuICAgICAgICBjb25zdCByaWQgPSBleHRyYWN0UnVuSWQoZXZ0KTtcbiAgICAgICAgaWYgKHJpZCAmJiByaWQgIT09IHRoaXMucnVuSWQpIHtcbiAgICAgICAgICB0aGlzLnJ1bklkID0gcmlkO1xuICAgICAgICAgIHRoaXMuZWxSdW4uc2V0VGV4dChgcnVuOiAke3JpZH1gKTtcbiAgICAgICAgfVxuXG4gICAgICAgIHZvaWQgdGhpcy52YXVsdC5hcHBlbmRSdW5FdmVudCh0aGlzLnNlc3Npb25JZCwgdGhpcy5ydW5JZCB8fCBcInN0cmVhbVwiLCBldnQpO1xuICAgICAgfSxcbiAgICB9KTtcbiAgfVxuXG4gIGFzeW5jIHNlbmRDdXJyZW50TWVzc2FnZSgpOiBQcm9taXNlPHZvaWQ+IHtcbiAgICBjb25zdCBtc2cgPSAodGhpcy5lbE1lc3NhZ2UudmFsdWUgPz8gXCJcIikudHJpbSgpO1xuICAgIGlmICghbXNnKSByZXR1cm47XG4gICAgaWYgKCF0aGlzLnNlc3Npb25JZCkge1xuICAgICAgbmV3IE5vdGljZShcIk5vIGFjdGl2ZSBzZXNzaW9uLiBDcmVhdGUgb3IgY29ubmVjdCBmaXJzdC5cIik7XG4gICAgICByZXR1cm47XG4gICAgfVxuXG4gICAgdGhpcy5yZWZyZXNoUnVudGltZSgpO1xuICAgIHRyeSB7XG4gICAgICBjb25zdCBtb2RlID0gdGhpcy5lbE1vZGUudmFsdWU7XG4gICAgICBsZXQgYXR0YWNobWVudFBhdGhzOiBzdHJpbmdbXSB8IHVuZGVmaW5lZCA9IHVuZGVmaW5lZDtcblxuICAgICAgaWYgKHRoaXMuYXR0YWNobWVudHMubGVuZ3RoID4gMCkge1xuICAgICAgICBjb25zdCBmaWxlcyA9IGF3YWl0IHRoaXMucmVhZEF0dGFjaG1lbnRzQXN5bmMoKTtcbiAgICAgICAgY29uc3QgdXAgPSBhd2FpdCB0aGlzLmFwaS51cGxvYWRBdHRhY2htZW50cyh0aGlzLnNlc3Npb25JZCwgZmlsZXMpO1xuICAgICAgICBhdHRhY2htZW50UGF0aHMgPSB1cC5hdHRhY2htZW50UGF0aHM7XG4gICAgICB9XG5cbiAgICAgIGNvbnN0IGlucHV0OiBTcmFTZW5kSW5wdXRSZXF1ZXN0ID0geyBtZXNzYWdlOiBtc2csIG1vZGUsIGF0dGFjaG1lbnRQYXRocyB9O1xuICAgICAgY29uc3QgcmVzID0gYXdhaXQgdGhpcy5hcGkuc2VuZElucHV0KHRoaXMuc2Vzc2lvbklkLCBpbnB1dCk7XG4gICAgICB0aGlzLnJ1bklkID0gcmVzLnJ1bklkO1xuICAgICAgdGhpcy5lbFJ1bi5zZXRUZXh0KGBydW46ICR7cmVzLnJ1bklkfWApO1xuICAgICAgdGhpcy5lbE1lc3NhZ2UudmFsdWUgPSBcIlwiO1xuICAgICAgdGhpcy5hdHRhY2htZW50cyA9IFtdO1xuICAgICAgdGhpcy5yZW5kZXJBdHRhY2htZW50cygpO1xuXG4gICAgICBhd2FpdCB0aGlzLnZhdWx0LndyaXRlUnVuTWV0YSh0aGlzLnNlc3Npb25JZCwgcmVzLnJ1bklkLCB7XG4gICAgICAgIHNlc3Npb25JZDogdGhpcy5zZXNzaW9uSWQsXG4gICAgICAgIHJ1bklkOiByZXMucnVuSWQsXG4gICAgICAgIGNyZWF0ZWRBdDogbmV3IERhdGUoKS50b0lTT1N0cmluZygpLFxuICAgICAgICBtb2RlLFxuICAgICAgfSk7XG4gICAgfSBjYXRjaCAoZTogYW55KSB7XG4gICAgICB0aGlzLnNldEVycm9yKGU/Lm1lc3NhZ2UgPz8gXCJzZW5kIGZhaWxlZFwiKTtcbiAgICAgIG5ldyBOb3RpY2UoYFNlbmQgZmFpbGVkOiAke3RoaXMubGFzdEVycm9yfWApO1xuICAgIH1cbiAgfVxuXG4gIC8vIC0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLVxuICAvLyAgUmVuZGVyaW5nXG4gIC8vIC0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLVxuXG4gIHByaXZhdGUgcmVuZGVyKCk6IHZvaWQge1xuICAgIGNvbnN0IHsgY29udGFpbmVyRWwgfSA9IHRoaXM7XG4gICAgY29udGFpbmVyRWwuZW1wdHkoKTtcbiAgICBjb250YWluZXJFbC5hZGRDbGFzcyhcImFldmF0YXItc3JhLXZpZXdcIik7XG5cbiAgICBjb250YWluZXJFbC5jcmVhdGVFbChcImgyXCIsIHsgdGV4dDogXCJBZXZhdGFyIFNSQVwiIH0pO1xuXG4gICAgdGhpcy5lbFN0YXR1cyA9IGNvbnRhaW5lckVsLmNyZWF0ZUVsKFwiZGl2XCIsIHsgdGV4dDogXCJTU0U6IERpc2Nvbm5lY3RlZFwiIH0pO1xuICAgIHRoaXMuZWxSdW4gPSBjb250YWluZXJFbC5jcmVhdGVFbChcImRpdlwiLCB7IHRleHQ6IFwicnVuOiAobm9uZSlcIiB9KTtcblxuICAgIGNvbnN0IHJvdyA9IGNvbnRhaW5lckVsLmNyZWF0ZUVsKFwiZGl2XCIpO1xuICAgIHJvdy5zdHlsZS5kaXNwbGF5ID0gXCJmbGV4XCI7XG4gICAgcm93LnN0eWxlLmdhcCA9IFwiOHB4XCI7XG4gICAgcm93LnN0eWxlLmFsaWduSXRlbXMgPSBcImNlbnRlclwiO1xuXG4gICAgdGhpcy5lbFNlc3Npb24gPSByb3cuY3JlYXRlRWwoXCJpbnB1dFwiKTtcbiAgICB0aGlzLmVsU2Vzc2lvbi50eXBlID0gXCJ0ZXh0XCI7XG4gICAgdGhpcy5lbFNlc3Npb24ucGxhY2Vob2xkZXIgPSBcInNlc3Npb25JZFwiO1xuICAgIHRoaXMuZWxTZXNzaW9uLnN0eWxlLmZsZXggPSBcIjFcIjtcblxuICAgIGNvbnN0IGJ0bkNvbm5lY3QgPSByb3cuY3JlYXRlRWwoXCJidXR0b25cIiwgeyB0ZXh0OiBcIkNvbm5lY3RcIiB9KTtcbiAgICBidG5Db25uZWN0Lm9uY2xpY2sgPSAoKSA9PiB0aGlzLmNvbm5lY3RUb1Nlc3Npb24odGhpcy5lbFNlc3Npb24udmFsdWUpO1xuXG4gICAgY29uc3QgYnRuTmV3ID0gcm93LmNyZWF0ZUVsKFwiYnV0dG9uXCIsIHsgdGV4dDogXCJOZXcgU2Vzc2lvblwiIH0pO1xuICAgIGJ0bk5ldy5vbmNsaWNrID0gKCkgPT4gdm9pZCB0aGlzLmNyZWF0ZU5ld1Nlc3Npb24oKTtcblxuICAgIGNvbnN0IGNvbnRyb2xzID0gY29udGFpbmVyRWwuY3JlYXRlRWwoXCJkaXZcIik7XG4gICAgY29udHJvbHMuc3R5bGUuZGlzcGxheSA9IFwiZmxleFwiO1xuICAgIGNvbnRyb2xzLnN0eWxlLmdhcCA9IFwiOHB4XCI7XG4gICAgY29udHJvbHMuc3R5bGUubWFyZ2luVG9wID0gXCI4cHhcIjtcblxuICAgIHRoaXMuZWxNb2RlID0gY29udHJvbHMuY3JlYXRlRWwoXCJzZWxlY3RcIik7XG4gICAgZm9yIChjb25zdCBtIG9mIFtcImNoYXRcIiwgXCJ2aWJlXCIsIFwidmliZV9sb29wXCJdKSB7XG4gICAgICBjb25zdCBvcHQgPSB0aGlzLmVsTW9kZS5jcmVhdGVFbChcIm9wdGlvblwiKTtcbiAgICAgIG9wdC52YWx1ZSA9IG07XG4gICAgICBvcHQudGV4dCA9IG07XG4gICAgfVxuXG4gICAgY29uc3QgYnRuU2VuZCA9IGNvbnRyb2xzLmNyZWF0ZUVsKFwiYnV0dG9uXCIsIHsgdGV4dDogXCJTZW5kXCIgfSk7XG4gICAgYnRuU2VuZC5vbmNsaWNrID0gKCkgPT4gdm9pZCB0aGlzLnNlbmRDdXJyZW50TWVzc2FnZSgpO1xuXG4gICAgY29uc3QgYnRuUHVsbCA9IGNvbnRyb2xzLmNyZWF0ZUVsKFwiYnV0dG9uXCIsIHsgdGV4dDogXCJQdWxsIERlbGl2ZXJhYmxlc1wiIH0pO1xuICAgIGJ0blB1bGwub25jbGljayA9ICgpID0+IHZvaWQgdGhpcy5wdWxsRGVsaXZlcmFibGVzQXN5bmMoKTtcblxuICAgIGNvbnN0IGF0dGFjaFJvdyA9IGNvbnRhaW5lckVsLmNyZWF0ZUVsKFwiZGl2XCIpO1xuICAgIGF0dGFjaFJvdy5zdHlsZS5kaXNwbGF5ID0gXCJmbGV4XCI7XG4gICAgYXR0YWNoUm93LnN0eWxlLmdhcCA9IFwiOHB4XCI7XG4gICAgYXR0YWNoUm93LnN0eWxlLm1hcmdpblRvcCA9IFwiOHB4XCI7XG4gICAgYXR0YWNoUm93LnN0eWxlLmFsaWduSXRlbXMgPSBcImNlbnRlclwiO1xuXG4gICAgY29uc3QgYnRuQXR0YWNoID0gYXR0YWNoUm93LmNyZWF0ZUVsKFwiYnV0dG9uXCIsIHsgdGV4dDogXCJBdHRhY2ggY3VycmVudCBub3RlXCIgfSk7XG4gICAgYnRuQXR0YWNoLm9uY2xpY2sgPSAoKSA9PiB2b2lkIHRoaXMuYXR0YWNoQ3VycmVudE5vdGVBc3luYygpO1xuXG4gICAgY29uc3QgYnRuQ2xlYXIgPSBhdHRhY2hSb3cuY3JlYXRlRWwoXCJidXR0b25cIiwgeyB0ZXh0OiBcIkNsZWFyIGF0dGFjaG1lbnRzXCIgfSk7XG4gICAgYnRuQ2xlYXIub25jbGljayA9ICgpID0+IHtcbiAgICAgIHRoaXMuYXR0YWNobWVudHMgPSBbXTtcbiAgICAgIHRoaXMucmVuZGVyQXR0YWNobWVudHMoKTtcbiAgICB9O1xuXG4gICAgdGhpcy5lbEF0dGFjaG1lbnRzID0gYXR0YWNoUm93LmNyZWF0ZUVsKFwiZGl2XCIsIHsgdGV4dDogXCJcIiB9KTtcbiAgICB0aGlzLmVsQXR0YWNobWVudHMuc3R5bGUub3BhY2l0eSA9IFwiMC44XCI7XG5cbiAgICB0aGlzLmVsTWVzc2FnZSA9IGNvbnRhaW5lckVsLmNyZWF0ZUVsKFwidGV4dGFyZWFcIik7XG4gICAgdGhpcy5lbE1lc3NhZ2UucGxhY2Vob2xkZXIgPSBcIkFzayBhIHJlc2VhcmNoIHF1ZXN0aW9uXHUyMDI2XCI7XG4gICAgdGhpcy5lbE1lc3NhZ2Uucm93cyA9IDQ7XG4gICAgdGhpcy5lbE1lc3NhZ2Uuc3R5bGUud2lkdGggPSBcIjEwMCVcIjtcbiAgICB0aGlzLmVsTWVzc2FnZS5zdHlsZS5tYXJnaW5Ub3AgPSBcIjhweFwiO1xuXG4gICAgdGhpcy5lbEVycm9yID0gY29udGFpbmVyRWwuY3JlYXRlRWwoXCJkaXZcIiwgeyB0ZXh0OiBcIlwiIH0pO1xuICAgIHRoaXMuZWxFcnJvci5zdHlsZS5tYXJnaW5Ub3AgPSBcIjhweFwiO1xuICAgIHRoaXMuZWxFcnJvci5zdHlsZS5jb2xvciA9IFwidmFyKC0tdGV4dC1lcnJvcilcIjtcblxuICAgIHRoaXMucmVuZGVyQXR0YWNobWVudHMoKTtcbiAgfVxuXG4gIHByaXZhdGUgcmVmcmVzaFJ1bnRpbWUoKTogdm9pZCB7XG4gICAgLy8gS2VlcCBydW50aW1lIGFsaWduZWQgd2l0aCBsYXRlc3Qgc2V0dGluZ3MgKHNpbXBsZSBNVlAgYXBwcm9hY2gpLlxuICAgIHRoaXMuYXBpLnNldEJhc2VVcmwodGhpcy5wbHVnaW4uc2V0dGluZ3MuYmFzZVVybCk7XG4gICAgdGhpcy5hcGkuc2V0VGltZW91dE1zKHRoaXMucGx1Z2luLnNldHRpbmdzLnJlcXVlc3RUaW1lb3V0TXMpO1xuICAgIHRoaXMuc3NlLnNldEJhc2VVcmwodGhpcy5wbHVnaW4uc2V0dGluZ3MuYmFzZVVybCk7XG4gICAgdGhpcy52YXVsdC5zZXRWYXVsdFJvb3QodGhpcy5wbHVnaW4uc2V0dGluZ3MudmF1bHRSb290KTtcbiAgfVxuXG4gIHByaXZhdGUgc2V0RXJyb3IobXNnOiBzdHJpbmcpOiB2b2lkIHtcbiAgICB0aGlzLmxhc3RFcnJvciA9IChtc2cgPz8gXCJcIikudHJpbSgpO1xuICAgIHRoaXMuZWxFcnJvci5zZXRUZXh0KHRoaXMubGFzdEVycm9yKTtcbiAgfVxuXG4gIHByaXZhdGUgcmVuZGVyQXR0YWNobWVudHMoKTogdm9pZCB7XG4gICAgY29uc3QgbiA9IHRoaXMuYXR0YWNobWVudHMubGVuZ3RoO1xuICAgIHRoaXMuZWxBdHRhY2htZW50cy5zZXRUZXh0KG4gPT09IDAgPyBcIkF0dGFjaG1lbnRzOiAobm9uZSlcIiA6IGBBdHRhY2htZW50czogJHtufWApO1xuICB9XG5cbiAgcHJpdmF0ZSBhc3luYyBhdHRhY2hDdXJyZW50Tm90ZUFzeW5jKCk6IFByb21pc2U8dm9pZD4ge1xuICAgIGNvbnN0IGYgPSB0aGlzLmFwcC53b3Jrc3BhY2UuZ2V0QWN0aXZlRmlsZSgpO1xuICAgIGlmICghZikge1xuICAgICAgbmV3IE5vdGljZShcIk5vIGFjdGl2ZSBmaWxlLlwiKTtcbiAgICAgIHJldHVybjtcbiAgICB9XG5cbiAgICBpZiAoIXRoaXMuYXR0YWNobWVudHMuc29tZSgoeCkgPT4geD8ucGF0aCA9PT0gZi5wYXRoKSkge1xuICAgICAgdGhpcy5hdHRhY2htZW50cy5wdXNoKGYpO1xuICAgICAgdGhpcy5yZW5kZXJBdHRhY2htZW50cygpO1xuICAgICAgbmV3IE5vdGljZShgQXR0YWNoZWQ6ICR7Zi5uYW1lfWApO1xuICAgIH1cbiAgfVxuXG4gIHByaXZhdGUgYXN5bmMgcmVhZEF0dGFjaG1lbnRzQXN5bmMoKTogUHJvbWlzZTxBcnJheTx7IGZpbGVuYW1lOiBzdHJpbmc7IGNvbnRlbnRUeXBlOiBzdHJpbmc7IGRhdGE6IFVpbnQ4QXJyYXkgfT4+IHtcbiAgICBjb25zdCBvdXQ6IEFycmF5PHsgZmlsZW5hbWU6IHN0cmluZzsgY29udGVudFR5cGU6IHN0cmluZzsgZGF0YTogVWludDhBcnJheSB9PiA9IFtdO1xuICAgIGZvciAoY29uc3QgZiBvZiB0aGlzLmF0dGFjaG1lbnRzKSB7XG4gICAgICBjb25zdCBuYW1lID0gU3RyaW5nKGY/Lm5hbWUgPz8gXCJmaWxlXCIpLnRyaW0oKSB8fCBcImZpbGVcIjtcbiAgICAgIGNvbnN0IGV4dCA9IG5hbWUudG9Mb3dlckNhc2UoKS5zcGxpdChcIi5cIikucG9wKCkgPz8gXCJcIjtcbiAgICAgIGNvbnN0IGNvbnRlbnRUeXBlID0gZ3Vlc3NDb250ZW50VHlwZShleHQpO1xuICAgICAgY29uc3QgYWIgPSBhd2FpdCB0aGlzLmFwcC52YXVsdC5yZWFkQmluYXJ5KGYpO1xuICAgICAgb3V0LnB1c2goeyBmaWxlbmFtZTogbmFtZSwgY29udGVudFR5cGUsIGRhdGE6IG5ldyBVaW50OEFycmF5KGFiKSB9KTtcbiAgICB9XG4gICAgcmV0dXJuIG91dDtcbiAgfVxuXG4gIHByaXZhdGUgYXN5bmMgcHVsbERlbGl2ZXJhYmxlc0FzeW5jKCk6IFByb21pc2U8dm9pZD4ge1xuICAgIGlmICghdGhpcy5zZXNzaW9uSWQpIHtcbiAgICAgIG5ldyBOb3RpY2UoXCJObyBhY3RpdmUgc2Vzc2lvbi5cIik7XG4gICAgICByZXR1cm47XG4gICAgfVxuXG4gICAgdGhpcy5yZWZyZXNoUnVudGltZSgpO1xuICAgIHRyeSB7XG4gICAgICBjb25zdCBzbmFwID0gYXdhaXQgdGhpcy5hcGkuZ2V0RGVsaXZlcmFibGVzKHRoaXMuc2Vzc2lvbklkKTtcbiAgICAgIGF3YWl0IHRoaXMudmF1bHQud3JpdGVEZWxpdmVyYWJsZXNKc29uKHRoaXMuc2Vzc2lvbklkLCBzbmFwKTtcblxuICAgICAgY29uc3QgYnJpZWZNZCA9IHJlbmRlckJyaWVmTWFya2Rvd24oc25hcD8uYnJpZWYpO1xuICAgICAgY29uc3QgZGVsaXZlcnlNZCA9IHJlbmRlckRlbGl2ZXJ5TWFya2Rvd24oc25hcD8uZGVsaXZlcnkpO1xuICAgICAgYXdhaXQgdGhpcy52YXVsdC53cml0ZURlbGl2ZXJhYmxlc01hcmtkb3duKHRoaXMuc2Vzc2lvbklkLCBicmllZk1kLCBkZWxpdmVyeU1kKTtcblxuICAgICAgbmV3IE5vdGljZShcIkRlbGl2ZXJhYmxlcyB3cml0dGVuIHRvIFZhdWx0L1NSQS5cIik7XG4gICAgfSBjYXRjaCAoZTogYW55KSB7XG4gICAgICB0aGlzLnNldEVycm9yKGU/Lm1lc3NhZ2UgPz8gXCJwdWxsIGRlbGl2ZXJhYmxlcyBmYWlsZWRcIik7XG4gICAgICBuZXcgTm90aWNlKGBQdWxsIGRlbGl2ZXJhYmxlcyBmYWlsZWQ6ICR7dGhpcy5sYXN0RXJyb3J9YCk7XG4gICAgfVxuICB9XG59XG5cbmZ1bmN0aW9uIGV4dHJhY3RSdW5JZChldnQ6IGFueSk6IHN0cmluZyB7XG4gIGNvbnN0IHJpZCA9IChldnQgYXMgYW55KT8ucnVuSWQ7XG4gIGlmICh0eXBlb2YgcmlkID09PSBcInN0cmluZ1wiICYmIHJpZC50cmltKCkpIHJldHVybiByaWQudHJpbSgpO1xuICBjb25zdCByYXcgPSAoZXZ0IGFzIGFueSk/LnZhbHVlPy5ydW5JZDtcbiAgaWYgKHR5cGVvZiByYXcgPT09IFwic3RyaW5nXCIgJiYgcmF3LnRyaW0oKSkgcmV0dXJuIHJhdy50cmltKCk7XG4gIHJldHVybiBcIlwiO1xufVxuXG5mdW5jdGlvbiBndWVzc0NvbnRlbnRUeXBlKGV4dDogc3RyaW5nKTogc3RyaW5nIHtcbiAgY29uc3QgZSA9IChleHQgPz8gXCJcIikudHJpbSgpLnRvTG93ZXJDYXNlKCk7XG4gIGlmIChlID09PSBcIm1kXCIpIHJldHVybiBcInRleHQvbWFya2Rvd25cIjtcbiAgaWYgKGUgPT09IFwidHh0XCIpIHJldHVybiBcInRleHQvcGxhaW5cIjtcbiAgaWYgKGUgPT09IFwianNvblwiKSByZXR1cm4gXCJhcHBsaWNhdGlvbi9qc29uXCI7XG4gIGlmIChlID09PSBcInBkZlwiKSByZXR1cm4gXCJhcHBsaWNhdGlvbi9wZGZcIjtcbiAgaWYgKGUgPT09IFwicG5nXCIpIHJldHVybiBcImltYWdlL3BuZ1wiO1xuICBpZiAoZSA9PT0gXCJqcGdcIiB8fCBlID09PSBcImpwZWdcIikgcmV0dXJuIFwiaW1hZ2UvanBlZ1wiO1xuICBpZiAoZSA9PT0gXCJnaWZcIikgcmV0dXJuIFwiaW1hZ2UvZ2lmXCI7XG4gIGlmIChlID09PSBcIndlYnBcIikgcmV0dXJuIFwiaW1hZ2Uvd2VicFwiO1xuICByZXR1cm4gXCJhcHBsaWNhdGlvbi9vY3RldC1zdHJlYW1cIjtcbn1cblxuXG4iLCAiaW1wb3J0IHsgcmVxdWVzdFVybCB9IGZyb20gXCJvYnNpZGlhblwiO1xuXG5pbXBvcnQgdHlwZSB7XG4gIFNyYUFwaUluZm9SZXNwb25zZSxcbiAgU3JhQ3JlYXRlU2Vzc2lvblJlc3BvbnNlLFxuICBTcmFEZWxpdmVyYWJsZXNSZXNwb25zZSxcbiAgU3JhTGlzdFNlc3Npb25zUmVzcG9uc2UsXG4gIFNyYVNlbmRJbnB1dFJlcXVlc3QsXG4gIFNyYVNlbmRJbnB1dFJlc3BvbnNlLFxufSBmcm9tIFwiLi90eXBlc1wiO1xuaW1wb3J0IHsgU3JhQXBpRXJyb3IgfSBmcm9tIFwiLi90eXBlc1wiO1xuaW1wb3J0IHsgYnVpbGRNdWx0aXBhcnRCb2R5LCB0eXBlIE11bHRpcGFydEZpbGVQYXJ0IH0gZnJvbSBcIi4vbXVsdGlwYXJ0XCI7XG5cbmV4cG9ydCBpbnRlcmZhY2UgU3JhQXBpQ2xpZW50T3B0aW9ucyB7XG4gIGJhc2VVcmw6IHN0cmluZztcbiAgdGltZW91dE1zPzogbnVtYmVyO1xuXG4gIC8vIEZ1dHVyZS1wcm9vZmluZyBmb3IgcmVtb3RlIGRlcGxveW1lbnRzIChhdXRoIGhlYWRlcnMsIGV0Yy4pXG4gIGhlYWRlcnM/OiBSZWNvcmQ8c3RyaW5nLCBzdHJpbmc+IHwgKCgpID0+IFJlY29yZDxzdHJpbmcsIHN0cmluZz4pO1xufVxuXG5leHBvcnQgY2xhc3MgU3JhQXBpQ2xpZW50IHtcbiAgcHJpdmF0ZSBiYXNlVXJsOiBzdHJpbmc7XG4gIHByaXZhdGUgdGltZW91dE1zOiBudW1iZXI7XG4gIHByaXZhdGUgaGVhZGVyczogUmVjb3JkPHN0cmluZywgc3RyaW5nPiB8ICgoKSA9PiBSZWNvcmQ8c3RyaW5nLCBzdHJpbmc+KSB8IHVuZGVmaW5lZDtcblxuICBjb25zdHJ1Y3RvcihvcHRpb25zOiBTcmFBcGlDbGllbnRPcHRpb25zKSB7XG4gICAgdGhpcy5iYXNlVXJsID0gbm9ybWFsaXplQmFzZVVybChvcHRpb25zLmJhc2VVcmwpO1xuICAgIHRoaXMudGltZW91dE1zID0gY2xhbXBUaW1lb3V0KG9wdGlvbnMudGltZW91dE1zKTtcbiAgICB0aGlzLmhlYWRlcnMgPSBvcHRpb25zLmhlYWRlcnM7XG4gIH1cblxuICBzZXRCYXNlVXJsKGJhc2VVcmw6IHN0cmluZykge1xuICAgIHRoaXMuYmFzZVVybCA9IG5vcm1hbGl6ZUJhc2VVcmwoYmFzZVVybCk7XG4gIH1cblxuICBzZXRUaW1lb3V0TXModGltZW91dE1zOiBudW1iZXIpIHtcbiAgICB0aGlzLnRpbWVvdXRNcyA9IGNsYW1wVGltZW91dCh0aW1lb3V0TXMpO1xuICB9XG5cbiAgYXN5bmMgaGVhbHRoKCk6IFByb21pc2U8eyBvazogYm9vbGVhbjsgc3RhdHVzOiBudW1iZXI7IGJvZHk6IHN0cmluZyB9PiB7XG4gICAgY29uc3QgdXJsID0gdGhpcy51cmwoXCIvaGVhbHRoXCIpO1xuICAgIGNvbnN0IHJlcyA9IGF3YWl0IHRoaXMucmVxdWVzdFRleHQoXCJHRVRcIiwgdXJsKTtcbiAgICByZXR1cm4geyBvazogcmVzLnN0YXR1cyA+PSAyMDAgJiYgcmVzLnN0YXR1cyA8IDMwMCwgc3RhdHVzOiByZXMuc3RhdHVzLCBib2R5OiByZXMudGV4dCB9O1xuICB9XG5cbiAgYXN5bmMgaW5mbygpOiBQcm9taXNlPFNyYUFwaUluZm9SZXNwb25zZT4ge1xuICAgIGNvbnN0IHVybCA9IHRoaXMudXJsKFwiL2FwaS9pbmZvXCIpO1xuICAgIHJldHVybiBhd2FpdCB0aGlzLnJlcXVlc3RKc29uPFNyYUFwaUluZm9SZXNwb25zZT4oXCJHRVRcIiwgdXJsKTtcbiAgfVxuXG4gIGFzeW5jIGNyZWF0ZVNlc3Npb24ocHJvdmlkZXJOYW1lPzogc3RyaW5nKTogUHJvbWlzZTx7IHNlc3Npb25JZDogc3RyaW5nIH0+IHtcbiAgICBjb25zdCB1cmwgPSB0aGlzLnVybChcIi9hcGkvc2Vzc2lvbnNcIik7XG4gICAgY29uc3QgYm9keSA9IHByb3ZpZGVyTmFtZSA/IHsgcHJvdmlkZXJOYW1lIH0gOiB7fTtcbiAgICBjb25zdCBqc29uID0gYXdhaXQgdGhpcy5yZXF1ZXN0SnNvbjxTcmFDcmVhdGVTZXNzaW9uUmVzcG9uc2U+KFwiUE9TVFwiLCB1cmwsIGJvZHkpO1xuICAgIGlmICghanNvbj8uc2Vzc2lvbklkKSB0aHJvdyBuZXcgU3JhQXBpRXJyb3IoXCJJbnZhbGlkIHJlc3BvbnNlOiBtaXNzaW5nIHNlc3Npb25JZFwiLCB7IHN0YXR1czogMCwgdXJsLCByZXNwb25zZVRleHQ6IFwiXCIgfSk7XG4gICAgcmV0dXJuIHsgc2Vzc2lvbklkOiBTdHJpbmcoanNvbi5zZXNzaW9uSWQpIH07XG4gIH1cblxuICBhc3luYyBsaXN0U2Vzc2lvbnMoKTogUHJvbWlzZTxTcmFMaXN0U2Vzc2lvbnNSZXNwb25zZT4ge1xuICAgIGNvbnN0IHVybCA9IHRoaXMudXJsKFwiL2FwaS9zZXNzaW9uc1wiKTtcbiAgICBjb25zdCBqc29uID0gYXdhaXQgdGhpcy5yZXF1ZXN0SnNvbjxhbnk+KFwiR0VUXCIsIHVybCk7XG4gICAgcmV0dXJuIHtcbiAgICAgIGNvdW50OiBOdW1iZXIoanNvbj8uY291bnQgPz8gMCkgfHwgMCxcbiAgICAgIHNlc3Npb25zOiBBcnJheS5pc0FycmF5KGpzb24/LnNlc3Npb25zKSA/IChqc29uLnNlc3Npb25zIGFzIGFueVtdKSA6IFtdLFxuICAgIH07XG4gIH1cblxuICBhc3luYyBzZW5kSW5wdXQoc2Vzc2lvbklkOiBzdHJpbmcsIGlucHV0OiBTcmFTZW5kSW5wdXRSZXF1ZXN0KTogUHJvbWlzZTx7IHJ1bklkOiBzdHJpbmcgfT4ge1xuICAgIGNvbnN0IHNpZCA9IFN0cmluZyhzZXNzaW9uSWQgPz8gXCJcIikudHJpbSgpO1xuICAgIGlmICghc2lkKSB0aHJvdyBuZXcgRXJyb3IoXCJzZXNzaW9uSWQgaXMgcmVxdWlyZWRcIik7XG4gICAgaWYgKCFpbnB1dD8ubWVzc2FnZSB8fCAhU3RyaW5nKGlucHV0Lm1lc3NhZ2UpLnRyaW0oKSkgdGhyb3cgbmV3IEVycm9yKFwibWVzc2FnZSBpcyByZXF1aXJlZFwiKTtcblxuICAgIGNvbnN0IHVybCA9IHRoaXMudXJsKGAvYXBpL3Nlc3Npb25zLyR7ZW5jb2RlVVJJQ29tcG9uZW50KHNpZCl9L2lucHV0YCk7XG4gICAgY29uc3QganNvbiA9IGF3YWl0IHRoaXMucmVxdWVzdEpzb248U3JhU2VuZElucHV0UmVzcG9uc2U+KFwiUE9TVFwiLCB1cmwsIGlucHV0KTtcbiAgICBpZiAoIWpzb24/LnJ1bklkKSB0aHJvdyBuZXcgU3JhQXBpRXJyb3IoXCJJbnZhbGlkIHJlc3BvbnNlOiBtaXNzaW5nIHJ1bklkXCIsIHsgc3RhdHVzOiAwLCB1cmwsIHJlc3BvbnNlVGV4dDogXCJcIiB9KTtcbiAgICByZXR1cm4geyBydW5JZDogU3RyaW5nKGpzb24ucnVuSWQpIH07XG4gIH1cblxuICBhc3luYyBnZXREZWxpdmVyYWJsZXMoc2Vzc2lvbklkOiBzdHJpbmcpOiBQcm9taXNlPFNyYURlbGl2ZXJhYmxlc1Jlc3BvbnNlPiB7XG4gICAgY29uc3Qgc2lkID0gU3RyaW5nKHNlc3Npb25JZCA/PyBcIlwiKS50cmltKCk7XG4gICAgaWYgKCFzaWQpIHRocm93IG5ldyBFcnJvcihcInNlc3Npb25JZCBpcyByZXF1aXJlZFwiKTtcblxuICAgIGNvbnN0IHVybCA9IHRoaXMudXJsKGAvYXBpL3Nlc3Npb25zLyR7ZW5jb2RlVVJJQ29tcG9uZW50KHNpZCl9L2RlbGl2ZXJhYmxlc2ApO1xuICAgIHJldHVybiBhd2FpdCB0aGlzLnJlcXVlc3RKc29uPFNyYURlbGl2ZXJhYmxlc1Jlc3BvbnNlPihcIkdFVFwiLCB1cmwpO1xuICB9XG5cbiAgYXN5bmMgdXBsb2FkQXR0YWNobWVudHMoXG4gICAgc2Vzc2lvbklkOiBzdHJpbmcsXG4gICAgZmlsZXM6IEFycmF5PHsgZmlsZW5hbWU6IHN0cmluZzsgY29udGVudFR5cGU6IHN0cmluZzsgZGF0YTogVWludDhBcnJheSB9PixcbiAgKTogUHJvbWlzZTx7IGF0dGFjaG1lbnRQYXRoczogc3RyaW5nW10gfT4ge1xuICAgIGNvbnN0IHNpZCA9IFN0cmluZyhzZXNzaW9uSWQgPz8gXCJcIikudHJpbSgpO1xuICAgIGlmICghc2lkKSB0aHJvdyBuZXcgRXJyb3IoXCJzZXNzaW9uSWQgaXMgcmVxdWlyZWRcIik7XG5cbiAgICBjb25zdCB1cmwgPSB0aGlzLnVybChgL2FwaS9zZXNzaW9ucy8ke2VuY29kZVVSSUNvbXBvbmVudChzaWQpfS91cGxvYWRzYCk7XG4gICAgY29uc3QgcGFydHM6IE11bHRpcGFydEZpbGVQYXJ0W10gPSAoZmlsZXMgPz8gW10pLm1hcCgoZikgPT4gKHtcbiAgICAgIGZpZWxkTmFtZTogXCJmaWxlXCIsXG4gICAgICBmaWxlbmFtZTogZi5maWxlbmFtZSxcbiAgICAgIGNvbnRlbnRUeXBlOiBmLmNvbnRlbnRUeXBlLFxuICAgICAgZGF0YTogZi5kYXRhLFxuICAgIH0pKTtcblxuICAgIGNvbnN0IG1wID0gYnVpbGRNdWx0aXBhcnRCb2R5KHBhcnRzKTtcbiAgICBjb25zdCByZXMgPSBhd2FpdCByZXF1ZXN0VXJsKHtcbiAgICAgIHVybCxcbiAgICAgIG1ldGhvZDogXCJQT1NUXCIsXG4gICAgICBoZWFkZXJzOiB0aGlzLnJlc29sdmVIZWFkZXJzKG1wLmNvbnRlbnRUeXBlKSBhcyBhbnksXG4gICAgICBib2R5OiBtcC5ib2R5IGFzIGFueSxcbiAgICAgIHRpbWVvdXQ6IHRoaXMudGltZW91dE1zIGFzIGFueSxcbiAgICAgIHRocm93OiBmYWxzZSxcbiAgICB9IGFzIGFueSk7XG5cbiAgICBjb25zdCBzdGF0dXMgPSBOdW1iZXIoKHJlcyBhcyBhbnkpPy5zdGF0dXMgPz8gMCkgfHwgMDtcbiAgICBjb25zdCB0ZXh0ID0gdHlwZW9mIChyZXMgYXMgYW55KT8udGV4dCA9PT0gXCJzdHJpbmdcIiA/ICgocmVzIGFzIGFueSkudGV4dCBhcyBzdHJpbmcpIDogXCJcIjtcbiAgICBpZiAoc3RhdHVzIDwgMjAwIHx8IHN0YXR1cyA+PSAzMDApIHtcbiAgICAgIHRocm93IG5ldyBTcmFBcGlFcnJvcihgSFRUUCAke3N0YXR1c31gLCB7IHN0YXR1cywgdXJsLCByZXNwb25zZVRleHQ6IHRyaW1Gb3JVaSh0ZXh0KSB9KTtcbiAgICB9XG5cbiAgICBsZXQganNvbjogYW55ID0gbnVsbDtcbiAgICB0cnkge1xuICAgICAganNvbiA9IChyZXMgYXMgYW55KT8uanNvbiA/PyBKU09OLnBhcnNlKHRleHQpO1xuICAgIH0gY2F0Y2gge1xuICAgICAgLy8gaWdub3JlXG4gICAgfVxuXG4gICAgY29uc3QgcGF0aHMgPSBBcnJheS5pc0FycmF5KGpzb24/LmF0dGFjaG1lbnRQYXRocykgPyBqc29uLmF0dGFjaG1lbnRQYXRocyA6IFtdO1xuICAgIHJldHVybiB7IGF0dGFjaG1lbnRQYXRoczogcGF0aHMubWFwKCh4OiBhbnkpID0+IFN0cmluZyh4ID8/IFwiXCIpLnRyaW0oKSkuZmlsdGVyKCh4OiBzdHJpbmcpID0+IHgubGVuZ3RoID4gMCkgfTtcbiAgfVxuXG4gIC8vID09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PVxuICAvLyAgSW50ZXJuYWxzXG4gIC8vID09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PVxuXG4gIHByaXZhdGUgdXJsKHBhdGg6IHN0cmluZyk6IHN0cmluZyB7XG4gICAgY29uc3QgcCA9IFN0cmluZyhwYXRoID8/IFwiXCIpLnRyaW0oKTtcbiAgICBpZiAoIXAuc3RhcnRzV2l0aChcIi9cIikpIHJldHVybiBgJHt0aGlzLmJhc2VVcmx9LyR7cH1gO1xuICAgIHJldHVybiBgJHt0aGlzLmJhc2VVcmx9JHtwfWA7XG4gIH1cblxuICBwcml2YXRlIHJlc29sdmVIZWFkZXJzKGNvbnRlbnRUeXBlPzogc3RyaW5nKTogUmVjb3JkPHN0cmluZywgc3RyaW5nPiB7XG4gICAgY29uc3QgaCA9IHR5cGVvZiB0aGlzLmhlYWRlcnMgPT09IFwiZnVuY3Rpb25cIiA/IHRoaXMuaGVhZGVycygpIDogdGhpcy5oZWFkZXJzO1xuICAgIGNvbnN0IG91dDogUmVjb3JkPHN0cmluZywgc3RyaW5nPiA9IHsgLi4uKGggPz8ge30pIH07XG4gICAgaWYgKGNvbnRlbnRUeXBlKSBvdXRbXCJDb250ZW50LVR5cGVcIl0gPSBjb250ZW50VHlwZTtcbiAgICByZXR1cm4gb3V0O1xuICB9XG5cbiAgcHJpdmF0ZSBhc3luYyByZXF1ZXN0VGV4dChtZXRob2Q6IHN0cmluZywgdXJsOiBzdHJpbmcsIGJvZHk/OiBhbnkpOiBQcm9taXNlPHsgc3RhdHVzOiBudW1iZXI7IHRleHQ6IHN0cmluZyB9PiB7XG4gICAgY29uc3QgbSA9IFN0cmluZyhtZXRob2QgPz8gXCJHRVRcIikudG9VcHBlckNhc2UoKTtcbiAgICBjb25zdCB0aW1lb3V0ID0gdGhpcy50aW1lb3V0TXM7XG5cbiAgICBjb25zdCByZXMgPSBhd2FpdCByZXF1ZXN0VXJsKHtcbiAgICAgIHVybCxcbiAgICAgIG1ldGhvZDogbSxcbiAgICAgIGhlYWRlcnM6IHRoaXMucmVzb2x2ZUhlYWRlcnModHlwZW9mIGJvZHkgPT09IFwic3RyaW5nXCIgPyBcImFwcGxpY2F0aW9uL2pzb25cIiA6IHVuZGVmaW5lZCkgYXMgYW55LFxuICAgICAgYm9keTogYm9keSA/ICh0eXBlb2YgYm9keSA9PT0gXCJzdHJpbmdcIiA/IGJvZHkgOiBKU09OLnN0cmluZ2lmeShib2R5KSkgOiB1bmRlZmluZWQsXG4gICAgICB0aW1lb3V0OiB0aW1lb3V0IGFzIGFueSxcbiAgICAgIHRocm93OiBmYWxzZSxcbiAgICB9IGFzIGFueSk7XG5cbiAgICBjb25zdCB0ZXh0ID0gdHlwZW9mIChyZXMgYXMgYW55KT8udGV4dCA9PT0gXCJzdHJpbmdcIiA/ICgocmVzIGFzIGFueSkudGV4dCBhcyBzdHJpbmcpIDogXCJcIjtcbiAgICBjb25zdCBzdGF0dXMgPSBOdW1iZXIoKHJlcyBhcyBhbnkpPy5zdGF0dXMgPz8gMCkgfHwgMDtcblxuICAgIGlmIChzdGF0dXMgPCAyMDAgfHwgc3RhdHVzID49IDMwMCkge1xuICAgICAgdGhyb3cgbmV3IFNyYUFwaUVycm9yKGBIVFRQICR7c3RhdHVzfWAsIHsgc3RhdHVzLCB1cmwsIHJlc3BvbnNlVGV4dDogdHJpbUZvclVpKHRleHQpIH0pO1xuICAgIH1cblxuICAgIHJldHVybiB7IHN0YXR1cywgdGV4dCB9O1xuICB9XG5cbiAgcHJpdmF0ZSBhc3luYyByZXF1ZXN0SnNvbjxUPihtZXRob2Q6IHN0cmluZywgdXJsOiBzdHJpbmcsIGJvZHk/OiBhbnkpOiBQcm9taXNlPFQ+IHtcbiAgICBjb25zdCBtID0gU3RyaW5nKG1ldGhvZCA/PyBcIkdFVFwiKS50b1VwcGVyQ2FzZSgpO1xuICAgIGNvbnN0IHRpbWVvdXQgPSB0aGlzLnRpbWVvdXRNcztcblxuICAgIGNvbnN0IHJlcyA9IGF3YWl0IHJlcXVlc3RVcmwoe1xuICAgICAgdXJsLFxuICAgICAgbWV0aG9kOiBtLFxuICAgICAgaGVhZGVyczogdGhpcy5yZXNvbHZlSGVhZGVycyhib2R5ICE9PSB1bmRlZmluZWQgPyBcImFwcGxpY2F0aW9uL2pzb25cIiA6IHVuZGVmaW5lZCkgYXMgYW55LFxuICAgICAgYm9keTogYm9keSAhPT0gdW5kZWZpbmVkID8gSlNPTi5zdHJpbmdpZnkoYm9keSkgOiB1bmRlZmluZWQsXG4gICAgICB0aW1lb3V0OiB0aW1lb3V0IGFzIGFueSxcbiAgICAgIHRocm93OiBmYWxzZSxcbiAgICB9IGFzIGFueSk7XG5cbiAgICBjb25zdCBzdGF0dXMgPSBOdW1iZXIoKHJlcyBhcyBhbnkpPy5zdGF0dXMgPz8gMCkgfHwgMDtcbiAgICBjb25zdCB0ZXh0ID0gdHlwZW9mIChyZXMgYXMgYW55KT8udGV4dCA9PT0gXCJzdHJpbmdcIiA/ICgocmVzIGFzIGFueSkudGV4dCBhcyBzdHJpbmcpIDogXCJcIjtcblxuICAgIGlmIChzdGF0dXMgPCAyMDAgfHwgc3RhdHVzID49IDMwMCkge1xuICAgICAgdGhyb3cgbmV3IFNyYUFwaUVycm9yKGBIVFRQICR7c3RhdHVzfWAsIHsgc3RhdHVzLCB1cmwsIHJlc3BvbnNlVGV4dDogdHJpbUZvclVpKHRleHQpIH0pO1xuICAgIH1cblxuICAgIHRyeSB7XG4gICAgICBjb25zdCBqc29uID0gKHJlcyBhcyBhbnkpPy5qc29uO1xuICAgICAgaWYgKGpzb24gJiYgdHlwZW9mIGpzb24gPT09IFwib2JqZWN0XCIpIHJldHVybiBqc29uIGFzIFQ7XG4gICAgfSBjYXRjaCB7XG4gICAgICAvLyBpZ25vcmVcbiAgICB9XG5cbiAgICB0cnkge1xuICAgICAgcmV0dXJuIEpTT04ucGFyc2UodGV4dCkgYXMgVDtcbiAgICB9IGNhdGNoIHtcbiAgICAgIHRocm93IG5ldyBTcmFBcGlFcnJvcihcIkludmFsaWQgSlNPTiByZXNwb25zZVwiLCB7IHN0YXR1cywgdXJsLCByZXNwb25zZVRleHQ6IHRyaW1Gb3JVaSh0ZXh0KSB9KTtcbiAgICB9XG4gIH1cbn1cblxuZnVuY3Rpb24gbm9ybWFsaXplQmFzZVVybChpbnB1dDogc3RyaW5nKTogc3RyaW5nIHtcbiAgY29uc3QgcyA9IChpbnB1dCA/PyBcIlwiKS50cmltKCk7XG4gIHJldHVybiBzLnJlcGxhY2UoL1xcLyskLywgXCJcIik7XG59XG5cbmZ1bmN0aW9uIGNsYW1wVGltZW91dChtcz86IG51bWJlcik6IG51bWJlciB7XG4gIGNvbnN0IG4gPSBOdW1iZXIuaXNGaW5pdGUobXMgYXMgYW55KSA/IChtcyBhcyBudW1iZXIpIDogMTUwMDA7XG4gIHJldHVybiBNYXRoLm1heCgxMDAwLCBNYXRoLm1pbigxMjBfMDAwLCBuKSk7XG59XG5cbmZ1bmN0aW9uIHRyaW1Gb3JVaSh0ZXh0OiBzdHJpbmcsIG1heCA9IDgwMCk6IHN0cmluZyB7XG4gIGNvbnN0IHMgPSAodGV4dCA/PyBcIlwiKS50cmltKCk7XG4gIHJldHVybiBzLmxlbmd0aCA8PSBtYXggPyBzIDogcy5zbGljZSgwLCBtYXgpICsgXCJcdTIwMjZcIjtcbn1cblxuXG4iLCAiLy8gPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09XG4vLyAgU1JBIEhUVFAgQVBJIHR5cGVzIChwbHVnaW4tc2lkZSlcbi8vXG4vLyAgTm90ZXM6XG4vLyAgLSBUaGlzIGlzIE5PVCBhIGNyb3NzLXJ1bnRpbWUgYm91bmRhcnkgY29udHJhY3QgaW5zaWRlIEFldmF0YXIuXG4vLyAgLSBXZSBrZWVwIHRoZXNlIHR5cGVzIG1pbmltYWwgYW5kIHJlc2lsaWVudCB0byBiYWNrZW5kIGNoYW5nZXMuXG4vLyA9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT1cblxuZXhwb3J0IGludGVyZmFjZSBTcmFTZXNzaW9uU3VtbWFyeSB7XG4gIHNlc3Npb25JZDogc3RyaW5nO1xuICBjcmVhdGVkQXQ6IHN0cmluZzsgLy8gSVNPIHN0cmluZ1xuICBwcm92aWRlck5hbWU/OiBzdHJpbmcgfCBudWxsO1xufVxuXG5leHBvcnQgaW50ZXJmYWNlIFNyYUxpc3RTZXNzaW9uc1Jlc3BvbnNlIHtcbiAgY291bnQ6IG51bWJlcjtcbiAgc2Vzc2lvbnM6IFNyYVNlc3Npb25TdW1tYXJ5W107XG59XG5cbmV4cG9ydCBpbnRlcmZhY2UgU3JhQ3JlYXRlU2Vzc2lvblJlc3BvbnNlIHtcbiAgb2s6IGJvb2xlYW47XG4gIHNlc3Npb25JZDogc3RyaW5nO1xufVxuXG5leHBvcnQgaW50ZXJmYWNlIFNyYVNlbmRJbnB1dFJlcXVlc3Qge1xuICBtZXNzYWdlOiBzdHJpbmc7XG4gIHJlcXVlc3RJZD86IHN0cmluZztcbiAgcHJvdmlkZXJOYW1lPzogc3RyaW5nO1xuICBtb2RlPzogXCJjaGF0XCIgfCBcInZpYmVcIiB8IFwidmliZV9sb29wXCIgfCBzdHJpbmc7XG4gIGxvb3A/OiB7XG4gICAgbWF4SXRlcmF0aW9ucz86IG51bWJlcjtcbiAgICBtYXhUb3RhbER1cmF0aW9uTXM/OiBudW1iZXI7XG4gIH07XG4gIHRvQWdlbnRzPzogc3RyaW5nW107XG4gIGF0dGFjaG1lbnRQYXRocz86IHN0cmluZ1tdO1xufVxuXG5leHBvcnQgaW50ZXJmYWNlIFNyYVNlbmRJbnB1dFJlc3BvbnNlIHtcbiAgb2s6IGJvb2xlYW47XG4gIHNlc3Npb25JZDogc3RyaW5nO1xuICBydW5JZDogc3RyaW5nO1xufVxuXG5leHBvcnQgaW50ZXJmYWNlIFNyYURlbGl2ZXJhYmxlc1Jlc3BvbnNlIHtcbiAgb2s6IGJvb2xlYW47XG4gIHNlc3Npb25JZDogc3RyaW5nO1xuICBicmllZj86IGFueTtcbiAgZGVsaXZlcnk/OiBhbnk7XG59XG5cbmV4cG9ydCBpbnRlcmZhY2UgU3JhQXBpSW5mb1Jlc3BvbnNlIHtcbiAgc3lzdGVtPzogYW55O1xuICBsbG0/OiBhbnk7XG4gIG1jcD86IGFueTtcbn1cblxuZXhwb3J0IGNsYXNzIFNyYUFwaUVycm9yIGV4dGVuZHMgRXJyb3Ige1xuICByZWFkb25seSBzdGF0dXM6IG51bWJlcjtcbiAgcmVhZG9ubHkgdXJsOiBzdHJpbmc7XG4gIHJlYWRvbmx5IHJlc3BvbnNlVGV4dDogc3RyaW5nO1xuXG4gIGNvbnN0cnVjdG9yKG1lc3NhZ2U6IHN0cmluZywgYXJnczogeyBzdGF0dXM6IG51bWJlcjsgdXJsOiBzdHJpbmc7IHJlc3BvbnNlVGV4dDogc3RyaW5nIH0pIHtcbiAgICBzdXBlcihtZXNzYWdlKTtcbiAgICB0aGlzLm5hbWUgPSBcIlNyYUFwaUVycm9yXCI7XG4gICAgdGhpcy5zdGF0dXMgPSBhcmdzLnN0YXR1cztcbiAgICB0aGlzLnVybCA9IGFyZ3MudXJsO1xuICAgIHRoaXMucmVzcG9uc2VUZXh0ID0gYXJncy5yZXNwb25zZVRleHQ7XG4gIH1cbn1cblxuXG4iLCAiLy8gPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09XG4vLyAgTWluaW1hbCBtdWx0aXBhcnQvZm9ybS1kYXRhIGJ1aWxkZXIgKGJyb3dzZXIvTm9kZSlcbi8vXG4vLyAgR29hbDpcbi8vICAtIFVwbG9hZCB2YXVsdCBmaWxlcyB0byBTUkEgL3VwbG9hZHMgZW5kcG9pbnQgKG11bHRpcGFydC9mb3JtLWRhdGEpXG4vLyAgLSBBdm9pZCBoZWF2eSBkZXBlbmRlbmNpZXM7IGtlZXAgaXQgZGV0ZXJtaW5pc3RpYyBhbmQgYm91bmRlZC5cbi8vID09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PVxuXG5leHBvcnQgaW50ZXJmYWNlIE11bHRpcGFydEZpbGVQYXJ0IHtcbiAgZmllbGROYW1lOiBzdHJpbmc7IC8vIGUuZy4sIFwiZmlsZVwiXG4gIGZpbGVuYW1lOiBzdHJpbmc7XG4gIGNvbnRlbnRUeXBlOiBzdHJpbmc7XG4gIGRhdGE6IFVpbnQ4QXJyYXk7XG59XG5cbmV4cG9ydCBpbnRlcmZhY2UgTXVsdGlwYXJ0Qm9keSB7XG4gIGNvbnRlbnRUeXBlOiBzdHJpbmc7XG4gIGJvZHk6IEFycmF5QnVmZmVyO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gYnVpbGRNdWx0aXBhcnRCb2R5KGZpbGVzOiBNdWx0aXBhcnRGaWxlUGFydFtdLCBib3VuZGFyeT86IHN0cmluZyk6IE11bHRpcGFydEJvZHkge1xuICBjb25zdCBsaXN0ID0gQXJyYXkuaXNBcnJheShmaWxlcykgPyBmaWxlcyA6IFtdO1xuICBpZiAobGlzdC5sZW5ndGggPT09IDApIHRocm93IG5ldyBFcnJvcihcIm5vIGZpbGVzXCIpO1xuXG4gIGNvbnN0IGIgPSBib3VuZGFyeSAmJiBib3VuZGFyeS50cmltKCkgPyBib3VuZGFyeS50cmltKCkgOiByYW5kb21Cb3VuZGFyeSgpO1xuICBjb25zdCBlbmMgPSBuZXcgVGV4dEVuY29kZXIoKTtcblxuICBjb25zdCBjaHVua3M6IFVpbnQ4QXJyYXlbXSA9IFtdO1xuXG4gIGZvciAoY29uc3QgZiBvZiBsaXN0KSB7XG4gICAgY29uc3QgZmllbGROYW1lID0gKGY/LmZpZWxkTmFtZSA/PyBcImZpbGVcIikudHJpbSgpIHx8IFwiZmlsZVwiO1xuICAgIGNvbnN0IGZpbGVuYW1lID0gc2FuaXRpemVGaWxlbmFtZSgoZj8uZmlsZW5hbWUgPz8gXCJmaWxlXCIpLnRyaW0oKSB8fCBcImZpbGVcIik7XG4gICAgY29uc3QgY29udGVudFR5cGUgPSAoZj8uY29udGVudFR5cGUgPz8gXCJhcHBsaWNhdGlvbi9vY3RldC1zdHJlYW1cIikudHJpbSgpIHx8IFwiYXBwbGljYXRpb24vb2N0ZXQtc3RyZWFtXCI7XG4gICAgY29uc3QgZGF0YSA9IGY/LmRhdGEgaW5zdGFuY2VvZiBVaW50OEFycmF5ID8gZi5kYXRhIDogbmV3IFVpbnQ4QXJyYXkoKTtcblxuICAgIGNodW5rcy5wdXNoKGVuYy5lbmNvZGUoYC0tJHtifVxcclxcbmApKTtcbiAgICBjaHVua3MucHVzaChcbiAgICAgIGVuYy5lbmNvZGUoXG4gICAgICAgIGBDb250ZW50LURpc3Bvc2l0aW9uOiBmb3JtLWRhdGE7IG5hbWU9XCIke2VzY2FwZVF1b3RlcyhmaWVsZE5hbWUpfVwiOyBmaWxlbmFtZT1cIiR7ZXNjYXBlUXVvdGVzKGZpbGVuYW1lKX1cIlxcclxcbmAsXG4gICAgICApLFxuICAgICk7XG4gICAgY2h1bmtzLnB1c2goZW5jLmVuY29kZShgQ29udGVudC1UeXBlOiAke2NvbnRlbnRUeXBlfVxcclxcblxcclxcbmApKTtcbiAgICBjaHVua3MucHVzaChkYXRhKTtcbiAgICBjaHVua3MucHVzaChlbmMuZW5jb2RlKFwiXFxyXFxuXCIpKTtcbiAgfVxuXG4gIGNodW5rcy5wdXNoKGVuYy5lbmNvZGUoYC0tJHtifS0tXFxyXFxuYCkpO1xuXG4gIGNvbnN0IG1lcmdlZCA9IGNvbmNhdEJ5dGVzKGNodW5rcyk7XG4gIHJldHVybiB7XG4gICAgY29udGVudFR5cGU6IGBtdWx0aXBhcnQvZm9ybS1kYXRhOyBib3VuZGFyeT0ke2J9YCxcbiAgICBib2R5OiB0b0FycmF5QnVmZmVyKG1lcmdlZCksXG4gIH07XG59XG5cbmZ1bmN0aW9uIHJhbmRvbUJvdW5kYXJ5KCk6IHN0cmluZyB7XG4gIC8vIFNtYWxsIGFuZCBzYWZlOiBhdm9pZCBodWdlIGhhc2hlcy5cbiAgY29uc3Qgcm5kID0gTWF0aC5mbG9vcihNYXRoLnJhbmRvbSgpICogMV8wMDBfMDAwXzAwMCkudG9TdHJpbmcoMTYpO1xuICByZXR1cm4gYC0tLS1hZXZhdGFyLXNyYS0ke0RhdGUubm93KCkudG9TdHJpbmcoMTYpfS0ke3JuZH1gO1xufVxuXG5mdW5jdGlvbiBjb25jYXRCeXRlcyhjaHVua3M6IFVpbnQ4QXJyYXlbXSk6IFVpbnQ4QXJyYXkge1xuICBsZXQgdG90YWwgPSAwO1xuICBmb3IgKGNvbnN0IGMgb2YgY2h1bmtzKSB0b3RhbCArPSBjLmJ5dGVMZW5ndGg7XG5cbiAgY29uc3Qgb3V0ID0gbmV3IFVpbnQ4QXJyYXkodG90YWwpO1xuICBsZXQgb2Zmc2V0ID0gMDtcbiAgZm9yIChjb25zdCBjIG9mIGNodW5rcykge1xuICAgIG91dC5zZXQoYywgb2Zmc2V0KTtcbiAgICBvZmZzZXQgKz0gYy5ieXRlTGVuZ3RoO1xuICB9XG4gIHJldHVybiBvdXQ7XG59XG5cbmZ1bmN0aW9uIHRvQXJyYXlCdWZmZXIodTg6IFVpbnQ4QXJyYXkpOiBBcnJheUJ1ZmZlciB7XG4gIC8vIFNsaWNlIHRvIGF2b2lkIGV4cG9zaW5nIGEgbGFyZ2VyIHVuZGVybHlpbmcgYnVmZmVyLlxuICByZXR1cm4gdTguYnVmZmVyLnNsaWNlKHU4LmJ5dGVPZmZzZXQsIHU4LmJ5dGVPZmZzZXQgKyB1OC5ieXRlTGVuZ3RoKTtcbn1cblxuZnVuY3Rpb24gZXNjYXBlUXVvdGVzKHM6IHN0cmluZyk6IHN0cmluZyB7XG4gIHJldHVybiAocyA/PyBcIlwiKS5yZXBsYWNlKC9cIi9nLCAnXFxcXFwiJyk7XG59XG5cbmZ1bmN0aW9uIHNhbml0aXplRmlsZW5hbWUobmFtZTogc3RyaW5nKTogc3RyaW5nIHtcbiAgY29uc3QgcyA9IChuYW1lID8/IFwiXCIpLnRyaW0oKTtcbiAgaWYgKCFzKSByZXR1cm4gXCJmaWxlXCI7XG4gIC8vIEtlZXAgaXQgc2ltcGxlOyByZW1vdmUgcGF0aCBzZXBhcmF0b3JzLlxuICByZXR1cm4gcy5yZXBsYWNlKC9bXFxcXC9dL2csIFwiX1wiKTtcbn1cblxuXG4iLCAiLy8gPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09XG4vLyAgU1NFIHBhcnNlciAoTm9kZS1mcmllbmRseSlcbi8vXG4vLyAgUGFyc2VzIFNlcnZlci1TZW50IEV2ZW50cyBzdHJlYW1zIGludG8gYGRhdGE6YCBwYXlsb2FkIGJsb2Nrcy5cbi8vICAtIFN1cHBvcnRzIGBcXG5cXG5gIGFuZCBgXFxyXFxuXFxyXFxuYCBkZWxpbWl0ZXJzXG4vLyAgLSBTdXBwb3J0cyBtdWx0aS1saW5lIGBkYXRhOmAgKGpvaW5lZCB3aXRoICdcXG4nKVxuLy8gPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09XG5cbmV4cG9ydCBjbGFzcyBTc2VQYXJzZXIge1xuICBwcml2YXRlIGJ1ZmZlciA9IFwiXCI7XG5cbiAgcHVzaChjaHVuazogc3RyaW5nKTogc3RyaW5nW10ge1xuICAgIGlmICghY2h1bmspIHJldHVybiBbXTtcblxuICAgIHRoaXMuYnVmZmVyICs9IGNodW5rO1xuICAgIC8vIE5vcm1hbGl6ZSBDUkxGIHRvIExGIGZvciBlYXNpZXIgcGFyc2luZy5cbiAgICB0aGlzLmJ1ZmZlciA9IHRoaXMuYnVmZmVyLnJlcGxhY2UoL1xcclxcbi9nLCBcIlxcblwiKTtcblxuICAgIGNvbnN0IG91dDogc3RyaW5nW10gPSBbXTtcbiAgICB3aGlsZSAodHJ1ZSkge1xuICAgICAgY29uc3QgaWR4ID0gdGhpcy5idWZmZXIuaW5kZXhPZihcIlxcblxcblwiKTtcbiAgICAgIGlmIChpZHggPCAwKSBicmVhaztcblxuICAgICAgY29uc3QgZnJhbWUgPSB0aGlzLmJ1ZmZlci5zbGljZSgwLCBpZHgpO1xuICAgICAgdGhpcy5idWZmZXIgPSB0aGlzLmJ1ZmZlci5zbGljZShpZHggKyAyKTtcblxuICAgICAgY29uc3QgZGF0YSA9IGV4dHJhY3REYXRhKGZyYW1lKTtcbiAgICAgIGlmIChkYXRhLmxlbmd0aCA+IDApIG91dC5wdXNoKGRhdGEpO1xuICAgIH1cblxuICAgIHJldHVybiBvdXQ7XG4gIH1cblxuICBmbHVzaCgpOiBzdHJpbmdbXSB7XG4gICAgLy8gSWYgdGhlIHN0cmVhbSBlbmRzIHdpdGhvdXQgZGVsaW1pdGVyLCBiZXN0LWVmZm9ydCBwYXJzZSByZW1haW5kZXIuXG4gICAgaWYgKCF0aGlzLmJ1ZmZlcikgcmV0dXJuIFtdO1xuICAgIGNvbnN0IHJlc3QgPSB0aGlzLmJ1ZmZlcjtcbiAgICB0aGlzLmJ1ZmZlciA9IFwiXCI7XG4gICAgY29uc3QgZGF0YSA9IGV4dHJhY3REYXRhKHJlc3QucmVwbGFjZSgvXFxyXFxuL2csIFwiXFxuXCIpKTtcbiAgICByZXR1cm4gZGF0YS5sZW5ndGggPiAwID8gW2RhdGFdIDogW107XG4gIH1cbn1cblxuZnVuY3Rpb24gZXh0cmFjdERhdGEoZnJhbWU6IHN0cmluZyk6IHN0cmluZyB7XG4gIGNvbnN0IGxpbmVzID0gKGZyYW1lID8/IFwiXCIpLnNwbGl0KFwiXFxuXCIpO1xuICBjb25zdCBwYXJ0czogc3RyaW5nW10gPSBbXTtcbiAgZm9yIChjb25zdCBsaW5lIG9mIGxpbmVzKSB7XG4gICAgaWYgKCFsaW5lKSBjb250aW51ZTtcbiAgICBpZiAobGluZS5zdGFydHNXaXRoKFwiZGF0YTpcIikpIHtcbiAgICAgIC8vIEtlZXAgbGVhZGluZyBzcGFjZXMgaW4gcGF5bG9hZCAoU1NFIHNwZWMgYWxsb3dzIHRoZW0pLCBidXQgdHJpbSBvbmUgb3B0aW9uYWwgc3BhY2UuXG4gICAgICBjb25zdCByYXcgPSBsaW5lLnNsaWNlKFwiZGF0YTpcIi5sZW5ndGgpO1xuICAgICAgcGFydHMucHVzaChyYXcuc3RhcnRzV2l0aChcIiBcIikgPyByYXcuc2xpY2UoMSkgOiByYXcpO1xuICAgIH1cbiAgfVxuICByZXR1cm4gcGFydHMuam9pbihcIlxcblwiKS50cmltKCk7XG59XG5cblxuIiwgImltcG9ydCB7IFNzZVBhcnNlciB9IGZyb20gXCIuL3NzZVBhcnNlclwiO1xuXG4vLyBOb2RlLW9ubHkgKGRlc2t0b3ApLiBBdm9pZCBicm93c2VyIEV2ZW50U291cmNlIHRvIHJlZHVjZSBDT1JTIHN1cnByaXNlcy5cbmltcG9ydCBodHRwIGZyb20gXCJub2RlOmh0dHBcIjtcbmltcG9ydCBodHRwcyBmcm9tIFwibm9kZTpodHRwc1wiO1xuXG5leHBvcnQgdHlwZSBTc2VTdGF0dXMgPSBcIkNvbm5lY3RpbmdcIiB8IFwiQ29ubmVjdGVkXCIgfCBcIkRpc2Nvbm5lY3RlZFwiIHwgXCJFcnJvclwiO1xuXG5leHBvcnQgaW50ZXJmYWNlIFNyYVNzZUNsaWVudE9wdGlvbnMge1xuICBiYXNlVXJsOiBzdHJpbmc7XG4gIGhlYWRlcnM/OiBSZWNvcmQ8c3RyaW5nLCBzdHJpbmc+IHwgKCgpID0+IFJlY29yZDxzdHJpbmcsIHN0cmluZz4pO1xufVxuXG5leHBvcnQgaW50ZXJmYWNlIFNyYVNzZUhhbmRsZXJzPFRFdmVudCA9IGFueT4ge1xuICBvblN0YXR1czogKHN0YXR1czogU3NlU3RhdHVzLCBkZXRhaWw/OiBzdHJpbmcpID0+IHZvaWQ7XG4gIG9uRXZlbnQ6IChldnQ6IFRFdmVudCkgPT4gdm9pZDtcbn1cblxuZXhwb3J0IGNsYXNzIFNyYVNzZUNsaWVudDxURXZlbnQgPSBhbnk+IHtcbiAgcHJpdmF0ZSBiYXNlVXJsOiBzdHJpbmc7XG4gIHByaXZhdGUgaGVhZGVyczogUmVjb3JkPHN0cmluZywgc3RyaW5nPiB8ICgoKSA9PiBSZWNvcmQ8c3RyaW5nLCBzdHJpbmc+KSB8IHVuZGVmaW5lZDtcblxuICBwcml2YXRlIHN0b3BwZWQgPSB0cnVlO1xuICBwcml2YXRlIHJldHJ5VGltZXI6IGFueSA9IG51bGw7XG4gIHByaXZhdGUgYXR0ZW1wdCA9IDA7XG5cbiAgcHJpdmF0ZSByZXE6IGh0dHAuQ2xpZW50UmVxdWVzdCB8IG51bGwgPSBudWxsO1xuXG4gIGNvbnN0cnVjdG9yKG9wdGlvbnM6IFNyYVNzZUNsaWVudE9wdGlvbnMpIHtcbiAgICB0aGlzLmJhc2VVcmwgPSBub3JtYWxpemVCYXNlVXJsKG9wdGlvbnMuYmFzZVVybCk7XG4gICAgdGhpcy5oZWFkZXJzID0gb3B0aW9ucy5oZWFkZXJzO1xuICB9XG5cbiAgc2V0QmFzZVVybChiYXNlVXJsOiBzdHJpbmcpIHtcbiAgICB0aGlzLmJhc2VVcmwgPSBub3JtYWxpemVCYXNlVXJsKGJhc2VVcmwpO1xuICB9XG5cbiAgY29ubmVjdChzZXNzaW9uSWQ6IHN0cmluZywgaGFuZGxlcnM6IFNyYVNzZUhhbmRsZXJzPFRFdmVudD4pIHtcbiAgICBjb25zdCBzaWQgPSBTdHJpbmcoc2Vzc2lvbklkID8/IFwiXCIpLnRyaW0oKTtcbiAgICBpZiAoIXNpZCkgdGhyb3cgbmV3IEVycm9yKFwic2Vzc2lvbklkIGlzIHJlcXVpcmVkXCIpO1xuICAgIHRoaXMuZGlzY29ubmVjdCgpO1xuXG4gICAgdGhpcy5zdG9wcGVkID0gZmFsc2U7XG4gICAgdGhpcy5hdHRlbXB0ID0gMDtcblxuICAgIGNvbnN0IHVybCA9IGAke3RoaXMuYmFzZVVybH0vYXBpL3Nlc3Npb25zLyR7ZW5jb2RlVVJJQ29tcG9uZW50KHNpZCl9L2FndWkvZXZlbnRzYDtcbiAgICB0aGlzLm9wZW4odXJsLCBoYW5kbGVycyk7XG4gIH1cblxuICBkaXNjb25uZWN0KCkge1xuICAgIHRoaXMuc3RvcHBlZCA9IHRydWU7XG4gICAgdGhpcy5hdHRlbXB0ID0gMDtcblxuICAgIGlmICh0aGlzLnJldHJ5VGltZXIpIHtcbiAgICAgIGNsZWFyVGltZW91dCh0aGlzLnJldHJ5VGltZXIpO1xuICAgICAgdGhpcy5yZXRyeVRpbWVyID0gbnVsbDtcbiAgICB9XG5cbiAgICB0cnkge1xuICAgICAgdGhpcy5yZXE/LmRlc3Ryb3koKTtcbiAgICB9IGNhdGNoIHtcbiAgICAgIC8vIGlnbm9yZVxuICAgIH1cbiAgICB0aGlzLnJlcSA9IG51bGw7XG4gIH1cblxuICAvLyA9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT1cbiAgLy8gIEludGVybmFsc1xuICAvLyA9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT1cblxuICBwcml2YXRlIG9wZW4odXJsOiBzdHJpbmcsIGhhbmRsZXJzOiBTcmFTc2VIYW5kbGVyczxURXZlbnQ+KSB7XG4gICAgaWYgKHRoaXMuc3RvcHBlZCkgcmV0dXJuO1xuICAgIGhhbmRsZXJzLm9uU3RhdHVzKFwiQ29ubmVjdGluZ1wiKTtcblxuICAgIGNvbnN0IHUgPSBuZXcgVVJMKHVybCk7XG4gICAgY29uc3QgaXNIdHRwcyA9IHUucHJvdG9jb2wgPT09IFwiaHR0cHM6XCI7XG4gICAgY29uc3QgbW9kID0gaXNIdHRwcyA/IGh0dHBzIDogaHR0cDtcblxuICAgIGNvbnN0IGhlYWRlcnMgPSB0aGlzLnJlc29sdmVIZWFkZXJzKCk7XG4gICAgaGVhZGVyc1tcIkFjY2VwdFwiXSA9IFwidGV4dC9ldmVudC1zdHJlYW1cIjtcbiAgICBoZWFkZXJzW1wiQ2FjaGUtQ29udHJvbFwiXSA9IFwibm8tY2FjaGVcIjtcblxuICAgIGNvbnN0IHJlcSA9IG1vZC5yZXF1ZXN0KFxuICAgICAge1xuICAgICAgICBtZXRob2Q6IFwiR0VUXCIsXG4gICAgICAgIHByb3RvY29sOiB1LnByb3RvY29sLFxuICAgICAgICBob3N0bmFtZTogdS5ob3N0bmFtZSxcbiAgICAgICAgcG9ydDogdS5wb3J0ID8gTnVtYmVyKHUucG9ydCkgOiB1bmRlZmluZWQsXG4gICAgICAgIHBhdGg6IHUucGF0aG5hbWUgKyB1LnNlYXJjaCxcbiAgICAgICAgaGVhZGVycyxcbiAgICAgIH0sXG4gICAgICAocmVzKSA9PiB7XG4gICAgICAgIGNvbnN0IHN0YXR1cyA9IE51bWJlcihyZXMuc3RhdHVzQ29kZSA/PyAwKSB8fCAwO1xuICAgICAgICBpZiAoc3RhdHVzIDwgMjAwIHx8IHN0YXR1cyA+PSAzMDApIHtcbiAgICAgICAgICBoYW5kbGVycy5vblN0YXR1cyhcIkVycm9yXCIsIGBIVFRQICR7c3RhdHVzfWApO1xuICAgICAgICAgIHJlcy5yZXN1bWUoKTtcbiAgICAgICAgICB0aGlzLnNjaGVkdWxlUmVjb25uZWN0KHVybCwgaGFuZGxlcnMsIGBIVFRQICR7c3RhdHVzfWApO1xuICAgICAgICAgIHJldHVybjtcbiAgICAgICAgfVxuXG4gICAgICAgIGhhbmRsZXJzLm9uU3RhdHVzKFwiQ29ubmVjdGVkXCIpO1xuICAgICAgICB0aGlzLmF0dGVtcHQgPSAwO1xuXG4gICAgICAgIGNvbnN0IHBhcnNlciA9IG5ldyBTc2VQYXJzZXIoKTtcbiAgICAgICAgcmVzLnNldEVuY29kaW5nKFwidXRmOFwiKTtcblxuICAgICAgICByZXMub24oXCJkYXRhXCIsIChjaHVuazogc3RyaW5nKSA9PiB7XG4gICAgICAgICAgaWYgKHRoaXMuc3RvcHBlZCkgcmV0dXJuO1xuICAgICAgICAgIGNvbnN0IGZyYW1lcyA9IHBhcnNlci5wdXNoKGNodW5rKTtcbiAgICAgICAgICBmb3IgKGNvbnN0IGRhdGEgb2YgZnJhbWVzKSB7XG4gICAgICAgICAgICBjb25zdCBldnQgPSBzYWZlSnNvblBhcnNlKGRhdGEpO1xuICAgICAgICAgICAgaWYgKCFldnQpIGNvbnRpbnVlO1xuICAgICAgICAgICAgaGFuZGxlcnMub25FdmVudChldnQgYXMgVEV2ZW50KTtcbiAgICAgICAgICB9XG4gICAgICAgIH0pO1xuXG4gICAgICAgIHJlcy5vbihcImVuZFwiLCAoKSA9PiB7XG4gICAgICAgICAgaWYgKHRoaXMuc3RvcHBlZCkgcmV0dXJuO1xuICAgICAgICAgIC8vIGZsdXNoIGFueSBwYXJ0aWFsIGZyYW1lXG4gICAgICAgICAgY29uc3QgcmVzdCA9IHBhcnNlci5mbHVzaCgpO1xuICAgICAgICAgIGZvciAoY29uc3QgZGF0YSBvZiByZXN0KSB7XG4gICAgICAgICAgICBjb25zdCBldnQgPSBzYWZlSnNvblBhcnNlKGRhdGEpO1xuICAgICAgICAgICAgaWYgKCFldnQpIGNvbnRpbnVlO1xuICAgICAgICAgICAgaGFuZGxlcnMub25FdmVudChldnQgYXMgVEV2ZW50KTtcbiAgICAgICAgICB9XG5cbiAgICAgICAgICBoYW5kbGVycy5vblN0YXR1cyhcIkRpc2Nvbm5lY3RlZFwiLCBcInN0cmVhbSBlbmRlZFwiKTtcbiAgICAgICAgICB0aGlzLnNjaGVkdWxlUmVjb25uZWN0KHVybCwgaGFuZGxlcnMsIFwic3RyZWFtIGVuZGVkXCIpO1xuICAgICAgICB9KTtcblxuICAgICAgICByZXMub24oXCJlcnJvclwiLCAoZXJyOiBhbnkpID0+IHtcbiAgICAgICAgICBpZiAodGhpcy5zdG9wcGVkKSByZXR1cm47XG4gICAgICAgICAgaGFuZGxlcnMub25TdGF0dXMoXCJFcnJvclwiLCBlcnI/Lm1lc3NhZ2UgPz8gXCJzdHJlYW0gZXJyb3JcIik7XG4gICAgICAgICAgdGhpcy5zY2hlZHVsZVJlY29ubmVjdCh1cmwsIGhhbmRsZXJzLCBlcnI/Lm1lc3NhZ2UgPz8gXCJzdHJlYW0gZXJyb3JcIik7XG4gICAgICAgIH0pO1xuICAgICAgfSxcbiAgICApO1xuXG4gICAgcmVxLm9uKFwiZXJyb3JcIiwgKGVycjogYW55KSA9PiB7XG4gICAgICBpZiAodGhpcy5zdG9wcGVkKSByZXR1cm47XG4gICAgICBoYW5kbGVycy5vblN0YXR1cyhcIkVycm9yXCIsIGVycj8ubWVzc2FnZSA/PyBcInJlcXVlc3QgZXJyb3JcIik7XG4gICAgICB0aGlzLnNjaGVkdWxlUmVjb25uZWN0KHVybCwgaGFuZGxlcnMsIGVycj8ubWVzc2FnZSA/PyBcInJlcXVlc3QgZXJyb3JcIik7XG4gICAgfSk7XG5cbiAgICByZXEuZW5kKCk7XG4gICAgdGhpcy5yZXEgPSByZXE7XG4gIH1cblxuICBwcml2YXRlIHNjaGVkdWxlUmVjb25uZWN0KHVybDogc3RyaW5nLCBoYW5kbGVyczogU3JhU3NlSGFuZGxlcnM8VEV2ZW50PiwgcmVhc29uOiBzdHJpbmcpIHtcbiAgICBpZiAodGhpcy5zdG9wcGVkKSByZXR1cm47XG4gICAgaWYgKHRoaXMucmV0cnlUaW1lcikgcmV0dXJuO1xuXG4gICAgdGhpcy5hdHRlbXB0Kys7XG4gICAgY29uc3QgZGVsYXlNcyA9IGJhY2tvZmZNcyh0aGlzLmF0dGVtcHQpO1xuICAgIGhhbmRsZXJzLm9uU3RhdHVzKFwiRGlzY29ubmVjdGVkXCIsIGByZWNvbm5lY3RpbmcgaW4gJHtkZWxheU1zfW1zICgke3JlYXNvbn0pYCk7XG5cbiAgICB0aGlzLnJldHJ5VGltZXIgPSBzZXRUaW1lb3V0KCgpID0+IHtcbiAgICAgIHRoaXMucmV0cnlUaW1lciA9IG51bGw7XG4gICAgICB0aGlzLm9wZW4odXJsLCBoYW5kbGVycyk7XG4gICAgfSwgZGVsYXlNcyk7XG4gIH1cblxuICBwcml2YXRlIHJlc29sdmVIZWFkZXJzKCk6IFJlY29yZDxzdHJpbmcsIHN0cmluZz4ge1xuICAgIGNvbnN0IGggPSB0eXBlb2YgdGhpcy5oZWFkZXJzID09PSBcImZ1bmN0aW9uXCIgPyB0aGlzLmhlYWRlcnMoKSA6IHRoaXMuaGVhZGVycztcbiAgICByZXR1cm4geyAuLi4oaCA/PyB7fSkgfTtcbiAgfVxufVxuXG5mdW5jdGlvbiBub3JtYWxpemVCYXNlVXJsKGlucHV0OiBzdHJpbmcpOiBzdHJpbmcge1xuICByZXR1cm4gKGlucHV0ID8/IFwiXCIpLnRyaW0oKS5yZXBsYWNlKC9cXC8rJC8sIFwiXCIpO1xufVxuXG5mdW5jdGlvbiBiYWNrb2ZmTXMoYXR0ZW1wdDogbnVtYmVyKTogbnVtYmVyIHtcbiAgLy8gMC41cywgMXMsIDJzLCA0cy4uLiBjYXBwZWQgYXQgMzBzICgrIHNtYWxsIGppdHRlcilcbiAgY29uc3QgYmFzZSA9IE1hdGgubWluKDMwXzAwMCwgNTAwICogTWF0aC5wb3coMiwgTWF0aC5tYXgoMCwgYXR0ZW1wdCAtIDEpKSk7XG4gIGNvbnN0IGppdHRlciA9IE1hdGguZmxvb3IoTWF0aC5yYW5kb20oKSAqIDIwMCk7XG4gIHJldHVybiBiYXNlICsgaml0dGVyO1xufVxuXG5mdW5jdGlvbiBzYWZlSnNvblBhcnNlKGlucHV0OiBzdHJpbmcpOiBhbnkgfCBudWxsIHtcbiAgaWYgKHR5cGVvZiBpbnB1dCAhPT0gXCJzdHJpbmdcIikgcmV0dXJuIG51bGw7XG4gIGNvbnN0IHMgPSBpbnB1dC50cmltKCk7XG4gIGlmICghcykgcmV0dXJuIG51bGw7XG4gIHRyeSB7XG4gICAgY29uc3Qgb2JqID0gSlNPTi5wYXJzZShzKTtcbiAgICByZXR1cm4gb2JqICYmIHR5cGVvZiBvYmogPT09IFwib2JqZWN0XCIgPyBvYmogOiBudWxsO1xuICB9IGNhdGNoIHtcbiAgICByZXR1cm4gbnVsbDtcbiAgfVxufVxuXG5cbiIsICIvLyA9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT1cbi8vICBWYXVsdCBwYXRoIGhlbHBlcnMgKFZhdWx0L1NSQS8qKVxuLy9cbi8vICBHb2Fsczpcbi8vICAtIEtlZXAgYSBkZXRlcm1pbmlzdGljIGxheW91dCB1bmRlciBhIHNpbmdsZSB2YXVsdFJvb3QgZm9sZGVyLlxuLy8gIC0gUHJldmVudCBwYXRoIHRyYXZlcnNhbCAvIGFic29sdXRlIHBhdGggd3JpdGVzLlxuLy8gIC0gTWFrZSBydW5JZCBzYWZlIG9uIGFsbCBkZXNrdG9wIE9TZXMgKGUuZy4sIFwiOlwiIGlzIGludmFsaWQgb24gV2luZG93cykuXG4vLyA9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT1cblxuZXhwb3J0IGZ1bmN0aW9uIG5vcm1hbGl6ZVZhdWx0Um9vdChpbnB1dDogc3RyaW5nKTogc3RyaW5nIHtcbiAgY29uc3QgcmF3ID0gKGlucHV0ID8/IFwiXCIpLnJlcGxhY2UoL1xcXFwvZywgXCIvXCIpLnRyaW0oKTtcbiAgaWYgKCFyYXcpIHJldHVybiBcIlNSQVwiO1xuXG4gIGlmIChyYXcuc3RhcnRzV2l0aChcIi9cIikpIHRocm93IG5ldyBFcnJvcihcInZhdWx0Um9vdCBtdXN0IGJlIGEgdmF1bHQtcmVsYXRpdmUgcGF0aFwiKTtcblxuICBjb25zdCBwYXJ0cyA9IHJhdy5zcGxpdChcIi9cIikuZmlsdGVyKEJvb2xlYW4pO1xuICBpZiAocGFydHMubGVuZ3RoID09PSAwKSByZXR1cm4gXCJTUkFcIjtcblxuICBmb3IgKGNvbnN0IHAgb2YgcGFydHMpIHtcbiAgICBpZiAocCA9PT0gXCIuXCIgfHwgcCA9PT0gXCIuLlwiKSB0aHJvdyBuZXcgRXJyb3IoXCJ2YXVsdFJvb3QgY29udGFpbnMgaW52YWxpZCBwYXRoIHNlZ21lbnRzXCIpO1xuICB9XG5cbiAgcmV0dXJuIHBhcnRzLmpvaW4oXCIvXCIpO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gc2FmZUlkRm9yUGF0aChpZDogc3RyaW5nKTogc3RyaW5nIHtcbiAgLy8gQWxsb3cgYSBjb25zZXJ2YXRpdmUgc2V0IG9mIGNoYXJhY3RlcnMgdG8gc3RheSBjcm9zcy1wbGF0Zm9ybS5cbiAgLy8gUmVwbGFjZSBldmVyeXRoaW5nIGVsc2Ugd2l0aCAnLScuXG4gIGNvbnN0IHMgPSAoaWQgPz8gXCJcIikudHJpbSgpO1xuICBpZiAoIXMpIHJldHVybiBcInVua25vd25cIjtcbiAgcmV0dXJuIHNcbiAgICAucmVwbGFjZSgvW15hLXpBLVowLTkuXy1dKy9nLCBcIi1cIilcbiAgICAucmVwbGFjZSgvLSsvZywgXCItXCIpXG4gICAgLnJlcGxhY2UoL14tKy8sIFwiXCIpXG4gICAgLnJlcGxhY2UoLy0rJC8sIFwiXCIpIHx8IFwidW5rbm93blwiO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gam9pblZhdWx0UGF0aCguLi5wYXJ0czogc3RyaW5nW10pOiBzdHJpbmcge1xuICBjb25zdCBzZWdzOiBzdHJpbmdbXSA9IFtdO1xuICBmb3IgKGNvbnN0IHBhcnQgb2YgcGFydHMpIHtcbiAgICBjb25zdCBzID0gKHBhcnQgPz8gXCJcIikucmVwbGFjZSgvXFxcXC9nLCBcIi9cIikudHJpbSgpO1xuICAgIGlmICghcykgY29udGludWU7XG4gICAgaWYgKHMuc3RhcnRzV2l0aChcIi9cIikpIHRocm93IG5ldyBFcnJvcihcImFic29sdXRlIHBhdGggaXMgbm90IGFsbG93ZWRcIik7XG5cbiAgICBmb3IgKGNvbnN0IHNlZyBvZiBzLnNwbGl0KFwiL1wiKSkge1xuICAgICAgY29uc3QgdCA9IHNlZy50cmltKCk7XG4gICAgICBpZiAoIXQpIGNvbnRpbnVlO1xuICAgICAgaWYgKHQgPT09IFwiLlwiIHx8IHQgPT09IFwiLi5cIikgdGhyb3cgbmV3IEVycm9yKFwicGF0aCB0cmF2ZXJzYWwgaXMgbm90IGFsbG93ZWRcIik7XG4gICAgICBzZWdzLnB1c2godCk7XG4gICAgfVxuICB9XG4gIHJldHVybiBzZWdzLmpvaW4oXCIvXCIpO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gc2Vzc2lvbkRpcih2YXVsdFJvb3Q6IHN0cmluZywgc2Vzc2lvbklkOiBzdHJpbmcpOiBzdHJpbmcge1xuICByZXR1cm4gam9pblZhdWx0UGF0aChub3JtYWxpemVWYXVsdFJvb3QodmF1bHRSb290KSwgXCJzZXNzaW9uc1wiLCBzYWZlSWRGb3JQYXRoKHNlc3Npb25JZCkpO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gcnVuRGlyKHZhdWx0Um9vdDogc3RyaW5nLCBzZXNzaW9uSWQ6IHN0cmluZywgcnVuSWQ6IHN0cmluZyk6IHN0cmluZyB7XG4gIHJldHVybiBqb2luVmF1bHRQYXRoKHNlc3Npb25EaXIodmF1bHRSb290LCBzZXNzaW9uSWQpLCBcInJ1bnNcIiwgc2FmZUlkRm9yUGF0aChydW5JZCkpO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gcnVuRXZlbnRzUGF0aCh2YXVsdFJvb3Q6IHN0cmluZywgc2Vzc2lvbklkOiBzdHJpbmcsIHJ1bklkOiBzdHJpbmcpOiBzdHJpbmcge1xuICByZXR1cm4gam9pblZhdWx0UGF0aChydW5EaXIodmF1bHRSb290LCBzZXNzaW9uSWQsIHJ1bklkKSwgXCJldmVudHMuanNvbmxcIik7XG59XG5cbmV4cG9ydCBmdW5jdGlvbiBydW5NZXRhUGF0aCh2YXVsdFJvb3Q6IHN0cmluZywgc2Vzc2lvbklkOiBzdHJpbmcsIHJ1bklkOiBzdHJpbmcpOiBzdHJpbmcge1xuICByZXR1cm4gam9pblZhdWx0UGF0aChydW5EaXIodmF1bHRSb290LCBzZXNzaW9uSWQsIHJ1bklkKSwgXCJydW4uanNvblwiKTtcbn1cblxuZXhwb3J0IGZ1bmN0aW9uIGRlbGl2ZXJhYmxlc0Rpcih2YXVsdFJvb3Q6IHN0cmluZywgc2Vzc2lvbklkOiBzdHJpbmcpOiBzdHJpbmcge1xuICByZXR1cm4gam9pblZhdWx0UGF0aChzZXNzaW9uRGlyKHZhdWx0Um9vdCwgc2Vzc2lvbklkKSwgXCJkZWxpdmVyYWJsZXNcIik7XG59XG5cbmV4cG9ydCBmdW5jdGlvbiBkZWxpdmVyYWJsZXNKc29uUGF0aCh2YXVsdFJvb3Q6IHN0cmluZywgc2Vzc2lvbklkOiBzdHJpbmcpOiBzdHJpbmcge1xuICByZXR1cm4gam9pblZhdWx0UGF0aChkZWxpdmVyYWJsZXNEaXIodmF1bHRSb290LCBzZXNzaW9uSWQpLCBcImRlbGl2ZXJhYmxlcy5qc29uXCIpO1xufVxuXG5leHBvcnQgZnVuY3Rpb24gZGVsaXZlcmFibGVzQnJpZWZQYXRoKHZhdWx0Um9vdDogc3RyaW5nLCBzZXNzaW9uSWQ6IHN0cmluZyk6IHN0cmluZyB7XG4gIHJldHVybiBqb2luVmF1bHRQYXRoKGRlbGl2ZXJhYmxlc0Rpcih2YXVsdFJvb3QsIHNlc3Npb25JZCksIFwiYnJpZWYubWRcIik7XG59XG5cbmV4cG9ydCBmdW5jdGlvbiBkZWxpdmVyYWJsZXNEZWxpdmVyeVBhdGgodmF1bHRSb290OiBzdHJpbmcsIHNlc3Npb25JZDogc3RyaW5nKTogc3RyaW5nIHtcbiAgcmV0dXJuIGpvaW5WYXVsdFBhdGgoZGVsaXZlcmFibGVzRGlyKHZhdWx0Um9vdCwgc2Vzc2lvbklkKSwgXCJkZWxpdmVyeS5tZFwiKTtcbn1cblxuXG4iLCAiaW1wb3J0IHR5cGUgeyBBcHAgfSBmcm9tIFwib2JzaWRpYW5cIjtcblxuaW1wb3J0IHtcbiAgZGVsaXZlcmFibGVzRGlyLFxuICBkZWxpdmVyYWJsZXNCcmllZlBhdGgsXG4gIGRlbGl2ZXJhYmxlc0RlbGl2ZXJ5UGF0aCxcbiAgZGVsaXZlcmFibGVzSnNvblBhdGgsXG4gIG5vcm1hbGl6ZVZhdWx0Um9vdCxcbiAgcnVuRGlyLFxuICBydW5FdmVudHNQYXRoLFxuICBydW5NZXRhUGF0aCxcbiAgc2Vzc2lvbkRpcixcbn0gZnJvbSBcIi4vcGF0aHNcIjtcblxuLy8gPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09XG4vLyAgVmF1bHRTdG9yZSAoVmF1bHQvU1JBLyopXG4vL1xuLy8gIE5vdGVzOlxuLy8gIC0gQmVzdC1lZmZvcnQgcGVyc2lzdGVuY2U6IG5ldmVyIGJyZWFrIHJ1bnMgaWYgdmF1bHQgd3JpdGVzIGZhaWwuXG4vLyAgLSBEZXNrdG9wLWZpcnN0OiB1c2VzIE9ic2lkaWFuIFZhdWx0L0RhdGFBZGFwdGVyIEFQSXMuXG4vLyA9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT1cblxuZXhwb3J0IGludGVyZmFjZSBSdW5NZXRhIHtcbiAgcnVuSWQ6IHN0cmluZztcbiAgc2Vzc2lvbklkOiBzdHJpbmc7XG4gIGNyZWF0ZWRBdD86IHN0cmluZztcbiAgbW9kZT86IHN0cmluZztcbiAgcmVxdWVzdElkPzogc3RyaW5nO1xuICBsYXN0U3RhdHVzPzogc3RyaW5nO1xufVxuXG5leHBvcnQgY2xhc3MgVmF1bHRTdG9yZSB7XG4gIHByaXZhdGUgcmVhZG9ubHkgYXBwOiBBcHA7XG4gIHByaXZhdGUgdmF1bHRSb290OiBzdHJpbmc7XG5cbiAgcHJpdmF0ZSBmbHVzaFRpbWVyOiBhbnkgPSBudWxsO1xuICBwcml2YXRlIHBlbmRpbmdBcHBlbmRzID0gbmV3IE1hcDxzdHJpbmcsIHN0cmluZ1tdPigpO1xuXG4gIGNvbnN0cnVjdG9yKGFwcDogQXBwLCB2YXVsdFJvb3Q6IHN0cmluZykge1xuICAgIHRoaXMuYXBwID0gYXBwO1xuICAgIHRoaXMudmF1bHRSb290ID0gbm9ybWFsaXplVmF1bHRSb290KHZhdWx0Um9vdCk7XG4gIH1cblxuICBzZXRWYXVsdFJvb3QodmF1bHRSb290OiBzdHJpbmcpIHtcbiAgICB0aGlzLnZhdWx0Um9vdCA9IG5vcm1hbGl6ZVZhdWx0Um9vdCh2YXVsdFJvb3QpO1xuICB9XG5cbiAgYXN5bmMgZW5zdXJlUm9vdCgpOiBQcm9taXNlPHZvaWQ+IHtcbiAgICBhd2FpdCB0aGlzLmVuc3VyZUZvbGRlcih0aGlzLnZhdWx0Um9vdCk7XG4gICAgYXdhaXQgdGhpcy5lbnN1cmVGb2xkZXIoYCR7dGhpcy52YXVsdFJvb3R9L3Nlc3Npb25zYCk7XG4gIH1cblxuICBhc3luYyBhcHBlbmRSdW5FdmVudChzZXNzaW9uSWQ6IHN0cmluZywgcnVuSWQ6IHN0cmluZywgZXZ0OiBhbnkpOiBQcm9taXNlPHZvaWQ+IHtcbiAgICB0cnkge1xuICAgICAgYXdhaXQgdGhpcy5lbnN1cmVSb290KCk7XG4gICAgICBhd2FpdCB0aGlzLmVuc3VyZUZvbGRlcihzZXNzaW9uRGlyKHRoaXMudmF1bHRSb290LCBzZXNzaW9uSWQpKTtcbiAgICAgIGF3YWl0IHRoaXMuZW5zdXJlRm9sZGVyKHJ1bkRpcih0aGlzLnZhdWx0Um9vdCwgc2Vzc2lvbklkLCBydW5JZCkpO1xuXG4gICAgICBjb25zdCBwYXRoID0gcnVuRXZlbnRzUGF0aCh0aGlzLnZhdWx0Um9vdCwgc2Vzc2lvbklkLCBydW5JZCk7XG4gICAgICBjb25zdCBsaW5lID0gSlNPTi5zdHJpbmdpZnkoZXZ0KSArIFwiXFxuXCI7XG4gICAgICB0aGlzLmVucXVldWVBcHBlbmQocGF0aCwgbGluZSk7XG4gICAgfSBjYXRjaCB7XG4gICAgICAvLyBiZXN0LWVmZm9ydCBvbmx5XG4gICAgfVxuICB9XG5cbiAgYXN5bmMgd3JpdGVSdW5NZXRhKHNlc3Npb25JZDogc3RyaW5nLCBydW5JZDogc3RyaW5nLCBtZXRhOiBSdW5NZXRhKTogUHJvbWlzZTx2b2lkPiB7XG4gICAgdHJ5IHtcbiAgICAgIGF3YWl0IHRoaXMuZW5zdXJlUm9vdCgpO1xuICAgICAgYXdhaXQgdGhpcy5lbnN1cmVGb2xkZXIoc2Vzc2lvbkRpcih0aGlzLnZhdWx0Um9vdCwgc2Vzc2lvbklkKSk7XG4gICAgICBhd2FpdCB0aGlzLmVuc3VyZUZvbGRlcihydW5EaXIodGhpcy52YXVsdFJvb3QsIHNlc3Npb25JZCwgcnVuSWQpKTtcblxuICAgICAgY29uc3QgcGF0aCA9IHJ1bk1ldGFQYXRoKHRoaXMudmF1bHRSb290LCBzZXNzaW9uSWQsIHJ1bklkKTtcbiAgICAgIGF3YWl0IHRoaXMuc2FmZVdyaXRlVGV4dChwYXRoLCBKU09OLnN0cmluZ2lmeShtZXRhLCBudWxsLCAyKSArIFwiXFxuXCIpO1xuICAgIH0gY2F0Y2gge1xuICAgICAgLy8gYmVzdC1lZmZvcnQgb25seVxuICAgIH1cbiAgfVxuXG4gIGFzeW5jIHdyaXRlRGVsaXZlcmFibGVzSnNvbihzZXNzaW9uSWQ6IHN0cmluZywgc25hcHNob3Q6IGFueSk6IFByb21pc2U8dm9pZD4ge1xuICAgIHRyeSB7XG4gICAgICBhd2FpdCB0aGlzLmVuc3VyZVJvb3QoKTtcbiAgICAgIGF3YWl0IHRoaXMuZW5zdXJlRm9sZGVyKHNlc3Npb25EaXIodGhpcy52YXVsdFJvb3QsIHNlc3Npb25JZCkpO1xuICAgICAgYXdhaXQgdGhpcy5lbnN1cmVGb2xkZXIoZGVsaXZlcmFibGVzRGlyKHRoaXMudmF1bHRSb290LCBzZXNzaW9uSWQpKTtcblxuICAgICAgY29uc3QgcGF0aCA9IGRlbGl2ZXJhYmxlc0pzb25QYXRoKHRoaXMudmF1bHRSb290LCBzZXNzaW9uSWQpO1xuICAgICAgYXdhaXQgdGhpcy5zYWZlV3JpdGVUZXh0KHBhdGgsIEpTT04uc3RyaW5naWZ5KHNuYXBzaG90LCBudWxsLCAyKSArIFwiXFxuXCIpO1xuICAgIH0gY2F0Y2gge1xuICAgICAgLy8gYmVzdC1lZmZvcnQgb25seVxuICAgIH1cbiAgfVxuXG4gIGFzeW5jIHdyaXRlRGVsaXZlcmFibGVzTWFya2Rvd24oc2Vzc2lvbklkOiBzdHJpbmcsIGJyaWVmTWQ6IHN0cmluZywgZGVsaXZlcnlNZDogc3RyaW5nKTogUHJvbWlzZTx2b2lkPiB7XG4gICAgdHJ5IHtcbiAgICAgIGF3YWl0IHRoaXMuZW5zdXJlUm9vdCgpO1xuICAgICAgYXdhaXQgdGhpcy5lbnN1cmVGb2xkZXIoc2Vzc2lvbkRpcih0aGlzLnZhdWx0Um9vdCwgc2Vzc2lvbklkKSk7XG4gICAgICBhd2FpdCB0aGlzLmVuc3VyZUZvbGRlcihkZWxpdmVyYWJsZXNEaXIodGhpcy52YXVsdFJvb3QsIHNlc3Npb25JZCkpO1xuXG4gICAgICBhd2FpdCB0aGlzLnNhZmVXcml0ZVRleHQoZGVsaXZlcmFibGVzQnJpZWZQYXRoKHRoaXMudmF1bHRSb290LCBzZXNzaW9uSWQpLCBicmllZk1kKTtcbiAgICAgIGF3YWl0IHRoaXMuc2FmZVdyaXRlVGV4dChkZWxpdmVyYWJsZXNEZWxpdmVyeVBhdGgodGhpcy52YXVsdFJvb3QsIHNlc3Npb25JZCksIGRlbGl2ZXJ5TWQpO1xuICAgIH0gY2F0Y2gge1xuICAgICAgLy8gYmVzdC1lZmZvcnRcbiAgICB9XG4gIH1cblxuICAvLyA9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT1cbiAgLy8gIEFwcGVuZCBidWZmZXJpbmcgKGF2b2lkIHRvbyBtYW55IHNtYWxsIHdyaXRlcylcbiAgLy8gPT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09XG5cbiAgcHJpdmF0ZSBlbnF1ZXVlQXBwZW5kKHBhdGg6IHN0cmluZywgdGV4dDogc3RyaW5nKSB7XG4gICAgY29uc3QgbGlzdCA9IHRoaXMucGVuZGluZ0FwcGVuZHMuZ2V0KHBhdGgpID8/IFtdO1xuICAgIGxpc3QucHVzaCh0ZXh0KTtcbiAgICB0aGlzLnBlbmRpbmdBcHBlbmRzLnNldChwYXRoLCBsaXN0KTtcblxuICAgIGlmICghdGhpcy5mbHVzaFRpbWVyKSB7XG4gICAgICB0aGlzLmZsdXNoVGltZXIgPSBzZXRUaW1lb3V0KCgpID0+IHtcbiAgICAgICAgdGhpcy5mbHVzaFRpbWVyID0gbnVsbDtcbiAgICAgICAgdm9pZCB0aGlzLmZsdXNoQXBwZW5kc0FzeW5jKCk7XG4gICAgICB9LCAyMDApO1xuICAgIH1cbiAgfVxuXG4gIHByaXZhdGUgYXN5bmMgZmx1c2hBcHBlbmRzQXN5bmMoKTogUHJvbWlzZTx2b2lkPiB7XG4gICAgY29uc3QgZW50cmllcyA9IEFycmF5LmZyb20odGhpcy5wZW5kaW5nQXBwZW5kcy5lbnRyaWVzKCkpO1xuICAgIHRoaXMucGVuZGluZ0FwcGVuZHMuY2xlYXIoKTtcblxuICAgIGZvciAoY29uc3QgW3BhdGgsIGNodW5rc10gb2YgZW50cmllcykge1xuICAgICAgY29uc3QgY29udGVudCA9IGNodW5rcy5qb2luKFwiXCIpO1xuICAgICAgdHJ5IHtcbiAgICAgICAgYXdhaXQgdGhpcy5zYWZlQXBwZW5kVGV4dChwYXRoLCBjb250ZW50KTtcbiAgICAgIH0gY2F0Y2gge1xuICAgICAgICAvLyBiZXN0LWVmZm9ydFxuICAgICAgfVxuICAgIH1cbiAgfVxuXG4gIC8vID09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PVxuICAvLyAgQWRhcHRlciBoZWxwZXJzXG4gIC8vID09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PVxuXG4gIHByaXZhdGUgYXN5bmMgZW5zdXJlRm9sZGVyKHBhdGg6IHN0cmluZyk6IFByb21pc2U8dm9pZD4ge1xuICAgIGNvbnN0IGFkYXB0ZXI6IGFueSA9ICh0aGlzLmFwcCBhcyBhbnkpPy52YXVsdD8uYWRhcHRlcjtcbiAgICBpZiAoIWFkYXB0ZXIpIHJldHVybjtcblxuICAgIGNvbnN0IGV4aXN0cyA9IGF3YWl0IGFkYXB0ZXIuZXhpc3RzKHBhdGgpLmNhdGNoKCgpID0+IGZhbHNlKTtcbiAgICBpZiAoZXhpc3RzKSByZXR1cm47XG5cbiAgICBhd2FpdCBhZGFwdGVyLm1rZGlyKHBhdGgpLmNhdGNoKCgpID0+IHVuZGVmaW5lZCk7XG4gIH1cblxuICBwcml2YXRlIGFzeW5jIHNhZmVXcml0ZVRleHQocGF0aDogc3RyaW5nLCBjb250ZW50OiBzdHJpbmcpOiBQcm9taXNlPHZvaWQ+IHtcbiAgICBjb25zdCBhZGFwdGVyOiBhbnkgPSAodGhpcy5hcHAgYXMgYW55KT8udmF1bHQ/LmFkYXB0ZXI7XG4gICAgaWYgKCFhZGFwdGVyKSByZXR1cm47XG4gICAgYXdhaXQgYWRhcHRlci53cml0ZShwYXRoLCBjb250ZW50KS5jYXRjaCgoKSA9PiB1bmRlZmluZWQpO1xuICB9XG5cbiAgcHJpdmF0ZSBhc3luYyBzYWZlQXBwZW5kVGV4dChwYXRoOiBzdHJpbmcsIGNvbnRlbnQ6IHN0cmluZyk6IFByb21pc2U8dm9pZD4ge1xuICAgIGNvbnN0IGFkYXB0ZXI6IGFueSA9ICh0aGlzLmFwcCBhcyBhbnkpPy52YXVsdD8uYWRhcHRlcjtcbiAgICBpZiAoIWFkYXB0ZXIpIHJldHVybjtcblxuICAgIC8vIERhdGFBZGFwdGVyLmFwcGVuZCBleGlzdHMgb24gZGVza3RvcDsgZmFsbGJhY2sgdG8gcmVhZCt3cml0ZSB3aGVuIGFic2VudC5cbiAgICBpZiAodHlwZW9mIGFkYXB0ZXIuYXBwZW5kID09PSBcImZ1bmN0aW9uXCIpIHtcbiAgICAgIGF3YWl0IGFkYXB0ZXIuYXBwZW5kKHBhdGgsIGNvbnRlbnQpLmNhdGNoKCgpID0+IHVuZGVmaW5lZCk7XG4gICAgICByZXR1cm47XG4gICAgfVxuXG4gICAgY29uc3QgcHJldiA9IGF3YWl0IGFkYXB0ZXIucmVhZChwYXRoKS5jYXRjaCgoKSA9PiBcIlwiKTtcbiAgICBhd2FpdCBhZGFwdGVyLndyaXRlKHBhdGgsIFN0cmluZyhwcmV2ID8/IFwiXCIpICsgY29udGVudCkuY2F0Y2goKCkgPT4gdW5kZWZpbmVkKTtcbiAgfVxufVxuXG5cbiIsICIvLyA9PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT1cbi8vICBEZWxpdmVyYWJsZXMgcmVuZGVyZXIgKEpTT04gLT4gTWFya2Rvd24pXG4vL1xuLy8gIEdvYWw6XG4vLyAgLSBQcm92aWRlIGh1bWFuLWZyaWVuZGx5IG1hcmtkb3duIGZpbGVzIGluIFZhdWx0L1NSQS9cbi8vICAtIEtlZXAgcmVuZGVyaW5nIGJlc3QtZWZmb3J0IGFuZCBib3VuZGVkIChhdm9pZCBnaWFudCBub3Rlcylcbi8vID09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PT09PVxuXG5leHBvcnQgZnVuY3Rpb24gcmVuZGVyQnJpZWZNYXJrZG93bihicmllZjogYW55KTogc3RyaW5nIHtcbiAgY29uc3QgYiA9IGJyaWVmID8/IHt9O1xuXG4gIGNvbnN0IGxpbmVzOiBzdHJpbmdbXSA9IFtdO1xuICBsaW5lcy5wdXNoKFwiIyBSZXNlYXJjaCBCcmllZlwiKTtcbiAgbGluZXMucHVzaChcIlwiKTtcblxuICBwdXNoRmllbGQobGluZXMsIFwiVXBkYXRlZEF0XCIsIGIudXBkYXRlZEF0KTtcbiAgcHVzaEZpZWxkKGxpbmVzLCBcIlZlcnNpb25cIiwgYi52ZXJzaW9uKTtcbiAgcHVzaEZpZWxkKGxpbmVzLCBcIlJld3JpdHRlblF1ZXN0aW9uXCIsIGIucmV3cml0dGVuUXVlc3Rpb24pO1xuICBwdXNoRmllbGQobGluZXMsIFwiU2NvcGVcIiwgYi5zY29wZSk7XG4gIHB1c2hGaWVsZChsaW5lcywgXCJTdWNjZXNzQ3JpdGVyaWFcIiwgYi5zdWNjZXNzQ3JpdGVyaWEpO1xuXG4gIHB1c2hMaXN0KGxpbmVzLCBcIkFzc3VtcHRpb25zXCIsIGIuYXNzdW1wdGlvbnMpO1xuICBwdXNoTGlzdChsaW5lcywgXCJSaXNrc1wiLCBiLnJpc2tzKTtcbiAgcHVzaExpc3QobGluZXMsIFwiVW5jZXJ0YWludGllc1wiLCBiLnVuY2VydGFpbnRpZXMpO1xuXG4gIGNvbnN0IHRlcm1zID0gQXJyYXkuaXNBcnJheShiLnRlcm1zKSA/IGIudGVybXMgOiBbXTtcbiAgaWYgKHRlcm1zLmxlbmd0aCA+IDApIHtcbiAgICBsaW5lcy5wdXNoKFwiXCIpO1xuICAgIGxpbmVzLnB1c2goXCIjIyBUZXJtc1wiKTtcbiAgICBmb3IgKGNvbnN0IHQgb2YgdGVybXMuc2xpY2UoMCwgNTApKSB7XG4gICAgICBjb25zdCB0ZXJtID0gU3RyaW5nKHQ/LnRlcm0gPz8gXCJcIikudHJpbSgpO1xuICAgICAgY29uc3QgbWVhbmluZyA9IFN0cmluZyh0Py5tZWFuaW5nID8/IFwiXCIpLnRyaW0oKTtcbiAgICAgIGlmICghdGVybSAmJiAhbWVhbmluZykgY29udGludWU7XG4gICAgICBsaW5lcy5wdXNoKGAtICoqJHtlc2NhcGVNZCh0ZXJtIHx8IFwidGVybVwiKX0qKjogJHtlc2NhcGVNZChtZWFuaW5nIHx8IFwiXCIpfWApO1xuICAgIH1cbiAgfVxuXG4gIGNvbnN0IG1pbGVzdG9uZXMgPSBBcnJheS5pc0FycmF5KGIubWlsZXN0b25lcykgPyBiLm1pbGVzdG9uZXMgOiBbXTtcbiAgaWYgKG1pbGVzdG9uZXMubGVuZ3RoID4gMCkge1xuICAgIGxpbmVzLnB1c2goXCJcIik7XG4gICAgbGluZXMucHVzaChcIiMjIE1pbGVzdG9uZXNcIik7XG4gICAgZm9yIChjb25zdCBtIG9mIG1pbGVzdG9uZXMuc2xpY2UoMCwgNTApKSB7XG4gICAgICBjb25zdCBpZHggPSBtPy5yb3VuZEluZGV4O1xuICAgICAgY29uc3Qgb3V0ID0gU3RyaW5nKG0/LmV4cGVjdGVkT3V0cHV0ID8/IFwiXCIpLnRyaW0oKTtcbiAgICAgIGxpbmVzLnB1c2goYC0gcm91bmQgJHtpZHggPz8gXCJcIn06ICR7ZXNjYXBlTWQob3V0KX1gKTtcbiAgICB9XG4gIH1cblxuICBsaW5lcy5wdXNoKFwiXCIpO1xuICByZXR1cm4gbGluZXMuam9pbihcIlxcblwiKTtcbn1cblxuZXhwb3J0IGZ1bmN0aW9uIHJlbmRlckRlbGl2ZXJ5TWFya2Rvd24oZGVsaXZlcnk6IGFueSk6IHN0cmluZyB7XG4gIGNvbnN0IGxpbmVzOiBzdHJpbmdbXSA9IFtdO1xuICBsaW5lcy5wdXNoKFwiIyBEZWxpdmVyeSBTbmFwc2hvdFwiKTtcbiAgbGluZXMucHVzaChcIlwiKTtcblxuICBjb25zdCBqc29uID0gc2FmZVByZXR0eUpzb24oZGVsaXZlcnkpO1xuICBsaW5lcy5wdXNoKFwiYGBganNvblwiKTtcbiAgbGluZXMucHVzaCh0cmltRm9yTm90ZShqc29uLCAyMF8wMDApKTtcbiAgbGluZXMucHVzaChcImBgYFwiKTtcbiAgbGluZXMucHVzaChcIlwiKTtcblxuICByZXR1cm4gbGluZXMuam9pbihcIlxcblwiKTtcbn1cblxuZnVuY3Rpb24gcHVzaEZpZWxkKGxpbmVzOiBzdHJpbmdbXSwgbmFtZTogc3RyaW5nLCB2YWx1ZTogYW55KSB7XG4gIGNvbnN0IHYgPSBTdHJpbmcodmFsdWUgPz8gXCJcIikudHJpbSgpO1xuICBpZiAoIXYpIHJldHVybjtcbiAgbGluZXMucHVzaChgLSAqKiR7bmFtZX0qKjogJHtlc2NhcGVNZCh2KX1gKTtcbn1cblxuZnVuY3Rpb24gcHVzaExpc3QobGluZXM6IHN0cmluZ1tdLCB0aXRsZTogc3RyaW5nLCB2YWx1ZTogYW55KSB7XG4gIGNvbnN0IGFyciA9IEFycmF5LmlzQXJyYXkodmFsdWUpID8gdmFsdWUgOiBbXTtcbiAgaWYgKGFyci5sZW5ndGggPT09IDApIHJldHVybjtcbiAgbGluZXMucHVzaChcIlwiKTtcbiAgbGluZXMucHVzaChgIyMgJHt0aXRsZX1gKTtcbiAgZm9yIChjb25zdCB4IG9mIGFyci5zbGljZSgwLCAxMDApKSB7XG4gICAgY29uc3QgcyA9IFN0cmluZyh4ID8/IFwiXCIpLnRyaW0oKTtcbiAgICBpZiAoIXMpIGNvbnRpbnVlO1xuICAgIGxpbmVzLnB1c2goYC0gJHtlc2NhcGVNZChzKX1gKTtcbiAgfVxufVxuXG5mdW5jdGlvbiBzYWZlUHJldHR5SnNvbihvYmo6IGFueSk6IHN0cmluZyB7XG4gIHRyeSB7XG4gICAgcmV0dXJuIEpTT04uc3RyaW5naWZ5KG9iaiA/PyBudWxsLCBudWxsLCAyKSA/PyBcIm51bGxcIjtcbiAgfSBjYXRjaCB7XG4gICAgcmV0dXJuIFwibnVsbFwiO1xuICB9XG59XG5cbmZ1bmN0aW9uIHRyaW1Gb3JOb3RlKHRleHQ6IHN0cmluZywgbWF4Q2hhcnM6IG51bWJlcik6IHN0cmluZyB7XG4gIGNvbnN0IHMgPSBTdHJpbmcodGV4dCA/PyBcIlwiKTtcbiAgaWYgKHMubGVuZ3RoIDw9IG1heENoYXJzKSByZXR1cm4gcztcbiAgcmV0dXJuIHMuc2xpY2UoMCwgbWF4Q2hhcnMpICsgXCJcXG4uLi4gKHRydW5jYXRlZClcXG5cIjtcbn1cblxuZnVuY3Rpb24gZXNjYXBlTWQoczogc3RyaW5nKTogc3RyaW5nIHtcbiAgLy8gTWluaW1hbCBlc2NhcGluZyBmb3IgbGlzdC9oZWFkaW5nIHN0YWJpbGl0eS5cbiAgcmV0dXJuIChzID8/IFwiXCIpLnJlcGxhY2UoL1xcci9nLCBcIlwiKS5yZXBsYWNlKC9cXG4vZywgXCIgXCIpLnRyaW0oKTtcbn1cblxuXG4iXSwKICAibWFwcGluZ3MiOiAiOzs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7QUFBQTtBQUFBO0FBQUE7QUFBQTtBQUFBO0FBQUEsSUFBQUEsbUJBQStCOzs7QUNBL0Isc0JBQW1FO0FBaUI1RCxJQUFNLHVCQUEwQztBQUFBLEVBQ3JELFNBQVM7QUFBQSxFQUNULFdBQVc7QUFBQSxFQUNYLGNBQWM7QUFBQSxFQUNkLGtCQUFrQjtBQUNwQjtBQUVPLElBQU0saUJBQU4sY0FBNkIsaUNBQWlCO0FBQUEsRUFHbkQsWUFBWSxLQUFVLFFBQTRFO0FBQ2hHLFVBQU0sS0FBSyxNQUFhO0FBQ3hCLFNBQUssU0FBUztBQUFBLEVBQ2hCO0FB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QVUsWUFBWTtBQUNwQixjQUFNLE9BQU8sTUFBTSxLQUFLLGFBQWE7QUFDckMsY0FBTSxNQUFNLE1BQU0sbUJBQW1CLHdCQUF3QixXQUFXO0FBQ3hFLFlBQUksQ0FBQztBQUFLO0FBQ1YsYUFBSyxpQkFBaUIsR0FBRztBQUFBLE1BQzNCO0FBQUEsSUFDRixDQUFDO0FBRUQsU0FBSyxXQUFXO0FBQUEsTUFDZCxJQUFJO0FBQUEsTUFDSixNQUFNO0FBQUEsTUFDTixVQUFVLFlBQVk7QUFDcEIsY0FBTSxPQUFPLE1BQU0sS0FBSyxhQUFhO0FBQ3JDLGNBQU0sS0FBSyxtQkFBbUI7QUFBQSxNQUNoQztBQUFBLElBQ0YsQ0FBQztBQUVELFNBQUssV0FBVztBQUFBLE1BQ2QsSUFBSTtBQUFBLE1BQ0osTUFBTTtBQUFBLE1BQ04sVUFBVSxNQUFNO0FBQ2QsWUFBSSx3QkFBTyx1Q0FBdUM7QUFBQSxNQUNwRDtBQUFBLElBQ0YsQ0FBQztBQUFBLEVBQ0g7QUFBQSxFQUVBLE1BQU0sZUFBOEI7QUFDbEMsVUFBTSxPQUFRLE1BQU0sS0FBSyxTQUFTLEVBQUUsTUFBTSxNQUFNLElBQUk7QUFDcEQsU0FBSyxXQUFXLEVBQUUsR0FBRyxzQkFBc0IsR0FBSSxzQkFBUSxDQUFDLEVBQUc7QUFBQSxFQUM3RDtBQUFBLEVBRUEsTUFBTSxlQUE4QjtBQUNsQyxVQUFNLEtBQUssU0FBUyxLQUFLLFFBQVE7QUFBQSxFQUNuQztBQUFBLEVBRUEsTUFBYyxlQUFpQztBQS9FakQ7QUFnRkksVUFBTSxXQUFXLEtBQUssSUFBSSxVQUFVLGdCQUFnQixhQUFhO0FBQ2pFLFVBQU0sUUFBTyxjQUFTLENBQUMsTUFBVixZQUFlLEtBQUssSUFBSSxVQUFVLGFBQWEsS0FBSztBQUNqRSxRQUFJLENBQUM7QUFBTSxZQUFNLElBQUksTUFBTSw2QkFBNkI7QUFFeEQsVUFBTSxLQUFLLGFBQWEsRUFBRSxNQUFNLGVBQWUsUUFBUSxLQUFLLENBQUM7QUFDN0QsU0FBSyxJQUFJLFVBQVUsV0FBVyxJQUFJO0FBQ2xDLFdBQU8sS0FBSztBQUFBLEVBQ2Q7QUFDRjtBQUVBLGVBQWUsbUJBQW1CLE9BQWUsYUFBc0M7QUExRnZGO0FBNkZFLFFBQU0sS0FBSSxZQUFPLE9BQU8sT0FBTyxXQUFXLE1BQWhDLFlBQXFDO0FBQy9DLFNBQU8sRUFBRSxLQUFLO0FBQ2hCOyIsCiAgIm5hbWVzIjogWyJpbXBvcnRfb2JzaWRpYW4iLCAiaW1wb3J0X29ic2lkaWFuIiwgImltcG9ydF9vYnNpZGlhbiIsICJub3JtYWxpemVCYXNlVXJsIiwgImNsYW1wVGltZW91dCIsICJub3JtYWxpemVCYXNlVXJsIiwgImh0dHBzIiwgImh0dHAiLCAiX2EiXQp9Cg==
