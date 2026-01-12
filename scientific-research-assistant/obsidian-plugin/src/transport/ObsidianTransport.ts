import { requestUrl } from "obsidian";

import { SraApiClient } from "../api/SraApiClient";
import { SraApiError } from "../api/types";
import { SraSseClient } from "../sse/SraSseClient";

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
//  ObsidianTransport
//
//  中文说明：
//  - 运行在 Obsidian Desktop (Electron + Node) 环境
//  - HTTP: 使用 requestUrl（避免 CORS）
//  - SSE: 复用 Node http/https 实现（SraSseClient）
//  - 提供 capabilities，用于 remote baseUrl 的安全降级（files/key reveal）
// ============================================================

export interface ObsidianTransportOptions {
  baseUrl: string;
  timeoutMs?: number;
  headers?: Record<string, string> | (() => Record<string, string>);
}

export function createObsidianTransport(options: ObsidianTransportOptions): SraTransport {
  const baseUrl = normalizeBaseUrl(options.baseUrl);
  const timeoutMs = clampTimeout(options.timeoutMs);
  const headers = options.headers;

  const api = new SraApiClient({ baseUrl, timeoutMs, headers });
  const sse = new SraSseClient<SraAgUiEvent>({ baseUrl, headers });

  const capabilities: SraTransportCapabilities = {
    filesApi: isLoopbackBaseUrl(baseUrl),
    revealApiKey: isLoopbackBaseUrl(baseUrl),
    nodeSse: true,
  };

  return {
    capabilities,

    // ============================================================
    //  Core endpoints
    // ============================================================
    health: () => api.health(),
    info: (): Promise<SraApiInfoResponse> => api.info(),

    listSessions: (): Promise<SraListSessionsResponse> => api.listSessions(),
    createSession: (providerName?: string) => api.createSession(providerName),
    sendInput: (sessionId: string, input: SraSendInputRequest) => api.sendInput(sessionId, input),
    getDeliverables: (sessionId: string): Promise<SraDeliverablesResponse> => api.getDeliverables(sessionId),
    uploadAttachments: (sessionId: string, files: SraUploadFile[]) => api.uploadAttachments(sessionId, files),

    // ============================================================
    //  Streaming (AG-UI SSE)
    // ============================================================
    connectAgUiEvents(sessionId: string, handlers: { onStatus?: (status: SraTransportStatus, detail?: string) => void; onEvent: (evt: SraAgUiEvent) => void; onError?: (err: unknown) => void; }): SraAgUiConnection {
      try {
        sse.connect(sessionId, {
          onStatus: (status, detail) => {
            handlers.onStatus?.(status as SraTransportStatus, detail);
          },
          onEvent: (evt) => {
            handlers.onEvent(evt);
          },
        });
      } catch (err) {
        handlers.onStatus?.("Error", (err as any)?.message ?? "connect error");
        handlers.onError?.(err);
      }

      return {
        close() {
          try {
            sse.disconnect();
          } catch {
            // ignore
          }
          handlers.onStatus?.("Disconnected");
        },
      };
    },

    // ============================================================
    //  Generic helpers (used by panel/controller refactors)
    // ============================================================
    async getJson(path: string): Promise<any> {
      return await requestJson("GET", makeUrl(baseUrl, path), headers, timeoutMs);
    },
    async putJson(path: string, body: any): Promise<any> {
      return await requestJson("PUT", makeUrl(baseUrl, path), headers, timeoutMs, body);
    },
    async postJson(path: string, body?: any): Promise<any> {
      return await requestJson("POST", makeUrl(baseUrl, path), headers, timeoutMs, body);
    },
    async deleteJson(path: string): Promise<any> {
      return await requestJson("DELETE", makeUrl(baseUrl, path), headers, timeoutMs);
    },
  };
}

// ============================================================
//  Internals
// ============================================================

function normalizeBaseUrl(input: string): string {
  return (input ?? "").trim().replace(/\/+$/, "");
}

function clampTimeout(ms?: number): number {
  const n = Number.isFinite(ms as any) ? (ms as number) : 15000;
  return Math.max(1000, Math.min(120_000, n));
}

function makeUrl(baseUrl: string, path: string): string {
  const b = normalizeBaseUrl(baseUrl);
  const p = String(path ?? "").trim();
  if (!p) throw new Error("path is required");
  const rel = p.startsWith("/") ? p : `/${p}`;
  return `${b}${rel}`;
}

function isLoopbackBaseUrl(baseUrl: string): boolean {
  try {
    const u = new URL(baseUrl);
    const h = (u.hostname ?? "").toLowerCase();
    return h === "localhost" || h === "127.0.0.1" || h === "::1";
  } catch {
    return false;
  }
}

function resolveHeaders(headers: Record<string, string> | (() => Record<string, string>) | undefined, contentType?: string): Record<string, string> {
  const h = typeof headers === "function" ? headers() : headers;
  const out: Record<string, string> = { ...(h ?? {}) };
  if (contentType) out["Content-Type"] = contentType;
  return out;
}

async function requestJson(method: string, url: string, headers: Record<string, string> | (() => Record<string, string>) | undefined, timeoutMs: number, body?: any): Promise<any> {
  const m = String(method ?? "GET").toUpperCase();

  const res = await requestUrl({
    url,
    method: m,
    headers: resolveHeaders(headers, body !== undefined ? "application/json" : undefined) as any,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    timeout: timeoutMs as any,
    throw: false,
  } as any);

  const status = Number((res as any)?.status ?? 0) || 0;
  const text = typeof (res as any)?.text === "string" ? ((res as any).text as string) : "";

  if (status < 200 || status >= 300) {
    throw new SraApiError(`HTTP ${status}`, { status, url, responseText: trimForUi(text) });
  }

  try {
    const json = (res as any)?.json;
    if (json && typeof json === "object") return json;
  } catch {
    // ignore
  }

  try {
    return JSON.parse(text);
  } catch {
    throw new SraApiError("Invalid JSON response", { status, url, responseText: trimForUi(text) });
  }
}

function trimForUi(text: string, max = 800): string {
  const s = (text ?? "").trim();
  return s.length <= max ? s : s.slice(0, max) + "…";
}


