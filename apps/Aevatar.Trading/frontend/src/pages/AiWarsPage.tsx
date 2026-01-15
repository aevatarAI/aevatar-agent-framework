import React from "react";
import { apiFetch, prettyJson } from "../api";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";

type ToolParameters = {
  required?: string[];
  items?: Record<
    string,
    {
      type?: string;
      required?: boolean;
      description?: string;
      defaultValue?: unknown;
      enum?: unknown[];
      minimum?: number;
      maximum?: number;
      minLength?: number;
      maxLength?: number;
      pattern?: string;
      format?: string;
    }
  >;
};

type AiWarsTool = {
  name: string;
  description: string;
  version: string;
  category: string;
  tags: string[];
  requiresConfirmation: boolean;
  isDangerous: boolean;
  parameters: ToolParameters;
  route: string;
  file: string;
};

type AiWarsIndexResponse = {
  count: number;
  tools: AiWarsTool[];
};

// =============================================================================
// AI Wars API 调试页（不手写 JSON）
//
// 目标：
// - 基于 tool manifest 参数，动态生成表单
// - 自动拼出 request JSON + curl（可复制）
// - 一键执行（危险/需要确认的接口默认禁用，需勾 confirm）
// =============================================================================

function normalizeBaseUrl(raw: string): string {
  const v = (raw ?? "").trim();
  return v ? v.replace(/\/+$/, "") : "";
}

function inferDefaultBaseUrl(): string {
  const env = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? "";
  if (env.trim()) return normalizeBaseUrl(env);
  // NOTE：如果你在 Vite dev server (5173) 上打开页面，走同源代理也能调用后端。
  // 但为了“可直接请求的字符串”（curl），默认给出后端常用端口。
  return "https://localhost:7100";
}

