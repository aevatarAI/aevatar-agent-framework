namespace Aevatar.Agents.AI.Core;

internal sealed class McpToolsContributor(IMcpToolsContributorHost host) : IAIGAgentToolContributor
{
    private readonly IMcpToolsContributorHost _host = host ?? throw new ArgumentNullException(nameof(host));

    public ToolContributionStage Stage => ToolContributionStage.BestEffort;

    public async Task ContributeAsync(CancellationToken ct)
    {
        await _host.RegisterMcpServersFromConfigurationBestEffortAsync(isRetry: false, ct);
    }
}

