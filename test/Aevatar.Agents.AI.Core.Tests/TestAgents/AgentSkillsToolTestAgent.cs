using Aevatar.Agents.AI.Core.Tests.Messages;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.Messages;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core.Tests.TestAgents;

/// <summary>
/// Test agent exposing tool execution helpers (used by AgentSkills tests).
/// </summary>
public sealed class AgentSkillsToolTestAgent : AIGAgentBase<TestAIGAgentState, TestAIGAgentConfig>
{
    public AgentSkillsToolTestAgent() : base(Guid.NewGuid().ToString("N"))
    {
    }

    public AgentSkillsToolTestAgent(string id) : base(id)
    {
    }

    public override Task<string> GetDescriptionAsync() => Task.FromResult("agent-skills-tool-test-agent");

    public async Task<ToolExecutionResult> ExecuteToolForTestAsync(
        string toolName,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeToolsAsync(cancellationToken);

        var ctx = new ToolExecutionContext
        {
            AgentId = Id.ToString(),
            ToolManager = ToolManager,
            Logger = NullLogger.Instance,
            GetSessionId = () => Id.ToString(),
            AllowInternalTools = AllowInternalTools,
            AllowDangerousTools = AllowDangerousTools
        };

        return await ToolManager.ExecuteToolAsync(
            toolName,
            parameters ?? new Dictionary<string, object>(),
            ctx,
            cancellationToken);
    }
}

