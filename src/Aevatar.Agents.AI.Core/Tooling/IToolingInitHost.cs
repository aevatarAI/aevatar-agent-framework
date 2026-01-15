using Aevatar.Agents.AI.Tool.Abstractions;

namespace Aevatar.Agents.AI.Core;

internal interface IToolingInitHost
{
    IAevatarToolManager CreateToolManager();
    Task RegisterToolsAsync(CancellationToken cancellationToken);
}

