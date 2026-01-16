import React from "react";
import { prettyJson } from "../api";

// ============================================================================
//  Exchange-style visualizations (no chart deps)
//
//  Goals:
//  - Positions: long/short exposure + per-position PnL bars (common exchange UI pattern)
//  - Fills: price sparkline + side-colored volume bars
//
//  Notes:
//  - Tool endpoints return loosely-shaped JSON; we parse defensively.
//  - This component intentionally avoids tables (per UX request).
// ============================================================================

type Side = "BUY" | "SELL" | "LONG" | "SHORT" | "UNKNOWN";

type Position = {
  symbol: string;
  side: Side;
  size: number;
  entryPrice?: number;
  markPrice?: number;
  unrealizedPnl?: number;
  notional?: number;
  leverage?: number;
};

type Fill = {
  ts?: number;
  symbol?: string;
  side: Side;
  price?: number;
  qty?: number;
  orderId?: string;
  fee?: number;
};

function isObj(x: unknown): x is Record<string, unknown> {
  return !!x && typeof x === "object" && !Array.isArray(x);
}

function pick(obj: unknown, keys: string[]): unknown {
  if (!isObj(obj)) return undefined;
  for (const k of keys) {
    if (k in obj) return obj[k];
  }
  // case-insensitive fallback
  const lowerMap = new Map<string, string>();
  for (const k of Object.keys(obj)) lowerMap.set(k.toLowerCase(), k);
  for (const k of keys) {
    const real = lowerMap.get(k.toLowerCase());
    if (real) return obj[real];
  }
  return undefined;
}

function toStr(v: unknown): string | undefined {
  if (v == null) return undefined;
  if (typeof v === "string") return v;
  if (typeof v === "number") return Number.isFinite(v) ? String(v) : undefined;
  return undefined;
}

function toNum(v: unknown): number | undefined {
  if (v == null) return undefined;
  if (typeof v === "number") return Number.isFinite(v) ? v : undefined;
  if (typeof v === "string") {
    const s = v.trim();
    if (!s) return undefined;
    const n = Number(s);
    return Number.isFinite(n) ? n : undefined;
  }
  return undefined;
}

function normalizeSide(v: unknown): Side {
  const s = (toStr(v) ?? "").trim().toUpperCase();
  if (!s) return "UNKNOWN";
  if (s === "BUY") return "BUY";
  if (s === "SELL") return "SELL";
  if (s === "LONG") return "LONG";
  if (s === "SHORT") return "SHORT";
  if (s === "OPEN_LONG") return "LONG";
  if (s === "OPEN_SHORT") return "SHORT";
  if (s === "CLOSE_LONG") return "SELL";
  if (s === "CLOSE_SHORT") return "BUY";
  return "UNKNOWN";
}

function tryExtractList(x: unknown): unknown[] {
  if (!x) return [];
  if (Array.isArray(x)) return x;
  if (!isObj(x)) return [];

  const obj = x as Record<string, unknown>;
  for (const k of ["data", "list", "items", "rows", "records", "result"]) {
    const got = tryExtractList(obj[k]);
    if (got.length) return got;
  }
  return [];
}

function formatNum(n: number | undefined, digits = 2): string {
  if (n == null || !Number.isFinite(n)) return "-";
  return n.toLocaleString(undefined, { maximumFractionDigits: digits, minimumFractionDigits: 0 });
}

function formatPct(n: number | undefined, digits = 1): string {
  if (n == null || !Number.isFinite(n)) return "-";
  return `${(n * 100).toFixed(digits)}%`;
}

function formatTime(ts?: number): string {
  if (!ts || !Number.isFinite(ts)) return "-";
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return "-";
  return d.toLocaleTimeString();
}

function formatDateTime(ts?: number): string {
  if (!ts || !Number.isFinite(ts)) return "-";
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return "-";
  return d.toLocaleString();
}

