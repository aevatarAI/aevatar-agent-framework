// ------------------------------------------------------------
//  Chat API
//  说明：
//  - 最小 HTTP 调用封装（/api/chat/stream）
//  - 只负责网络与错误处理
// ------------------------------------------------------------
export type ChatApi = {
  send: (text: string) => Promise<void>;
};

type ChatHandlers = {
  onStatus: (text: string) => void;
  onAssistantStart: () => void;
  onAssistantDelta: (text: string) => void;
  onAssistantDone: () => void;
};

export function createChatApi(backendUrl: string, handlers: ChatHandlers): ChatApi {
  async function send(text: string) {
    if (!backendUrl) {
      handlers.onStatus("missing backend url");
      return;
    }
    if (!text.trim()) {
      handlers.onStatus("");
      return;
    }

    const sessionId = process.env.AEVATAR_SESSION_ID ?? "unknown";
    const runId = String(Date.now());
    const debug =
      (process.env.AEVATAR_TUI_DEBUG ?? "") === "1" ||
      (process.env.AEVATAR_TUI_DEBUG ?? "").toLowerCase() === "true";
    handlers.onStatus("sending...");
    let started = false;
    const startedAt = Date.now();
    let firstChunkMs: number | null = null;
    let chunkCount = 0;
    let totalLen = 0;
    try {
      const requestInit: any = {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ text }),
      };
      if (debug) requestInit.verbose = true;
      // #region agent log
      fetch("http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          location: "api/chat.ts:send:start",
          message: "chat_send_start",
          data: { url: `${backendUrl}/api/chat/stream`, textLen: text.length, debug },
          timestamp: Date.now(),
          sessionId,
          runId,
          hypothesisId: "H1",
        }),
      }).catch(() => {});
      // #endregion
      const res = await fetch(`${backendUrl}/api/chat/stream`, requestInit);
      if (!res.ok) {
        let message = `error: http ${res.status}`;
        try {
          const json = (await res.json()) as any;
          if (json?.response) {
            message = `error: ${json.response}`;
          }
        } catch {
          // ignore json parsing error
        }
        handlers.onStatus(message);
        return;
      }

      if (!res.body) {
        handlers.onStatus("error: empty response body");
        return;
      }

      handlers.onAssistantStart();
      started = true;

      if (debug) handlers.onStatus("streaming...");
      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      while (true) {
        const { value, done } = await reader.read();
        if (done) break;
        if (value) {
          const chunk = decoder.decode(value, { stream: true });
          if (chunk) {
            if (firstChunkMs === null) firstChunkMs = Date.now() - startedAt;
            chunkCount += 1;
            totalLen += chunk.length;
            handlers.onAssistantDelta(chunk);
          }
        }
      }
      const tail = decoder.decode();
      if (tail) {
        if (firstChunkMs === null) firstChunkMs = Date.now() - startedAt;
        chunkCount += 1;
        totalLen += tail.length;
        handlers.onAssistantDelta(tail);
      }
      handlers.onAssistantDone();
      // #region agent log
      fetch("http://127.0.0.1:7242/ingest/602d30ab-17ad-45f0-a915-8a7cf2e47189", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          location: "api/chat.ts:send:done",
          message: "chat_stream_done",
          data: {
            chunkCount,
            totalLen,
            firstChunkMs,
            durationMs: Date.now() - startedAt,
          },
          timestamp: Date.now(),
          sessionId,
          runId,
          hypothesisId: "H1",
        }),
      }).catch(() => {});
      // #endregion
      handlers.onStatus("");
    } catch (e: any) {
      if (started) handlers.onAssistantDone();
      const message = e?.message ?? String(e);
      const detail = debug && e?.cause ? ` (cause: ${String(e.cause)})` : "";
      handlers.onStatus(`error: ${message}${detail}`);
    }
  }

  return { send };
}

