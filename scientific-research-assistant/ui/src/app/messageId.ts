export function parseAgUiMessageId(messageId: string): { sessionId: string; agent: string; runId: string; rest: string } {
  const parts = String(messageId || "").split(":");
  // Expected:
  // - msg:{sessionId}:{agent}:{runId}(:rest...)
  // - NOTE: backend runId is "{sessionId}:{seq}" so messageId becomes:
  //         msg:{sessionId}:{agent}:{sessionId}:{seq}
  // - sys:... (ignored here)
  if (parts.length >= 4 && parts[0] === "msg") {
    const sessionId = parts[1] || "";
    const agent = parts[2] || "assistant";

    // Default (no ":" inside runId)
    let runId = parts[3] || "";
    let rest = parts.slice(4).join(":");

    // If runId is in "{sessionId}:{seq}" form, restore the full runId and keep remaining as rest.
    if (parts.length >= 5 && parts[3] === sessionId && /^\d+$/.test(parts[4] || "")) {
      runId = `${parts[3]}:${parts[4]}`;
      rest = parts.slice(5).join(":");
    }

    return { sessionId, agent, runId, rest };
  }
  return { sessionId: "", agent: "system", runId: "", rest: "" };
}

export function agentDisplayName(agent: string): string {
  const a = String(agent || "").trim();
  if (!a) return "unknown";
  if (a === "assistant") return "research_assistant";
  return a;
}


