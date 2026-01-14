import React from "react";
import { prettyJson } from "../api";

type AnyObj = Record<string, unknown>;

type StreamItem = {
  id: string;
  receivedAt: number;
  symbol: string;
  typeName: string;
  ts?: string;
  summary: string;
  payload?: AnyObj;
  raw: unknown;
};

function safeString(x: unknown): string {
  if (typeof x === "string") return x;
  if (x == null) return "";
  return String(x);
}

function pickSymbol(payload: AnyObj): string | null {
  const sym = payload.symbol;
  if (typeof sym === "string" && sym.trim()) return sym.trim();

  const affected = payload.affectedSymbols;
  if (Array.isArray(affected) && affected.length) {
    const s = affected.find((x) => typeof x === "string" && x.trim());
    if (typeof s === "string") return s.trim();
  }

  return null;
}

function parseTypeName(typeUrl: unknown): string {
  const t = safeString(typeUrl);
  if (!t) return "UnknownEvent";
  const lastSlash = t.lastIndexOf("/");
  return lastSlash >= 0 ? t.slice(lastSlash + 1) : t;
}

function parseStreamLine(line: string): StreamItem | null {
  // Line is a JSONL entry:
  // { auditRunId, auditAgentId, envelope: { payload: { "@type": "...", ... } , timestamp... } }
  let root: AnyObj;
  try {
    root = JSON.parse(line) as AnyObj;
  } catch {
    return null;
  }

  const envelope = (root.envelope ?? null) as AnyObj | null;
  const payload = (envelope?.payload ?? null) as AnyObj | null;
  const typeName = parseTypeName(payload?.["@type"]);
  const symbol = payload ? pickSymbol(payload) : null;

  const ts =
    (typeof envelope?.timestamp === "string" ? (envelope.timestamp as string) : undefined) ??
    (typeof payload?.timestamp === "string" ? (payload.timestamp as string) : undefined);

  const summary = buildSummary(typeName, payload ?? {});

  return {
    id: `${Date.now()}_${Math.random().toString(16).slice(2)}`,
    receivedAt: Date.now(),
    symbol: symbol ?? "GLOBAL",
    typeName,
    ts,
    summary,
    payload: payload ?? undefined,
    raw: root,
  };
}

function num(x: unknown): number | null {
  const n = typeof x === "number" ? x : typeof x === "string" ? Number(x) : NaN;
  return Number.isFinite(n) ? n : null;
}

