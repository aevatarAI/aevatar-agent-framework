import React from "react";
import { apiFetch, fetchPositions, sendAgUiChat } from "../api";
import type { BalanceInfo, MetaResponse, PositionsResponse, TickerResponse } from "../types";
import { Button } from "../components/Button";
import { useAgUiStream } from "../hooks/useAgUiStream";

type AgUiMessage = {
  id: string;
  role: string;
  content: string;
  name?: string;
};

const DEFAULT_SYMBOL = "cmt_btcusdt";

function formatNum(n: number | null | undefined, digits = 2): string {
  if (!Number.isFinite(n ?? NaN)) return "-";
  return Number(n).toLocaleString(undefined, {
    maximumFractionDigits: digits,
    minimumFractionDigits: 0,
  });
}

function formatPct(n: number | null | undefined): string {
  if (!Number.isFinite(n ?? NaN)) return "-";
  return `${n!.toFixed(2)}%`;
}

function classForChange(n: number | null | undefined): string {
  if (!Number.isFinite(n ?? NaN)) return "";
  return (n ?? 0) >= 0 ? "up" : "down";
}

export function TradingPage() {
  const [meta, setMeta] = React.useState<MetaResponse | null>(null);
  const [symbol, setSymbol] = React.useState(DEFAULT_SYMBOL);
  const [ticker, setTicker] = React.useState<TickerResponse | null>(null);
  const [positions, setPositions] = React.useState<PositionsResponse | null>(null);
  const [balances, setBalances] = React.useState<Array<BalanceInfo> | null>(null);
  const [lastUpdated, setLastUpdated] = React.useState<Date | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const [chatInput, setChatInput] = React.useState("");
  const [chatSending, setChatSending] = React.useState(false);
  const [chatError, setChatError] = React.useState<string | null>(null);
  const [sidebarCollapsed, setSidebarCollapsed] = React.useState(false);

  const agui = useAgUiStream(true);

  /* --------------------------------------------------------------------------
   * Market refresh (price + positions)
   * -------------------------------------------------------------------------- */
  const refreshMarket = React.useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [metaRes, tickerRes, posRes, balRes] = await Promise.all([
        apiFetch<MetaResponse>("/api/meta"),
        apiFetch<TickerResponse>(
          `/api/weex-test/ticker?${new URLSearchParams({ symbol }).toString()}`,
        ),
        fetchPositions(),
        apiFetch<{ count: number; balances: Array<BalanceInfo> }>("/api/weex-test/balances"),
      ]);

      setMeta(metaRes);
      setTicker(tickerRes);
      setPositions(posRes);
      setBalances(balRes.balances ?? []);
      setLastUpdated(new Date());
    } catch (e) {
      const msg =
        typeof e === "object" && e && "message" in e ? String((e as { message?: unknown }).message) : String(e);
      setError(msg);
    } finally {
      setLoading(false);
    }
  }, [symbol]);

  React.useEffect(() => {
    refreshMarket();
  }, [refreshMarket]);

  React.useEffect(() => {
    const timer = setInterval(() => refreshMarket(), 8000);
    return () => clearInterval(timer);
  }, [refreshMarket]);

  React.useEffect(() => {
    if (!meta?.exchange?.symbols?.length) return;
    if (meta.exchange.symbols.includes(symbol)) return;
    setSymbol(meta.exchange.symbols[0]);
  }, [meta, symbol]);

  const netAsset = React.useMemo(() => {
    if (!balances || balances.length === 0) {
      return { value: null as number | null, unit: "USDT", note: "暂无余额数据" };
    }
    const usdt = balances.find((b) => b.currency.toUpperCase() === "USDT");
    if (usdt) {
      return { value: usdt.balance, unit: "USDT", note: "USDT Balance" };
    }
    const total = balances.reduce((sum, b) => sum + (Number.isFinite(b.balance) ? b.balance : 0), 0);
    return { value: total, unit: "TOTAL", note: "未折算" };
  }, [balances]);

  /* --------------------------------------------------------------------------
   * AG-UI Chat (streaming + input)
   * -------------------------------------------------------------------------- */
  const chatMessages = React.useMemo<AgUiMessage[]>(() => {
    return agui.messages
      .filter((m) => m.role === "assistant" || m.role === "user" || m.role === "system")
      .filter((m) => m.name !== "trade_audit");
  }, [agui.messages]);
  const limitedMessages = React.useMemo(() => {
    const max = 120;
    return chatMessages.length > max ? chatMessages.slice(chatMessages.length - max) : chatMessages;
  }, [chatMessages]);

  const sendChat = React.useCallback(async () => {
    const text = chatInput.trim();
    if (!text) return;

    setChatSending(true);
    setChatError(null);
    try {
      await sendAgUiChat(text);
      setChatInput("");
    } catch (e) {
      const msg =
        typeof e === "object" && e && "message" in e ? String((e as { message?: unknown }).message) : String(e);
      setChatError(msg);
    } finally {
      setChatSending(false);
    }
  }, [chatInput]);

  const handleChatKeyDown = React.useCallback(
    (event: React.KeyboardEvent<HTMLTextAreaElement>) => {
      if (event.key !== "Enter") return;
      if (event.shiftKey) return;
      event.preventDefault();
      void sendChat();
    },
    [sendChat],
  );

  const chatEndRef = React.useRef<HTMLDivElement | null>(null);
  React.useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [limitedMessages, agui.streamingMessageId]);

  const symbols = meta?.exchange?.symbols ?? [symbol];
  const change = ticker?.change24h ?? null;
  const isStreaming = agui.streamingMessageId !== null;

  return (
    <div className="tradeRoot">
      <div className="tradeHeader">
        <div>
          <div className="tradeTitle">Trading Console</div>
          <div className="tradeSubtitle">AI 交互 · 币价 · 仓位</div>
        </div>
        <div className="tradeActions">
          <span className={`tradeStatus ${agui.connected ? "ok" : "off"}`}>
            {agui.connected ? "AGUI Online" : "AGUI Offline"}
          </span>
          <Button variant="primary" onClick={() => refreshMarket()} disabled={loading}>
            {loading ? "Refreshing..." : "Refresh"}
          </Button>
        </div>
      </div>

      {error ? <div className="tradeError">加载失败：{error}</div> : null}

      <div className={`tradeLayout ${sidebarCollapsed ? "sidebar-collapsed" : ""}`}>
        <section className="tradeMain">
          <div className="chatShell">
            <div className="chatShellHeader">
              <div>
                <div className="chatShellTitle">AI 交互</div>
                <div className="chatShellSubtitle">实时感知你的想法，辅助决策</div>
              </div>
              <div className="chatShellStatus">
                <span className={`chatStatusDot ${isStreaming ? "live" : agui.connected ? "ok" : "off"}`} />
                <span>{isStreaming ? "Streaming" : agui.connected ? "Connected" : "Offline"}</span>
              </div>
            </div>

            <div className="chatScroll">
              {limitedMessages.length === 0 ? (
                <div className="chatEmpty">还没有对话，先告诉 AI 你的想法。</div>
              ) : (
                limitedMessages.map((msg) => (
                  <div key={msg.id} className={`chatBubble ${msg.role}`}>
                    <div className="chatBubbleRole">{msg.role}</div>
                    <div className="chatBubbleText">{msg.content}</div>
                  </div>
                ))
              )}
              <div ref={chatEndRef} />
            </div>

            <div className="chatComposer">
              <textarea
                value={chatInput}
                onChange={(e) => setChatInput(e.target.value)}
                onKeyDown={handleChatKeyDown}
                placeholder="告诉 AI 你关注的点（Enter 发送，Shift+Enter 换行）"
                rows={3}
              />
              <div className="chatComposerActions">
                {chatError ? <span className="chatError">{chatError}</span> : null}
                <Button
                  variant="primary"
                  onClick={() => void sendChat()}
                  disabled={chatSending || !chatInput.trim()}
                >
                  {chatSending ? "Sending..." : "Send"}
                </Button>
              </div>
            </div>
          </div>
        </section>

        <aside className={`tradeSide ${sidebarCollapsed ? "collapsed" : ""}`}>
          <div className="sideHeader">
            <div className="sideTitle">侧边栏</div>
            <button className="sideToggle" onClick={() => setSidebarCollapsed((v) => !v)}>
              {sidebarCollapsed ? "展开" : "收起"}
            </button>
          </div>

          {!sidebarCollapsed ? (
            <>
              <section className="sideBlock">
                <div className="sideBlockHeader">币价</div>
                <div className="sideContent">
                  <div className="sideRow">
                    <label className="tradeLabel">Price Symbol</label>
                    <select value={symbol} onChange={(e) => setSymbol(e.target.value)}>
                      {symbols.map((s) => (
                        <option key={s} value={s}>
                          {s}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div className="priceCard">
                    <div className="priceTop">
                      <div>
                        <div className="priceSymbol">{ticker?.symbol ?? symbol}</div>
                        <div className="priceValue">{formatNum(ticker?.lastPrice, 4)}</div>
                        <div className={`priceChange ${classForChange(change)}`}>
                          {change === null ? "-" : `${change >= 0 ? "+" : ""}${formatPct(change)}`}
                        </div>
                      </div>
                      <div className="assetCard">
                        <div className="assetLabel">净资产</div>
                        <div className="assetValue">
                          {netAsset.value === null ? "-" : `${formatNum(netAsset.value, 2)} ${netAsset.unit}`}
                        </div>
                        <div className="assetNote">{netAsset.note}</div>
                      </div>
                    </div>
                    <div className="priceGrid">
                      <div>
                        <span>24h 高</span>
                        <strong>{formatNum(ticker?.high24h, 4)}</strong>
                      </div>
                      <div>
                        <span>24h 低</span>
                        <strong>{formatNum(ticker?.low24h, 4)}</strong>
                      </div>
                      <div>
                        <span>24h 量</span>
                        <strong>{formatNum(ticker?.volume24h, 2)}</strong>
                      </div>
                      <div>
                        <span>Bid / Ask</span>
                        <strong>
                          {formatNum(ticker?.bidPrice, 4)} / {formatNum(ticker?.askPrice, 4)}
                        </strong>
                      </div>
                    </div>
                  </div>
                </div>
              </section>

              <section className="sideBlock">
                <div className="sideBlockHeader">仓位</div>
                <div className="sideContent">
                  <div className="sideScroll">
                    <div className="positionsTable">
                      <div className="positionsRow head">
                        <span>Symbol</span>
                        <span>Side</span>
                        <span>Size</span>
                        <span>Entry</span>
                        <span>Mark</span>
                        <span>PnL</span>
                      </div>
                      {(positions?.positions ?? []).length === 0 ? (
                        <div className="positionsEmpty">暂无仓位</div>
                      ) : (
                        positions?.positions.map((pos) => (
                          <div key={`${pos.symbol}-${pos.side}`} className="positionsRow">
                            <span className="mono">{pos.symbol}</span>
                            <span className={`posSide ${pos.side?.toUpperCase()}`}>{pos.side ?? "-"}</span>
                            <span>{formatNum(pos.size, 4)}</span>
                            <span>{formatNum(pos.entryPrice ?? null, 4)}</span>
                            <span>{formatNum(pos.markPrice ?? null, 4)}</span>
                            <span className={pos.unrealizedPnl && pos.unrealizedPnl < 0 ? "down" : "up"}>
                              {formatNum(pos.unrealizedPnl ?? null, 2)}
                            </span>
                          </div>
                        ))
                      )}
                    </div>
                  </div>
                </div>
              </section>
            </>
          ) : null}
        </aside>
      </div>
    </div>
  );
}