function escapeSingleQuotesForBash(s: string): string {
  // Bash: single-quote escape pattern: ' => '\'' (close, escape, reopen)
  return s.replace(/'/g, `'\\''`);
}

function buildCurl(url: string, body: unknown): string {
  const json = JSON.stringify(body ?? {}, null, 2);
  const bodyEscaped = escapeSingleQuotesForBash(json);
  const insecure = url.startsWith("https://localhost") || url.startsWith("https://127.");
  return [
    `curl -sS ${insecure ? "-k " : ""}-X POST '${url}' \\`,
    `  -H 'Content-Type: application/json' \\`,
    `  -d '${bodyEscaped}'`,
  ].join("\n");
}

type ParamDef = NonNullable<ToolParameters["items"]>[string];

function isBlank(v: unknown): boolean {
  return v === null || v === undefined || (typeof v === "string" && !v.trim());
}

function inferPrefillValue(name: string, def: ParamDef | undefined): unknown {
  const type = (def?.type ?? "string").toLowerCase();
  const n = name.toLowerCase();

  // --- 常用字段：给“可直接请求”一个可跑的默认 ---
  if (n === "symbol") return "cmt_btcusdt";
  if (n === "marginmode") return 1;
  if (n === "coinid") return 2;
  if (n === "locale") return "en-US";

  // --- 类型兜底 ---
  if (type === "boolean") return false;
  if (type === "integer" || type === "number") return 0;
  if (type === "object") return ""; // 采用 { text: ... } 包装，空串表示“未填写”
  return "";
}

function coerceValue(name: string, def: ParamDef | undefined, raw: unknown): unknown {
  const type = (def?.type ?? "string").toLowerCase();

  if (type === "boolean") {
    return !!raw;
  }

  if (type === "integer") {
    if (isBlank(raw)) return undefined;
    const n = typeof raw === "number" ? raw : Number.parseInt(String(raw), 10);
    return Number.isFinite(n) ? Math.trunc(n) : undefined;
  }

  if (type === "number") {
    if (isBlank(raw)) return undefined;
    const n = typeof raw === "number" ? raw : Number.parseFloat(String(raw));
    return Number.isFinite(n) ? n : undefined;
  }

  if (type === "object") {
    // 友好模式：用户只填一段文本，我们包装成 { text: "..." }。
    // （uploadAiLog 的 input/output 是 object，这样无需手写 JSON）
    if (isBlank(raw)) return undefined;
    return { text: String(raw) };
  }

  // 默认：string
  if (isBlank(raw)) return undefined;
  return String(raw);
}

function buildRequestBody(tool: AiWarsTool, values: Record<string, unknown>): Record<string, unknown> {
  const items = tool.parameters.items ?? {};
  const obj: Record<string, unknown> = {};

  for (const [k, v] of Object.entries(values ?? {})) {
    const coerced = coerceValue(k, items[k], v);
    if (coerced !== undefined) obj[k] = coerced;
  }

  return obj;
}

type ToolGroup = "all" | "market" | "account" | "trade" | "upload" | "other";

function getToolGroup(t: AiWarsTool): Exclude<ToolGroup, "all"> {
  const tags = (t.tags ?? []).map((x) => x.toLowerCase());
  if (tags.includes("market")) return "market";
  if (tags.includes("account")) return "account";
  if (tags.includes("trade")) return "trade";
  if (tags.includes("upload")) return "upload";

  const n = t.name.toLowerCase();
  if (n.startsWith("weex_ai_market_")) return "market";
  if (n.startsWith("weex_ai_account_")) return "account";
  if (n.startsWith("weex_ai_order_")) return "trade";
  return "other";
}

function groupLabel(g: Exclude<ToolGroup, "all">): string {
  switch (g) {
    case "market":
      return "行情";
    case "account":
      return "账户";
    case "trade":
      return "交易";
    case "upload":
      return "上传";
    default:
      return "其他";
  }
}

function groupHint(g: ToolGroup): string {
  switch (g) {
    case "market":
      return "看行情：价格 / K线 / 深度 / 成交 / 资金费率";
    case "account":
      return "看账户：余额 / 仓位 / 杠杆 / 模式设置";
    case "trade":
      return "做交易：下单 / 撤单 / 查询订单 / 止盈止损（危险操作默认隐藏）";
    case "upload":
      return "提交 AI 记录：把策略输入/输出上传给主办方";
    case "other":
      return "其他：不常用或文档归类不明确的接口";
    default:
      return "小白建议顺序：1) 账户设置 2) 余额 3) 行情 4) 订单/交易 5) 上传 AI log";
  }
}

function groupIcon(g: Exclude<ToolGroup, "all">): string {
  switch (g) {
    case "market":
      return "MKT";
    case "account":
      return "ACC";
    case "trade":
      return "TRD";
    case "upload":
      return "UPL";
    default:
      return "OTH";
  }
}

export function AiWarsPage() {
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [tools, setTools] = React.useState<AiWarsTool[] | null>(null);
  const [q, setQ] = React.useState("");

  const [requestBaseUrl, setRequestBaseUrl] = React.useState<string>(() => inferDefaultBaseUrl());
  const [group, setGroup] = React.useState<ToolGroup>("all");
  const [showDangerous, setShowDangerous] = React.useState(false);

  // per-tool UI state
  const [valuesByTool, setValuesByTool] = React.useState<Record<string, Record<string, unknown>>>({});
  const [confirmByTool, setConfirmByTool] = React.useState<Record<string, boolean>>({});
  const [outByTool, setOutByTool] = React.useState<Record<string, string>>({});
  const [runningByTool, setRunningByTool] = React.useState<Record<string, boolean>>({});

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const res = await apiFetch<AiWarsIndexResponse>("/api/ai-wars");
      setTools(res.tools ?? []);
    } catch (e: any) {
      setError(String(e?.message ?? e));
    } finally {
      setLoading(false);
    }
  }

  React.useEffect(() => {
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const groupCounts = React.useMemo(() => {
    const counts: Record<Exclude<ToolGroup, "all">, number> = {
      market: 0,
      account: 0,
      trade: 0,
      upload: 0,
      other: 0,
    };
    for (const t of tools ?? []) {
      counts[getToolGroup(t)] += 1;
    }
    return counts;
  }, [tools]);

  const filtered = (tools ?? [])
    .filter((t) => {
      const g = getToolGroup(t);
      if (group !== "all" && g !== group) return false;
      if (!showDangerous && (t.isDangerous || t.requiresConfirmation)) return false;
      return true;
    })
    .filter((t) => {
      if (!q.trim()) return true;
      const needle = q.trim().toLowerCase();
      return (
        t.name.toLowerCase().includes(needle) ||
        t.description.toLowerCase().includes(needle) ||
        (t.tags ?? []).some((x) => x.toLowerCase().includes(needle))
      );
    })
    .sort((a, b) => {
      const order: Record<Exclude<ToolGroup, "all">, number> = {
        account: 0,
        market: 1,
        trade: 2,
        upload: 3,
        other: 9,
      };
      const ga = getToolGroup(a);
      const gb = getToolGroup(b);
      if (order[ga] !== order[gb]) return order[ga] - order[gb];
      return a.name.localeCompare(b.name);
    });

  return (
    <div className="page">
      <div className="grid2">
        <Panel
          title="AI Wars API（DotNetSkills）"
          subtitle="后端会扫描 apps/Aevatar.Trading/Tools/DotNetSkills/ai-wars/** 并映射为 /api/ai-wars/{toolName}（Swagger 可见）；本页用表单生成请求参数，无需手写 JSON。"
          right={
            <div className="row" style={{ gap: 8 }}>
              <Button onClick={refresh} disabled={loading}>
                {loading ? "刷新中…" : "刷新"}
              </Button>
            </div>
          }
        >
          {error ? (
            <div className="kvItem" style={{ borderColor: "rgba(239, 68, 68, 0.35)" }}>
              <div className="k">Error</div>
              <div className="v">{error}</div>
            </div>
          ) : null}

          <div className="kvItem" style={{ marginBottom: 12 }}>
            <div className="k">请求基址（用于生成 curl）</div>
            <div className="v">
              <div className="row" style={{ gap: 8 }}>
                <input
                  className="input"
                  value={requestBaseUrl}
                  onChange={(e) => setRequestBaseUrl(e.target.value)}
                  placeholder="例如：http://localhost:7100"
                  style={{ flex: 1 }}
                />
                <Button onClick={() => setRequestBaseUrl("https://localhost:7100")}>https://localhost:7100</Button>
                <Button onClick={() => setRequestBaseUrl("http://localhost:7100")}>http://localhost:7100</Button>
              </div>
              <div className="muted" style={{ marginTop: 6 }}>
                提示：执行请求时依然走浏览器同源代理（无需 CORS）；这里只影响“可直接复制的 curl 字符串”。
              </div>
            </div>
          </div>

          <div className="row" style={{ gap: 10, marginBottom: 12 }}>
            <input
              className="input"
              placeholder="搜索 toolName / tags / description…"
              value={q}
              onChange={(e) => setQ(e.target.value)}
              style={{ flex: 1 }}
            />
            <div className="muted2" style={{ minWidth: 160, textAlign: "right" }}>
              {tools ? `共 ${tools.length} 个工具` : "加载中…"}
            </div>
          </div>

          <div className="row" style={{ gap: 8, flexWrap: "wrap", marginBottom: 10 }}>
            <button className={`chip ${group === "all" ? "on" : ""}`} onClick={() => setGroup("all")}>
              全部
            </button>
            <button className={`chip ${group === "account" ? "on" : ""}`} onClick={() => setGroup("account")}>
              账户 ({groupCounts.account})
            </button>
            <button className={`chip ${group === "market" ? "on" : ""}`} onClick={() => setGroup("market")}>
              行情 ({groupCounts.market})
            </button>
            <button className={`chip ${group === "trade" ? "on" : ""}`} onClick={() => setGroup("trade")}>
              交易 ({groupCounts.trade})
            </button>
            <button className={`chip ${group === "upload" ? "on" : ""}`} onClick={() => setGroup("upload")}>
              上传 ({groupCounts.upload})
            </button>

            <label className="row" style={{ gap: 8, marginLeft: "auto" }}>
              <input type="checkbox" checked={showDangerous} onChange={(e) => setShowDangerous(e.target.checked)} />
              <span className="muted2">显示危险操作（下单/撤单/改杠杆）</span>
            </label>
          </div>
          <div className="muted" style={{ marginBottom: 12 }}>
            {groupHint(group)}
          </div>

          {tools ? (
            <div className="toolGrid">
              {filtered.map((t) => {
                const confirm = !!confirmByTool[t.name];
                const running = !!runningByTool[t.name];
                const out = outByTool[t.name] ?? "";

                const requiresConfirm = t.isDangerous || t.requiresConfirmation;
                const required = t.parameters.required ?? [];
                const items = t.parameters.items ?? {};
                const toolGroup = getToolGroup(t);

                const toolValues = valuesByTool[t.name] ?? {};
                const bodyObj = buildRequestBody(t, toolValues);

                const missing = required.filter((k) => {
                  const def = items[k];
                  const type = (def?.type ?? "string").toLowerCase();
                  const v = toolValues[k];
                  if (type === "boolean") return v === undefined;
                  return isBlank(v);
                });

                const base = normalizeBaseUrl(requestBaseUrl) || inferDefaultBaseUrl();
                // Always include confirm in query so requests are always "constructible".
                // - For non-dangerous tools it is ignored.
                // - For dangerous tools, backend still requires confirm=true.
                const qs = `?confirm=${confirm ? "true" : "false"}`;
                const curlUrl = `${base}${t.route}${qs}`;
                const curl = buildCurl(curlUrl, bodyObj);

                return (
                  <details key={t.name} className="details toolCard">
                    <summary className="detailsSummary toolSummary">
                      <span className="badge gray">{groupIcon(toolGroup)}</span>
                      <span style={{ fontWeight: 850 }}>{t.name}</span>
                      <span className="muted2" style={{ marginLeft: 8, fontWeight: 750 }}>
                        {groupLabel(toolGroup)}
                      </span>
                      {requiresConfirm ? <span className="badge danger">危险</span> : <span className="badge ok">安全</span>}
                      <span className="muted2" style={{ marginLeft: 8, fontWeight: 650, overflow: "hidden", textOverflow: "ellipsis" }}>
                        {t.description}
                      </span>
                      <span className="muted" style={{ marginLeft: "auto" }}>
                        {t.route}
                      </span>
                    </summary>

                    <div className="kv" style={{ marginTop: 10, gap: 10 }}>
                      <div className="kvItem">
                        <div className="k">File</div>
                        <div className="v">
                          <code>{t.file}</code>
                        </div>
                      </div>
                      <div className="kvItem">
                        <div className="k">Tags</div>
                        <div className="v">
                          {(t.tags ?? []).length ? (
                            <div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
                              {t.tags.map((x) => (
                                <span key={x} className="tag">
                                  {x}
                                </span>
                              ))}
                            </div>
                          ) : (
                            <span className="muted">—</span>
                          )}
                        </div>
                      </div>
                      <div className="kvItem">
                        <div className="k">Required</div>
                        <div className="v">{required.length ? required.join(", ") : <span className="muted">无</span>}</div>
                      </div>

                      <div className="kvItem">
                        <div className="k">参数表单（自动生成请求）</div>
                        <div className="v" style={{ width: "100%" }}>
                          <div className="row" style={{ gap: 8, marginBottom: 10, flexWrap: "wrap" }}>
                            <Button
                              onClick={() => {
                                const next: Record<string, unknown> = {};
                                const keys = Array.from(new Set([...(t.parameters.required ?? []), ...Object.keys(items)]));
                                for (const k of keys) {
                                  next[k] = inferPrefillValue(k, items[k]);
                                }
                                setValuesByTool((m) => ({ ...m, [t.name]: next }));
                              }}
                            >
                              生成示例参数
                            </Button>
                            <Button onClick={() => setValuesByTool((m) => ({ ...m, [t.name]: {} }))}>清空</Button>

                            {requiresConfirm ? (
                              <label className="row" style={{ gap: 8 }}>
                                <input
                                  type="checkbox"
                                  checked={confirm}
                                  onChange={(e) =>
                                    setConfirmByTool((m) => ({ ...m, [t.name]: e.target.checked }))
                                  }
                                />
                                <span className="muted2">confirm=true（危险/需要确认）</span>
                              </label>
                            ) : (
                              <span className="muted2">无需 confirm</span>
                            )}

                            <Button
                              variant="primary"
                              disabled={running || (requiresConfirm && !confirm) || missing.length > 0}
                              onClick={async () => {
                                setRunningByTool((m) => ({ ...m, [t.name]: true }));
                                try {
                                  const res = await apiFetch<unknown>(`${t.route}${qs}`, {
                                    method: "POST",
                                    body: JSON.stringify(bodyObj),
                                  });
                                  setOutByTool((m) => ({ ...m, [t.name]: prettyJson(res) }));
                                } catch (e: any) {
                                  setOutByTool((m) => ({ ...m, [t.name]: `ERROR: ${String(e?.message ?? e)}` }));
                                } finally {
                                  setRunningByTool((m) => ({ ...m, [t.name]: false }));
                                }
                              }}
                            >
                              {running ? "执行中…" : "执行"}
                            </Button>
                          </div>

                          {missing.length ? (
                            <div className="kvItem" style={{ borderColor: "rgba(239, 68, 68, 0.35)", marginBottom: 10 }}>
                              <div className="k">缺少必填参数</div>
                              <div className="v">{missing.join(", ")}</div>
                            </div>
                          ) : null}

                          <div className="form" style={{ gridTemplateColumns: "1fr", gap: 10 }}>
                            {Array.from(new Set([...(t.parameters.required ?? []), ...Object.keys(items)]))
                              .filter(Boolean)
                              .map((key) => {
                                const def = items[key];
                                const type = (def?.type ?? "string").toLowerCase();
                                const isReq = required.includes(key) || !!def?.required;
                                const val = toolValues[key];

                                return (
                                  <div key={key} className="field">
                                    <label>
                                      <span style={{ fontWeight: 750 }}>{key}</span>
                                      <span className="muted2" style={{ marginLeft: 8 }}>
                                        {type}
                                      </span>
                                      {isReq ? (
                                        <span className="muted2" style={{ marginLeft: 8, color: "rgba(220, 38, 38, 0.9)" }}>
                                          *required
                                        </span>
                                      ) : null}
                                    </label>

                                    {type === "boolean" ? (
                                      <label className="row" style={{ gap: 8 }}>
                                        <input
                                          type="checkbox"
                                          checked={!!val}
                                          onChange={(e) =>
                                            setValuesByTool((m) => ({
                                              ...m,
                                              [t.name]: { ...(m[t.name] ?? {}), [key]: e.target.checked },
                                            }))
                                          }
                                        />
                                        <span className="muted2">{!!val ? "true" : "false"}</span>
                                      </label>
                                    ) : type === "object" ? (
                                      <textarea
                                        className="textarea"
                                        rows={4}
                                        value={typeof val === "string" ? val : val ? String(val) : ""}
                                        onChange={(e) =>
                                          setValuesByTool((m) => ({
                                            ...m,
                                            [t.name]: { ...(m[t.name] ?? {}), [key]: e.target.value },
                                          }))
                                        }
                                        placeholder="直接写文本即可（会自动包装成 { text: ... }）"
                                      />
                                    ) : (
                                      <input
                                        className="input"
                                        type={type === "integer" || type === "number" ? "number" : "text"}
                                        value={val === undefined || val === null ? "" : String(val)}
                                        onChange={(e) =>
                                          setValuesByTool((m) => ({
                                            ...m,
                                            [t.name]: { ...(m[t.name] ?? {}), [key]: e.target.value },
                                          }))
                                        }
                                        placeholder={def?.description ? def.description.split("\n")[0] : ""}
                                      />
                                    )}

                                    {def?.description ? <div className="muted">{def.description}</div> : null}
                                  </div>
                                );
                              })}
                          </div>
                        </div>
                      </div>

                      <div className="kvItem">
                        <div className="k">请求 JSON（可复制）</div>
                        <div className="v" style={{ width: "100%" }}>
                          <div className="row" style={{ gap: 8, marginBottom: 8 }}>
                            <Button
                              onClick={async () => {
                                try {
                                  await navigator.clipboard.writeText(prettyJson(bodyObj));
                                } catch {
                                  // ignore
                                }
                              }}
                            >
                              复制 JSON
                            </Button>
                          </div>
                          <pre className="pre">{prettyJson(bodyObj)}</pre>
                        </div>
                      </div>

                      <div className="kvItem">
                        <div className="k">curl（可直接请求的字符串）</div>
                        <div className="v" style={{ width: "100%" }}>
                          <div className="row" style={{ gap: 8, marginBottom: 8 }}>
                            <Button
                              onClick={async () => {
                                try {
                                  await navigator.clipboard.writeText(curl);
                                } catch {
                                  // ignore
                                }
                              }}
                            >
                              复制 curl
                            </Button>
                          </div>
                          <pre className="pre">{curl}</pre>
                        </div>
                      </div>

                      <div className="kvItem">
                        <div className="k">Output</div>
                        <div className="v" style={{ width: "100%" }}>
                          <pre className="pre">{out || "—"}</pre>
                        </div>
                      </div>
                    </div>
                  </details>
                );
              })}
            </div>
          ) : (
            <div className="muted">正在加载 /api/ai-wars…</div>
          )}
        </Panel>
      </div>
    </div>
  );
}


