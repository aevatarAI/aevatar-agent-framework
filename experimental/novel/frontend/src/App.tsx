import React, { useEffect, useMemo, useRef, useState } from "react";
import { Panel, PanelGroup, PanelResizeHandle } from "react-resizable-panels";
import MonacoEditor from "@monaco-editor/react";
import { loader as monacoLoader } from "@monaco-editor/react";
import type * as monaco from "monaco-editor";
import { open as openDialog } from "@tauri-apps/plugin-dialog";

import ChatPanel from "@/components/ChatPanel";
import CommandPalette from "@/components/CommandPalette";
import LibraryPanel from "@/components/LibraryPanel";
import StatusBar from "@/components/StatusBar";
import Tabs from "@/components/Tabs";

import { buildCoreCommands, installGlobalShortcuts } from "@/lib/keyboard";
import { fileUriToPath } from "@/lib/uri";
import type { ChatMessage, CursorPos, LibraryIndex, LibraryFileItem, OpenFile } from "@/lib/types";
import {
  connectSidecarEvents,
  defaultSidecarUrl,
  getProjectRootInfo,
  listDirectory,
  normalizeBaseUrl,
  readTextFile,
  setProjectRoot,
  smartContinue,
  writeTextFile,
  type SidecarConnectionState,
} from "@/lib/sidecar";

import type { SidecarEvent } from "@/gen/novel_sidecar_pb";

function fileNameOf(fullPath: string): string {
  const parts = fullPath.split(/[/\\]+/g);
  return parts[parts.length - 1] || fullPath;
}

function languageOf(fullPath: string): "markdown" | "plaintext" {
  const lower = fullPath.toLowerCase();
  if (lower.endsWith(".md")) return "markdown";
  return "plaintext";
}

function clampEvents(evts: SidecarEvent[], max: number): SidecarEvent[] {
  if (evts.length <= max) return evts;
  return evts.slice(evts.length - max);
}

const EMPTY_LIBRARY: LibraryIndex = {
  chapters: [],
  objects: [],
  roles: [],
  rules: [],
  other: [],
};

function newId(prefix: string): string {
  try {
    // modern browsers / Tauri webview
    return `${prefix}_${crypto.randomUUID()}`;
  } catch {
    return `${prefix}_${Date.now()}_${Math.random().toString(16).slice(2)}`;
  }
}

function relPathOf(projectRoot: string, fullPath: string): string {
  const root = (projectRoot ?? "").trim().replace(/[/\\]+$/g, "");
  if (!root) return fullPath;
  if (!fullPath.startsWith(root)) return fullPath;
  return fullPath.slice(root.length).replace(/^[/\\]+/g, "") || fullPath;
}

function categoryFor(relPath: string): keyof LibraryIndex {
  const parts = relPath.split(/[/\\]+/g).filter(Boolean);
  const top = String(parts[0] ?? "").toLowerCase();

  // Strict, project-root layout driven categorization.
  if (top === "chapters") return "chapters";
  if (top === "objects") return "objects";
  if (top === "roles") return "roles";
  if (top === "rules") return "rules";
  return "other";
}

const RESERVED_TOP_DIRS = new Set(["chapters", "objects", "roles", "rules"]);
const IGNORED_SCAN_DIR_NAMES = new Set([".git", ".index", "bin", "obj", "node_modules", "dist", "target"]);

function baseName(relPath: string): string {
  const parts = relPath.split(/[/\\]+/g).filter(Boolean);
  return parts[parts.length - 1] ?? relPath;
}

function buildLibraryIndexFromRelativePaths(paths: string[]): LibraryIndex {
  const index: LibraryIndex = {
    chapters: [],
    objects: [],
    roles: [],
    rules: [],
    other: [],
  };

  for (const raw of paths) {
    const rel = String(raw ?? "").replace(/^[/\\]+/g, "");
    if (!rel) continue;
    const kind = categoryFor(rel);
    index[kind].push({ path: rel, relPath: rel, name: fileNameOf(rel) });
  }

  const sorter = (a: LibraryFileItem, b: LibraryFileItem) => a.relPath.localeCompare(b.relPath);
  index.chapters.sort(sorter);
  index.objects.sort(sorter);
  index.roles.sort(sorter);
  index.rules.sort(sorter);
  index.other.sort(sorter);
  return index;
}

