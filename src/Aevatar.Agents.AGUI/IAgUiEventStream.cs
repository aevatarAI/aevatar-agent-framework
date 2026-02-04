namespace Aevatar.Agents.AGUI;

/// <summary>
/// Unified AG-UI event stream surface.
///
/// 中文 + ASCII:
/// - Sessions and Workspaces both expose AG-UI events via an async stream.
/// - This interface is a small "meeting point" to reduce duplicated concepts across modules.
/// </summary>
public interface IAgUiEventStream
{
    IAsyncEnumerable<AgUiEvent> SubscribeAsync(CancellationToken ct);
}

