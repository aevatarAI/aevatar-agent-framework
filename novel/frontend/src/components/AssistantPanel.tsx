import React, { useMemo } from "react";
import { timestampDate } from "@bufbuild/protobuf/wkt";
import type { SidecarEvent } from "@/gen/novel_sidecar_pb";
import { fileUriToPath } from "@/lib/uri";

type Props = {
  events: SidecarEvent[];
  lastError: string | null;
  onOpenPath: (fullPath: string) => void;
};

function fmtTime(ts: any): string {
  try {
    const d = timestampDate(ts);
    return d.toLocaleTimeString();
  } catch {
    return "";
  }
}

export default function AssistantPanel(props: Props) {
  const latestUnitTests = useMemo(() => {
    const out: any[] = [];
    for (let i = props.events.length - 1; i >= 0; i--) {
      const e: any = props.events[i] as any;
      if (e?.payload?.case === "unitTestsCompleted") {
        out.push(e.payload.value);
      }
      if (out.length >= 5) break;
    }
    return out;
  }, [props.events]);

  const recentEvents = useMemo(() => props.events.slice(-50).reverse(), [props.events]);

  return (
    <div className="Panel">
      <div className="PanelHeader">
        <div className="PanelHeaderTitle">Agent</div>
        <span style={{ color: "var(--muted)", fontSize: 12 }}>SSE + Protobuf JSON</span>
      </div>

      <div style={{ padding: 10, display: "flex", flexDirection: "column", gap: 14, height: "100%", overflow: "auto" }}>
        {props.lastError ? (
          <div style={{ border: "1px solid rgba(239,68,68,0.25)", background: "rgba(239,68,68,0.08)", borderRadius: 12, padding: 10 }}>
            <div style={{ color: "var(--text)", fontWeight: 600, marginBottom: 6 }}>连接/解码错误</div>
            <div style={{ color: "var(--muted)", fontSize: 12, lineHeight: 1.5 }}>{props.lastError}</div>
          </div>
        ) : null}

        <section>
          <div style={{ color: "var(--muted)", fontSize: 12, textTransform: "uppercase", letterSpacing: 0.8, marginBottom: 8 }}>
            Narrative Tests（最新 5 次）
          </div>
          {latestUnitTests.length === 0 ? (
            <div style={{ color: "var(--muted)", fontSize: 13 }}>暂无测试结果事件</div>
          ) : (
            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              {latestUnitTests.map((t: any) => {
                const summary = t?.summary;
                const reportUri = t?.testReport?.uri ?? t?.test_report?.uri;
                const reportPath = typeof reportUri === "string" ? fileUriToPath(reportUri) : null;

                const status = String(summary?.status ?? "").toUpperCase();
                const failedCount = Array.isArray(summary?.failures) ? summary.failures.length : 0;
                const ok = status.includes("PASSED") || failedCount === 0;

                return (
                  <div
                    key={`${t?.eventId ?? t?.event_id ?? ""}_${t?.timestamp ?? ""}`}
                    style={{
                      border: "1px solid var(--border)",
                      background: "rgba(255,255,255,0.02)",
                      borderRadius: 12,
                      padding: 10,
                    }}
                  >
                    <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 10 }}>
                      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                        <span className={`Dot ${ok ? "DotOk" : "DotBad"}`} />
                        <span style={{ fontWeight: 600, color: "var(--text)", fontSize: 13 }}>
                          {ok ? "PASSED" : "FAILED"}{" "}
                          <span style={{ fontWeight: 400, color: "var(--muted)" }}>
                            {failedCount ? `(${failedCount})` : ""}
                          </span>
                        </span>
                      </div>
                      <div style={{ color: "var(--muted)", fontSize: 12 }}>{fmtTime(t?.timestamp)}</div>
                    </div>
                    {reportPath ? (
                      <div style={{ marginTop: 8 }}>
                        <button className="Btn" onClick={() => props.onOpenPath(reportPath)}>
                          打开测试报告
                        </button>
                        <div style={{ marginTop: 6, color: "var(--muted)", fontSize: 12, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                          {reportPath}
                        </div>
                      </div>
                    ) : null}
                  </div>
                );
              })}
            </div>
          )}
        </section>

        <section>
          <div style={{ color: "var(--muted)", fontSize: 12, textTransform: "uppercase", letterSpacing: 0.8, marginBottom: 8 }}>
            Events（最近 50 条）
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            {recentEvents.length === 0 ? (
              <div style={{ color: "var(--muted)", fontSize: 13 }}>等待 sidecar 推送事件…</div>
            ) : (
              recentEvents.map((e: any, idx) => {
                const c = e?.payload?.case ?? "unknown";
                const v = e?.payload?.value;
                const title =
                  c === "fileChanged"
                    ? `FILE ${v?.kind ?? ""}: ${v?.relativePath ?? v?.relative_path ?? v?.fullPath ?? ""}`
                    : c === "projectRootChanged"
                      ? `ROOT: ${v?.info?.projectRoot ?? v?.info?.project_root ?? ""}`
                      : c === "writingSessionLogUpdated"
                        ? `SESSION LOG UPDATED`
                        : c === "unitTestsCompleted"
                          ? `UNIT TESTS COMPLETED`
                          : c;
                return (
                  <div
                    key={`${e?.eventId ?? ""}_${idx}`}
                    style={{
                      border: "1px solid rgba(255,255,255,0.06)",
                      borderRadius: 10,
                      padding: "8px 10px",
                      background: "rgba(0,0,0,0.18)",
                    }}
                  >
                    <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 10 }}>
                      <div style={{ color: "var(--text)", fontSize: 13, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                        {title}
                      </div>
                      <div style={{ color: "var(--muted)", fontSize: 12 }}>{fmtTime(e?.timestamp)}</div>
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </section>
      </div>
    </div>
  );
}