function parsePositions(raw: unknown): Position[] {
  const list = tryExtractList(raw).filter(isObj);
  const out: Position[] = [];

  for (const it of list) {
    const symbol = (toStr(pick(it, ["symbol", "instId", "instrumentId", "contractCode", "contract_code"])) ?? "").trim();
    const side = normalizeSide(pick(it, ["holdSide", "posSide", "positionSide", "side", "direction"]));
    const size =
      toNum(pick(it, ["size", "pos", "position", "positionAmt", "holdVol", "total", "qty", "quantity", "amount"])) ?? 0;

    // AI Wars position fields (as observed):
    // - open_value: notional at entry (string)
    // - unrealizePnl: unrealized PnL (string)
    const openValue = toNum(pick(it, ["open_value", "openValue", "openValue", "open_value"]));

    const entryFromRaw = toNum(pick(it, ["entryPrice", "openPrice", "avgOpenPrice", "avgPrice", "openAvgPrice"]));
    const markFromRaw = toNum(pick(it, ["markPrice", "marketPrice", "lastPrice", "price", "mark_price", "market_price", "last_price"]));
    const unrealizedPnl = toNum(pick(it, ["unrealizePnl", "unrealizedPnl", "upl", "unrealizedProfit", "floatingProfit", "pnl"]));
    const leverage = toNum(pick(it, ["leverage", "lever"]));

    const absSize = Math.abs(size);
    const entryPrice =
      entryFromRaw != null ? entryFromRaw : openValue != null && absSize > 0 ? openValue / absSize : undefined;

    const isShort = side === "SHORT" || side === "SELL";
    const markPrice =
      markFromRaw != null
        ? markFromRaw
        : entryPrice != null && unrealizedPnl != null && absSize > 0
          ? isShort
            ? entryPrice - unrealizedPnl / absSize
            : entryPrice + unrealizedPnl / absSize
          : undefined;

    const computedNotional =
      toNum(pick(it, ["notional", "positionValue", "position_value", "value", "marketValue", "market_value"])) ??
      (markPrice != null && absSize > 0 ? absSize * markPrice : openValue != null ? Math.abs(openValue) : undefined);

    // Skip empty rows
    if (!symbol && !size && unrealizedPnl == null) continue;

    out.push({
      symbol: symbol || "UNKNOWN",
      side,
      size,
      entryPrice,
      markPrice,
      unrealizedPnl,
      notional: computedNotional,
      leverage,
    });
  }

  // Exchange UIs typically show biggest positions first.
  return out.sort((a, b) => (Math.abs(b.notional ?? 0) || Math.abs(b.size)) - (Math.abs(a.notional ?? 0) || Math.abs(a.size)));
}

function parseFills(raw: unknown): Fill[] {
  const list = tryExtractList(raw).filter(isObj);
  const out: Fill[] = [];

  for (const it of list) {
    const symbol = (toStr(pick(it, ["symbol", "instId", "instrumentId", "contractCode", "contract_code"])) ?? "").trim();

    // AI Wars fills fields (as observed):
    // - orderSide: BUY/SELL
    // - positionSide: LONG/SHORT
    // - fillSize: qty (string)
    // - fillValue: quote value (string)  -> price ~= fillValue / fillSize
    // - fillFee: fee (string)
    // - createdTime: unix ms
    const side = normalizeSide(
      pick(it, ["orderSide", "order_side", "side", "tradeSide", "trade_side", "positionSide", "position_side", "legacyOrdeDirection"]),
    );

    const qty = toNum(
      pick(it, [
        "fillSize",
        "fill_size",
        "qty",
        "quantity",
        "size",
        "vol",
        "volume",
        "amount",
        "fillQty",
        "fill_qty",
        "dealQty",
        "deal_qty",
        "tradeQty",
        "trade_qty",
        "filled_qty",
        "filledQty",
        "filledSize",
      ]),
    );

    const value = toNum(pick(it, ["fillValue", "fill_value", "value", "quoteValue", "quote_value"]));
    const priceRaw = toNum(
      pick(it, [
        "price",
        "fillPrice",
        "fill_price",
        "tradePrice",
        "trade_price",
        "dealPrice",
        "deal_price",
        "price_avg",
        "priceAvg",
        "avgPrice",
      ]),
    );
    const price =
      priceRaw != null ? priceRaw : value != null && qty != null && qty !== 0 ? Math.abs(value / qty) : undefined;
    const orderId = toStr(pick(it, ["orderId", "order_id", "orderID"]));
    const fee = toNum(pick(it, ["fillFee", "fill_fee", "fee", "fees"]));
    const ts =
      toNum(pick(it, ["ts", "timestamp", "time", "createTime", "createdTime", "created_time", "tradeTime", "fillTime", "cTime", "uTime"])) ??
      undefined;

    if (!symbol && !price && !qty) continue;

    out.push({ symbol: symbol || undefined, side, price, qty, fee, orderId, ts });
  }

  // Keep chronological order for charts (old -> new).
  out.sort((a, b) => (a.ts ?? 0) - (b.ts ?? 0));
  return out.slice(Math.max(0, out.length - 80));
}

