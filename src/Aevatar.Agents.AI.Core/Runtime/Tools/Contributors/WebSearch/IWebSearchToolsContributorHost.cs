namespace Aevatar.Agents.AI.Core;

internal interface IWebSearchToolsContributorHost
{
    Task<bool> RegisterWebSearchToolBestEffortAsync(CancellationToken ct);
}

