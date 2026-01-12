import { requestUrl } from "obsidian";

import type {
  SraApiInfoResponse,
  SraCreateSessionResponse,
  SraDeliverablesResponse,
  SraListSessionsResponse,
  SraSendInputRequest,
  SraSendInputResponse,
} from "./types";
import { SraApiError } from "./types";
import { buildMultipartBody, type MultipartFilePart } from "./multipart";

export interface SraApiClientOptions {
  baseUrl: string;
  timeoutMs?: number;

  // Future-proofing for remote deployments (auth headers, etc.)
  headers?: Record<string, string> | (() => Record<string, string>);
}

export class SraApiClient {
  private baseUrl: string;
  private timeoutMs: number;
  private headers: Record<string, string> | (() => Record<string, string>) | undefined;

  constructor(options: SraApiClientOptions) {
    this.baseUrl = normalizeBaseUrl(options.baseUrl);
    this.timeoutMs = clampTimeout(options.timeoutMs);
    this.headers = options.headers;
  }

  setBaseUrl(baseUrl: string) {
    this.baseUrl = normalizeBaseUrl(baseUrl);
  }

  setTimeoutMs(timeoutMs: number) {
    this.timeoutMs = clampTimeout(timeoutMs);
  }

  async health(): Promise<{ ok: boolean; status: number; body: string }> {
    const url = this.url("/health");
    const res = await this.requestText("GET", url);
    return { ok: res.status >= 200 && res.status < 300, status: res.status, body: res.text };
  }

  async info(): Promise<SraApiInfoResponse> {
    const url = this.url("/api/info");
    return await this.requestJson<SraApiInfoResponse>("GET", url);
  }

  async createSession(providerName?: string): Promise<{ sessionId: string }> {
    const url = this.url("/api/sessions");
    const body = providerName ? { providerName } : {};
    const json = await this.requestJson<SraCreateSessionResponse>("POST", url, body);
    if (!json?.sessionId) throw new SraApiError("Invalid response: missing sessionId", { status: 0, url, responseText: "" });
    return { sessionId: String(json.sessionId) };
  }

  async listSessions(): Promise<SraListSessionsResponse> {
    const url = this.url("/api/sessions");
    const json = await this.requestJson<any>("GET", url);
    return {
      count: Number(json?.count ?? 0) || 0,
      sessions: Array.isArray(json?.sessions) ? (json.sessions as any[]) : [],
    };
  }

  async sendInput(sessionId: string, input: SraSendInputRequest): Promise<{ runId: string }> {
    const sid = String(sessionId ?? "").trim();
    if (!sid) throw new Error("sessionId is required");
    if (!input?.message || !String(input.message).trim()) throw new Error("message is required");

    const url = this.url(`/api/sessions/${encodeURIComponent(sid)}/input`);
    const json = await this.requestJson<SraSendInputResponse>("POST", url, input);
    if (!json?.runId) throw new SraApiError("Invalid response: missing runId", { status: 0, url, responseText: "" });
    return { runId: String(json.runId) };
  }

  async getDeliverables(sessionId: string): Promise<SraDeliverablesResponse> {
    const sid = String(sessionId ?? "").trim();
    if (!sid) throw new Error("sessionId is required");

    const url = this.url(`/api/sessions/${encodeURIComponent(sid)}/deliverables`);
    return await this.requestJson<SraDeliverablesResponse>("GET", url);
  }

