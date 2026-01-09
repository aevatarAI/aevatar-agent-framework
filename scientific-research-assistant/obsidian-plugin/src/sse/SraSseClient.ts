import { SseParser } from "./sseParser";

// Node-only (desktop). Avoid browser EventSource to reduce CORS surprises.
import http from "node:http";
import https from "node:https";

export type SseStatus = "Connecting" | "Connected" | "Disconnected" | "Error";

export interface SraSseClientOptions {
  baseUrl: string;
  headers?: Record<string, string> | (() => Record<string, string>);
}

export interface SraSseHandlers<TEvent = any> {
  onStatus: (status: SseStatus, detail?: string) => void;
  onEvent: (evt: TEvent) => void;
}

export class SraSseClient<TEvent = any> {
  private baseUrl: string;
  private headers: Record<string, string> | (() => Record<string, string>) | undefined;

  private stopped = true;
  private retryTimer: any = null;
  private attempt = 0;

  private req: http.ClientRequest | null = null;

  constructor(options: SraSseClientOptions) {
    this.baseUrl = normalizeBaseUrl(options.baseUrl);
    this.headers = options.headers;
  }

  setBaseUrl(baseUrl: string) {
    this.baseUrl = normalizeBaseUrl(baseUrl);
  }

  connect(sessionId: string, handlers: SraSseHandlers<TEvent>) {
    const sid = String(sessionId ?? "").trim();
    if (!sid) throw new Error("sessionId is required");
    this.disconnect();

    this.stopped = false;
    this.attempt = 0;

    const url = `${this.baseUrl}/api/sessions/${encodeURIComponent(sid)}/agui/events`;
    this.open(url, handlers);
  }

  disconnect() {
    this.stopped = true;
    this.attempt = 0;

    if (this.retryTimer) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }

    try {
      this.req?.destroy();
    } catch {
      // ignore
    }
    this.req = null;
  }

  // ============================================================
  //  Internals
  // ============================================================

  private open(url: string, handlers: SraSseHandlers<TEvent>) {
    if (this.stopped) return;
    handlers.onStatus("Connecting");

    const u = new URL(url);
    const isHttps = u.protocol === "https:";
    const mod = isHttps ? https : http;

    const headers = this.resolveHeaders();
    headers["Accept"] = "text/event-stream";
    headers["Cache-Control"] = "no-cache";

    const req = mod.request(
      {
        method: "GET",
        protocol: u.protocol,
        hostname: u.hostname,
        port: u.port ? Number(u.port) : undefined,
        path: u.pathname + u.search,
        headers,
      },
      (res) => {
        const status = Number(res.statusCode ?? 0) || 0;
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

        res.on("data", (chunk: string) => {
          if (this.stopped) return;
          const frames = parser.push(chunk);
          for (const data of frames) {
            const evt = safeJsonParse(data);
            if (!evt) continue;
            handlers.onEvent(evt as TEvent);
          }
        });

        res.on("end", () => {
          if (this.stopped) return;
          // flush any partial frame
          const rest = parser.flush();
          for (const data of rest) {
            const evt = safeJsonParse(data);
            if (!evt) continue;
            handlers.onEvent(evt as TEvent);
          }

          handlers.onStatus("Disconnected", "stream ended");
          this.scheduleReconnect(url, handlers, "stream ended");
        });

        res.on("error", (err: any) => {
          if (this.stopped) return;
          handlers.onStatus("Error", err?.message ?? "stream error");
          this.scheduleReconnect(url, handlers, err?.message ?? "stream error");
        });
      },
    );

    req.on("error", (err: any) => {
      if (this.stopped) return;
      handlers.onStatus("Error", err?.message ?? "request error");
      this.scheduleReconnect(url, handlers, err?.message ?? "request error");
    });

    req.end();
    this.req = req;
  }

  private scheduleReconnect(url: string, handlers: SraSseHandlers<TEvent>, reason: string) {
    if (this.stopped) return;
    if (this.retryTimer) return;

    this.attempt++;
    const delayMs = backoffMs(this.attempt);
    handlers.onStatus("Disconnected", `reconnecting in ${delayMs}ms (${reason})`);

    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      this.open(url, handlers);
    }, delayMs);
  }

  private resolveHeaders(): Record<string, string> {
    const h = typeof this.headers === "function" ? this.headers() : this.headers;
    return { ...(h ?? {}) };
  }
}

function normalizeBaseUrl(input: string): string {
  return (input ?? "").trim().replace(/\/+$/, "");
}

function backoffMs(attempt: number): number {
  // 0.5s, 1s, 2s, 4s... capped at 30s (+ small jitter)
  const base = Math.min(30_000, 500 * Math.pow(2, Math.max(0, attempt - 1)));
  const jitter = Math.floor(Math.random() * 200);
  return base + jitter;
}

function safeJsonParse(input: string): any | null {
  if (typeof input !== "string") return null;
  const s = input.trim();
  if (!s) return null;
  try {
    const obj = JSON.parse(s);
    return obj && typeof obj === "object" ? obj : null;
  } catch {
    return null;
  }
}


