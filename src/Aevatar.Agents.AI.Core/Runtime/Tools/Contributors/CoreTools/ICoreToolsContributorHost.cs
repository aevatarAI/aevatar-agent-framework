using Aevatar.Agents.AI.Tool.Abstractions;

namespace Aevatar.Agents.AI.Core;

internal interface ICoreToolsContributorHost
{
    Task RegisterToolAsync(IAevatarTool tool, CancellationToken ct);
}

