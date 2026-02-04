namespace Aevatar.Agents.AI.Core;

internal interface IAIGAgentToolContributor
{
    ToolContributionStage Stage { get; }
    Task ContributeAsync(CancellationToken ct);
}