function buildSummary(typeName: string, payload: AnyObj): string {
  switch (typeName) {
    case "DecisionCycleStartedEvent": {
      const trigger = safeString(payload.trigger);
      return trigger ? `cycle started · ${trigger}` : "cycle started";
    }
    case "DecisionCycleCompletedEvent": {
      const dir = safeString(payload.direction).toUpperCase() || "HOLD";
      const executed = payload.executed === true ? "executed" : "not-executed";
      const conf = num(payload.confidence);
      return `cycle completed · ${dir} · ${executed}${conf != null ? ` · conf=${conf}` : ""}`;
    }
    case "TradingDecisionEvent": {
      const dir = safeString(payload.direction).toUpperCase() || "HOLD";
      const conf = num(payload.confidence);
      const pos = num(payload.suggestedPositionPct);
      return `${dir}${conf != null ? ` · conf=${conf}` : ""}${pos != null ? ` · pos=${pos.toFixed(2)}%` : ""}`;
    }
    case "MarketSentimentAnalysisEvent": {
      const score = num(payload.sentimentScore);
      const trend = safeString(payload.sentimentTrend).toUpperCase();
      const conf = num(payload.confidence);
      const sum = safeString(payload.analysisSummary);
      return `sentiment · score=${score ?? "?"}${trend ? ` · ${trend}` : ""}${conf != null ? ` · conf=${conf}` : ""}${sum ? ` · ${sum}` : ""}`;
    }
    case "TechnicalAnalysisEvent": {
      const trend = safeString(payload.trendDirection).toUpperCase();
      const strength = num(payload.trendStrength);
      const signal = safeString(payload.signal).toUpperCase();
      const conf = num(payload.confidence);
      const sum = safeString(payload.analysisSummary);
      return `technical · ${trend || "?"}${strength != null ? `(${strength})` : ""}${signal ? ` · ${signal}` : ""}${conf != null ? ` · conf=${conf}` : ""}${sum ? ` · ${sum}` : ""}`;
    }
    case "ApprovedTradeEvent": {
      const side = safeString(payload.side).toUpperCase();
      const qty = num(payload.quantity);
      const price = num(payload.price);
      return `risk approved · ${side || "?"}${qty != null ? ` · qty=${qty}` : ""}${price != null ? ` · price=${price}` : ""}`;
    }
    case "TradeRejectedEvent": {
      const notes = safeString(payload.riskNotes);
      return notes ? `risk rejected · ${notes}` : "risk rejected";
    }
    case "OrderExecutedEvent": {
      const orderId = safeString(payload.orderId);
      const side = safeString(payload.side).toUpperCase();
      const qty = num(payload.quantity);
      const price = num(payload.price);
      return `executed · ${side || "?"}${qty != null ? ` · qty=${qty}` : ""}${price != null ? ` · price=${price}` : ""}${orderId ? ` · orderId=${orderId}` : ""}`;
    }
    case "OrderFailedEvent": {
      const err = safeString(payload.errorMessage || payload.error);
      return err ? `order failed · ${err}` : "order failed";
    }
    case "AiWarsLogUploadRequestedEvent":
      return "ai-wars upload requested";
    case "AiWarsLogUploadSucceededEvent":
      return "ai-wars upload succeeded";
    case "AiWarsLogUploadFailedEvent": {
      const err = safeString(payload.error);
      return err ? `ai-wars upload failed · ${err}` : "ai-wars upload failed";
    }
    default:
      return typeName;
  }
}

function formatTime(ts?: string): string {
  if (!ts) return "-";
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return ts;
  return d.toLocaleTimeString();
}