async function scanSupportedTextFiles(baseUrl: string): Promise<string[]> {
  const root = await listDirectory({ baseUrl, relativePath: "", includeDirectories: true, includeFiles: true, onlySupportedTextFiles: true });

  const topDirs: string[] = [];
  const rootFiles: string[] = [];

  for (const e of (root as any).entries ?? []) {
    const rel = String(e?.relativePath ?? e?.relative_path ?? "").replace(/^[/\\]+/g, "");
    if (!rel) continue;
    if (Boolean(e?.isDirectory ?? e?.is_directory)) {
      topDirs.push(baseName(rel));
    } else {
      rootFiles.push(rel);
    }
  }

  const topDirSet = new Set(topDirs.map((x) => x.toLowerCase()));

  const walk = async (startDir: string): Promise<string[]> => {
    const out: string[] = [];
    const stack: string[] = [startDir];

    while (stack.length) {
      const dir = stack.pop()!;
      const resp = await listDirectory({
        baseUrl,
        relativePath: dir,
        includeDirectories: true,
        includeFiles: true,
        onlySupportedTextFiles: true,
      });

      for (const x of (resp as any).entries ?? []) {
        const rel = String(x?.relativePath ?? x?.relative_path ?? "").replace(/^[/\\]+/g, "");
        if (!rel) continue;
        const isDir = Boolean(x?.isDirectory ?? x?.is_directory);
        if (isDir) {
          const name = baseName(rel);
          const lower = name.toLowerCase();
          if (lower.startsWith(".")) continue;
          if (IGNORED_SCAN_DIR_NAMES.has(lower)) continue;
          stack.push(rel);
        } else {
          out.push(rel);
        }
      }
    }

    return out;
  };

  const files = new Set<string>();

  // Root-level supported files always go in "其他".
  for (const f of rootFiles) files.add(f);

  // First: strict top dirs (if present).
  for (const d of ["chapters", "objects", "roles", "rules"]) {
    if (!topDirSet.has(d)) continue;
    for (const f of await walk(d)) files.add(f);
  }

  // Then: everything else (top-level dirs not in reserved set).
  for (const d of topDirs) {
    const lower = d.toLowerCase();
    if (RESERVED_TOP_DIRS.has(lower)) continue;
    if (lower.startsWith(".")) continue;
    if (IGNORED_SCAN_DIR_NAMES.has(lower)) continue;
    for (const f of await walk(d)) files.add(f);
  }

  return Array.from(files.values());
}

function resolveInitialSidecarUrl(): string {
  // Prefer explicit env config (Vite) over persisted value.
  const env = ((import.meta as any).env?.VITE_NOVEL_SIDECAR_URL as string | undefined)?.trim();
  const stored = (localStorage.getItem("novel.sidecarUrl") ?? "").trim();

  const chosen = env || stored || defaultSidecarUrl();

  // Port policy: 5000 is forbidden in this repo. Migrate old installs automatically.
  return chosen.replace(/:5000\b/g, ":5678");
}

