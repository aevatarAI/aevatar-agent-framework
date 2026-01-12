using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    private async Task<ToolExecutionResult> ExecuteAllowedToolAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
    {
        // Enforce policy again at execution time (defense in depth).
        var toolDef =
            _registeredToolsCache.FirstOrDefault(t =>
                string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (toolDef != null && !IsToolAllowedByPolicy(toolDef))
        {
            var content = JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tool execution denied by agent policy.",
                tool = toolName,
                deniedReason = BuildToolPolicyDenyReason(toolDef)
            });

            return new ToolExecutionResult
            {
                ToolName = toolName,
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolName}' is denied by agent policy.",
                Content = content,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }

        if (AIGAgentKeys.TryGetToolAllowlist(llmRequest, out var allowlist) &&
            allowlist.Count > 0 &&
            !allowlist.Contains(toolName))
        {
            // Deny execution (return a tool result the model can read)
            var content = JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tool is not allowed by current allowlist.",
                tool = toolName,
                allowedTools = allowlist.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
            });

            return new ToolExecutionResult
            {
                ToolName = toolName,
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolName}' is not allowed by current allowlist.",
                Content = content,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }

        return await ExecuteToolAsync(toolName, args, executionContext, cancellationToken);
    }

    private bool IsToolAllowedByPolicy(ToolDefinition tool)
    {
        if (!AllowInternalTools && tool.RequiresInternalAccess)
            return false;

        if (!AllowDangerousTools && (tool.IsDangerous || tool.RequiresConfirmation))
            return false;

        return true;
    }

    private string BuildToolPolicyDenyReason(ToolDefinition tool)
    {
        if (!AllowInternalTools && tool.RequiresInternalAccess)
            return "RequiresInternalAccess is disabled (AllowInternalTools=false).";

        if (!AllowDangerousTools && (tool.IsDangerous || tool.RequiresConfirmation))
            return "Dangerous/confirmation tools are disabled (AllowDangerousTools=false).";

        return "Denied by policy.";
    }

    private void TryApplyToolAllowlistFromSkillsLoadResult(
        AevatarLLMRequest llmRequest,
        string toolName,
        string? toolResultJson)
    {
        if (!string.Equals(toolName, "skills_load", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(toolResultJson))
        {
            // No payload -> clear allowlist (best-effort).
            AIGAgentKeys.ClearToolAllowlist(llmRequest);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(toolResultJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                // failed load -> do not change allowlist
                return;
            }

            if (!root.TryGetProperty("allowedTools", out var allowedEl) || allowedEl.ValueKind != JsonValueKind.Array)
            {
                // No allowlist -> clear
                AIGAgentKeys.ClearToolAllowlist(llmRequest);
                return;
            }

            var list = new List<string>();
            foreach (var el in allowedEl.EnumerateArray())
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    list.Add(s.Trim());
            }

            if (list.Count == 0)
            {
                AIGAgentKeys.ClearToolAllowlist(llmRequest);
                return;
            }

            llmRequest.Context ??= new Dictionary<string, object>();
            llmRequest.Context[AIGAgentKeys.ToolAllowlist] = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                var skillName = nameEl.GetString();
                if (!string.IsNullOrWhiteSpace(skillName))
                {
                    llmRequest.Context[AIGAgentKeys.ToolAllowlistSourceSkill] = skillName!;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to parse skills_load result for tool allowlist (best-effort).");
        }
    }
}