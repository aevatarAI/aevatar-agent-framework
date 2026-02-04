using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal sealed class ToolPolicyRuntime
{
    private readonly IToolPolicyHost _host;

    internal ToolPolicyRuntime(IToolPolicyHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    internal bool IsToolAllowedByPolicy(ToolDefinition tool)
    {
        if (!_host.AllowInternalTools && tool.RequiresInternalAccess)
            return false;

        if (!_host.AllowDangerousTools && (tool.IsDangerous || tool.RequiresConfirmation))
            return false;

        return true;
    }

    internal string BuildToolPolicyDenyReason(ToolDefinition tool)
    {
        if (!_host.AllowInternalTools && tool.RequiresInternalAccess)
            return "RequiresInternalAccess is disabled (AllowInternalTools=false).";

        if (!_host.AllowDangerousTools && (tool.IsDangerous || tool.RequiresConfirmation))
            return "Dangerous/confirmation tools are disabled (AllowDangerousTools=false).";

        return "Denied by policy.";
    }

    internal AIGAgentBase.YamlToolPolicy BuildYamlToolPolicy(AgentYamlConfig yaml)
    {
        var skillToolNames = _host.GetYamlSkillToolNames();
        var allowlist = BuildToolAllowlistFromYaml(yaml, skillToolNames);
        var dangerousToolNames = _host.GetYamlDangerousToolNames();
        var enableDangerous = _host.ShouldEnableDangerousToolsFromYaml(yaml, allowlist, dangerousToolNames);

        return new AIGAgentBase.YamlToolPolicy(allowlist, dangerousToolNames, enableDangerous);
    }

    internal void LogYamlToolPolicyDecision(AgentYamlConfig yaml, AIGAgentBase.YamlToolPolicy policy)
    {
        var allowlistCount = policy.Allowlist?.Count ?? 0;
        var allowlistHash = BuildAllowlistFingerprint(policy.Allowlist);
        var allowlistSample = BuildAllowlistSample(policy.Allowlist, maxItems: 6);

        _host.Logger.LogDebug(
            "YAML tool policy decision: allowlist_count={AllowCount} allowlist_hash={AllowHash} allowlist_sample={AllowSample} dangerous_names_count={DangerCount} enable_dangerous={EnableDangerous} skills_count={SkillsCount} tools_count={ToolsCount}",
            allowlistCount,
            allowlistHash,
            allowlistSample,
            policy.DangerousToolNames?.Count ?? 0,
            policy.EnableDangerousTools,
            yaml?.Skills?.Count ?? 0,
            yaml?.Tools?.Count ?? 0);
    }

    internal IReadOnlySet<string> BuildToolAllowlistFromYaml(
        AgentYamlConfig yaml,
        IReadOnlyCollection<string> skillToolNames)
    {
        var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (yaml == null)
            return allow;

        if (yaml.Tools is { Count: > 0 })
        {
            foreach (var t in yaml.Tools)
            {
                var name = (t ?? string.Empty).Trim();
                if (name.Length > 0)
                    allow.Add(name);
            }
        }

        var autoSkillTools = _host.GetSkillToolsAutoIncludedFromYaml(yaml, skillToolNames);
        if (autoSkillTools is { Count: > 0 })
        {
            foreach (var t in autoSkillTools)
                allow.Add(t);
        }

        return allow;
    }

    private static string BuildAllowlistFingerprint(IReadOnlySet<string>? allowlist)
    {
        if (allowlist == null || allowlist.Count == 0)
            return "empty";

        var ordered = allowlist.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        var joined = string.Join("|", ordered);
        var bytes = Encoding.UTF8.GetBytes(joined);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildAllowlistSample(IReadOnlySet<string>? allowlist, int maxItems)
    {
        if (allowlist == null || allowlist.Count == 0)
            return "[]";

        var items = allowlist
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxItems, 1, 16))
            .ToArray();

        return "[" + string.Join(", ", items) + "]";
    }

    internal void TryApplyToolAllowlistFromSkillsLoadResult(
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
            _host.Logger.LogDebug(ex, "Failed to parse skills_load result for tool allowlist (best-effort).");
        }
    }
}

