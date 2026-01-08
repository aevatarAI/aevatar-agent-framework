export type ChatRole = "user" | "assistant" | "system" | "tool" | string;

export type ToolOutput = {
  toolCallId: string;
  name: string;
  status: "running" | "done";
  isMcp?: boolean;
  success?: boolean;
  durationMs?: number;
  error?: string;
  resultPreview?: string;
};

export type Message = {
  id: string;
  role: ChatRole;
  content: string;
  toolOutputs?: ToolOutput[];
  isFinal?: boolean;
};

// ============================================================
//  MessageStore (perf)
//
//  Goal:
//  - Avoid re-rendering the whole chat list on every streamed token.
//  - Only the message being appended re-renders.
// ============================================================

export class MessageStore {
  private map = new Map<string, Message>();
  private order: string[] = [];
  private orderVer = 0;
  private msgVer = new Map<string, number>();
  private listeners = new Set<() => void>();

  subscribe = (cb: () => void) => {
    this.listeners.add(cb);
    return () => this.listeners.delete(cb);
  };

  private emit() {
    for (const cb of this.listeners) cb();
  }

  getOrderVersion = () => this.orderVer;
  getOrder = () => this.order;
  getMessageVersion = (id: string) => this.msgVer.get(id) ?? 0;
  getMessage = (id: string) => this.map.get(id);

  clear() {
    this.map.clear();
    this.order = [];
    this.msgVer.clear();
    this.orderVer++;
    this.emit();
  }

  resetFromSnapshot(messages: Message[]) {
    this.map.clear();
    this.msgVer.clear();
    this.order = [];
    for (const m of messages) {
      if (!m?.id) continue;
      const msg: Message = { ...m, isFinal: true };
      this.map.set(msg.id, msg);
      this.msgVer.set(msg.id, 1);
      this.order.push(msg.id);
    }
    this.orderVer++;
    this.emit();
  }

  upsertMessage(msg: Message) {
    const id = String(msg?.id ?? "").trim();
    if (!id) return;

    const existing = this.map.get(id);
    const isNew = !existing;
    const next: Message = existing ? { ...existing, ...msg } : { ...msg };
    this.map.set(id, next);
    this.msgVer.set(id, (this.msgVer.get(id) ?? 0) + 1);

    if (isNew) {
      this.order = [...this.order, id];
      this.orderVer++;
    }

    this.emit();
  }

  ensureMessage(id: string, role: ChatRole) {
    const mid = String(id || "").trim();
    if (!mid) return;
    if (this.map.has(mid)) return;
    this.map.set(mid, { id: mid, role, content: "", isFinal: false });
    this.msgVer.set(mid, 1);
    this.order = [...this.order, mid];
    this.orderVer++;
    this.emit();
  }

  appendDelta(id: string, delta: string) {
    const mid = String(id || "").trim();
    if (!mid) return;
    const d = String(delta ?? "");
    if (!d) return;

    const existing = this.map.get(mid);
    if (!existing) {
      this.map.set(mid, { id: mid, role: "assistant", content: d, isFinal: false });
      this.msgVer.set(mid, 1);
      this.order = [...this.order, mid];
      this.orderVer++;
      this.emit();
      return;
    }

    existing.content = `${existing.content}${d}`;
    this.map.set(mid, existing);
    this.msgVer.set(mid, (this.msgVer.get(mid) ?? 0) + 1);
    this.emit();
  }

  markFinal(id: string) {
    const mid = String(id || "").trim();
    if (!mid) return;
    const existing = this.map.get(mid);
    if (!existing) return;
    if (existing.isFinal) return;
    existing.isFinal = true;
    this.map.set(mid, existing);
    this.msgVer.set(mid, (this.msgVer.get(mid) ?? 0) + 1);
    this.emit();
  }

  upsertTool(messageId: string, tool: ToolOutput) {
    const mid = String(messageId || "").trim();
    if (!mid || !tool?.toolCallId) return;
    const existing = this.map.get(mid);
    if (!existing) return;

    const arr = [...(existing.toolOutputs || [])];
    const idx = arr.findIndex((x) => x.toolCallId === tool.toolCallId);
    if (idx >= 0) arr[idx] = { ...arr[idx], ...tool };
    else arr.push(tool);

    this.map.set(mid, { ...existing, toolOutputs: arr });
    this.msgVer.set(mid, (this.msgVer.get(mid) ?? 0) + 1);
    this.emit();
  }
}
