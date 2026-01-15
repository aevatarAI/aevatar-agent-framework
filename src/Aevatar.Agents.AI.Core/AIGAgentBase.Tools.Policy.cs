using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    protected internal sealed record YamlToolPolicy(
        IReadOnlySet<string> Allowlist,
        IReadOnlySet<string> DangerousToolNames,
        bool EnableDangerousTools);

    private static readonly IReadOnlySet<string> DefaultYamlDangerousToolNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "python_exec",
            "skills_run_python"
        };

    private static readonly IReadOnlyCollection<string> DefaultYamlSkillToolNames =
    [
        "skills_list",
        "skills_load",
        "find_helpful_skills",
        "find_helpful_alls",
        "list_skills",
        "read_skill_document",
        "skills_files",
        "skills_read_file",
        "skills_run_python"
    ];

    /// <summary>
    /// Skill tool names used for YAML allowlist building.
    /// Override to customize or reduce the default skills tool surface.
    /// </summary>
    protected virtual IReadOnlyCollection<string> GetYamlSkillToolNames()
        => DefaultYamlSkillToolNames;

    internal IReadOnlyCollection<string> InternalGetYamlSkillToolNames()
        => GetYamlSkillToolNames();

    /// <summary>
    /// Policy hook: default skill roots used when YAML enables skills.
    /// Default: ~/.aevatar/skills (via AgentYamlConfigLoader).
    /// </summary>
    protected virtual IReadOnlyCollection<string> GetYamlDefaultSkillRoots()
        => new[] { AgentYamlConfigLoader.GetSkillsDirectory() };

    internal IReadOnlyCollection<string> InternalGetYamlDefaultSkillRoots()
        => GetYamlDefaultSkillRoots();

    /// <summary>
    /// Policy hook: dangerous tool names used by YAML to decide AllowDangerousTools.
    /// Default: python_exec + skills_run_python (case-insensitive).
    /// </summary>
    protected virtual IReadOnlySet<string> GetYamlDangerousToolNames()
        => DefaultYamlDangerousToolNames;

    internal IReadOnlySet<string> InternalGetYamlDangerousToolNames()
        => GetYamlDangerousToolNames();

    /// <summary>
    /// Policy hook: build the final YAML tool policy (allowlist + dangerous enablement).
    /// Default behavior matches current semantics.
    /// </summary>
    protected virtual YamlToolPolicy BuildYamlToolPolicy(AgentYamlConfig yaml)
    {
        var skillToolNames = GetYamlSkillToolNames();
        var allowlist = BuildToolAllowlistFromYaml(yaml, skillToolNames);
        var dangerousToolNames = GetYamlDangerousToolNames();
        var enableDangerous = ShouldEnableDangerousToolsFromYaml(yaml, allowlist, dangerousToolNames);

        return new YamlToolPolicy(allowlist, dangerousToolNames, enableDangerous);
    }

    internal YamlToolPolicy InternalBuildYamlToolPolicy(AgentYamlConfig yaml)
        => BuildYamlToolPolicy(yaml);

    /// <summary>
    /// Policy hook: emit a structured audit log for YAML tool policy decisions.
    /// Default: Debug-level, best-effort (no behavior change).
    /// </summary>
    protected virtual void LogYamlToolPolicyDecision(AgentYamlConfig yaml, YamlToolPolicy policy)
    {
        var allowlistCount = policy.Allowlist?.Count ?? 0;
        var allowlistHash = BuildAllowlistFingerprint(policy.Allowlist);
        var allowlistSample = BuildAllowlistSample(policy.Allowlist, maxItems: 6);

        Logger.LogDebug(
            "YAML tool policy decision: allowlist_count={AllowCount} allowlist_hash={AllowHash} allowlist_sample={AllowSample} dangerous_names_count={DangerCount} enable_dangerous={EnableDangerous} skills_count={SkillsCount} tools_count={ToolsCount}",
            allowlistCount,
            allowlistHash,
            allowlistSample,
            policy.DangerousToolNames?.Count ?? 0,
            policy.EnableDangerousTools,
            yaml?.Skills?.Count ?? 0,
            yaml?.Tools?.Count ?? 0);
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

    internal void InternalLogYamlToolPolicyDecision(AgentYamlConfig yaml, YamlToolPolicy policy)
        => LogYamlToolPolicyDecision(yaml, policy);

    /// <summary>
    /// Policy hook: decide whether YAML allowlist should enable dangerous tools.
    /// Default behavior matches current semantics: only enable if allowlist explicitly includes a dangerous tool.
    /// </summary>
    protected virtual bool ShouldEnableDangerousToolsFromYaml(
        AgentYamlConfig yaml,
        IReadOnlySet<string> allowlist,
        IReadOnlySet<string> dangerousToolNames)
    {
        if (allowlist == null || allowlist.Count == 0)
            return false;

        return allowlist.Any(x => dangerousToolNames.Contains(x));
    }

    internal bool InternalShouldEnableDangerousToolsFromYaml(
        AgentYamlConfig yaml,
        IReadOnlySet<string> allowlist,
        IReadOnlySet<string> dangerousToolNames)
        => ShouldEnableDangerousToolsFromYaml(yaml, allowlist, dangerousToolNames);

    /// <summary>
    /// Policy hook: build the baseline tool allowlist from YAML config.
    /// Default behavior matches current semantics:
    /// - yaml.tools => explicit allowlist
    /// - yaml.skills present => auto-include skills tool surface
    /// </summary>
    protected virtual IReadOnlySet<string> BuildToolAllowlistFromYaml(
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

        var autoSkillTools = GetSkillToolsAutoIncludedFromYaml(yaml, skillToolNames);
        if (autoSkillTools is { Count: > 0 })
        {
            foreach (var t in autoSkillTools)
                allow.Add(t);
        }

        return allow;
    }

    /// <summary>
    /// Policy hook: decide which skills tools should be auto-included into YAML allowlist.
    /// Default: include the full skills tool surface only when yaml.skills is present.
    /// </summary>
    protected virtual IReadOnlyCollection<string> GetSkillToolsAutoIncludedFromYaml(
        AgentYamlConfig yaml,
        IReadOnlyCollection<string> skillToolNames)
    {
        if (yaml?.Skills is { Count: > 0 } && skillToolNames != null)
            return skillToolNames;

        return Array.Empty<string>();
    }

    internal IReadOnlySet<string> InternalBuildToolAllowlistFromYaml(
        AgentYamlConfig yaml,
        IReadOnlyCollection<string> skillToolNames)
        => BuildToolAllowlistFromYaml(yaml, skillToolNames);
    private async Task<ToolExecutionResult> ExecuteAllowedToolAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
    {
        return await Tooling.ExecuteAllowedToolAsync(toolName, args, executionContext, llmRequest, cancellationToken);
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