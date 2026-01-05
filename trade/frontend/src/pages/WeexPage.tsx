import React from "react";
import { apiFetch, prettyJson } from "../api";
import type { BalanceInfo, CancelOrderResult, OrderInfo, OrderRequest, OrderResult, TickerResponse } from "../types";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";

type JsonBoxProps = { title: string; value: unknown };
function JsonBox(props: JsonBoxProps) {
  return (
    <div className="kvItem">
      <div className="k">{props.title}</div>
      <pre className="pre">
        <code>{prettyJson(props.value)}</code>
      </pre>
    </div>
  );
}

export function WeexPage() {
  const [symbol, setSymbol] = React.useState("cmt_btcusdt");

  const [ticker, setTicker] = React.useState<TickerResponse | null>(null);
  const [balances, setBalances] = React.useState<Array<BalanceInfo> | null>(null);
  const [openOrders, setOpenOrders] = React.useState<Array<OrderInfo> | null>(null);

  const [place, setPlace] = React.useState<OrderRequest>({
    symbol: "cmt_btcusdt",
    side: "buy",
    orderType: "limit",
    force: "normal",
    quantity: "0.001",
    price: "1",
    clientOrderId: "",
  });
  const [placeResult, setPlaceResult] = React.useState<OrderResult | null>(null);

  const [cancelOrderId, setCancelOrderId] = React.useState("");
  const [cancelClientOrderId, setCancelClientOrderId] = React.useState("");
  const [cancelResult, setCancelResult] = React.useState<CancelOrderResult | null>(null);

  const [error, setError] = React.useState<string | null>(null);
  const [busy, setBusy] = React.useState(false);

  async function run<T>(fn: () => Promise<T>): Promise<T> {
    setBusy(true);
    setError(null);
    try {
      return await fn();
    } catch (e) {
      const msg = typeof e === "object" && e && "message" in e ? String((e as { message?: unknown }).message) : String(e);
      setError(msg);
      throw e;
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="grid cols-2">
      <Panel
        title="WEEX 工具箱（/api/weex-test）"
        subtitle="只用于 hackathon 预检/联调，不依赖 TradingSystem 初始化"
        right={
          <a href="/swagger" target="_blank" rel="noreferrer">
            Swagger
          </a>
        }
      >
        {error ? (
          <div className="kvItem" style={{ borderColor: "rgba(239, 68, 68, 0.35)", marginBottom: 12 }}>
            <div className="k" style={{ color: "rgba(239, 68, 68, 0.9)" }}>
              Error
            </div>
            <div className="v">{error}</div>
          </div>
        ) : null}

        <div className="row" style={{ marginBottom: 12 }}>
          <div className="field" style={{ minWidth: 260 }}>
            <label>Symbol</label>
            <input value={symbol} onChange={(e) => setSymbol(e.target.value)} />
          </div>
          <Button
            disabled={busy}
            onClick={() =>
              run(async () => {
                const q = new URLSearchParams({ symbol });
                const res = await apiFetch<TickerResponse>(`/api/weex-test/ticker?${q.toString()}`);
                setTicker(res);
                return res;
              })
            }
          >
            Get Ticker
          </Button>
          <Button
            disabled={busy}
            onClick={() =>
              run(async () => {
                const res = await apiFetch<{ count: number; balances: Array<BalanceInfo> }>(`/api/weex-test/balances`);
                setBalances(res.balances);
                return res;
              })
            }
          >
            Get Balances
          </Button>
          <Button
            disabled={busy}
            onClick={() =>
              run(async () => {
                const q = new URLSearchParams(symbol ? { symbol } : {});
                const res = await apiFetch<{ count: number; orders: Array<OrderInfo> }>(
                  `/api/weex-test/open-orders?${q.toString()}`,
                );
                setOpenOrders(res.orders);
                return res;
              })
            }
          >
            Get Open Orders
          </Button>
        </div>

        <div className="kv">
          {ticker ? <JsonBox title="Ticker" value={ticker} /> : <div className="muted">Ticker：未请求</div>}
          {balances ? <JsonBox title={`Balances (${balances.length})`} value={balances} /> : <div className="muted">Balances：未请求</div>}
          {openOrders ? (
            <JsonBox title={`Open Orders (${openOrders.length})`} value={openOrders} />
          ) : (
            <div className="muted">Open Orders：未请求</div>
          )}
        </div>
      </Panel>

      <Panel title="下单 / 撤单" subtitle="直接调用 /api/weex-test/place-order 与 /api/weex-test/cancel-order">
        <div className="grid" style={{ gap: 12 }}>
          <div className="kvItem">
            <div className="k">Place Order</div>
            <div className="form">
              <div className="grid cols-2">
                <div className="field">
                  <label>Symbol</label>
                  <input
                    value={place.symbol}
                    onChange={(e) => setPlace((p) => ({ ...p, symbol: e.target.value }))}
                    placeholder="cmt_btcusdt"
                  />
                </div>
                <div className="field">
                  <label>Side</label>
                  <select value={place.side} onChange={(e) => setPlace((p) => ({ ...p, side: e.target.value as "buy" | "sell" }))}>
                    <option value="buy">buy</option>
                    <option value="sell">sell</option>
                  </select>
                </div>
              </div>

              <div className="grid cols-2">
                <div className="field">
                  <label>Order Type</label>
                  <select
                    value={place.orderType}
                    onChange={(e) => setPlace((p) => ({ ...p, orderType: e.target.value as "limit" | "market" }))}
                  >
                    <option value="limit">limit</option>
                    <option value="market">market</option>
                  </select>
                </div>
                <div className="field">
                  <label>Force</label>
                  <select
                    value={place.force ?? "normal"}
                    onChange={(e) =>
                      setPlace((p) => ({ ...p, force: e.target.value as "normal" | "postOnly" | "fok" | "ioc" }))
                    }
                  >
                    <option value="normal">normal</option>
                    <option value="postOnly">postOnly</option>
                    <option value="fok">fok</option>
                    <option value="ioc">ioc</option>
                  </select>
                </div>
              </div>

              <div className="grid cols-2">
                <div className="field">
                  <label>Quantity（string）</label>
                  <input
                    value={place.quantity}
                    onChange={(e) => setPlace((p) => ({ ...p, quantity: e.target.value }))}
                    placeholder="0.001"
                  />
                </div>
                <div className="field">
                  <label>Price（limit 必填）</label>
                  <input
                    value={place.price ?? ""}
                    onChange={(e) => setPlace((p) => ({ ...p, price: e.target.value || undefined }))}
                    placeholder="例如 67000"
                    disabled={place.orderType !== "limit"}
                  />
                </div>
              </div>

              <div className="field">
                <label>ClientOrderId（可选）</label>
                <input
                  value={place.clientOrderId ?? ""}
                  onChange={(e) => setPlace((p) => ({ ...p, clientOrderId: e.target.value || undefined }))}
                  placeholder="你自己的幂等 ID"
                />
              </div>

              <div className="row">
                <Button
                  variant="primary"
                  disabled={busy}
                  onClick={() =>
                    run(async () => {
                      const payload: OrderRequest = {
                        symbol: place.symbol,
                        side: place.side,
                        orderType: place.orderType,
                        force: place.force,
                        quantity: place.quantity,
                        price: place.orderType === "limit" ? place.price : undefined,
                        clientOrderId: place.clientOrderId || undefined,
                      };
                      const res = await apiFetch<OrderResult>("/api/weex-test/place-order", {
                        method: "POST",
                        body: JSON.stringify(payload),
                      });
                      setPlaceResult(res);
                      return res;
                    })
                  }
                >
                  Place Order
                </Button>
                <span className="muted">注意：这是真实下单接口，务必确认你后端配置与账户。</span>
              </div>
              {placeResult ? <JsonBox title="Place Result" value={placeResult} /> : null}
            </div>
          </div>

          <div className="kvItem">
            <div className="k">Cancel Order</div>
            <div className="form">
              <div className="field">
                <label>Symbol</label>
                <input value={symbol} onChange={(e) => setSymbol(e.target.value)} />
              </div>
              <div className="grid cols-2">
                <div className="field">
                  <label>OrderId（可选）</label>
                  <input value={cancelOrderId} onChange={(e) => setCancelOrderId(e.target.value)} placeholder="orderId" />
                </div>
                <div className="field">
                  <label>ClientOrderId（可选）</label>
                  <input
                    value={cancelClientOrderId}
                    onChange={(e) => setCancelClientOrderId(e.target.value)}
                    placeholder="clientOrderId"
                  />
                </div>
              </div>
              <div className="row">
                <Button
                  variant="danger"
                  disabled={busy}
                  onClick={() =>
                    run(async () => {
                      const q = new URLSearchParams({
                        symbol,
                        ...(cancelOrderId ? { orderId: cancelOrderId } : {}),
                        ...(cancelClientOrderId ? { clientOrderId: cancelClientOrderId } : {}),
                      });
                      const res = await apiFetch<CancelOrderResult>(`/api/weex-test/cancel-order?${q.toString()}`, {
                        method: "POST",
                        body: "{}",
                      });
                      setCancelResult(res);
                      return res;
                    })
                  }
                >
                  Cancel
                </Button>
                <span className="muted">后端会把缺失参数传下去；你至少需要提供 orderId 或 clientOrderId。</span>
              </div>
              {cancelResult ? <JsonBox title="Cancel Result" value={cancelResult} /> : null}
            </div>
          </div>
        </div>
      </Panel>
    </div>
  );
}


