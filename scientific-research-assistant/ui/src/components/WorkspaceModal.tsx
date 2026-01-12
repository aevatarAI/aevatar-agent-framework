import { X } from "lucide-react";

export function WorkspaceModal(props: { open: boolean; onClose: () => void; workspace: any }) {
  const { open, onClose, workspace } = props;
  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="w-full max-w-3xl bg-white border border-slate-200 rounded-xl overflow-hidden">
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200">
          <div className="text-sm font-medium text-slate-900">Workspace State (STATE_SNAPSHOT)</div>
          <button onClick={onClose} className="p-2 rounded hover:bg-slate-100 transition" aria-label="Close">
            <X size={16} />
          </button>
        </div>

        <div className="p-4">
          {workspace ? (
            <pre className="text-xs text-slate-900 whitespace-pre-wrap break-words max-h-[70vh] overflow-auto bg-slate-50 border border-slate-200 rounded-lg p-3">
              {JSON.stringify(workspace, null, 2)}
            </pre>
          ) : (
            <div className="text-sm text-slate-500">No workspace state yet (run vibe once).</div>
          )}
        </div>
      </div>
    </div>
  );
}


