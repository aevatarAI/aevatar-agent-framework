import type {
  SraAgUiConnection,
  SraAgUiEvent,
  SraApiInfoResponse,
  SraDeliverablesResponse,
  SraListSessionsResponse,
  SraSendInputRequest,
  SraTransport,
  SraTransportCapabilities,
  SraTransportStatus,
  SraUploadFile,
} from "../../../ui/src/transport/SraTransport";

// ============================================================
//  WebTransport
//
//  中文说明：
//  - 运行在浏览器（Vite dev / 静态构建产物）中
//  - 使用 fetch + EventSource
//  - 默认使用相对路径 `/api/*`，在 dev 下由 Vite proxy 转发到 sidecar
// ============================================================

export function createWebTransport(): SraTransport {
  const capabilities: SraTransportCapabilities = {
    // NOTE: Web host does not (yet) support remote baseUrl switching.
    // For now we keep these enabled to match existing web UI behavior.
    filesApi: true,
    revealApiKey: true,
    nodeSse: false,
  };

  return {
    capabilities,

    async health() {
      const res = await fetch("/health", { method: "GET" });
      const text = await res.text().catch(() => "");
      return { ok: res.ok, status: res.status, body: text };
    },

    async info(): Promise<SraApiInfoResponse> {
      return await requestJson("GET", "/api/info");
    },

    async listSessions(): Promise<SraListSessionsResponse> {
      const json: any = await requestJson("GET", "/api/sessions");
      return {
        count: Number(json?.count ?? 0) || 0,
        sessions: Array.isArray(json?.sessions) ? json.sessions : [],
      };
    },

    async createSession(providerName?: string): Promise<{ sessionId: string }> {
      const body = providerName ? { providerName: String(providerName) } : {};
      const json: any = await requestJson("POST", "/api/sessions", body);
      const sid = String(json?.sessionId ?? "").trim();
      if (!sid) throw new Error("Invalid response: missing sessionId");
      return { sessionId: sid };
    },

    async sendInput(sessionId: string, input: SraSendInputRequest): Promise<{ runId: string }> {
      const sid = String(sessionId ?? "").trim();
      if (!sid) throw new Error("sessionId is required");
      if (!input?.message || !String(input.message).trim()) throw new Error("message is required");

      const json: any = await requestJson("POST", `/api/sessions/${encodeURIComponent(sid)}/input`, input);
      const runId = String(json?.runId ?? "").trim();
      if (!runId) throw new Error("Invalid response: missing runId");
      return { runId };
    },

    async getDeliverables(sessionId: string): Promise<SraDeliverablesResponse> {
      const sid = String(sessionId ?? "").trim();
      if (!sid) throw new Error("sessionId is required");
      return await requestJson("GET", `/api/sessions/${encodeURIComponent(sid)}/deliverables`);
    },

    async uploadAttachments(sessionId: string, files: SraUploadFile[]): Promise<{ attachmentPaths: string[] }> {
      const sid = String(sessionId ?? "").trim();
      if (!sid) throw new Error("sessionId is required");

      const fd = new FormData();
      for (const f of files ?? []) {
        if (!f?.data || !f.filename) continue;
        const blob = new Blob([f.data], { type: String(f.contentType || "application/octet-stream") });
        fd.append("file", blob, String(f.filename));
      }

      const res = await fetch(`/api/sessions/${encodeURIComponent(sid)}/uploads`, { method: "POST", body: fd });
      const text = await res.text().catch(() => "");
      if (!res.ok) throw new Error(`HTTP ${res.status}${text ? `: ${trimForUi(text)}` : ""}`);

      const json = safeJsonParse(text);
      const arr = Array.isArray((json as any)?.attachmentPaths) ? (json as any).attachmentPaths : [];
      const attachmentPaths = arr.map((x: any) => String(x ?? "").trim()).filter((x: string) => x.length > 0);
      return { attachmentPaths };
    },

    connectAgUiEvents(sessionId: string, handlers: { onStatus?: (status: SraTransportStatus, detail?: string) => void; onEvent: (evt: SraAgUiEvent) => void; onError?: (err: unknown) => void; }): SraAgUiConnection {
      const sid = String(sessionId ?? "").trim();
      if (!sid) throw new Error("sessionId is required");

      const url = `/api/sessions/${encodeURIComponent(sid)}/agui/events`;
      const es = new EventSource(url);

      handlers.onStatus?.("Connecting");

      es.onopen = () => {
        handlers.onStatus?.("Connected");
      };

      es.onmessage = (e: MessageEvent) => {
        const evt = safeJsonParse((e as any)?.data);
        if (!evt || typeof evt !== "object") return;
        handlers.onEvent(evt);
      };

      es.onerror = (err) => {
        handlers.onStatus?.("Error", "EventSource error");
        handlers.onError?.(err);
      };

      return {
        close() {
          try {
            es.close();
          } catch {
            // ignore
          }
          handlers.onStatus?.("Disconnected");
        },
      };
    },

    async getJson(path: string): Promise<any> {
      return await requestJson("GET", normalizePath(path));
    },

    async putJson(path: string, body: any): Promise<any> {
      return await requestJson("PUT", normalizePath(path), body);
    },

    async postJson(path: string, body?: any): Promise<any> {
      return await requestJson("POST", normalizePath(path), body);
    },

    async deleteJson(path: string): Promise<any> {
      return await requestJson("DELETE", normalizePath(path));
    },
  };
}

async function requestJson(method: string, path: string, body?: any): Promise<any> {
  const res = await fetch(path, {
    method: String(method ?? "GET").toUpperCase(),
    headers: body !== undefined ? { "content-type": "application/json" } : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  const text = await res.text().catch(() => "");
  if (!res.ok) throw new Error(`HTTP ${res.status}${text ? `: ${trimForUi(text)}` : ""}`);

  const json = safeJsonParse(text);
  if (json === null) throw new Error("Invalid JSON response");
  return json;
}

function normalizePath(path: string): string {
  const p = String(path ?? "").trim();
  if (!p) throw new Error("path is required");
  return p.startsWith("/") ? p : `/${p}`;
}

function safeJsonParse(input: any): any | null {
  if (typeof input !== "string") return null;
  try {
    return JSON.parse(input);
  } catch {
    return null;
  }
}

function trimForUi(text: string, max = 800): string {
  const s = (text ?? "").trim();
  return s.length <= max ? s : s.slice(0, max) + "…";
}


