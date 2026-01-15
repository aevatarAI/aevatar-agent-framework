// ============================================================
//  UI-local Types (NOT cross-boundary)
//
//  NOTE:
//  - UI <-> Sidecar messages are Protobuf (see `src/gen/*`).
//  - These are UI-only view models and can be plain TypeScript.
// ============================================================

export type TreeNode = {
  name: string;
  path: string;
  isDir: boolean;
  children?: TreeNode[];
};

export type OpenFile = {
  path: string;
  name: string;
  language: "markdown" | "plaintext";
  content: string;
  isDirty: boolean;
  /** UI-only loading state (e.g. fetching from sidecar). */
  isLoading?: boolean;
};

export type CursorPos = {
  line: number;
  column: number;
};

export type CommandItem = {
  id: string;
  title: string;
  subtitle?: string;
  shortcut?: string;
  run: () => void;
};

// ============================================================
//  NovelOS View Models
// ============================================================

// Library categorization is based on the project-root directory layout:
// - chapters/ -> 正文
// - objects/  -> 设定
// - roles/    -> 人物小传
// - rules/    -> 规则
// - others    -> 其他（保留目录名，避免同名文件混淆）
export type LibraryCategory = "chapters" | "objects" | "roles" | "rules" | "other";

export type LibraryFileItem = {
  /** Absolute path */
  path: string;
  /** Path relative to project root (best-effort) */
  relPath: string;
  name: string;
};

export type LibraryIndex = Record<LibraryCategory, LibraryFileItem[]>;

export type ChatRole = "user" | "assistant" | "system";

export type ChatMessage = {
  id: string;
  role: ChatRole;
  text: string;
  ts: number;
};


