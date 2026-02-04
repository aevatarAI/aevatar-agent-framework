using Aevatar.Agents.AI.Tool.Tools.CoreTools;

namespace Aevatar.Agents.AI.Core;

internal sealed class CoreToolsContributor(ICoreToolsContributorHost host) : IAIGAgentToolContributor
{
    private readonly ICoreToolsContributorHost _host = host ?? throw new ArgumentNullException(nameof(host));

    public ToolContributionStage Stage => ToolContributionStage.Core;

    public async Task ContributeAsync(CancellationToken ct)
    {
        // Core: state query
        await _host.RegisterToolAsync(new StateQueryTool(), ct);

        // Core: event publishing (requires PublishEventCallback)
        await _host.RegisterToolAsync(new EventPublisherTool(), ct);
    }
}

