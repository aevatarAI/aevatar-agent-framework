using Aevatar.Agents.AI.Abstractions;

namespace Aevatar.Agents.AI.Core.Utils;

/// <summary>
/// Centralized keys and helpers used by AIGAgentBase across different contexts.
///
/// WHY:
/// - Avoid stringly-typed key drift (same key hard-coded in multiple files).
/// - Concentrate multi-type parsing logic (e.g. tool allowlist supports HashSet/array/list/string).
/// </summary>
public static class AIGAgentKeys
{
    // ============================================================
    //  State.Context keys (MapField<string,string>)
    // ============================================================

    public const string HistorySummary = "history_summary";

    // ============================================================
    //  LLMRequest.Context keys (Dictionary<string, object>)
    // ============================================================

    public const string ToolAllowlist = "aevatar.allowed_tools";
    public const string ToolAllowlistSourceSkill = "aevatar.allowed_tools.source_skill";
    // Also accepted from ChatRequest.Context (string -> string).
    public const string SuppressExecutionTrace = "aevatar.trace.suppress";

    // ============================================================
    //  Hook metadata keys (Dictionary<string, object>)
    // ============================================================

    public const string HookDenyTool = "deny_tool";
    public const string HookDenyReason = "deny_reason";

    // ============================================================
    //  Tool allowlist helpers
    // ============================================================

    public static bool TryGetToolAllowlist(AevatarLLMRequest llmRequest, out HashSet<string> allowlist)
    {
        allowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (llmRequest.Context == null)
            return false;

        if (!llmRequest.Context.TryGetValue(ToolAllowlist, out var v) || v == null)
            return false;

        switch (v)
        {
            case HashSet<string> s:
                allowlist = s;
                return true;
            case string[] arr:
                allowlist = new HashSet<string>(
                    arr.Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
                return true;
            case List<string> list:
                allowlist = new HashSet<string>(
                    list.Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
                return true;
            case string single when !string.IsNullOrWhiteSpace(single):
                allowlist = new HashSet<string>([single.Trim()], StringComparer.OrdinalIgnoreCase);
                return true;
            default:
                return false;
        }
    }

    public static void ClearToolAllowlist(AevatarLLMRequest llmRequest)
    {
        llmRequest.Context?.Remove(ToolAllowlist);
        llmRequest.Context?.Remove(ToolAllowlistSourceSkill);
    }
}