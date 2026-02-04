namespace Aevatar.Agents.AI.Core;

internal sealed class AgentSkillsToolsContributor(IAgentSkillsToolsContributorHost host) : IAIGAgentToolContributor
{
    private readonly IAgentSkillsToolsContributorHost _host = host ?? throw new ArgumentNullException(nameof(host));

    public ToolContributionStage Stage => ToolContributionStage.BestEffort;

    public async Task ContributeAsync(CancellationToken ct)
    {
        await _host.RegisterAgentSkillsToolsAsync(ct);
    }
}

