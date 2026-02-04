using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Tool.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core.Tests.Runtime.Policy;

public sealed class ToolPolicyRuntimeTests
{
    [Fact(DisplayName = "IsToolAllowedByPolicy denies internal tools when internal tools are disabled")]
    public void IsToolAllowedByPolicy_ShouldDenyInternal_WhenDisabled()
    {
        // Arrange
        var host = new Host { AllowInternalTools = false, AllowDangerousTools = true };
        var rt = new ToolPolicyRuntime(host);

        // Act
        var ok = rt.IsToolAllowedByPolicy(new ToolDefinition { Name = "x", RequiresInternalAccess = true });

        // Assert
        ok.Should().BeFalse();
    }

    [Fact(DisplayName = "IsToolAllowedByPolicy denies dangerous/confirmation tools when dangerous tools are disabled")]
    public void IsToolAllowedByPolicy_ShouldDenyDangerous_WhenDisabled()
    {
        // Arrange
        var host = new Host { AllowInternalTools = true, AllowDangerousTools = false };
        var rt = new ToolPolicyRuntime(host);

        // Act
        var dangerousOk = rt.IsToolAllowedByPolicy(new ToolDefinition { Name = "x", IsDangerous = true });
        var confirmOk = rt.IsToolAllowedByPolicy(new ToolDefinition { Name = "x2", RequiresConfirmation = true });

        // Assert
        dangerousOk.Should().BeFalse();
        confirmOk.Should().BeFalse();
    }

    [Fact(DisplayName = "TryApplyToolAllowlistFromSkillsLoadResult clears allowlist when skills_load returns none")]
    public void TryApplyToolAllowlistFromSkillsLoadResult_ShouldClear_WhenNoAllowedTools()
    {
        // Arrange
        var host = new Host();
        var rt = new ToolPolicyRuntime(host);

        var llm = new AevatarLLMRequest { Context = new Dictionary<string, object>() };
        llm.Context[AIGAgentKeys.ToolAllowlist] = new HashSet<string> { "t1" };

        // Act
        rt.TryApplyToolAllowlistFromSkillsLoadResult(llm, "skills_load", """{"success":true}""");

        // Assert
        llm.Context.ContainsKey(AIGAgentKeys.ToolAllowlist).Should().BeFalse();
    }

    [Fact(DisplayName = "TryApplyToolAllowlistFromSkillsLoadResult sets allowlist and source skill")]
    public void TryApplyToolAllowlistFromSkillsLoadResult_ShouldSetAllowlist_AndSourceSkill()
    {
        // Arrange
        var host = new Host();
        var rt = new ToolPolicyRuntime(host);

        var llm = new AevatarLLMRequest { Context = new Dictionary<string, object>() };

        // Act
        rt.TryApplyToolAllowlistFromSkillsLoadResult(llm, "skills_load",
            """{"success":true,"name":"MySkill","allowedTools":["a","b","a"]}""");

        // Assert
        llm.Context.Should().ContainKey(AIGAgentKeys.ToolAllowlist);
        llm.Context.Should().ContainKey(AIGAgentKeys.ToolAllowlistSourceSkill);
        llm.Context[AIGAgentKeys.ToolAllowlistSourceSkill].Should().Be("MySkill");

        var allow = llm.Context[AIGAgentKeys.ToolAllowlist].Should().BeAssignableTo<HashSet<string>>().Subject;
        allow.Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact(DisplayName = "BuildYamlToolPolicy uses host hooks to build allowlist and dangerous set")]
    public void BuildYamlToolPolicy_ShouldUseHostHooks()
    {
        // Arrange
        var host = new Host
        {
            SkillToolNames = new[] { "skills_load" },
            SkillToolsAutoIncluded = new[] { "skills_load" },
            DangerousToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "python_exec" }
        };
        var rt = new ToolPolicyRuntime(host);

        var yaml = new AgentYamlConfig { Id = "id", Name = "n", Tools = ["t1"], Skills = ["s1"] };

        // Act
        var policy = rt.BuildYamlToolPolicy(yaml);

        // Assert
        policy.Allowlist.Should().Contain("t1");
        policy.Allowlist.Should().Contain("skills_load");
        policy.DangerousToolNames.Should().Contain("python_exec");
    }

    private sealed class Host : IToolPolicyHost
    {
        public bool AllowInternalTools { get; set; } = true;
        public bool AllowDangerousTools { get; set; } = true;
        public IReadOnlyCollection<string> SkillToolNames { get; set; } = Array.Empty<string>();
        public IReadOnlyCollection<string> DefaultSkillRoots { get; set; } = Array.Empty<string>();
        public IReadOnlySet<string> DangerousToolNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyCollection<string> SkillToolsAutoIncluded { get; set; } = Array.Empty<string>();

        public Microsoft.Extensions.Logging.ILogger Logger { get; } = NullLogger.Instance;

        public IReadOnlyCollection<string> GetYamlSkillToolNames() => SkillToolNames;
        public IReadOnlyCollection<string> GetYamlDefaultSkillRoots() => DefaultSkillRoots;
        public IReadOnlySet<string> GetYamlDangerousToolNames() => DangerousToolNames;

        public bool ShouldEnableDangerousToolsFromYaml(
            AgentYamlConfig yaml,
            IReadOnlySet<string> allowlist,
            IReadOnlySet<string> dangerousToolNames)
        {
            // default: enable if allowlist contains any dangerous tool
            return allowlist.Any(t => dangerousToolNames.Contains(t));
        }

        public IReadOnlyCollection<string> GetSkillToolsAutoIncludedFromYaml(
            AgentYamlConfig yaml,
            IReadOnlyCollection<string> skillToolNames)
        {
            return SkillToolsAutoIncluded.Count > 0 ? SkillToolsAutoIncluded : Array.Empty<string>();
        }
    }
}