function clamp01(x: number): number {
  if (!Number.isFinite(x)) return 0;
  return Math.max(0, Math.min(1, x));
}

function svgPathFromPoints(points: number[], w: number, h: number): string {
  const n = points.length;
  if (n < 2) return "";
  const min = Math.min(...points);
  const max = Math.max(...points);
  const span = max - min || 1;

  const toX = (i: number) => (i / (n - 1)) * w;
  const toY = (v: number) => h - ((v - min) / span) * h;

  let d = `M ${toX(0).toFixed(2)} ${toY(points[0]).toFixed(2)}`;
  for (let i = 1; i < n; i++) {
    d += ` L ${toX(i).toFixed(2)} ${toY(points[i]).toFixed(2)}`;
  }
  return d;
}

function ExposureBar(props: { longValue: number; shortValue: number }) {
  const long = Math.max(0, props.longValue);
  const short = Math.max(0, props.shortValue);
  const total = long + short;
  const longPct = total > 0 ? long / total : 0;

  return (
    <div className="vizExposure">
      <div className="vizExposureBar" role="img" aria-label="Long/Short exposure">
        <div className="vizExposureLong" style={{ width: `${clamp01(longPct) * 100}%` }} />
        <div className="vizExposureShort" style={{ width: `${clamp01(1 - longPct) * 100}%` }} />
      </div>
      <div className="vizLegendRow">
        <div className="vizLegend">
          <span className="dot long" /> Long {formatPct(total > 0 ? long / total : 0)}
        </div>
        <div className="vizLegend">
          <span className="dot short" /> Short {formatPct(total > 0 ? short / total : 0)}
        </div>
      </div>
    </div>
  );
}

function Sparkline(props: { points: number[]; colorClass: "up" | "down" | "muted" }) {
  const w = 320;
  const h = 72;
  const d = svgPathFromPoints(props.points, w, h);

  return (
    <div className="vizSparkWrap">
      <svg viewBox={`0 0 ${w} ${h}`} className={`vizSpark ${props.colorClass}`}>
        <path d={d} fill="none" stroke="currentColor" strokeWidth="2" />
      </svg>
    </div>
  );
}

function VolumeBars(props: { bars: Array<{ side: Side; value: number }> }) {
  const w = 320;
  const h = 72;
  const n = props.bars.length;
  const max = Math.max(1, ...props.bars.map((b) => Math.abs(b.value || 0)));
  const bw = n > 0 ? w / n : w;

  return (
    <div className="vizBarsWrap">
      <svg viewBox={`0 0 ${w} ${h}`} className="vizBars">
        {props.bars.map((b, i) => {
          const v = Math.abs(b.value || 0);
          const bh = (v / max) * (h - 2);
          const x = i * bw;
          const y = h - bh;
          const cls = b.side === "BUY" || b.side === "LONG" ? "buy" : b.side === "SELL" || b.side === "SHORT" ? "sell" : "muted";
          return <rect key={i} x={x + 0.5} y={y} width={Math.max(1, bw - 1)} height={bh} className={cls} rx="1.5" />;
        })}
      </svg>
    </div>
  );
}

