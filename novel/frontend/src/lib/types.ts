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


