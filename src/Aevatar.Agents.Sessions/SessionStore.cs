using Aevatar.Agents.Abstractions.Persistence;

namespace Aevatar.Agents.Sessions;

public interface ISessionStore
{
    Task<SessionState?> GetAsync(string sessionId, CancellationToken ct = default);
    Task SaveAsync(SessionState state, CancellationToken ct = default);
    Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default);
}

internal sealed class SessionStore(IStateStore<SessionState> store) : ISessionStore
{
    private readonly IStateStore<SessionState> _store =
        store ?? throw new ArgumentNullException(nameof(store));

    public Task<SessionState?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Task.FromResult<SessionState?>(null);

        // #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionId = sessionId.Trim(),
                runId = string.Empty,
                hypothesisId = "H21",
                location = "SessionStore.cs:GetAsync",
                message = "session_state_get",
                data = new
                {
                    storeType = _store.GetType().FullName ?? _store.GetType().Name
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }) + Environment.NewLine);
        // #endregion

        return _store.LoadAsync(sessionId.Trim(), ct);
    }

    public Task SaveAsync(SessionState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(state.SessionId))
            throw new ArgumentException("SessionState.session_id is required.", nameof(state));

        // #region agent log
        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionId = state.SessionId.Trim(),
                runId = string.Empty,
                hypothesisId = "H20",
                location = "SessionStore.cs:SaveAsync",
                message = "session_state_save",
                data = new
                {
                    storeType = _store.GetType().FullName ?? _store.GetType().Name,
                    workflowName = state.WorkflowName ?? string.Empty
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }) + Environment.NewLine);
        // #endregion

        return _store.SaveAsync(state.SessionId.Trim(), state, ct);
    }

    public Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Task.FromResult(false);

        return _store.ExistsAsync(sessionId.Trim(), ct);
    }
}
