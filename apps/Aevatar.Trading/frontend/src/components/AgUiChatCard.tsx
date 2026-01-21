import React from "react";

type AgUiMessage = {
  id: string;
  role: string;
  content: string;
  name?: string;
};

type AgUiChatCardProps = {
  messages: AgUiMessage[];
  streamingMessageId: string | null;
  fallbackText: string;
  fallbackLive: boolean;
  connected: boolean;
};

export function AgUiChatCard({
  messages,
  streamingMessageId,
  fallbackText,
  fallbackLive,
  connected,
}: AgUiChatCardProps) {
  const { latestAssistant, isStreamingChat } = React.useMemo(() => {
    const reversed = [...messages].reverse();
    const latestChat =
      reversed.find((m) => m.role === "assistant" && (m.name === "market_chat" || m.id.includes(":chat:"))) ??
      null;
    const fallback =
      latestChat ?? reversed.find((m) => m.role === "assistant") ?? null;
    const streamingIsChat = streamingMessageId
      ? streamingMessageId.includes(":chat:")
      : false;
    return { latestAssistant: fallback, isStreamingChat: streamingIsChat };
  }, [messages, streamingMessageId]);

  const displayText = (latestAssistant?.content ?? fallbackText).trim() || "等待 AI 输出...";
  const isLive =
    (streamingMessageId !== null && isStreamingChat) || (!latestAssistant && fallbackLive);
  const status = isLive ? "LIVE" : latestAssistant ? "DONE" : "WAIT";

  return (
    <div className="chatStreamCard">
      <div className="chatStreamHeader">
        <div className="chatStreamTitle">
          <span className={`chatStreamDot ${status === "LIVE" ? "live" : status === "DONE" ? "done" : "wait"}`} />
          AI Chat (AGUI)
        </div>
        <div className="row">
          <span className={`chatStreamTag ${status === "LIVE" ? "live" : status === "DONE" ? "done" : "wait"}`}>
            {status}
          </span>
          <span className={`badge ${connected ? "ok" : "gray"}`}>
            {connected ? "AGUI" : "AGUI OFF"}
          </span>
        </div>
      </div>
      <div className="chatStreamBody">
        <div className="chatBubble">
          <div className="chatBubbleMeta">
            <span className="chatRole">AI</span>
            <span className="muted">{latestAssistant ? "最新 AGUI 输出" : "等待 AGUI 推送"}</span>
          </div>
          <pre className="chatBubbleText">
            {displayText}
            {isLive ? <span className="decisionCursor">▌</span> : null}
          </pre>
        </div>
      </div>
    </div>
  );
}

