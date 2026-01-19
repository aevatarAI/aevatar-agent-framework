import React from "react";

type AgUiMessage = {
  id: string;
  role: string;
  content: string;
  name?: string;
  toolCallId?: string;
};

type AgUiEvent = {
  type: string;
  timestamp?: number | null;
  [key: string]: unknown;
};

type AgUiStreamState = {
  connected: boolean;
  messages: AgUiMessage[];
  streamingMessageId: string | null;
  lastEventAt: number | null;
};

function getApiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_BASE_URL as string | undefined;
  if (!raw) return "";
  return raw.replace(/\/+$/, "");
}

export function useAgUiStream(enabled: boolean): AgUiStreamState {
  const [connected, setConnected] = React.useState(false);
  const [messages, setMessages] = React.useState<AgUiMessage[]>([]);
  const [streamingMessageId, setStreamingMessageId] = React.useState<string | null>(null);
  const [lastEventAt, setLastEventAt] = React.useState<number | null>(null);

  const messageIndexRef = React.useRef<Map<string, AgUiMessage>>(new Map());
  const orderRef = React.useRef<string[]>([]);

  const inferName = React.useCallback((id: string) => {
    if (id.includes(":chat:")) return "market_chat";
    if (id.includes(":cycle:")) return "trade_audit";
    return undefined;
  }, []);

  const fetchSnapshot = React.useCallback(async () => {
    try {
      const url = `${getApiBaseUrl()}/api/agui/messages`;
      const res = await fetch(url);
      if (!res.ok) return;
      const snapshot = (await res.json()) as AgUiMessage[];
      if (!Array.isArray(snapshot)) return;

      const map = new Map<string, AgUiMessage>();
      const order: string[] = [];
      snapshot.forEach((m) => {
        map.set(m.id, m);
        order.push(m.id);
      });
      messageIndexRef.current = map;
      orderRef.current = order;
      setMessages(snapshot);
    } catch {
      // ignore polling errors
    }
  }, []);

  React.useEffect(() => {
    if (!enabled) return;

    /* ------------------------------------------------------------
     * AG-UI SSE：MessagesSnapshot + TextMessage streaming
     * ------------------------------------------------------------ */
    const url = `${getApiBaseUrl()}/api/agui/events`;
    const es = new EventSource(url);

    const rebuildMessages = () => {
      const order = orderRef.current;
      const map = messageIndexRef.current;
      setMessages(order.map((id) => map.get(id)!).filter(Boolean));
    };

    const upsertMessage = (msg: AgUiMessage) => {
      const map = messageIndexRef.current;
      const order = orderRef.current;
      if (!map.has(msg.id)) order.push(msg.id);
      map.set(msg.id, msg);
      rebuildMessages();
    };

    const appendMessage = (id: string, role: string, delta: string) => {
      const map = messageIndexRef.current;
      const existing = map.get(id);
      if (!existing) {
        const name = role === "user" ? "user" : inferName(id);
        upsertMessage({ id, role, content: delta, name });
        return;
      }
      const next = { ...existing, content: `${existing.content ?? ""}${delta}` };
      map.set(id, next);
      rebuildMessages();
    };

    const resetMessages = (snapshot: AgUiMessage[]) => {
      const map = new Map<string, AgUiMessage>();
      const order: string[] = [];
      snapshot.forEach((m) => {
        map.set(m.id, m);
        order.push(m.id);
      });
      messageIndexRef.current = map;
      orderRef.current = order;
      setMessages(snapshot);
    };

    es.onopen = () => setConnected(true);
    es.onerror = () => setConnected(false);
    es.onmessage = (event) => {
      try {
        const parsed = JSON.parse(event.data) as AgUiEvent;
        setLastEventAt(Date.now());

        if (parsed.type === "MESSAGES_SNAPSHOT") {
          const snapshot = (parsed.messages as AgUiMessage[] | undefined) ?? [];
          resetMessages(snapshot);
          return;
        }

        if (parsed.type === "TEXT_MESSAGE_START") {
          const messageId = String(parsed.messageId ?? "");
          const role = String(parsed.role ?? "assistant");
          setStreamingMessageId(messageId);
          const name = role === "user" ? "user" : inferName(messageId);
          upsertMessage({ id: messageId, role, content: "", name });
          return;
        }

        if (parsed.type === "TEXT_MESSAGE_CONTENT") {
          const messageId = String(parsed.messageId ?? "");
          const delta = String(parsed.delta ?? "");
          if (messageId && delta) {
            appendMessage(messageId, "assistant", delta);
          }
          return;
        }

        if (parsed.type === "TEXT_MESSAGE_END") {
          const messageId = String(parsed.messageId ?? "");
          setStreamingMessageId((current) => (current === messageId ? null : current));
        }
      } catch {
        // ignore malformed events
      }
    };

    return () => {
      es.close();
      setConnected(false);
    };
  }, [enabled, inferName]);

  React.useEffect(() => {
    if (!enabled) return;

    const timer = setInterval(() => {
      const now = Date.now();
      const stale = !connected || !lastEventAt || now - lastEventAt > 15000;
      if (stale) fetchSnapshot();
    }, 5000);

    return () => clearInterval(timer);
  }, [enabled, connected, lastEventAt, fetchSnapshot]);

  return { connected, messages, streamingMessageId, lastEventAt };
}

