// Minimal local implementation of "@agui/sdk" used by this repo.
//
// WHY:
// - The real "@agui/sdk" is an external dependency.
// - In this monorepo environment we keep a small shim so the AG-UI integration
//   is runnable without extra package installs.
//
// Behavior:
// - Connects to an AG-UI SSE endpoint (message events with JSON payload).
// - Dispatches to handlers by `evt.type`.

type Handler = (event: any) => void;

export class AgUiClient {
  private readonly es: EventSource;
  private readonly handlers = new Map<string, Set<Handler>>();

  constructor(url: string, _options?: any) {
    this.es = new EventSource(url);

    this.es.onopen = (e: Event) => {
      this.emit("open", e);
    };

    this.es.onmessage = (e: MessageEvent) => {
      const evt = safeJsonParse((e as any)?.data);
      if (!evt || typeof evt !== "object") return;
      const type = (evt as any).type;
      if (!type || typeof type !== "string") return;
      this.emit(type, evt);
    };

    this.es.onerror = (err) => {
      this.emit("error", err);
    };
  }

  on(type: string, handler: Handler) {
    if (!type || !handler) return;
    const key = String(type);
    const set = this.handlers.get(key) ?? new Set<Handler>();
    set.add(handler);
    this.handlers.set(key, set);
  }

  close() {
    try {
      this.es.close();
    } catch {
      // ignore
    }
  }

  private emit(type: string, event: any) {
    const set = this.handlers.get(type);
    if (!set || set.size === 0) return;
    for (const h of set) {
      try {
        h(event);
      } catch {
        // ignore handler errors
      }
    }
  }
}

function safeJsonParse(input: any): any | null {
  if (typeof input !== "string") return null;
  try {
    return JSON.parse(input);
  } catch {
    return null;
  }
}


