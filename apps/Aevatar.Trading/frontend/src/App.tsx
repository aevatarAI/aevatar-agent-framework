import React from "react";
import { TradingPage } from "./pages/TradingPage";
import { WeexPage } from "./pages/WeexPage";
import { AiWarsPage } from "./pages/AiWarsPage";

type TabKey = "trading" | "weex" | "aiwars";

export function App() {
  const [tab, setTab] = React.useState<TabKey>("trading");

  return (
    <div className="app">
      <div className="shell">
        <div className="topbar">
          <div className="brand">
            <h1>Aevatar Trading Decision Center</h1>
            <p>
              聚焦仓位、风险与 AI 决策触发：交易闭环可观测，交易所可配置切换。<span style={{ color: "var(--muted2)" }}>默认走同源代理。</span>
            </p>
          </div>
          <div className="nav">
            <div className="tabs" role="tablist" aria-label="console-tabs">
              <button className={`tab ${tab === "trading" ? "active" : ""}`} onClick={() => setTab("trading")}>
                Decision Center
              </button>
              <button className={`tab ${tab === "weex" ? "active" : ""}`} onClick={() => setTab("weex")}>
                Exchange Tools（高级）
              </button>
              <button className={`tab ${tab === "aiwars" ? "active" : ""}`} onClick={() => setTab("aiwars")}>
                AI Wars APIs
              </button>
            </div>
          </div>
        </div>

        {tab === "trading" ? <TradingPage /> : tab === "weex" ? <WeexPage /> : <AiWarsPage />}

        <div className="muted" style={{ marginTop: 14 }}>
          Tip：Demo 建议先用 <code>DryRun</code> 跑通闭环，再切 <code>Live</code>；策略日志在 <code>trade-audit/*.md</code>。
        </div>
      </div>
    </div>
  );
}


