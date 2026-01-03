import React from "react";
import { apiFetch, prettyJson } from "../api";
import type {
  AgentsResponse,
  AuditLatestResponse,
  BalanceInfo,
  MetaResponse,
  OrderInfo,
  TickerResponse,
  TradingSystemStatus,
} from "../types";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { StatusPill } from "../components/StatusPill";
import { FillsViz, PositionsViz } from "../components/ExchangeViz";

type CallState = { busy: boolean; error?: string };
type SectionErrors = Partial<Record<"meta" | "status" | "ticker" | "balances" | "orders" | "positions" | "fills" | "audit", string>>;

function useCallState() {
  const [state, setState] = React.useState<CallState>({ busy: false });
  const run = React.useCallback(async <T,>(fn: () => Promise<T>) => {
    setState({ busy: true });
    try {
      const res = await fn();
      setState({ busy: false });
      return res;
    } catch (e) {
      const msg =
        typeof e === "object" && e && "message" in e ? String((e as { message?: unknown }).message) : String(e);
      setState({ busy: false, error: msg });
      throw e;
    }
  }, []);
  return { state, run };
}

function formatNum(n: number, digits = 2): string {
  if (!Number.isFinite(n)) return "-";
  return n.toLocaleString(undefined, { maximumFractionDigits: digits, minimumFractionDigits: 0 });
}

function formatIso(iso?: string | null): string {
  if (!iso) return "-";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString();
}

function badgeKindForDirection(dir: string): "buy" | "sell" | "hold" {
  const x = (dir ?? "").toUpperCase();
  if (x === "BUY") return "buy";
  if (x === "SELL") return "sell";
  return "hold";
}

type StrategyCycle = {
  cycleId: string;
  symbol: string;
  trigger?: string;
  startTime?: string;
  endTime?: string;
  decisionId?: string;
  direction?: string;
  confidence?: number;
  executedToRisk?: boolean;
  executionMode?: string;
  ai?: { sentiment?: string; technical?: string; news?: string; reasoning?: string };
  snapshot?: { sentiment?: string; technical?: string; news?: string };
  risk?: { result?: string; details?: Array<string> };
  execution?: string;
};

