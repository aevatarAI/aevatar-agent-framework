import React, { useEffect, useMemo, useRef, useState } from "react";
import { Panel, PanelGroup, PanelResizeHandle } from "react-resizable-panels";
import MonacoEditor from "@monaco-editor/react";
import type * as monaco from "monaco-editor";
import { open as openDialog } from "@tauri-apps/plugin-dialog";

import AssistantPanel from "@/components/AssistantPanel";
import CommandPalette from "@/components/CommandPalette";
import FileTree from "@/components/FileTree";
import StatusBar from "@/components/StatusBar";
import Tabs from "@/components/Tabs";

import { buildCoreCommands, installGlobalShortcuts } from "@/lib/keyboard";
import { loadProjectTree, openTextFile, saveTextFile } from "@/lib/fs";
import { fileUriToPath } from "@/lib/uri";
import type { CursorPos, OpenFile, TreeNode } from "@/lib/types";
import {
  connectSidecarEvents,
  defaultSidecarUrl,
  getProjectRootInfo,
  normalizeBaseUrl,
  setProjectRoot,
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

export default function App() {
  const [sidecarUrl, setSidecarUrl] = useState<string>(() => {
    const v = localStorage.getItem("novel.sidecarUrl");
    return v?.trim() ? v : defaultSidecarUrl();
  });
  const sidecarBaseUrl = useMemo(() => normalizeBaseUrl(sidecarUrl), [sidecarUrl]);

  const [sidecarState, setSidecarState] = useState<SidecarConnectionState>("idle");
  const [sidecarError, setSidecarError] = useState<string | null>(null);

  const [projectRoot, setProjectRootState] = useState<string>("");
  const [tree, setTree] = useState<TreeNode | null>(null);

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

  const [paletteOpen, setPaletteOpen] = useState(false);
  const [showSidebar, setShowSidebar] = useState(true);
  const [showAssistant, setShowAssistant] = useState(true);

  const [events, setEvents] = useState<SidecarEvent[]>([]);
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

  const refreshTree = async (rootOverride?: string) => {
    const root = (rootOverride ?? projectRoot).trim();
    if (!root) {
      setTree(null);
      return;
    }
    try {
      const t = await loadProjectTree(root);
      setTree(t);
    } catch (err: any) {
      setSidecarError(`加载文件树失败：${err?.message ?? String(err)}`);
      setTree(null);
    }
  };

  const syncProjectRootFromSidecar = async () => {
    try {
      const info = await getProjectRootInfo(sidecarBaseUrl);
      const root = (info as any).projectRoot ?? "";
      setProjectRootState(root);
      await refreshTree(root);
    } catch (err: any) {
      // Sidecar may be down; UI should still be usable for local edits later.
      setSidecarError(`读取 sidecar ProjectRoot 失败：${err?.message ?? String(err)}`);
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
      await refreshTree(finalRoot);
    } catch (err: any) {
      setSidecarError(`设置 ProjectRoot 失败：${err?.message ?? String(err)}`);
      // Still allow local tree view even if sidecar is down.
      setProjectRootState(root);
      await refreshTree(root);
    }
  };

  const openFile = async (fullPath: string) => {
    setSidecarError(null);
    const exists = openFilesRef.current.some((f) => f.path === fullPath);
    if (exists) {
      setActivePath(fullPath);
      return;
    }

    try {
      const content = await openTextFile(fullPath);
      const of: OpenFile = {
        path: fullPath,
        name: fileNameOf(fullPath),
        language: languageOf(fullPath),
        content,
        isDirty: false,
      };
      setOpenFiles((prev) => (prev.some((x) => x.path === fullPath) ? prev : [...prev, of]));
      setActivePath(fullPath);
    } catch (err: any) {
      setSidecarError(`打开文件失败：${err?.message ?? String(err)}`);
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
      await saveTextFile(f.path, f.content);
      setOpenFiles((prev) =>
        prev.map((x) => (x.path === f.path ? { ...x, isDirty: false } : x)),
      );
    } catch (err: any) {
      setSidecarError(`保存失败：${err?.message ?? String(err)}`);
    }
  };

  const reconnectSidecar = () => setReconnectNonce((n) => n + 1);

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
          }
        }

        // File changed -> if file is open and NOT dirty, reload from disk (SSOT).
        if (p.case === "fileChanged") {
          const fullPath = p.value?.fullPath ?? "";
          if (fullPath) {
            const opened = openFilesRef.current.find((f) => f.path === fullPath);
            if (opened && !opened.isDirty) {
              try {
                const content = await openTextFile(fullPath);
                setOpenFiles((prev) =>
                  prev.map((x) => (x.path === fullPath ? { ...x, content } : x)),
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
                <FileTree
                  root={tree}
                  activePath={activePath}
                  onOpenFile={(p) => openFile(p)}
                  onRefresh={() => refreshTree()}
                />
              </Panel>
              <PanelResizeHandle style={{ width: 4, background: "rgba(255,255,255,0.06)" }} />
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
                        先点右上角“打开项目”，再从左侧文件树打开章节/报告。
                      </div>
                    </div>
                  </div>
                ) : (
                  <MonacoEditor
                    height="100%"
                    path={activeFile.path}
                    language={activeFile.language}
                    value={activeFile.content}
                    onChange={(v) => {
                      const next = v ?? "";
                      setOpenFiles((prev) =>
                        prev.map((x) =>
                          x.path === activeFile.path ? { ...x, content: next, isDirty: true } : x,
                        ),
                      );
                    }}
                    onMount={(editor, monacoApi) => {
                      editorRef.current = editor;

                      try {
                        monacoApi.editor.defineTheme("novelos-dark", {
                          base: "vs-dark",
                          inherit: true,
                          rules: [],
                          colors: {
                            "editor.background": "#0b1220",
                            "editor.lineHighlightBackground": "#101b2f",
                            "editorLineNumber.foreground": "#4b5563",
                            "editorLineNumber.activeForeground": "#e6edf3",
                            "editorCursor.foreground": "#7c3aed",
                            "editor.selectionBackground": "#312e81",
                            "editor.inactiveSelectionBackground": "#1f2a44",
                            "editorIndentGuide.background": "#1f2937",
                            "editorIndentGuide.activeBackground": "#374151",
                          },
                        });
                        monacoApi.editor.setTheme("novelos-dark");
                      } catch {
                        // Best-effort: theme setup
                      }

                      editor.onDidChangeCursorPosition((e) => {
                        setCursor({ line: e.position.lineNumber, column: e.position.column });
                      });
                    }}
                    options={{
                      fontFamily: "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace",
                      fontSize: 14,
                      minimap: { enabled: true },
                      smoothScrolling: true,
                      renderWhitespace: "selection",
                      wordWrap: activeFile.language === "plaintext" ? "on" : "off",
                      lineNumbers: "on",
                      scrollBeyondLastLine: false,
                      bracketPairColorization: { enabled: true },
                      automaticLayout: true,
                    }}
                  />
                )}
              </div>
            </div>
          </Panel>

          {showAssistant ? (
            <>
              <PanelResizeHandle style={{ width: 4, background: "rgba(255,255,255,0.06)" }} />
              <Panel defaultSize={22} minSize={18}>
                <AssistantPanel
                  events={events}
                  lastError={sidecarError}
                  onOpenPath={(p) => openFile(p)}
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


