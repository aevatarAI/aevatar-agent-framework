using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Core.StateProtection;

namespace Aevatar.Agents.AI.Core.Configuration;

// ============================================================
//  AgentYamlConfigApplier
//
//  Goal:
//  - Apply AgentYamlConfig (YAML) onto a runtime AIGAgentBase instance.
//
//  Scope:
//  - Config knobs: model/temp/max_tokens/top_p/penalties/stop_sequences
//  - System prompt: system_prompt/persona composition
//  - Tools baseline allowlist: yaml.tools (+ auto-add skills tools when yaml.skills present)
//  - Skills: enable skills runtime and set default roots to ~/.aevatar/skills
//
//  Fallback:
//  - If yaml is null -> no-op
//  - If fields are missing -> leave current agent settings unchanged
// ============================================================

public static class AgentYamlConfigApplier
{
    private static IDisposable? BeginConfigScopeIfNeeded()
    {
        return StateProtectionContext.IsModifiable
            ? null
            : StateProtectionContext.BeginInitializationScope();
    }

    public static void ApplyModelKnobs(AgentYamlConfig yaml, AevatarAIAgentConfig cfg)
    {
        if (yaml == null || cfg == null) return;

        using var scope = BeginConfigScopeIfNeeded();

        if (!string.IsNullOrWhiteSpace(yaml.Model))
            cfg.Model = yaml.Model.Trim();

        if (yaml.Temperature.HasValue)
            cfg.Temperature = (float)yaml.Temperature.Value;

        if (yaml.MaxTokens.HasValue)
            cfg.MaxOutputTokens = yaml.MaxTokens.Value;

        if (yaml.TopP.HasValue)
            cfg.TopP = (float)yaml.TopP.Value;

        if (yaml.FrequencyPenalty.HasValue)
            cfg.FrequencyPenalty = (float)yaml.FrequencyPenalty.Value;

        if (yaml.PresencePenalty.HasValue)
            cfg.PresencePenalty = (float)yaml.PresencePenalty.Value;

        if (yaml.StopSequences is { Count: > 0 })
        {
            cfg.StopSequences.Clear();
            foreach (var s in yaml.StopSequences)
            {
                var t = (s ?? string.Empty).Trim();
                if (t.Length > 0)
                    cfg.StopSequences.Add(t);
            }
        }

        if (!string.IsNullOrWhiteSpace(yaml.SystemPrompt))
            cfg.SystemPrompt = yaml.SystemPrompt;
    }

    public static void ApplySystemPrompt(AIGAgentBase agent, AgentYamlConfig yaml, string? role)
    {
        if (agent == null || yaml == null) return;

        using var scope = BeginConfigScopeIfNeeded();

        var roleKey = (role ?? string.Empty).Trim();
        var roleLine = roleKey.Length == 0
            ? "You are a role-driven agent."
            : $"You are the '{roleKey}' role in a mesh-driven workflow.";

        // 1) system_prompt wins
        if (!string.IsNullOrWhiteSpace(yaml.SystemPrompt))
        {
            var pinned = yaml.Skills is { Count: > 0 }
                ? $"\n\nPinned skills (recommended to load early via skills_load):\n- {string.Join("\n- ", yaml.Skills.Select(s => (s ?? string.Empty).Trim()).Where(s => s.Length > 0))}"
                : string.Empty;

            agent.SystemPrompt = $"{roleLine}\n\n{yaml.SystemPrompt.Trim()}{pinned}";

            // Ensure GetEffectiveSystemPrompt sees it even if Config.SystemPrompt is preferred.
            agent.InternalConfig.SystemPrompt = agent.SystemPrompt;
            return;
        }

        // 2) persona fallback
        if (yaml.Persona != null)
        {
            var p = yaml.Persona;
            var parts = new List<string> { roleLine };

            if (!string.IsNullOrWhiteSpace(p.Role))
                parts.Add($"You are {p.Role.Trim()}.");

            if (p.Expertise is { Count: > 0 })
                parts.Add($"Your expertise includes: {string.Join(", ", p.Expertise)}.");

            if (!string.IsNullOrWhiteSpace(p.Style))
                parts.Add($"Communication style: {p.Style.Trim()}.");

            if (p.Traits is { Count: > 0 })
                parts.Add($"Key traits: {string.Join(", ", p.Traits)}.");

            agent.SystemPrompt = string.Join("\n\n", parts);
            agent.InternalConfig.SystemPrompt = agent.SystemPrompt;
        }
    }

    public static async Task ApplyToolsAndSkillsAsync(
        AIGAgentBase agent,
        AgentYamlConfig yaml,
        CancellationToken ct)
    {
        if (agent == null || yaml == null) return;

        // Skills roots + enablement only if YAML requests it.
        if (yaml.Skills is { Count: > 0 })
        {
            agent.AllowInternalTools = true;
            await agent.ConfigureAgentSkillsAsync(
                roots: agent.InternalGetYamlDefaultSkillRoots(),
                enable: true,
                cancellationToken: ct);
        }

        // Baseline tool allowlist + dangerous tools policy (hooked in AIGAgentBase).
        var policy = agent.InternalBuildYamlToolPolicy(yaml);
        agent.InternalLogYamlToolPolicyDecision(yaml, policy);
        var allow = policy.Allowlist;

        agent.SetFixedToolAllowlist(allow.Count == 0 ? null : allow);

        // Tool packs: register tools that appear in yaml.tools (best-effort).
        await agent.RegisterYamlToolPacksAsync(yaml, ct);

        // Dangerous tools: enable only if explicitly allowlisted by YAML.
        // (We keep fallback semantics for agents that already configure AllowDangerousTools themselves.)
        if (policy.EnableDangerousTools)
        {
            agent.AllowDangerousTools = true;
        }
    }

    /// <summary>
    /// Convenience: apply config + prompt + tools/skills in one go (best-effort).
    /// </summary>
    public static async Task ApplyAsync(
        AIGAgentBase agent,
        AgentYamlConfig? yaml,
        string? role,
        CancellationToken ct)
    {
        if (agent == null || yaml == null) return;

        using var scope = BeginConfigScopeIfNeeded();

        ApplyModelKnobs(yaml, agent.InternalConfig);
        ApplySystemPrompt(agent, yaml, role);
        await ApplyToolsAndSkillsAsync(agent, yaml, ct);
    }
}