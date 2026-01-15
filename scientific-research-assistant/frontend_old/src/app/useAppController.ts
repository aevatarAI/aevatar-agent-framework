import { useEffect, useMemo } from "react";

import { createWebTransport } from "../transport/WebTransport";
import { useWorkbenchController } from "../../../ui/src/controller/useWorkbenchController";

// ============================================================
//  Web wrapper controller
//
//  中文说明：
//  - 保持 Web UI 对外 API 不变（`App.tsx` 仍然调用 useAppController）
//  - 真实逻辑在 shared `useWorkbenchController` 中
// ============================================================

export function useAppController() {
  const transport = useMemo(() => createWebTransport(), []);

  const query = useMemo(() => new URLSearchParams(window.location.search), []);
  const pageView = query.get("view") || "";
  const sessionFromQuery = (query.get("session") || "").trim();

  const ctrl = useWorkbenchController({ transport });

  useEffect(() => {
    // Support opening dedicated pages (e.g. DAG view) with a preselected session via query param.
    if (sessionFromQuery && !ctrl.sessionId) {
      ctrl.connectToSession(sessionFromQuery);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionFromQuery]);

  return {
    pageView,
    sessionFromQuery,
    ...ctrl,
  };
}


