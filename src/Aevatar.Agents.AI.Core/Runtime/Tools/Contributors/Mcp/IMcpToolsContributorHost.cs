namespace Aevatar.Agents.AI.Core;

internal interface IMcpToolsContributorHost
{
    Task<bool> RegisterMcpServersFromConfigurationBestEffortAsync(bool isRetry, CancellationToken ct);
}

