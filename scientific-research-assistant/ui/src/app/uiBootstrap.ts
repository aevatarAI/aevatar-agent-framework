import type { MessageStore, ToolOutput } from "./messageStore";

// ------------------------------------------------------------
//  UI bootstrap custom events
//
//  中文说明：
//  - 通过 CUSTOM 事件回灌“前端投影”，让刷新后无需 SSE replay 也能恢复视图
//  - 保持 controller 代码可读
// ------------------------------------------------------------

export function applyUiBootstrapCustomEvent(args: {
  name: string;
  value: any;
  store: MessageStore;
  messageMetaRef: { current: Record<string, any> };
  setRunSteps: (v: { order: string[]; map: Record<string, any> }) => void;
}) {
  const { name, value: v, store, messageMetaRef, setRunSteps } = args;

  const upsertTool = (messageId: string, tool: ToolOutput) => {
    store.upsertTool(messageId, tool);
  };

  if (name === "aevatar.vibe.message_meta_snapshot") {
    const items: any[] = Array.isArray(v?.items) ? v.items : [];
    for (const it of items) {
      const messageId = String(it?.messageId ?? "").trim();
      if (!messageId) continue;
      messageMetaRef.current[messageId] = {
        messageId,
        agent: it?.agent ?? "",
        stepName: it?.stepName ?? "",
        providerName: it?.providerName ?? "",
      };
    }
    return { handled: true as const };
  }

  if (name === "aevatar.ui.tools_snapshot") {
    const arr: any[] = Array.isArray(v?.tools) ? v.tools : [];
    for (const t of arr) {
      const messageId = String(t?.messageId ?? "").trim();
      const toolCallId = String(t?.toolCallId ?? "").trim();
      const toolName = String(t?.toolName ?? "").trim();
      const status = String(t?.status ?? "").trim();
      if (!messageId || !toolCallId || !toolName) continue;
      upsertTool(messageId, {
        toolCallId,
        name: toolName,
        status: status === "done" ? "done" : "running",
        error: t?.error ? String(t.error) : undefined,
        resultPreview: t?.resultPreview ? String(t.resultPreview) : undefined,
      });
    }
    return { handled: true as const };
  }

  if (name === "aevatar.ui.run_steps_snapshot") {
    const order: string[] = Array.isArray(v?.order) ? v.order.map((x: any) => String(x)).filter(Boolean) : [];
    const map = v?.map && typeof v.map === "object" ? v.map : {};
    if (order.length > 0) setRunSteps({ order, map });
    return { handled: true as const };
  }

  return { handled: false as const };
}