export default function App() {
  const [sidecarUrl, setSidecarUrl] = useState<string>(() => {
    return resolveInitialSidecarUrl();
  });
  const sidecarBaseUrl = useMemo(() => normalizeBaseUrl(sidecarUrl), [sidecarUrl]);

  const [sidecarState, setSidecarState] = useState<SidecarConnectionState>("idle");
  const [sidecarError, setSidecarError] = useState<string | null>(null);

  const [projectRoot, setProjectRootState] = useState<string>("");
  const [library, setLibrary] = useState<LibraryIndex>(EMPTY_LIBRARY);

  const [openFiles, setOpenFiles] = useState<OpenFile[]>([]);
  const [activePath, setActivePath] = useState<string | null>(null);
  const activeFile = useMemo(
    () => (activePath ? openFiles.find((f) => f.path === activePath) ?? null : null),
    [activePath, openFiles],
  );
  const openFilesRef = useRef<OpenFile[]>([]);
  useEffect(() => {
    openFilesRef.current = openFiles;
  }, [openFiles]);

  const [cursor, setCursor] = useState<CursorPos | null>(null);
  const editorRef = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
  const monacoMountedRef = useRef(false);
  const [usePlainEditor, setUsePlainEditor] = useState(false);
  const [monacoReady, setMonacoReady] = useState(false);

  const [paletteOpen, setPaletteOpen] = useState(false);
  const [showSidebar, setShowSidebar] = useState(true);
  const [showAssistant, setShowAssistant] = useState(true);

  const [events, setEvents] = useState<SidecarEvent[]>([]);
  const [chatMessages, setChatMessages] = useState<ChatMessage[]>(() => [
    {
      id: newId("sys"),
      role: "system",
      ts: Date.now(),
      text: "欢迎来到 NovelOS（v1）。左侧选文件，中间写作/编辑，右侧是聊天面板（聊天能力待接入）。",
    },
  ]);
  const [aiBusy, setAiBusy] = useState(false);
  const [reconnectNonce, setReconnectNonce] = useState(0);
  const actionsRef = useRef<{
    saveActiveFile: () => void;
    chooseProjectRoot: () => void;
    refreshTree: () => void;
    reconnectSidecar: () => void;
  }>({
    saveActiveFile: () => {},
    chooseProjectRoot: () => {},
    refreshTree: () => {},
    reconnectSidecar: () => {},
  });

  // Persist sidecar URL.
  useEffect(() => {
    localStorage.setItem("novel.sidecarUrl", sidecarBaseUrl);
  }, [sidecarBaseUrl]);

  // Preload Monaco early to reduce perceived "Loading..." latency on first file open.
  useEffect(() => {
    let cancelled = false;
    monacoLoader
      .init()
      .then(() => {
        if (!cancelled) setMonacoReady(true);
      })
      .catch(() => {
        if (!cancelled) setMonacoReady(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const refreshTree = async (rootOverride?: string) => {
    try {
      // NOTE: We intentionally fetch file index from sidecar (SSOT guardrail layer),
      // not from Tauri fs directly. This keeps UI consistent with sidecar watchers.
      const files = await scanSupportedTextFiles(sidecarBaseUrl);
      setLibrary(buildLibraryIndexFromRelativePaths(files));
    } catch (err: any) {
      setSidecarError(`加载文件列表失败（${sidecarBaseUrl}）：${err?.message ?? String(err)}`);
      setLibrary(EMPTY_LIBRARY);
    }
  };

  const syncProjectRootFromSidecar = async () => {
    try {
      const info = await getProjectRootInfo(sidecarBaseUrl);
      const root = (info as any).projectRoot ?? "";
      setProjectRootState(root);
      await refreshTree();
    } catch (err: any) {
      // Sidecar may be down; UI should still be usable for local edits later.
      setSidecarError(`读取 sidecar ProjectRoot 失败（${sidecarBaseUrl}）：${err?.message ?? String(err)}`);
    }
  };

  const chooseProjectRoot = async () => {
    const selected = await openDialog({
      title: "选择 Novel 项目根目录（SSOT）",
      directory: true,
      recursive: true,
    });
    if (!selected) return;
    if (Array.isArray(selected)) return;

    const root = String(selected);
    try {
      const info = await setProjectRoot(sidecarBaseUrl, root);
      const finalRoot = (info as any).projectRoot ?? root;
      setProjectRootState(finalRoot);
      await refreshTree();
    } catch (err: any) {
      setSidecarError(`设置 ProjectRoot 失败：${err?.message ?? String(err)}`);
      // Sidecar is required for file indexing in v1; keep UI consistent.
      setProjectRootState(root);
      await refreshTree();
    }
  };

  const openFile = async (relativePath: string) => {
    setSidecarError(null);
    const rel = String(relativePath ?? "").replace(/^[/\\]+/g, "");
    if (!rel) return;

    const exists = openFilesRef.current.some((f) => f.path === rel);
    if (exists) {
      setActivePath(rel);
      return;
    }

    // Create a placeholder tab immediately for better perceived performance.
    const ext = rel.toLowerCase().endsWith(".md") ? "markdown" : "plaintext";
    const placeholder: OpenFile = {
      path: rel,
      name: fileNameOf(rel),
      language: ext,
      content: "",
      isDirty: false,
      isLoading: true,
    };
    setOpenFiles((prev) => (prev.some((x) => x.path === rel) ? prev : [...prev, placeholder]));
    setActivePath(rel);

    try {
      const resp = await readTextFile({ baseUrl: sidecarBaseUrl, relativePath: rel });
      const of: OpenFile = {
        path: rel,
        name: fileNameOf(rel),
        language: resp.format,
        content: resp.text,
        isDirty: false,
        isLoading: false,
      };
      setOpenFiles((prev) => prev.map((x) => (x.path === rel ? of : x)));

      // Reset editor mode for the new file.
      monacoMountedRef.current = false;
      setUsePlainEditor(false);
    } catch (err: any) {
      setSidecarError(`打开文件失败：${err?.message ?? String(err)}`);
      setOpenFiles((prev) => prev.map((x) => (x.path === rel ? { ...x, isLoading: false } : x)));
    }
  };

  const closeFile = (fullPath: string) => {
    setOpenFiles((prev) => {
      const next = prev.filter((f) => f.path !== fullPath);
      if (activePath === fullPath) {
        setActivePath(next.length ? next[next.length - 1].path : null);
      }
      return next;
    });
  };

  const saveActiveFile = async () => {
    const f = activeFile;
    if (!f || !f.isDirty) return;

    try {
      const resp = await writeTextFile({ baseUrl: sidecarBaseUrl, relativePath: f.path, content: f.content });
      if (!resp.success) {
        throw new Error(resp.message || "write_failed");
      }
      setOpenFiles((prev) =>
        prev.map((x) => (x.path === f.path ? { ...x, isDirty: false } : x)),
      );
    } catch (err: any) {
      setSidecarError(`保存失败：${err?.message ?? String(err)}`);
    }
  };

  const reconnectSidecar = () => setReconnectNonce((n) => n + 1);

  // Monaco sometimes fails to load under strict CSP/worker rules in Tauri.
  // If it doesn't mount within a short window, fall back to a plain textarea editor (never stuck on "Loading...").
  useEffect(() => {
    if (!activeFile) return;
    if (activeFile.isLoading) return;
    monacoMountedRef.current = false;
    setUsePlainEditor(!monacoReady);

    const t = setTimeout(() => {
      if (!monacoMountedRef.current) {
        setUsePlainEditor(true);
      }
    }, 2500);

    return () => clearTimeout(t);
  }, [activeFile?.path, activeFile?.isLoading, monacoReady]);

  const newChat = () => {
    setChatMessages([
      {
        id: newId("sys"),
        role: "system",
        ts: Date.now(),
        text: "新会话已创建（本地）。",
      },
    ]);
  };

  const appendContinuation = (base: string, addition: string): string => {
    const a = String(base ?? "");
    const b = String(addition ?? "").trim();
    if (!b) return a;

    // Keep paragraph spacing readable.
    const needsBreak = a.length > 0 && !a.endsWith("\n");
    const sep = needsBreak ? "\n\n" : "\n";
    return a + sep + b + "\n";
  };

  const smartContinueActive = async (instruction?: string) => {
    if (aiBusy) return;

    const f = activeFile;
    if (!f) {
      setChatMessages((prev) => [
        ...prev,
        { id: newId("sys"), role: "system", ts: Date.now(), text: "请先从左侧打开一个文件，再进行智能续写。" },
      ]);
      return;
    }
    if (f.isLoading) return;

    const inst = String(instruction ?? "").trim();

    setAiBusy(true);
    setChatMessages((prev) => [
      ...prev,
      {
        id: newId("sys"),
        role: "system",
        ts: Date.now(),
        text: `智能续写：生成中…\n目标：${f.path}${inst ? `\n指令：${inst}` : ""}`,
      },
    ]);

    try {
      const resp = await smartContinue({
        baseUrl: sidecarBaseUrl,
        targetRelativePath: f.path,
        draftText: f.content,
        instruction: inst,
        maxOutputChars: 500,
        maxContextChars: 60000,
        includeObjects: true,
        includeRoles: true,
        includeRules: true,
      });

      if (!resp.success) {
        throw new Error(resp.message || "smart_continue_failed");
      }

      const gen = String(resp.generatedText ?? "").trim();
      if (!gen) {
        throw new Error("empty_generation");
      }

      // Insert into the active file (append) for now (v1).
      setOpenFiles((prev) =>
        prev.map((x) =>
          x.path === f.path ? { ...x, content: appendContinuation(x.content, gen), isDirty: true } : x,
        ),
      );

      setChatMessages((prev) => [
        ...prev,
        {
          id: newId("a"),
          role: "assistant",
          ts: Date.now(),
          text: gen,
        },
      ]);
    } catch (err: any) {
      setChatMessages((prev) => [
        ...prev,
        {
          id: newId("sys"),
          role: "system",
          ts: Date.now(),
          text: `智能续写失败：${err?.message ?? String(err)}`,
        },
      ]);
    } finally {
      setAiBusy(false);
    }
  };

  const sendChat = (text: string) => {
    if (aiBusy) {
      setChatMessages((prev) => [
        ...prev,
        { id: newId("sys"), role: "system", ts: Date.now(), text: "智能续写进行中，请稍等…" },
      ]);
      return;
    }
    const userMsg: ChatMessage = { id: newId("u"), role: "user", ts: Date.now(), text };
    setChatMessages((prev) => [...prev, userMsg]);
    void smartContinueActive(text);
  };

  // Keep latest actions for keyboard shortcuts (installed once).
  useEffect(() => {
    actionsRef.current = { saveActiveFile, chooseProjectRoot, refreshTree: () => refreshTree(), reconnectSidecar };
  }, [saveActiveFile, chooseProjectRoot, refreshTree, reconnectSidecar]);

  // Keyboard shortcuts.
  useEffect(() => {
    return installGlobalShortcuts({
      onSave: () => actionsRef.current.saveActiveFile(),
      onOpenProject: () => actionsRef.current.chooseProjectRoot(),
      onRefreshTree: () => actionsRef.current.refreshTree(),
      onReconnectSidecar: () => actionsRef.current.reconnectSidecar(),
      onOpenCommandPalette: () => setPaletteOpen(true),
      onToggleSidebar: () => setShowSidebar((v) => !v),
      onToggleAssistant: () => setShowAssistant((v) => !v),
    });
  }, []);

  // On startup: try to get ProjectRoot from sidecar (if running).
  useEffect(() => {
    syncProjectRootFromSidecar();
  }, [sidecarBaseUrl]);

  // SSE subscription.
  useEffect(() => {
    setSidecarError(null);
    const dispose = connectSidecarEvents({
      baseUrl: sidecarBaseUrl,
      onState: (s, err) => {
        setSidecarState(s);
        if (s === "connected") {
          setSidecarError(null);
          return;
        }
        if (err) setSidecarError(err);
      },
      onEvent: async (evt) => {
        setEvents((prev) => clampEvents([...prev, evt], 200));

        const p: any = (evt as any).payload;
        if (!p || !p.case) return;

        // Project root switch -> refresh tree.
        if (p.case === "projectRootChanged") {
          const root = p.value?.info?.projectRoot ?? "";
          if (root) {
            setProjectRootState(root);
            await refreshTree(root);
            setChatMessages((prev) => [
              ...prev,
              { id: newId("sys"), role: "system", ts: Date.now(), text: `项目根目录已更新：${root}` },
            ]);
          }
        }

        // File changed -> if file is open and NOT dirty, reload from disk (SSOT).
        if (p.case === "fileChanged") {
          const rel = String(p.value?.relativePath ?? p.value?.relative_path ?? "").replace(/^[/\\]+/g, "");
          if (rel) {
            const opened = openFilesRef.current.find((f) => f.path === rel);
            if (opened && !opened.isDirty) {
              try {
                const content = await readTextFile({ baseUrl: sidecarBaseUrl, relativePath: rel });
                setOpenFiles((prev) =>
                  prev.map((x) => (x.path === rel ? { ...x, content: content.text, language: content.format } : x)),
                );
              } catch {
                // Best-effort
              }
            }
          }
        }

        // Unit test results -> open report if user clicks in assistant panel.
        if (p.case === "unitTestsCompleted") {
          const uri = p.value?.testReport?.uri ?? "";
          const reportPath = typeof uri === "string" ? fileUriToPath(uri) : null;
          // Best-effort: keep tree in sync when reports appear.
          if (reportPath) {
            await refreshTree();
          }

          const summary = p.value?.summary;
          const status = String(summary?.status ?? "").toUpperCase();
          const failures = Array.isArray(summary?.failures) ? summary.failures.length : 0;
          const label = status.includes("PASSED") || failures === 0 ? "PASSED" : "FAILED";
          setChatMessages((prev) => [
            ...prev,
            {
              id: newId("sys"),
              role: "system",
              ts: Date.now(),
              text: `Narrative Tests：${label}${failures ? `（failures: ${failures}）` : ""}${reportPath ? `\n报告：${reportPath}` : ""}`,
            },
          ]);
        }
      },
    });
    return dispose;
  }, [sidecarBaseUrl, reconnectNonce]);

  const commands = useMemo(() => {
    return buildCoreCommands({
      openProject: chooseProjectRoot,
      refreshTree: () => refreshTree(),
      reconnectSidecar,
      toggleSidebar: () => setShowSidebar((v) => !v),
      toggleAssistant: () => setShowAssistant((v) => !v),
    });
  }, [sidecarBaseUrl, projectRoot, openFiles, activePath]);

  const monacoOptions = useMemo(() => {
    const lang = activeFile?.language ?? "plaintext";
    const isMd = lang === "markdown";
    const isTxt = lang === "plaintext";

    return {
      // Markdown should feel like a document, not code.
      fontFamily: isMd
        ? "ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, \"SF Pro Display\", \"Segoe UI\", \"PingFang SC\", \"Hiragino Sans GB\", \"Microsoft YaHei\", Arial, sans-serif"
        : "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, \"Liberation Mono\", \"Courier New\", monospace",
      fontSize: isMd ? 15 : 14,
      lineHeight: isMd ? 24 : 20,

      minimap: { enabled: !isMd },
      smoothScrolling: true,

      // Markdown/text: wrap for reading.
      wordWrap: isMd || isTxt ? "on" : "off",
      lineNumbers: "on",
      renderWhitespace: isMd ? "none" : "selection",

      scrollBeyondLastLine: false,
      bracketPairColorization: { enabled: true },
      automaticLayout: true,
      padding: { top: isMd ? 14 : 10, bottom: isMd ? 16 : 10 },
    } as const;
  }, [activeFile?.language]);

  return (
    <div className="Shell">
      <div className="TopBar">
        <div className="TopBarTitle">NovelOS</div>
        <div className="TopBarMeta" title={projectRoot || ""} style={{ flex: 1 }}>
          {projectRoot ? `SSOT: ${projectRoot}` : "SSOT: 未设置（点击“打开项目”）"}
        </div>
        <button className="Btn" onClick={chooseProjectRoot} title="选择项目根目录（SSOT）">
          打开项目
        </button>
        <button className="Btn" onClick={() => setPaletteOpen(true)} title="命令面板（⌘P）">
          命令 ⌘P
        </button>
        <button className="Btn BtnPrimary" onClick={saveActiveFile} title="保存（⌘S）">
          保存 ⌘S
        </button>
      </div>

      <div className="Main">
        <PanelGroup direction="horizontal" style={{ height: "100%" }}>
          {showSidebar ? (
            <>
              <Panel defaultSize={22} minSize={15}>
                <LibraryPanel
                  projectRoot={projectRoot}
                  index={library}
                  activePath={activePath}
                  onOpenFile={(p) => openFile(p)}
                  onRefresh={() => refreshTree()}
                />
              </Panel>
              <PanelResizeHandle className="ResizeHandle" />
            </>
          ) : null}

          <Panel defaultSize={showAssistant ? 56 : 78} minSize={25}>
            <div className="EditorWrap">
              <Tabs
                files={openFiles}
                activePath={activePath}
                onActivate={(p) => setActivePath(p)}
                onClose={closeFile}
              />
              <div className="EditorBody">
                {!activeFile ? (
                  <div className="Welcome">
                    <div className="WelcomeCard">
                      <div className="WelcomeTitle">把文件当真相源，把智能体当引擎。</div>
                      <div className="WelcomeHint">
                        - 正文章节必须是 <code>.txt</code>（纯文本）<br />
                        - 产物报告是 <code>.md</code>（结构化）<br />
                        - sidecar 负责监听文件变更与跑 Narrative Tests（SSE 推送结果）<br />
                        <br />
                        先点右上角“打开项目”，再从左侧选择章节/设定/大纲文件打开编辑。
                      </div>
                    </div>
                  </div>
                ) : (
                  activeFile.isLoading ? (
                    <div className="EditorLoading">
                      <div className="EditorLoadingSpinner" />
                      <div className="EditorLoadingText">正在读取文件…</div>
                      <div className="EditorLoadingSub">{activeFile.path}</div>
                    </div>
                  ) : (
                  usePlainEditor ? (
                    <div className="PlainEditorWrap">
                      <div className="PlainEditorBanner">
                        Monaco 加载失败或被 CSP/Worker 限制拦截，已切换为简易编辑器（可编辑可保存）。
                      </div>
                      <textarea
                        className="PlainEditor"
                        value={activeFile.content}
                        onChange={(e) => {
                          const next = e.target.value ?? "";
                          setOpenFiles((prev) =>
                            prev.map((x) => (x.path === activeFile.path ? { ...x, content: next, isDirty: true } : x)),
                          );
                        }}
                        spellCheck={false}
                      />
                    </div>
                  ) : (
                    <MonacoEditor
                      height="100%"
                      path={activeFile.path}
                      language={activeFile.language}
                      value={activeFile.content}
                      loading={<div className="EditorLoadingText">加载编辑器…</div>}
                      onChange={(v) => {
                        const next = v ?? "";
                        setOpenFiles((prev) =>
                          prev.map((x) =>
                            x.path === activeFile.path ? { ...x, content: next, isDirty: true } : x,
                          ),
                        );
                      }}
                      onMount={(editor, monacoApi) => {
                        monacoMountedRef.current = true;
                        setUsePlainEditor(false);
                        editorRef.current = editor;

                        try {
                          monacoApi.editor.defineTheme("novelos-light", {
                            base: "vs",
                            inherit: true,
                            rules: [],
                            colors: {
                              "editor.background": "#ffffff",
                              "editor.lineHighlightBackground": "#f3f4f6",
                              "editorLineNumber.foreground": "#9ca3af",
                              "editorLineNumber.activeForeground": "#111827",
                              "editorCursor.foreground": "#16a34a",
                              "editor.selectionBackground": "#dbeafe",
                              "editor.inactiveSelectionBackground": "#eef2ff",
                              "editorIndentGuide.background": "#e5e7eb",
                              "editorIndentGuide.activeBackground": "#cbd5e1",
                            },
                          });
                          monacoApi.editor.setTheme("novelos-light");
                        } catch {
                          // Best-effort: theme setup
                        }

                        editor.onDidChangeCursorPosition((e) => {
                          setCursor({ line: e.position.lineNumber, column: e.position.column });
                        });
                      }}
                      options={monacoOptions as any}
                    />
                  )
                  )
                )}
              </div>
            </div>
          </Panel>

          {showAssistant ? (
            <>
              <PanelResizeHandle className="ResizeHandle" />
              <Panel defaultSize={22} minSize={18}>
                <ChatPanel
                  sidecarUrl={sidecarBaseUrl}
                  sidecarState={sidecarState}
                  lastError={sidecarError}
                  messages={chatMessages}
                  onSend={sendChat}
                  onNewChat={newChat}
                  onSmartContinue={(inst) => smartContinueActive(inst)}
                  activeFilePath={activeFile?.path ?? null}
                  busy={aiBusy}
                />
              </Panel>
            </>
          ) : null}
        </PanelGroup>
      </div>

      <StatusBar
        sidecarUrl={sidecarBaseUrl}
        sidecarState={sidecarState}
        projectRoot={projectRoot}
        activeFile={activeFile}
        cursor={cursor}
      />

      <CommandPalette isOpen={paletteOpen} items={commands} onClose={() => setPaletteOpen(false)} />
    </div>
  );
}


