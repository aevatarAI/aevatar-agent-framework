import { useEffect } from "react";

// ============================================================
//  ApiKeyModal (Web host override)
//
//  中文说明：
//  - 这是“示范集成”：科研平台不再复制一份复杂 React UI
//  - 直接复用 `@aevatar/secrets-ui` 的静态页面（iframe）
//  - 页面内部调用同域 `/api/llm/*` 与 `/api/secrets/*`（后端仍然 loopback-only）
// ============================================================

type ApiKeyModalProps = {
  // NOTE: keep compatible with shared workbench signature; not used in iframe mode.
  transport: any;
  open: boolean;
  defaultProvider: string;
  providers: string[];
  onClose: () => void;
  onSaved?: () => void;
};

export default function ApiKeyModal(props: ApiKeyModalProps) {
  const { open, onClose, onSaved } = props;

  useEffect(() => {
    if (!open) return;

    function onKeyDown(ev: KeyboardEvent) {
      if (ev.key === "Escape") onClose();
    }

    function onMsg(ev: MessageEvent) {
      const data: any = ev?.data;
      if (!data || typeof data !== "object") return;
      if (data.type === "aevatar-secrets-ui:close") onClose();
      if (data.type === "aevatar-secrets-ui:saved") onSaved?.();
    }

    window.addEventListener("keydown", onKeyDown);
    window.addEventListener("message", onMsg);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("message", onMsg);
    };
  }, [open, onClose, onSaved]);

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4"
      onMouseDown={(e) => {
        // click outside to close
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="w-full max-w-3xl bg-white border border-slate-200 rounded-xl overflow-hidden shadow-xl">
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200">
          <div className="text-sm font-medium text-slate-900">Set API Key</div>
          <button onClick={onClose} className="p-2 rounded hover:bg-slate-100 transition" aria-label="Close">
            <span className="text-slate-700">✕</span>
          </button>
        </div>

        <div className="h-[80vh] bg-white">
          <iframe
            title="Aevatar Secrets UI"
            src="/secrets-ui/index.html"
            className="w-full h-full border-0"
          />
        </div>
      </div>
    </div>
  );
}


