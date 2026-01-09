export function parseAgUiMessageId(messageId: string): { sessionId: string; agent: string; runId: string; rest: string } {
  const parts = String(messageId || "").split(":");
  // Expected: msg:{sessionId}:{agent}:{runId}(:rest...) | sys:...
  if (parts.length >= 4 && parts[0] === "msg") {
    return {
      sessionId: parts[1] || "",
      agent: parts[2] || "assistant",
      runId: parts[3] || "",
      rest: parts.slice(4).join(":"),
    };
  }
  return { sessionId: "", agent: "system", runId: "", rest: "" };
}

export function agentDisplayName(agent: string): string {
  const a = String(agent || "").trim();
  if (!a) return "unknown";
  if (a === "assistant") return "research_assistant";
  return a;
}


