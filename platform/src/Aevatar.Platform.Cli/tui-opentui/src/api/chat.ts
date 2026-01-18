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

    const debug =
      (process.env.AEVATAR_TUI_DEBUG ?? "") === "1" ||
      (process.env.AEVATAR_TUI_DEBUG ?? "").toLowerCase() === "true";
    handlers.onStatus("sending...");
    let started = false;
    try {
      const requestInit: any = {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ text }),
      };
      if (debug) requestInit.verbose = true;
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
          if (chunk) handlers.onAssistantDelta(chunk);
        }
      }
      const tail = decoder.decode();
      if (tail) handlers.onAssistantDelta(tail);
      handlers.onAssistantDone();
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

