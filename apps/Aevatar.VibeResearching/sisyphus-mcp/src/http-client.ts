/** Options for creating an HttpClient instance. */
export interface HttpClientOptions {
  /** Base URL for all requests (e.g., "http://localhost:8080"). */
  baseUrl: string;
  /** Request timeout in milliseconds. Default: 30000. */
  timeoutMs?: number;
}

/**
 * Error thrown by HttpClient for HTTP errors, timeouts, and connection failures.
 * The statusCode is 0 for non-HTTP errors (timeout, connection refused).
 */
export class HttpClientError extends Error {
  constructor(
    message: string,
    public readonly statusCode: number,
  ) {
    super(message);
    this.name = "HttpClientError";
  }
}

/** A single parsed Server-Sent Event. */
export interface SSEEvent {
  /** Event type (e.g., "result", "phase"). Defaults to "message" if not specified. */
  event: string;
  /** Raw data payload (concatenated data: lines). */
  data: string;
}

/**
 * A lightweight HTTP client built on native fetch.
 * Each microservice gets its own instance with a dedicated base URL.
 */
export class HttpClient {
  private readonly baseUrl: string;
  private readonly timeoutMs: number;

  constructor(options: HttpClientOptions) {
    this.baseUrl = options.baseUrl;
    this.timeoutMs = options.timeoutMs ?? 30_000;
  }

  /** Send a GET request. Returns parsed JSON response. */
  async get<T>(path: string, params?: Record<string, string>): Promise<T> {
    return this.request<T>("GET", path, { params });
  }

  /** Send a POST request with a JSON body. Returns parsed JSON response. */
  async post<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>("POST", path, { body });
  }

  /** Send a PUT request with a JSON body. Returns parsed JSON response. */
  async put<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>("PUT", path, { body });
  }

  /** Send a DELETE request with a JSON body. Returns parsed JSON response. */
  async delete<T>(path: string, body: unknown): Promise<T> {
    return this.request<T>("DELETE", path, { body });
  }

  /**
   * Send a GET request and return the raw text response.
   * Used for endpoints that return non-JSON content (e.g., text/markdown).
   */
  async getText(path: string, params?: Record<string, string>): Promise<string> {
    return this.request<string>("GET", path, { params, raw: true });
  }

  /**
   * Sends a POST request and consumes the response as an SSE event stream.
   * Returns an array of all parsed SSE events.
   *
   * Unlike post(), this method sends Accept: text/event-stream, reads the full
   * response as text, and parses SSE wire format. HTTP errors (e.g., 400 validation)
   * are handled via the standard handleErrorResponse path.
   */
  async postSSE(path: string, body: unknown, timeoutMs?: number): Promise<SSEEvent[]> {
    const url = new URL(path, this.baseUrl);
    const effectiveTimeout = timeoutMs ?? this.timeoutMs;
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), effectiveTimeout);

    try {
      const response = await fetch(url.toString(), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Accept": "text/event-stream",
        },
        body: JSON.stringify(body),
        signal: controller.signal,
      });

      if (!response.ok) {
        await this.handleErrorResponse(response);
      }

      const text = await response.text();
      return parseSSEText(text);
    } catch (error) {
      throw this.wrapFetchError(error, "POST", path);
    } finally {
      clearTimeout(timeoutId);
    }
  }

  private async request<T>(
    method: string,
    path: string,
    options?: { body?: unknown; params?: Record<string, string>; raw?: boolean },
  ): Promise<T> {
    const url = new URL(path, this.baseUrl);
    if (options?.params) {
      for (const [key, value] of Object.entries(options.params)) {
        url.searchParams.set(key, value);
      }
    }

    const headers: Record<string, string> = {};
    let fetchBody: string | undefined;
    if (options?.body !== undefined) {
      headers["Content-Type"] = "application/json";
      fetchBody = JSON.stringify(options.body);
    }

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), this.timeoutMs);

    try {
      const response = await fetch(url.toString(), {
        method,
        headers,
        body: fetchBody,
        signal: controller.signal,
      });

      if (!response.ok) {
        await this.handleErrorResponse(response);
      }

      if (options?.raw) {
        return (await response.text()) as T;
      }
      return (await response.json()) as T;
    } catch (error) {
      throw this.wrapFetchError(error, method, path);
    } finally {
      clearTimeout(timeoutId);
    }
  }

  /** Re-throws HttpClientError as-is; wraps AbortError and network errors. */
  private wrapFetchError(error: unknown, method: string, path: string): HttpClientError {
    if (error instanceof HttpClientError) {
      return error;
    }
    if (error instanceof Error && error.name === "AbortError") {
      return new HttpClientError(
        `Request timed out after ${this.timeoutMs}ms: ${method} ${path}`,
        0,
      );
    }
    return new HttpClientError(
      `Failed to connect to service at ${this.baseUrl}: ${(error as Error).message}`,
      0,
    );
  }

  /** Reads the response body once as text, then attempts JSON parse for error details. */
  private async handleErrorResponse(response: Response): Promise<never> {
    const text = await response.text().catch(() => "");
    let errorMessage: string;
    try {
      const body = JSON.parse(text) as { error?: string };
      errorMessage = body.error ?? `HTTP ${response.status}`;
    } catch {
      errorMessage = text.slice(0, 500) || `HTTP ${response.status}`;
    }
    throw new HttpClientError(errorMessage, response.status);
  }
}

/**
 * Parses raw SSE text into an array of SSEEvent objects.
 * Handles the standard SSE wire format:
 *   event: <type>\n
 *   data: <line1>\n
 *   data: <line2>\n
 *   \n
 *
 * Events without an explicit event: field default to type "message".
 * Comment lines (starting with `:`) and unrecognized fields are skipped.
 */
function parseSSEText(text: string): SSEEvent[] {
  const events: SSEEvent[] = [];
  const lines = text.split("\n");

  let currentEvent = "";
  let dataLines: string[] = [];

  for (const line of lines) {
    if (line === "") {
      // Blank line = event boundary
      if (dataLines.length > 0) {
        events.push({
          event: currentEvent || "message",
          data: dataLines.join("\n"),
        });
      }
      currentEvent = "";
      dataLines = [];
      continue;
    }

    if (line.startsWith(":")) {
      // SSE comment line -- skip
      continue;
    }

    if (line.startsWith("event:")) {
      currentEvent = line.slice("event:".length).trim();
    } else if (line.startsWith("data:")) {
      dataLines.push(line.slice("data:".length).trimStart());
    }
    // Ignore id:, retry:, and any other unrecognized fields
  }

  // Handle trailing event without final blank line
  if (dataLines.length > 0) {
    events.push({
      event: currentEvent || "message",
      data: dataLines.join("\n"),
    });
  }

  return events;
}
