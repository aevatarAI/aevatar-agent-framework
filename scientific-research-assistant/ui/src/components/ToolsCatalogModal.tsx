import { Search, X } from "lucide-react";

type ToolSummary = {
  name: string;
  description?: string;
  category?: string;
  source?: string;
  tags?: string[];
};

export function ToolsCatalogModal(props: {
  open: boolean;
  onClose: () => void;
  tools: ToolSummary[];
  mcpTools: ToolSummary[];
  filteredTools: ToolSummary[];
  toolSearch: string;
  setToolSearch: (v: string) => void;
}) {
  const { open, onClose, tools, mcpTools, filteredTools, toolSearch, setToolSearch } = props;
  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 bg-black/40 flex items-center justify-center p-4">
      <div className="w-full max-w-3xl bg-white border border-slate-200 rounded-xl overflow-hidden">
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-200">
          <div className="text-sm font-medium text-slate-900">Tools Catalog</div>
          <button onClick={onClose} className="p-2 rounded hover:bg-slate-100 transition" aria-label="Close">
            <X size={16} />
          </button>
        </div>

        <div className="p-4 border-b border-slate-200">
          <div className="flex items-center gap-2 bg-slate-50 border border-slate-200 rounded-lg px-3 py-2">
            <Search size={14} className="text-slate-400" />
            <input
              value={toolSearch}
              onChange={(e) => setToolSearch(e.target.value)}
              placeholder="Search tool name / description…"
              className="w-full bg-transparent outline-none text-sm text-slate-900 placeholder:text-slate-400"
            />
            <div className="text-xs text-slate-500 font-mono">
              {mcpTools.length}/{tools.length}
            </div>
          </div>
          <div className="text-xs text-slate-500 mt-2">Showing up to 200 results (type to filter). MCP tools are tagged.</div>
        </div>

        <div className="max-h-[65vh] overflow-y-auto p-4 space-y-2">
          {filteredTools.length === 0 ? (
            <div className="text-sm text-slate-500">No tools matched.</div>
          ) : (
            filteredTools.map((t) => (
              <div key={t.name} className="border border-slate-200 rounded-lg p-3 hover:border-slate-300 transition bg-white">
                <div className="flex items-center justify-between gap-3">
                  <div className="text-sm text-slate-900 font-mono break-all">{t.name}</div>
                  <div className="flex items-center gap-2 shrink-0">
                    {(t.source || "").toUpperCase() === "MCP" && (
                      <span className="text-[10px] px-2 py-0.5 rounded bg-indigo-50 text-indigo-700 border border-indigo-200">
                        MCP
                      </span>
                    )}
                    {t.category && (
                      <span className="text-[10px] px-2 py-0.5 rounded bg-slate-50 text-slate-700 border border-slate-200">
                        {t.category}
                      </span>
                    )}
                  </div>
                </div>
                {t.description && <div className="text-xs text-slate-600 mt-1 whitespace-pre-wrap">{t.description}</div>}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}