export function AiOutputStream(props: { symbols?: string[] }) {
  const [connected, setConnected] = React.useState(false);
  const [itemsBySymbol, setItemsBySymbol] = React.useState<Record<string, StreamItem[]>>({});
  const [paused, setPaused] = React.useState(false);
  // Multi-symbol mode: show all symbols by default; allow user to collapse to 10.
  const [showAllCards, setShowAllCards] = React.useState(true);
  const [expandedBySymbol, setExpandedBySymbol] = React.useState<Record<string, boolean>>({});

  const symbols = props.symbols && props.symbols.length ? props.symbols : Object.keys(itemsBySymbol);
  const visibleSymbols = (symbols.length ? symbols : ["GLOBAL"]).slice(0, showAllCards ? 9999 : 10);

  type SentimentStatus = {
    score?: number;
    trend?: string;
    confidence?: number;
    summary?: string;
    ts?: string;
    updatedAt: number;
  };
  type TechnicalStatus = {
    trend?: string;
    signal?: string;
    rsi?: number;
    strength?: number;
    confidence?: number;
    summary?: string;
    ts?: string;
    updatedAt: number;
  };
  const [statusBySymbol, setStatusBySymbol] = React.useState<
    Record<string, { sentiment?: SentimentStatus; technical?: TechnicalStatus }>
  >({});

  function normText(s: unknown): string {
    return safeString(s).replace(/\s+/g, " ").trim();
  }

  function asNum(x: unknown): number | undefined {
    const n = num(x);
    return n == null ? undefined : n;
  }

  function isSignificantSentimentChange(prev: SentimentStatus | undefined, next: SentimentStatus): boolean {
    if (!prev) return true;
    if ((prev.trend || "").toUpperCase() !== (next.trend || "").toUpperCase()) return true;
    const ds = (next.score ?? 0) - (prev.score ?? 0);
    if (Math.abs(ds) >= 10) return true;
    const dc = (next.confidence ?? 0) - (prev.confidence ?? 0);
    if (Math.abs(dc) >= 15) return true;
    if (normText(prev.summary) !== normText(next.summary)) return true;
    return false;
  }

  function isSignificantTechnicalChange(prev: TechnicalStatus | undefined, next: TechnicalStatus): boolean {
    if (!prev) return true;
    if ((prev.trend || "").toUpperCase() !== (next.trend || "").toUpperCase()) return true;
    if ((prev.signal || "").toUpperCase() !== (next.signal || "").toUpperCase()) return true;
    const dr = (next.rsi ?? 0) - (prev.rsi ?? 0);
    if (Math.abs(dr) >= 5) return true;
    const ds = (next.strength ?? 0) - (prev.strength ?? 0);
    if (Math.abs(ds) >= 2) return true;
    const dc = (next.confidence ?? 0) - (prev.confidence ?? 0);
    if (Math.abs(dc) >= 15) return true;
    if (normText(prev.summary) !== normText(next.summary)) return true;
    return false;
  }

  function formatStatusAge(updatedAt: number): string {
    const sec = Math.max(0, Math.floor((Date.now() - updatedAt) / 1000));
    if (sec < 60) return `${sec}s`;
    const m = Math.floor(sec / 60);
    const s = sec % 60;
    return `${m}m${s}s`;
  }

  React.useEffect(() => {
    const es = new EventSource(`/api/audit/stream?replayLines=250`);
    es.onopen = () => setConnected(true);
    es.onerror = () => setConnected(false);
    es.onmessage = (e) => {
      if (paused) return;
      const item = parseStreamLine(e.data);
      if (!item) return;

      // Market analysis events: do not spam the event list.
      // Instead keep a per-symbol status that updates only on significant change.
      if (item.typeName === "MarketSentimentAnalysisEvent") {
        const p = (item.payload ?? {}) as AnyObj;
        const sym = item.symbol || "GLOBAL";
        const next: SentimentStatus = {
          score: asNum(p.sentimentScore),
          trend: normText(p.sentimentTrend),
          confidence: asNum(p.confidence),
          summary: normText(p.analysisSummary),
          ts: typeof p.timestamp === "string" ? (p.timestamp as string) : item.ts,
          updatedAt: Date.now(),
        };
        setStatusBySymbol((prev) => {
          const cur = prev[sym]?.sentiment;
          if (!isSignificantSentimentChange(cur, next)) return prev;
          return { ...prev, [sym]: { ...(prev[sym] ?? {}), sentiment: next } };
        });
        return;
      }

      if (item.typeName === "TechnicalAnalysisEvent") {
        const p = (item.payload ?? {}) as AnyObj;
        const sym = item.symbol || "GLOBAL";
        const next: TechnicalStatus = {
          trend: normText(p.trendDirection),
          signal: normText(p.signal),
          rsi: asNum(p.rsi),
          strength: asNum(p.trendStrength),
          confidence: asNum(p.confidence),
          summary: normText(p.analysisSummary),
          ts: typeof p.timestamp === "string" ? (p.timestamp as string) : item.ts,
          updatedAt: Date.now(),
        };
        setStatusBySymbol((prev) => {
          const cur = prev[sym]?.technical;
          if (!isSignificantTechnicalChange(cur, next)) return prev;
          return { ...prev, [sym]: { ...(prev[sym] ?? {}), technical: next } };
        });
        return;
      }

      setItemsBySymbol((prev) => {
        const next = { ...prev };
        const key = item.symbol || "GLOBAL";
        const arr = next[key] ? [...next[key]] : [];
        arr.unshift(item);
        if (arr.length > 200) arr.length = 200;
        next[key] = arr;
        return next;
      });
    };
    return () => es.close();
  }, [paused]);

  return (
    <div>
      <div className="ai-stream-toolbar">
        <div className={`ai-stream-dot ${connected ? "ok" : "bad"}`} />
        <div className="ai-stream-meta">
          <div className="ai-stream-title">AI Output Stream</div>
          <div className="ai-stream-sub">SSE from /api/audit/stream · {connected ? "connected" : "disconnected"}</div>
        </div>
        <div style={{ flex: 1 }} />
        <button className="btn-secondary" onClick={() => setShowAllCards((x) => !x)}>
          {showAllCards ? "Show 10" : "Show all"}
        </button>
        <button
          className="btn-secondary"
          onClick={() => {
            const wantOpen = visibleSymbols.some((s) => !expandedBySymbol[s]);
            const next: Record<string, boolean> = { ...expandedBySymbol };
            for (const s of visibleSymbols) next[s] = wantOpen;
            setExpandedBySymbol(next);
          }}
          style={{ marginLeft: 8 }}
        >
          {visibleSymbols.every((s) => expandedBySymbol[s]) ? "Collapse all" : "Expand all"}
        </button>
        <button className="btn-secondary" onClick={() => setPaused((x) => !x)}>
          {paused ? "Resume" : "Pause"}
        </button>
        <button className="btn-secondary" onClick={() => setItemsBySymbol({})} style={{ marginLeft: 8 }}>
          Clear
        </button>
      </div>

      <div className="ai-cards-grid">
        {visibleSymbols.map((sym) => {
          const items = itemsBySymbol[sym] ?? [];
          const latest = items[0];
          const open = !!expandedBySymbol[sym];
          const st = statusBySymbol[sym];
          return (
            <details
              key={sym}
              className={`ai-card ${connected ? "streaming" : ""}`}
              open={open}
              onToggle={(e) => {
                const isOpen = (e.target as HTMLDetailsElement).open;
                setExpandedBySymbol((prev) => ({ ...prev, [sym]: isOpen }));
              }}
            >
              <summary className="ai-card-header">
                <div className="ai-card-symbol">{sym}</div>
                <div className="ai-card-count">{items.length} events</div>
                <div className="ai-card-status">
                  <span className="ai-pill">
                    S: {st?.sentiment?.score ?? "-"} {st?.sentiment?.trend ? `· ${st.sentiment.trend}` : ""}
                    {st?.sentiment?.updatedAt ? ` · ${formatStatusAge(st.sentiment.updatedAt)}` : ""}
                  </span>
                  <span className="ai-pill">
                    T: {st?.technical?.signal ?? "-"} {st?.technical?.trend ? `· ${st.technical.trend}` : ""}
                    {st?.technical?.updatedAt ? ` · ${formatStatusAge(st.technical.updatedAt)}` : ""}
                  </span>
                </div>
                <div style={{ flex: 1 }} />
                <div className="ai-card-time">{latest ? formatTime(latest.ts) : "-"}</div>
              </summary>

              {items.length === 0 ? (
                <div className="ai-card-empty">Waiting for events…</div>
              ) : (
                <div className="ai-card-body">
                  {items.slice(0, 40).map((it) => (
                    <details key={it.id} className="ai-item" open={false}>
                      <summary className="ai-item-summary">
                        <span className="ai-item-type">{it.typeName}</span>
                        <span className="ai-item-text">{it.summary}</span>
                        <span style={{ flex: 1 }} />
                        <span className="ai-item-ts">{formatTime(it.ts)}</span>
                      </summary>
                      <div className="ai-item-detail">
                        <div className="ai-item-kv">
                          <div>
                            <span className="ai-k">type</span>
                            <span className="ai-v">{it.typeName}</span>
                          </div>
                          <div>
                            <span className="ai-k">symbol</span>
                            <span className="ai-v">{it.symbol}</span>
                          </div>
                        </div>
                        <details style={{ marginTop: 8 }}>
                          <summary className="ai-raw-summary">Raw JSON</summary>
                          <pre className="ai-raw">{prettyJson(it.raw)}</pre>
                        </details>
                      </div>
                    </details>
                  ))}
                  {items.length > 40 ? <div className="ai-card-more">… {items.length - 40} more (kept in memory)</div> : null}
                </div>
              )}
            </details>
          );
        })}
      </div>
    </div>
  );
}