  async uploadAttachments(
    sessionId: string,
    files: Array<{ filename: string; contentType: string; data: Uint8Array }>,
  ): Promise<{ attachmentPaths: string[] }> {
    const sid = String(sessionId ?? "").trim();
    if (!sid) throw new Error("sessionId is required");

    const url = this.url(`/api/sessions/${encodeURIComponent(sid)}/uploads`);
    const parts: MultipartFilePart[] = (files ?? []).map((f) => ({
      fieldName: "file",
      filename: f.filename,
      contentType: f.contentType,
      data: f.data,
    }));

    const mp = buildMultipartBody(parts);
    const res = await requestUrl({
      url,
      method: "POST",
      headers: this.resolveHeaders(mp.contentType) as any,
      body: mp.body as any,
      timeout: this.timeoutMs as any,
      throw: false,
    } as any);

    const status = Number((res as any)?.status ?? 0) || 0;
    const text = typeof (res as any)?.text === "string" ? ((res as any).text as string) : "";
    if (status < 200 || status >= 300) {
      throw new SraApiError(`HTTP ${status}`, { status, url, responseText: trimForUi(text) });
    }

    let json: any = null;
    try {
      json = (res as any)?.json ?? JSON.parse(text);
    } catch {
      // ignore
    }

    const paths = Array.isArray(json?.attachmentPaths) ? json.attachmentPaths : [];
    return { attachmentPaths: paths.map((x: any) => String(x ?? "").trim()).filter((x: string) => x.length > 0) };
  }

  // ============================================================
  //  Internals
  // ============================================================

  private url(path: string): string {
    const p = String(path ?? "").trim();
    if (!p.startsWith("/")) return `${this.baseUrl}/${p}`;
    return `${this.baseUrl}${p}`;
  }

  private resolveHeaders(contentType?: string): Record<string, string> {
    const h = typeof this.headers === "function" ? this.headers() : this.headers;
    const out: Record<string, string> = { ...(h ?? {}) };
    if (contentType) out["Content-Type"] = contentType;
    return out;
  }

  private async requestText(method: string, url: string, body?: any): Promise<{ status: number; text: string }> {
    const m = String(method ?? "GET").toUpperCase();
    const timeout = this.timeoutMs;

    const res = await requestUrl({
      url,
      method: m,
      headers: this.resolveHeaders(typeof body === "string" ? "application/json" : undefined) as any,
      body: body ? (typeof body === "string" ? body : JSON.stringify(body)) : undefined,
      timeout: timeout as any,
      throw: false,
    } as any);

    const text = typeof (res as any)?.text === "string" ? ((res as any).text as string) : "";
    const status = Number((res as any)?.status ?? 0) || 0;

    if (status < 200 || status >= 300) {
      throw new SraApiError(`HTTP ${status}`, { status, url, responseText: trimForUi(text) });
    }

    return { status, text };
  }

  private async requestJson<T>(method: string, url: string, body?: any): Promise<T> {
    const m = String(method ?? "GET").toUpperCase();
    const timeout = this.timeoutMs;

    const res = await requestUrl({
      url,
      method: m,
      headers: this.resolveHeaders(body !== undefined ? "application/json" : undefined) as any,
      body: body !== undefined ? JSON.stringify(body) : undefined,
      timeout: timeout as any,
      throw: false,
    } as any);

    const status = Number((res as any)?.status ?? 0) || 0;
    const text = typeof (res as any)?.text === "string" ? ((res as any).text as string) : "";

    if (status < 200 || status >= 300) {
      throw new SraApiError(`HTTP ${status}`, { status, url, responseText: trimForUi(text) });
    }

    try {
      const json = (res as any)?.json;
      if (json && typeof json === "object") return json as T;
    } catch {
      // ignore
    }

    try {
      return JSON.parse(text) as T;
    } catch {
      throw new SraApiError("Invalid JSON response", { status, url, responseText: trimForUi(text) });
    }
  }
}

function normalizeBaseUrl(input: string): string {
  const s = (input ?? "").trim();
  return s.replace(/\/+$/, "");
}

function clampTimeout(ms?: number): number {
  const n = Number.isFinite(ms as any) ? (ms as number) : 15000;
  return Math.max(1000, Math.min(120_000, n));
}

function trimForUi(text: string, max = 800): string {
  const s = (text ?? "").trim();
  return s.length <= max ? s : s.slice(0, max) + "…";
}


