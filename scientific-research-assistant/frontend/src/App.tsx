import { useMemo } from "react";

import { createWebTransport } from "./transport/WebTransport";
import { SraWorkbenchApp } from "../../ui/src";

// ============================================================
//  Web host root
//
//  中文说明：
//  - Web 前端保持入口不变（Vite 仍然渲染 <App />）
//  - 真实 UI 位于 shared `SraWorkbenchApp`
//  - ApiKeyModal 暂时由宿主注入（后续会在 shared 中改为 transport 驱动）
// ============================================================

export default function App() {
  const transport = useMemo(() => createWebTransport(), []);
  return <SraWorkbenchApp transport={transport} />;
}


