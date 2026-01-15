import React, { useEffect, useMemo, useRef, useState } from "react";
import clsx from "clsx";
import type { ChatMessage } from "@/lib/types";
import type { SidecarConnectionState } from "@/lib/sidecar";
import MarkdownView from "@/components/MarkdownView";

type Props = {
  sidecarUrl: string;
  sidecarState: SidecarConnectionState;
  lastError: string | null;
  messages: ChatMessage[];
  onSend: (text: string) => void;
  onNewChat: () => void;
  onSmartContinue: (instruction?: string) => void;
  activeFilePath: string | null;
  busy?: boolean;
};

function fmtTime(ts: number): string {
  try {
    return new Date(ts).toLocaleTimeString();
  } catch {
    return "";
  }
}

function roleLabel(role: ChatMessage["role"]): string {
  if (role === "user") return "你";
  if (role === "assistant") return "智能体";
  return "系统";
}

export default function ChatPanel(props: Props) {
  const [draft, setDraft] = useState("");
  const scrollerRef = useRef<HTMLDivElement | null>(null);

  const canSend = useMemo(() => draft.trim().length > 0, [draft]);
  const busy = Boolean(props.busy);

  useEffect(() => {
    // Best-effort: auto-scroll to bottom when new messages arrive.
    const el = scrollerRef.current;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
  }, [props.messages.length]);

  const send = () => {
    const text = draft.trim();
    if (!text) return;
    setDraft("");
    props.onSend(text);
  };

  return (
    <div className="Panel">
      <div className="PanelHeader">
        <div className="PanelHeaderTitle">智能体</div>
        <div className="ChatHeaderMeta" title={props.sidecarUrl}>
          <span className={clsx("Dot", props.sidecarState === "connected" && "DotOk", props.sidecarState === "error" && "DotBad")} />
          <span className="ChatHeaderMetaText">
            {props.sidecarState === "connected"
              ? "已连接"
              : props.sidecarState === "connecting"
                ? "连接中…"
                : props.sidecarState === "error"
                  ? "连接失败"
                  : "未连接"}
          </span>
        </div>
      </div>

      <div className="ChatBody">
        <div className="ChatContext">
          <div className="ChatContextLabel">目标文件</div>
          <div className="ChatContextValue" title={props.activeFilePath || ""}>
            {props.activeFilePath ? props.activeFilePath : "未选择文件（先从左侧打开正文/设定/规则）"}
          </div>
        </div>

        {props.lastError ? (
          <div className="Alert AlertDanger">
            <div className="AlertTitle">连接/解码错误</div>
            <div className="AlertText">{props.lastError}</div>
          </div>
        ) : null}

        <div className="ChatScroll" ref={scrollerRef}>
          {props.messages.length === 0 ? (
            <div className="EmptyHint">先从左侧打开文件，然后在这里下指令（v1：聊天能力待接入）。</div>
          ) : (
            props.messages.map((m) => (
              <div key={m.id} className={clsx("ChatMsg", m.role === "user" && "ChatMsgUser")}>
                <div className="ChatMsgMeta">
                  <span className="ChatMsgRole">{roleLabel(m.role)}</span>
                  <span className="ChatMsgTime">{fmtTime(m.ts)}</span>
                </div>
                <div
                  className={clsx(
                    "ChatMsgBubble",
                    m.role === "assistant" && "ChatMsgBubbleAssistant",
                    m.role === "system" && "ChatMsgBubbleSystem",
                    m.role === "user" && "ChatMsgBubbleUser",
                  )}
                >
                  {m.role === "user" ? <div className="ChatPlain">{m.text}</div> : <MarkdownView text={m.text} />}
                </div>
              </div>
            ))
          )}
        </div>

        <div className="ChatComposer">
          <button className="Btn" onClick={props.onNewChat} title="清空聊天（本地）">
            新会话
          </button>
          <div className="ChatComposerInputWrap">
            <textarea
              className="Textarea"
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              placeholder="输入你的需求（Enter 续写，Shift+Enter 换行）"
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  send();
                }
              }}
            />
          </div>
          <button className="Btn" onClick={() => props.onSmartContinue("")} disabled={busy || !props.activeFilePath} title="不带指令，直接续写一段">
            智能续写
          </button>
          <button
            className={clsx("Btn", "BtnPrimary")}
            onClick={send}
            disabled={busy || !props.activeFilePath || !canSend}
            title="带指令续写（把输入当作作者指令）"
          >
            {busy ? "生成中…" : "续写"}
          </button>
        </div>
      </div>
    </div>
  );
}


