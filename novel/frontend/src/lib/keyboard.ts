import type { CommandItem } from "@/lib/types";

// ============================================================
//  Keyboard shortcuts (VSCode-ish)
// ============================================================

type ShortcutHandlers = {
  onSave: () => void;
  onOpenProject: () => void;
  onRefreshTree: () => void;
  onReconnectSidecar: () => void;
  onOpenCommandPalette: () => void;
  onToggleSidebar: () => void;
  onToggleAssistant: () => void;
};

function isEditableTarget(el: EventTarget | null): boolean {
  if (!el || !(el instanceof HTMLElement)) return false;
  const tag = el.tagName.toLowerCase();
  if (tag === "input" || tag === "textarea") return true;
  return el.isContentEditable;
}

export function installGlobalShortcuts(handlers: ShortcutHandlers): () => void {
  const onKeyDown = (e: KeyboardEvent) => {
    const mod = e.metaKey || e.ctrlKey;
    if (!mod) return;

    const key = e.key.toLowerCase();

    // Cmd/Ctrl+O: Open Project
    if (key === "o") {
      e.preventDefault();
      handlers.onOpenProject();
      return;
    }

    // Cmd/Ctrl+S: Save
    if (key === "s") {
      e.preventDefault();
      handlers.onSave();
      return;
    }

    // Cmd/Ctrl+R: Refresh Tree | Cmd/Ctrl+Shift+R: Reconnect
    if (key === "r") {
      e.preventDefault();
      if (e.shiftKey) handlers.onReconnectSidecar();
      else handlers.onRefreshTree();
      return;
    }

    // Cmd/Ctrl+P: Command palette
    if (key === "p") {
      e.preventDefault();
      handlers.onOpenCommandPalette();
      return;
    }

    // Cmd/Ctrl+B: Toggle sidebar
    if (key === "b") {
      e.preventDefault();
      handlers.onToggleSidebar();
      return;
    }

    // Cmd/Ctrl+J: Toggle assistant (Cursor-ish)
    if (key === "j") {
      // Avoid stealing from text inputs.
      if (isEditableTarget(e.target)) return;
      e.preventDefault();
      handlers.onToggleAssistant();
      return;
    }
  };

  window.addEventListener("keydown", onKeyDown);
  return () => window.removeEventListener("keydown", onKeyDown);
}

export function buildCoreCommands(args: {
  openProject: () => void;
  refreshTree: () => void;
  reconnectSidecar: () => void;
  toggleSidebar: () => void;
  toggleAssistant: () => void;
}): CommandItem[] {
  return [
    {
      id: "open-project",
      title: "打开项目根目录（SSOT）",
      subtitle: "选择一个包含 chapters/*.txt 与 artifacts/*.md 的项目目录",
      shortcut: "⌘O",
      run: args.openProject,
    },
    {
      id: "refresh-tree",
      title: "刷新文件树",
      shortcut: "⌘R",
      run: args.refreshTree,
    },
    {
      id: "reconnect-sidecar",
      title: "重连 Sidecar 事件流（SSE）",
      shortcut: "⌘⇧R",
      run: args.reconnectSidecar,
    },
    {
      id: "toggle-sidebar",
      title: "切换左侧栏",
      shortcut: "⌘B",
      run: args.toggleSidebar,
    },
    {
      id: "toggle-assistant",
      title: "切换右侧智能体面板",
      shortcut: "⌘J",
      run: args.toggleAssistant,
    },
  ];
}