function parseAuditMarkdown(md: string): StrategyCycle[] {
  if (!md) return [];
  const blocks = md.split("\n## Cycle ").slice(1).map((x) => "## Cycle " + x);
  const cycles: StrategyCycle[] = [];

  for (const b of blocks) {
    const head = b.match(/^## Cycle `([^`]+)` — `([^`]+)`/m);
    if (!head) continue;
    const cycleId = head[1];
    const symbol = head[2];

    const time = b.match(/- Time\(UTC\): `([^`]*)` → `([^`]*)`/);
    const trigger = b.match(/- Trigger: `([^`]*)`/);
    const decisionLine = b.match(/- Decision:\s+\*\*(.+?)\*\*\s+\(confidence=(\d+)\)/);
    const decisionId = b.match(/- DecisionId: `([^`]*)`/);
    const execToRisk = b.match(/- ExecutedToRisk: `([^`]*)`\s+\|\s+Mode: `([^`]*)`/);

    const ai: StrategyCycle["ai"] = {};
    const aiSent = b.match(/- Sentiment:\s+(.+)/);
    const aiTech = b.match(/- Technical:\s+(.+)/);
    const aiNews = b.match(/- News:\s+(.+)/);
    const aiReason = b.match(/- Reasoning:\s+(.+)/);
    if (aiSent) ai.sentiment = aiSent[1].trim();
    if (aiTech) ai.technical = aiTech[1].trim();
    if (aiNews) ai.news = aiNews[1].trim();
    if (aiReason) ai.reasoning = aiReason[1].trim();

    const snapshot: StrategyCycle["snapshot"] = {};
    const snapSent = b.match(/- Sentiment:\s+(score=.+)/);
    const snapTech = b.match(/- Technical:\s+(trend=.+)/);
    const snapNews = b.match(/- News:\s+(impact=.+)/);
    if (snapSent) snapshot.sentiment = snapSent[1].trim();
    if (snapTech) snapshot.technical = snapTech[1].trim();
    if (snapNews) snapshot.news = snapNews[1].trim();

    const riskLines: string[] = [];
    const riskSection = b.split("\n### Risk Control")[1]?.split("\n### Execution Result")[0] ?? "";
    for (const line of riskSection.split("\n")) {
      const m = line.match(/^- (.+)$/);
      if (m) riskLines.push(m[1]);
    }

    const execSection = b.split("\n### Execution Result")[1] ?? "";
    const execLine = execSection.match(/- (.+)/);

    const cycle: StrategyCycle = {
      cycleId,
      symbol,
      trigger: trigger?.[1] ?? undefined,
      startTime: time?.[1] ? time[1] : undefined,
      endTime: time?.[2] ? time[2] : undefined,
      decisionId: decisionId?.[1] ?? undefined,
      direction: decisionLine?.[1] ?? undefined,
      confidence: decisionLine ? Number(decisionLine[2]) : undefined,
      executedToRisk: execToRisk ? execToRisk[1].trim().toUpperCase() === "YES" : undefined,
      executionMode: execToRisk ? execToRisk[2] : undefined,
      ai: Object.keys(ai).length ? ai : undefined,
      snapshot: Object.keys(snapshot).length ? snapshot : undefined,
      risk: riskLines.length ? { result: riskLines[0] ?? "", details: riskLines.slice(1) } : undefined,
      execution: execLine?.[1] ?? undefined,
    };
    cycles.push(cycle);
  }

  // newest at top (file is append-only)
  return cycles.reverse();
}

type ToolExecResponse = {
  success: boolean;
  exitCode: number;
  toolName: string;
  file: string;
  data: unknown;
  stderr?: string | null;
};

export function TradingPage() {
  const [meta, setMeta] = React.useState<MetaResponse | null>(null);
  const [status, setStatus] = React.useState<TradingSystemStatus | null>(null);
  const [agents, setAgents] = React.useState<AgentsResponse | null>(null);

  const [symbol, setSymbol] = React.useState("cmt_btcusdt");
  const [symbolTouched, setSymbolTouched] = React.useState(false);

  const [ticker, setTicker] = React.useState<TickerResponse | null>(null);
  const [balances, setBalances] = React.useState<Array<BalanceInfo> | null>(null);
  const [orders, setOrders] = React.useState<Array<OrderInfo> | null>(null);
  const [positionsTool, setPositionsTool] = React.useState<ToolExecResponse | null>(null);
  const [fillsTool, setFillsTool] = React.useState<ToolExecResponse | null>(null);

  const [audit, setAudit] = React.useState<AuditLatestResponse | null>(null);
  const [cycles, setCycles] = React.useState<Array<StrategyCycle>>([]);
  const [selectedCycleId, setSelectedCycleId] = React.useState<string | null>(null);

  const [autoRefresh, setAutoRefresh] = React.useState(true);
  const [errors, setErrors] = React.useState<SectionErrors>({});
  const [lastUpdatedAt, setLastUpdatedAt] = React.useState<Date | null>(null);
  const [agentsExpanded, setAgentsExpanded] = React.useState(false);

  const calls = useCallState();
  const [stopReason, setStopReason] = React.useState("UI stop");
  const refreshInFlight = React.useRef(false);

  const refreshAll = React.useCallback(async () => {
    if (refreshInFlight.current) return;
    refreshInFlight.current = true;

    try {
    const nextErrors: SectionErrors = {};

    const results = await Promise.allSettled([
      apiFetch<MetaResponse>("/api/meta"),
      apiFetch<TradingSystemStatus>("/api/trading/status"),
      apiFetch<AgentsResponse>("/api/agents"),
      apiFetch<TickerResponse>(`/api/weex-test/ticker?${new URLSearchParams({ symbol }).toString()}`),
      apiFetch<{ count: number; balances: Array<BalanceInfo> }>("/api/weex-test/balances"),
      apiFetch<{ count: number; orders: Array<OrderInfo> }>(
        `/api/weex-test/open-orders?${new URLSearchParams(symbol ? { symbol } : {}).toString()}`,
      ),
      apiFetch<ToolExecResponse>("/api/ai-wars/weex_ai_account_position_all_position?confirm=false", {
        method: "POST",
        body: "{}",
      }),
      apiFetch<ToolExecResponse>("/api/ai-wars/weex_ai_order_fills?confirm=false", {
        method: "POST",
        body: JSON.stringify({ symbol, limit: 100 }),
      }),
      apiFetch<AuditLatestResponse>("/api/audit/latest?maxBytes=200000"),
    ]);

    const [metaRes, statusRes, agentsRes, tickerRes, balRes, ordRes, posRes, fillsRes, auditRes] = results;

    if (metaRes.status === "fulfilled") {
      setMeta(metaRes.value);
      if (!symbolTouched && metaRes.value.trading?.symbol) {
        setSymbol(metaRes.value.trading.symbol);
      }
    } else {
      nextErrors.meta = String(metaRes.reason?.message ?? metaRes.reason);
    }

    if (statusRes.status === "fulfilled") setStatus(statusRes.value);
    else nextErrors.status = String(statusRes.reason?.message ?? statusRes.reason);

    if (agentsRes.status === "fulfilled") setAgents(agentsRes.value);

    if (tickerRes.status === "fulfilled") setTicker(tickerRes.value);
    else nextErrors.ticker = String(tickerRes.reason?.message ?? tickerRes.reason);

    if (balRes.status === "fulfilled") setBalances(balRes.value.balances);
    else nextErrors.balances = String(balRes.reason?.message ?? balRes.reason);

    if (ordRes.status === "fulfilled") setOrders(ordRes.value.orders);
    else nextErrors.orders = String(ordRes.reason?.message ?? ordRes.reason);

    if (posRes.status === "fulfilled") setPositionsTool(posRes.value);
    else nextErrors.positions = String(posRes.reason?.message ?? posRes.reason);

    if (fillsRes.status === "fulfilled") setFillsTool(fillsRes.value);
    else nextErrors.fills = String(fillsRes.reason?.message ?? fillsRes.reason);

    if (auditRes.status === "fulfilled") {
      setAudit(auditRes.value);
      const parsed = parseAuditMarkdown(auditRes.value.content ?? "");
      setCycles(parsed);
      if (parsed.length && !selectedCycleId) setSelectedCycleId(parsed[0].cycleId);
    } else {
      nextErrors.audit = String(auditRes.reason?.message ?? auditRes.reason);
    }

    setErrors(nextErrors);
    setLastUpdatedAt(new Date());
    } finally {
      refreshInFlight.current = false;
    }
  }, [selectedCycleId, symbol, symbolTouched]);

  React.useEffect(() => {
    void refreshAll();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  React.useEffect(() => {
    if (!autoRefresh) return;
    const t = window.setInterval(() => {
      void refreshAll();
    }, 5000);
    return () => window.clearInterval(t);
  }, [autoRefresh, refreshAll]);

  const selected = cycles.find((c) => c.cycleId === selectedCycleId) ?? cycles[0] ?? null;
  const usdt = balances?.find((b) => b.currency.toUpperCase() === "USDT") ?? null;

  const executionMode = meta?.trading.executionMode ?? "-";
  const modeBadge = executionMode.toUpperCase().includes("LIVE") ? "danger" : "ok";

  return (
    <div className="dash">
      <div className="dashGrid">
        <div className="dashMain">
          <Panel
            title="AI 自动交易 Dashboard"
            subtitle="策略（AI 决策/风控/执行）× 账户（余额/订单）× 系统健康"
            right={
              <div className="row">
                <Button onClick={() => calls.run(refreshAll)} disabled={calls.state.busy}>
                  刷新
                </Button>
                <button className={`chip ${autoRefresh ? "on" : ""}`} onClick={() => setAutoRefresh((v) => !v)}>
                  Auto Refresh: {autoRefresh ? "ON" : "OFF"}
                </button>
                <a href="/swagger" target="_blank" rel="noreferrer">
                  Swagger
                </a>
              </div>
            }
          >
            <div className="statsGrid">
              <div className="statCard">
                <div className="k">Execution Mode</div>
                <div className="v">
                  <span className={`badge ${modeBadge}`}>{executionMode}</span>
                </div>
                <div className="s">Live 会真实下单；建议先 DryRun 观察策略。</div>
              </div>
              <div className="statCard">
                <div className="k">Symbol / Interval</div>
                <div className="v mono">
                  {meta?.trading.symbol ?? symbol} · {meta?.trading.interval ?? "-"}
                </div>
                <div className="s">数据采样周期影响分析与决策频率。</div>
              </div>
              <div className="statCard">
                <div className="k">Last Price</div>
                <div className="v mono">{ticker ? formatNum(ticker.lastPrice, 6) : "-"}</div>
                <div className="s">{errors.ticker ? `Ticker error: ${errors.ticker}` : formatIso(ticker?.timestamp)}</div>
              </div>
              <div className="statCard">
                <div className="k">USDT Equity</div>
                <div className="v mono">{usdt ? formatNum(usdt.balance, 4) : "-"}</div>
                <div className="s">{usdt ? `available=${formatNum(usdt.available, 4)} frozen=${formatNum(usdt.frozen, 4)}` : "从 /api/weex-test/balances 获取"}</div>
              </div>
            </div>

            <div className="dashControls">
              <div className="row">
                <Button
                  variant="primary"
                  disabled={calls.state.busy}
                  onClick={() =>
                    calls.run(async () => {
                      await apiFetch("/api/trading/initialize", { method: "POST", body: "{}" });
                      await refreshAll();
                    })
                  }
                >
                  Initialize
                </Button>
                <Button
                  variant="primary"
                  disabled={calls.state.busy}
                  onClick={() =>
                    calls.run(async () => {
                      if (executionMode.toUpperCase().includes("LIVE")) {
                        const ok = window.confirm("当前是 Live 模式：将真实下单。确认 Start？");
                        if (!ok) return;
                      }
                      await apiFetch("/api/trading/start", { method: "POST", body: "{}" });
                      await refreshAll();
                    })
                  }
                >
                  Start
                </Button>
                <Button
                  variant="danger"
                  disabled={calls.state.busy}
                  onClick={() =>
                    calls.run(async () => {
                      const q = new URLSearchParams({ reason: stopReason });
                      await apiFetch(`/api/trading/stop?${q.toString()}`, { method: "POST", body: "{}" });
                      await refreshAll();
                    })
                  }
                >
                  Stop
                </Button>
                <Button
                  disabled={calls.state.busy}
                  onClick={() =>
                    calls.run(async () => {
                      await apiFetch("/api/trading/sync-account", { method: "POST", body: "{}" });
                      await refreshAll();
                    })
                  }
                >
                  Sync Account
                </Button>
                <div className="field" style={{ minWidth: 220 }}>
                  <label>Stop reason</label>
                  <input value={stopReason} onChange={(e) => setStopReason(e.target.value)} />
                </div>
                <div className="field" style={{ minWidth: 200 }}>
                  <label>Symbol（查询余额/订单/行情）</label>
                  <input
                    value={symbol}
                    onChange={(e) => {
                      setSymbolTouched(true);
                      setSymbol(e.target.value);
                    }}
                    placeholder="cmt_btcusdt"
                  />
                </div>
                <div className="muted" style={{ marginLeft: "auto" }}>
                  {lastUpdatedAt ? `Updated: ${lastUpdatedAt.toLocaleTimeString()}` : "—"}
                  {calls.state.error ? <span style={{ marginLeft: 10, color: "rgba(239, 68, 68, 0.9)" }}>{calls.state.error}</span> : null}
                </div>
              </div>
            </div>
          </Panel>

          <div className="grid cols-2-eq">
            <Panel
              title="仓位概览"
              subtitle="来自 /api/ai-wars/weex_ai_account_position_all_position（合约：/capi/v2/account/position/allPosition）"
            >
              <PositionsViz raw={positionsTool} error={errors.positions} />
            </Panel>

            <Panel title="成交概览" subtitle="来自 /api/ai-wars/weex_ai_order_fills（合约：/capi/v2/order/fills，默认 limit=100）">
              <FillsViz raw={fillsTool} error={errors.fills} />
            </Panel>
          </div>

          <Panel
            title="AI 策略 · 决策时间线"
            subtitle="来自 TradeAudit 的 Markdown（每个 cycle：AI 分析 → 决策 → 风控 → 执行）"
            right={
              <div className="row">
                <span className="muted">
                  {audit?.file ? (
                    <>
                      Log: <span className="mono">{audit.file}</span> ({formatIso(audit.updatedAtUtc)})
                    </>
                  ) : (
                    "暂无策略日志"
                  )}
                </span>
              </div>
            }
          >
            {errors.audit ? <div className="kvItem" style={{ borderColor: "rgba(239, 68, 68, 0.35)" }}>{errors.audit}</div> : null}

            <div className="timelineGrid">
              <div className="timelineList">
                {cycles.length ? (
                  cycles.slice(0, 18).map((c) => {
                    const dir = c.direction ?? "HOLD";
                    const kind = badgeKindForDirection(dir);
                    const active = selectedCycleId === c.cycleId;
                    return (
                      <button
                        key={c.cycleId}
                        className={`timelineItem ${active ? "active" : ""}`}
                        onClick={() => setSelectedCycleId(c.cycleId)}
                      >
                        <div className="row" style={{ justifyContent: "space-between", gap: 8 }}>
                          <span className={`badge ${kind}`}>{dir}</span>
                          <span className="mono muted">{c.endTime ? c.endTime : ""}</span>
                        </div>
                        <div className="muted" style={{ marginTop: 6 }}>
                          conf={c.confidence ?? "-"} · {c.executedToRisk ? "toRisk=YES" : "toRisk=NO"} · {c.executionMode ?? "-"}
                        </div>
                        <div className="muted" style={{ marginTop: 4 }}>
                          {c.risk?.result ? `risk: ${c.risk.result}` : "risk: -"} · {c.execution ? c.execution : "exec: -"}
                        </div>
                      </button>
                    );
                  })
                ) : (
                  <div className="muted">暂无 cycle（先 Start 让系统跑一会儿）</div>
                )}
              </div>

              <div className="timelineDetail">
                {selected ? (
                  <div className="kv">
                    <div className="kvItem">
                      <div className="k">Decision</div>
                      <div className="v">
                        <div className="row" style={{ gap: 8 }}>
                          <span className={`badge ${badgeKindForDirection(selected.direction ?? "HOLD")}`}>
                            {selected.direction ?? "HOLD"}
                          </span>
                          <span className="mono">confidence={selected.confidence ?? "-"}</span>
                          <span className="mono">mode={selected.executionMode ?? "-"}</span>
                          <span className="mono">toRisk={selected.executedToRisk ? "YES" : "NO"}</span>
                        </div>
                        <div className="muted" style={{ marginTop: 6 }}>
                          cycle={selected.cycleId} · decisionId={selected.decisionId ?? "-"} · trigger={selected.trigger ?? "-"}
                        </div>
                      </div>
                    </div>

                    <div className="kvItem">
                      <div className="k">AI Strategy（可读）</div>
                      <div className="v">
                        <div className="kv" style={{ gap: 6 }}>
                          <div className="row" style={{ justifyContent: "space-between" }}>
                            <span className="muted2">Sentiment</span>
                            <span style={{ textAlign: "right" }}>{selected.ai?.sentiment ?? "-"}</span>
                          </div>
                          <div className="row" style={{ justifyContent: "space-between" }}>
                            <span className="muted2">Technical</span>
                            <span style={{ textAlign: "right" }}>{selected.ai?.technical ?? "-"}</span>
                          </div>
                          <div className="row" style={{ justifyContent: "space-between" }}>
                            <span className="muted2">News</span>
                            <span style={{ textAlign: "right" }}>{selected.ai?.news ?? "-"}</span>
                          </div>
                          <div className="row" style={{ justifyContent: "space-between" }}>
                            <span className="muted2">Reasoning</span>
                            <span style={{ textAlign: "right" }}>{selected.ai?.reasoning ?? "-"}</span>
                          </div>
                        </div>
                      </div>
                    </div>

                    <div className="kvItem">
                      <div className="k">Risk / Execution</div>
                      <div className="v">
                        <div className="row" style={{ gap: 8, marginBottom: 8 }}>
                          <span className="badge gray">{selected.risk?.result ?? "risk: -"}</span>
                          <span className="badge gray">{selected.execution ?? "exec: -"}</span>
                        </div>
                        {selected.risk?.details?.length ? (
                          <div className="muted">
                            {selected.risk.details.slice(0, 6).map((x, i) => (
                              <div key={i}>- {x}</div>
                            ))}
                          </div>
                        ) : null}
                      </div>
                    </div>

                    <div className="kvItem">
                      <div className="k">Raw</div>
                      <pre className="pre" style={{ maxHeight: 240 }}>
                        <code>{audit?.content ? audit.content.slice(0, 12000) : ""}</code>
                      </pre>
                    </div>
                  </div>
                ) : (
                  <div className="muted">选择一个 cycle 查看详情</div>
                )}
              </div>
            </div>
          </Panel>

          <Panel title="当前订单（Open Orders）" subtitle="来自 /api/weex-test/open-orders（合约：/capi/v2/order/current）">
            {errors.orders ? (
              <div className="kvItem" style={{ borderColor: "rgba(239, 68, 68, 0.35)", marginBottom: 12 }}>
                <div className="k">Orders error</div>
                <div className="v">{errors.orders}</div>
              </div>
            ) : null}

            {orders && orders.length ? (
              <div className="tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Symbol</th>
                      <th>Side</th>
                      <th>Type</th>
                      <th>Status</th>
                      <th className="num">Price</th>
                      <th className="num">Qty</th>
                      <th className="num">Filled</th>
                      <th className="mono">OrderId</th>
                      <th className="mono">Created</th>
                    </tr>
                  </thead>
                  <tbody>
                    {orders.slice(0, 30).map((o) => (
                      <tr key={o.orderId}>
                        <td className="mono">{o.symbol}</td>
                        <td>
                          <span className={`badge ${o.side.toLowerCase() === "buy" ? "buy" : "sell"}`}>{o.side}</span>
                        </td>
                        <td className="mono">{o.orderType}</td>
                        <td className="mono">{o.status}</td>
                        <td className="num mono">{formatNum(o.price, 6)}</td>
                        <td className="num mono">{formatNum(o.quantity, 6)}</td>
                        <td className="num mono">{formatNum(o.filledQuantity, 6)}</td>
                        <td className="mono">{o.orderId}</td>
                        <td className="mono">{formatIso(o.createTime)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <div className="muted">暂无 open orders</div>
            )}
          </Panel>
        </div>

        <div className="dashSide">
          <Panel title="环境信息" subtitle="来自 /api/meta（安全配置快照）">
            {errors.meta ? (
              <div className="kvItem" style={{ borderColor: "rgba(239, 68, 68, 0.35)", marginBottom: 12 }}>
                <div className="k">Meta error</div>
                <div className="v">{errors.meta}</div>
              </div>
            ) : null}

            {meta ? (
              <div className="kv">
                <div className="kvItem">
                  <div className="k">Weex</div>
                  <div className="v">
                    <div className="row" style={{ justifyContent: "space-between" }}>
                      <span className="muted2">Mode</span>
                      <span className="mono">{meta.weex.mode}</span>
                    </div>
                    <div className="row" style={{ justifyContent: "space-between" }}>
                      <span className="muted2">BaseUrl</span>
                      <span className="mono" style={{ textAlign: "right" }}>
                        {meta.weex.baseUrl}
                      </span>
                    </div>
                  </div>
                </div>
                <div className="kvItem">
                  <div className="k">Trading</div>
                  <div className="v">
                    <div className="row" style={{ justifyContent: "space-between" }}>
                      <span className="muted2">Symbol</span>
                      <span className="mono">{meta.trading.symbol}</span>
                    </div>
                    <div className="row" style={{ justifyContent: "space-between" }}>
                      <span className="muted2">Interval</span>
                      <span className="mono">{meta.trading.interval}</span>
                    </div>
                    <div className="row" style={{ justifyContent: "space-between" }}>
                      <span className="muted2">Mode</span>
                      <span className="mono">{meta.trading.executionMode}</span>
                    </div>
                  </div>
                </div>
                <div className="kvItem">
                  <div className="k">Audit</div>
                  <div className="v">
                    <div className="row" style={{ justifyContent: "space-between" }}>
                      <span className="muted2">Enabled</span>
                      <span className="mono">{meta.audit.enabled ? "true" : "false"}</span>
                    </div>
                    <div className="row" style={{ justifyContent: "space-between" }}>
                      <span className="muted2">OutputDir</span>
                      <span className="mono">{meta.audit.outputDir}</span>
                    </div>
                  </div>
                </div>
              </div>
            ) : (
              <div className="muted">正在加载 meta…</div>
            )}
          </Panel>

          <Panel title="余额（Balances）" subtitle="来自 /api/weex-test/balances（合约：/capi/v2/account/assets）">
            {errors.balances ? (
              <div className="kvItem" style={{ borderColor: "rgba(239, 68, 68, 0.35)", marginBottom: 12 }}>
                <div className="k">Balances error</div>
                <div className="v">{errors.balances}</div>
              </div>
            ) : null}

            {balances && balances.length ? (
              <div className="tableWrap" style={{ maxHeight: 420 }}>
                <table className="table">
                  <thead>
                    <tr>
                      <th>Currency</th>
                      <th className="num">Balance</th>
                      <th className="num">Available</th>
                      <th className="num">Frozen</th>
                    </tr>
                  </thead>
                  <tbody>
                    {balances.slice(0, 50).map((b) => (
                      <tr key={b.currency}>
                        <td className="mono">{b.currency}</td>
                        <td className="num mono">{formatNum(b.balance, 6)}</td>
                        <td className="num mono">{formatNum(b.available, 6)}</td>
                        <td className="num mono">{formatNum(b.frozen, 6)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <div className="muted">暂无余额数据</div>
            )}
          </Panel>

          <Panel title="系统健康" subtitle="Agent 状态（WS 不可用时也会自动走 REST polling）">
            {errors.status ? (
              <div className="kvItem" style={{ borderColor: "rgba(239, 68, 68, 0.35)", marginBottom: 12 }}>
                <div className="k">Status error</div>
                <div className="v">{errors.status}</div>
              </div>
            ) : null}

            {status ? (
              <div className="row" style={{ gap: 8 }}>
                <StatusPill label="DataCollector" value={status.dataCollector} />
                <StatusPill label="Sentiment" value={status.sentimentAnalyst} />
                <StatusPill label="Technical" value={status.technicalAnalyst} />
                <StatusPill label="Coordinator" value={status.coordinator} />
                <StatusPill label="Risk" value={status.riskManager} />
                <StatusPill label="Executor" value={status.executor} />
                <StatusPill label="Audit" value={status.tradeAudit} />
                <StatusPill label="AiWars" value={status.aiWarsUploader} />
              </div>
            ) : (
              <div className="muted">正在加载 status…</div>
            )}

            <div className="kv" style={{ marginTop: 12 }}>
              <details className="details" open={agentsExpanded} onToggle={(e) => setAgentsExpanded((e.target as HTMLDetailsElement).open)}>
                <summary className="detailsSummary">
                  <span className="muted2">Agents（点击展开）</span>
                  <span className="muted" style={{ marginLeft: "auto" }}>
                    {agentsExpanded ? "收起" : "展开"}
                  </span>
                </summary>
                <div className="kvItem" style={{ marginTop: 10 }}>
                  <div className="k">Agents（/api/agents）</div>
                  <div className="v">
                    {agents ? (
                      <div className="kv" style={{ gap: 8 }}>
                        {agents.agents.map((x) => (
                          <div key={x.name} className="row" style={{ justifyContent: "space-between" }}>
                            <span style={{ color: "var(--muted2)", fontWeight: 650 }}>{x.name}</span>
                            <span style={{ textAlign: "right" }}>{x.status}</span>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <span className="muted">正在加载 agents…</span>
                    )}
                  </div>
                </div>
              </details>
            </div>
          </Panel>
        </div>
      </div>
    </div>
  );
}
