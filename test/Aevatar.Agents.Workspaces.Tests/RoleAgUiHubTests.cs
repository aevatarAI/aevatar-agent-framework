using Aevatar.Agents.AGUI;
using Aevatar.Agents.Workspaces.Hubs;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Workspaces.Tests;

public sealed class RoleAgUiHubTests
{
    [Fact]
    public async Task PublishDelegation_ShouldEmitCustomEvent()
    {
        var hub = new RoleAgUiHub();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var collectTask = CollectUntilAsync(hub, evt => evt is CustomEvent custom && custom.Name == "WORKSPACE_DELEGATION", cts.Token);

        hub.PublishDelegation("root", "worker", "hello", "req-1");

        var events = await collectTask;
        events.OfType<CustomEvent>().Any(e => e.Name == "WORKSPACE_DELEGATION").ShouldBeTrue();
    }

    private static async Task<List<AgUiEvent>> CollectUntilAsync(
        RoleAgUiHub hub,
        Func<AgUiEvent, bool> predicate,
        CancellationToken ct)
    {
        var list = new List<AgUiEvent>();
        try
        {
            await foreach (var evt in hub.SubscribeAsync(ct))
            {
                list.Add(evt);
                if (predicate(evt))
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }

        return list;
    }
}
