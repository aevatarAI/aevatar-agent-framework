using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal interface IMemorySearchToolsContributorHost
{
    ILogger Logger { get; }
    IStateQueryService? CqrsStateQueryService { get; }
    IMemoryStore? MemoryStore { get; }
    IMemoryVectorIndex? MemoryVectorIndex { get; }

    Task RegisterToolAsync(IAevatarTool tool, CancellationToken ct);
}

