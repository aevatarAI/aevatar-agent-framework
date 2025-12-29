import { readDir, readTextFile, writeTextFile } from "@tauri-apps/plugin-fs";
import type { TreeNode } from "@/lib/types";

// ============================================================
//  SSOT File Access (UI)
//
//  RULE:
//  - 正文 `.txt` + 产物 `.md` 是唯一真相源（SSOT）。
//  - UI 直接读写文件；sidecar 监听文件变更并产出派生资产/索引。
// ============================================================

const IGNORED_DIR_NAMES = new Set([
  ".git",
  ".index",
  "bin",
  "obj",
  "node_modules",
]);

type DirEntry = {
  name?: string;
  path: string;
  children?: DirEntry[];
};

function isInterestingFile(pathStr: string): boolean {
  const lower = pathStr.toLowerCase();
  return lower.endsWith(".txt") || lower.endsWith(".md");
}

function shouldIgnorePath(p: string): boolean {
  const parts = p.split(/[/\\]+/g);
  for (const part of parts) {
    if (!part) continue;
    if (part.startsWith(".")) {
      // allow `.demo_project`? It starts with '_' so ok.
      if (part === ".") continue;
      if (part === "..") continue;
      return true;
    }
    if (IGNORED_DIR_NAMES.has(part)) return true;
  }
  return false;
}

function entryName(e: DirEntry): string {
  if (e.name) return e.name;
  const parts = e.path.split(/[/\\]+/g);
  return parts[parts.length - 1] || e.path;
}

function sortNodes(nodes: TreeNode[]): TreeNode[] {
  return [...nodes].sort((a, b) => {
    if (a.isDir !== b.isDir) return a.isDir ? -1 : 1;
    return a.name.localeCompare(b.name);
  });
}

function toTreeNodes(entries: DirEntry[]): TreeNode[] {
  const out: TreeNode[] = [];
  for (const e of entries) {
    if (!e?.path) continue;
    if (shouldIgnorePath(e.path)) continue;

    const children = e.children ? toTreeNodes(e.children) : undefined;
    const isDir = Array.isArray(e.children);

    if (!isDir && !isInterestingFile(e.path)) continue;

    out.push({
      name: entryName(e),
      path: e.path,
      isDir,
      children: children?.length ? children : undefined,
    });
  }
  return sortNodes(out);
}

export async function loadProjectTree(projectRoot: string): Promise<TreeNode> {
  // `readDir` is recursive in the Tauri fs plugin (v2).
  const entries = (await readDir(projectRoot)) as unknown as DirEntry[];
  return {
    name: projectRoot.split(/[/\\]+/g).pop() || projectRoot,
    path: projectRoot,
    isDir: true,
    children: toTreeNodes(entries),
  };
}

export async function openTextFile(fullPath: string): Promise<string> {
  return await readTextFile(fullPath);
}

export async function saveTextFile(fullPath: string, content: string): Promise<void> {
  await writeTextFile(fullPath, content);
}


