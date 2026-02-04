using Aevatar.Agents.AI.Tool.Tools.BuiltIn;

namespace Aevatar.Agents.AI.Core;

internal sealed class MemorySearchToolsContributor(IMemorySearchToolsContributorHost host) : IAIGAgentToolContributor
{
    private readonly IMemorySearchToolsContributorHost _host = host ?? throw new ArgumentNullException(nameof(host));

    public ToolContributionStage Stage => ToolContributionStage.Core;

    public async Task ContributeAsync(CancellationToken ct)
    {
        await _host.RegisterToolAsync(
            new AevatarMemorySearchTool(
                new LoggerAdapter<AevatarMemorySearchTool>(_host.Logger),
                _host.CqrsStateQueryService,
                _host.MemoryStore,
                _host.MemoryVectorIndex),
            ct);
    }
}

