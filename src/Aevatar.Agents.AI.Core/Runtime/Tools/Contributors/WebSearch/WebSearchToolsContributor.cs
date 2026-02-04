namespace Aevatar.Agents.AI.Core;

internal sealed class WebSearchToolsContributor(IWebSearchToolsContributorHost host) : IAIGAgentToolContributor
{
    private readonly IWebSearchToolsContributorHost _host = host ?? throw new ArgumentNullException(nameof(host));

    public ToolContributionStage Stage => ToolContributionStage.BestEffort;

    public async Task ContributeAsync(CancellationToken ct)
    {
        await _host.RegisterWebSearchToolBestEffortAsync(ct);
    }
}

