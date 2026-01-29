using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Sessions.Runtime;

public sealed record SessionAgUiBootstrapContext(
    SessionState Session,
    SessionPrimaryAgentInfo Primary,
    SessionAgUiStream Stream,
    IGAgentActorManager ActorManager,
    SessionRuntimeOptions Options);

public interface ISessionAgUiBootstrapper
{
    Task<IReadOnlyList<AgUiEvent>> BuildAsync(SessionAgUiBootstrapContext context, CancellationToken ct);
}

public sealed class DefaultSessionAgUiBootstrapper : ISessionAgUiBootstrapper
{
    private readonly ILogger<DefaultSessionAgUiBootstrapper> _logger;

    public DefaultSessionAgUiBootstrapper(ILogger<DefaultSessionAgUiBootstrapper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<AgUiEvent>> BuildAsync(SessionAgUiBootstrapContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.Primary.AgentId))
            return Array.Empty<AgUiEvent>();

        var laneId = string.IsNullOrWhiteSpace(context.Primary.Role) ? "primary" : context.Primary.Role;
        var actors = new List<AgUiActor>
        {
            new()
            {
                ActorId = context.Primary.AgentId,
                LaneId = laneId
            }
        };

        var messages = await AgUiBootstrap.CollectAssistantMessagesAsync(
            context.ActorManager,
            actors,
            new AgUiMessageSnapshotOptions
            {
                ThreadId = context.Session.SessionId,
                MaxAssistantMessages = Math.Max(1, context.Options.MaxSnapshotMessages)
            },
            ct);

        if (messages.Count == 0)
            return Array.Empty<AgUiEvent>();

        _logger.LogDebug("[SessionAgUiBootstrap] Messages snapshot: session={SessionId}, count={Count}",
            context.Session.SessionId, messages.Count);

        return new AgUiEvent[]
        {
            new MessagesSnapshotEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Messages = messages.ToList()
            }
        };
    }
}
