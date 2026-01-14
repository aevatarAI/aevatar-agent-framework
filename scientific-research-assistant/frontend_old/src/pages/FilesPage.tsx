import { useMemo } from "react";

import { createWebTransport } from "../transport/WebTransport";
import SharedFilesPage from "../../../ui/src/pages/FilesPage";

// ============================================================
//  Web wrapper: FilesPage
//
//  中文说明：
//  - 旧实现是 Web-only 的 fetch(/api/*) 调用
//  - 现在统一走 shared FilesPage + transport，保证 Web/Obsidian 行为一致
// ============================================================

export default function FilesPage(props: { sessionId: string; sessionFromQuery?: string }) {
  const transport = useMemo(() => createWebTransport(), []);

  const pathFromQuery = useMemo(() => {
    try {
      const q = new URLSearchParams(window.location.search);
      return (q.get("path") || "").trim();
    } catch {
      return "";
    }
  }, []);

  return (
    <SharedFilesPage
      transport={transport}
      sessionId={props.sessionId}
      sessionFromQuery={props.sessionFromQuery}
      initialPath={pathFromQuery}
      onBack={() => {
        try {
          window.location.assign("/");
        } catch {
          // ignore
        }
      }}
    />
  );
}