export function PositionsViz(props: { raw: unknown; error?: string }) {
  if (props.error) {
    return (
      <div className="vizError">
        <div className="k">Positions error</div>
        <div className="v">{props.error}</div>
      </div>
    );
  }

  const positions = parsePositions(props.raw);
  const longNotional = positions
    .filter((p) => p.side === "LONG" || p.side === "BUY")
    .reduce((acc, p) => acc + Math.abs(p.notional ?? 0), 0);
  const shortNotional = positions
    .filter((p) => p.side === "SHORT" || p.side === "SELL")
    .reduce((acc, p) => acc + Math.abs(p.notional ?? 0), 0);
  const totalUPnl = positions.reduce((acc, p) => acc + (p.unrealizedPnl ?? 0), 0);
  const maxAbsPnl = Math.max(1, ...positions.map((p) => Math.abs(p.unrealizedPnl ?? 0)));

  return (
    <div className="viz">
      <div className="vizTop">
        <div className="vizMetric">
          <div className="k">仓位数</div>
          <div className="v mono">{positions.length}</div>
        </div>
        <div className="vizMetric">
          <div className="k">名义敞口（多/空）</div>
          <div className="v mono">
            多 {formatNum(longNotional, 2)} / 空 {formatNum(shortNotional, 2)}
          </div>
        </div>
        <div className="vizMetric">
          <div className="k">浮动盈亏（PnL）</div>
          <div className={`v mono ${totalUPnl >= 0 ? "pos" : "neg"}`}>{formatNum(totalUPnl, 4)}</div>
        </div>
      </div>

      <div className="muted" style={{ marginTop: 2 }}>
        口径：名义敞口优先用接口字段（notional/positionValue），缺失时用 |size|×mark 估算；PnL 为浮动盈亏（未平仓）。
      </div>

      <ExposureBar longValue={longNotional} shortValue={shortNotional} />

      {positions.length ? (
        <div className="vizRows">
          {positions.slice(0, 8).map((p, idx) => {
            const pnl = p.unrealizedPnl ?? 0;
            const frac = clamp01(Math.abs(pnl) / maxAbsPnl);
            const cls = pnl >= 0 ? "pos" : "neg";
            const sideCls = p.side === "LONG" || p.side === "BUY" ? "buy" : p.side === "SHORT" || p.side === "SELL" ? "sell" : "hold";
            return (
              <div key={`${p.symbol}-${idx}`} className="vizRow">
                <div className="vizRowLeft">
                  <div className="row" style={{ justifyContent: "space-between", gap: 10 }}>
                    <div className="mono" style={{ fontWeight: 850 }}>
                      {p.symbol}
                    </div>
                    <span className={`badge ${sideCls}`}>{p.side}</span>
                  </div>
                  <div className="muted" style={{ marginTop: 4 }}>
                    数量={formatNum(p.size, 6)} · 开仓={formatNum(p.entryPrice, 4)} · 标记={formatNum(p.markPrice, 4)} · 杠杆=
                    {formatNum(p.leverage, 2)} · 名义≈{formatNum(p.notional, 2)}
                  </div>
                </div>
                <div className="vizRowRight">
                  <div className="vizPnlBar">
                    <div className={`vizPnlFill ${cls}`} style={{ width: `${frac * 100}%` }} />
                  </div>
                  <div className={`mono ${cls}`} style={{ marginTop: 6, textAlign: "right", fontWeight: 850 }}>
                    {formatNum(pnl, 6)}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      ) : (
        <div className="muted">暂无仓位（或接口返回为空）</div>
      )}

      <details className="details" style={{ marginTop: 10 }}>
        <summary className="detailsSummary">
          <span className="muted2">Raw（点击展开）</span>
          <span className="muted" style={{ marginLeft: "auto" }}>
            展开
          </span>
        </summary>
        <pre className="pre" style={{ maxHeight: 360, marginTop: 10 }}>
          {prettyJson(props.raw).slice(0, 20000)}
        </pre>
      </details>
    </div>
  );
}

export function FillsViz(props: { raw: unknown; error?: string }) {
  if (props.error) {
    return (
      <div className="vizError">
        <div className="k">Fills error</div>
        <div className="v">{props.error}</div>
      </div>
    );
  }

  const fills = parseFills(props.raw);
  const prices = fills.map((f) => f.price).filter((x): x is number => typeof x === "number" && Number.isFinite(x));
  const last = fills.length ? fills[fills.length - 1] : null;
  const buyCount = fills.filter((f) => f.side === "BUY" || f.side === "LONG").length;
  const sellCount = fills.filter((f) => f.side === "SELL" || f.side === "SHORT").length;

  const totalCount = buyCount + sellCount;
  const lastPrice = last?.price ?? (prices.length ? prices[prices.length - 1] : undefined);
  const firstPrice = prices.length ? prices[0] : undefined;
  const trend = lastPrice != null && firstPrice != null ? lastPrice - firstPrice : 0;

  const bars = fills.map((f) => ({ side: f.side, value: f.qty ?? 0 }));

  return (
    <div className="viz">
      <div className="vizTop">
        <div className="vizMetric">
          <div className="k">成交数（Fills）</div>
          <div className="v mono">{fills.length}</div>
        </div>
        <div className="vizMetric">
          <div className="k">买 / 卖</div>
          <div className="v mono">
            {buyCount} / {sellCount}
          </div>
        </div>
        <div className="vizMetric">
          <div className="k">最新成交价</div>
          <div className={`v mono ${trend >= 0 ? "pos" : "neg"}`}>{formatNum(lastPrice, 4)}</div>
        </div>
      </div>

      <div className="muted" style={{ marginTop: 2 }}>
        说明：这里展示的是“成交明细”（fills / trade details），不是挂单列表；默认只取最近 N 条（用于快速对账/排错）。
      </div>

      {prices.length >= 2 ? (
        <div className="vizChartBlock">
          <div className="vizLegendRow">
            <div className="vizLegend">
              <span className={`dot ${trend >= 0 ? "long" : "short"}`} /> 最新成交价走势
            </div>
            <div className="muted mono" style={{ marginLeft: "auto" }}>
              {formatTime(fills[0]?.ts)} → {formatTime(fills[fills.length - 1]?.ts)}
            </div>
          </div>
          <Sparkline points={prices} colorClass={trend >= 0 ? "up" : "down"} />
        </div>
      ) : (
        <div className="muted">暂无成交价格序列</div>
      )}

      {fills.length ? (
        <div className="vizChartBlock" style={{ marginTop: 10 }}>
          <div className="vizLegendRow">
            <div className="vizLegend">
              <span className="dot long" /> 买入量（qty）
            </div>
            <div className="vizLegend">
              <span className="dot short" /> 卖出量（qty）
            </div>
            <div className="muted mono" style={{ marginLeft: "auto" }}>
              {totalCount ? `buy%=${formatPct(buyCount / totalCount, 0)}` : ""}
            </div>
          </div>
          <VolumeBars bars={bars} />
        </div>
      ) : (
        <div className="muted">暂无成交记录</div>
      )}

      {fills.length ? (
        <div className="tableWrap" style={{ marginTop: 10, maxHeight: 320 }}>
          <table className="table">
            <thead>
              <tr>
                <th>Time</th>
                <th>Symbol</th>
                <th>Side</th>
                <th className="num">Price</th>
                <th className="num">Qty</th>
                <th className="mono">OrderId</th>
              </tr>
            </thead>
            <tbody>
              {fills
                .slice(Math.max(0, fills.length - 20))
                .slice()
                .reverse()
                .map((f, i) => {
                  const sideCls = f.side === "BUY" || f.side === "LONG" ? "buy" : f.side === "SELL" || f.side === "SHORT" ? "sell" : "gray";
                  return (
                    <tr key={i}>
                      <td className="mono">{formatDateTime(f.ts)}</td>
                      <td className="mono">{f.symbol ?? "-"}</td>
                      <td>
                        <span className={`badge ${sideCls}`}>{f.side}</span>
                      </td>
                      <td className="num mono">{formatNum(f.price, 6)}</td>
                      <td className="num mono">{formatNum(f.qty, 6)}</td>
                      <td className="mono">{f.orderId ?? "-"}</td>
                    </tr>
                  );
                })}
            </tbody>
          </table>
        </div>
      ) : null}

      <details className="details" style={{ marginTop: 10 }}>
        <summary className="detailsSummary">
          <span className="muted2">Raw（点击展开）</span>
          <span className="muted" style={{ marginLeft: "auto" }}>
            展开
          </span>
        </summary>
        <pre className="pre" style={{ maxHeight: 360, marginTop: 10 }}>
          {prettyJson(props.raw).slice(0, 20000)}
        </pre>
      </details>
    </div>
  );
}


