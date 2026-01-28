using Volo.Abp.Application.Services;
using Aevatar.VibeResearching.Agents.Contracts.Sessions;
using Aevatar.VibeResearching.Sessions.DTOs;

namespace Aevatar.VibeResearching.Sessions.Services;

/// <summary>
/// Application service for managing research sessions.
/// </summary>
public interface ISessionAppService : IApplicationService
{
    /// <summary>
    /// Creates a new research session.
    /// </summary>
    /// <param name="input">Session creation parameters</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created session record</returns>
    Task<VibeSessionRecord> CreateAsync(CreateSessionDto input, CancellationToken ct = default);

    /// <summary>
    /// Gets all research sessions.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of session records</returns>
    Task<IReadOnlyList<VibeSessionRecord>> GetListAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a specific session by ID.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Session record if found, null otherwise</returns>
    Task<VibeSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a research session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Submits user input to a session (fires asynchronous processing).
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="input">User input data</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Run identifier for tracking</returns>
    Task<string> SubmitInputAsync(string sessionId, SessionInputDto input, CancellationToken ct = default);

    /// <summary>
    /// Triggers MCP reconnection for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    Task ReconnectMcpAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the tools snapshot for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Tools snapshot data</returns>
    Task<object> GetToolsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the status of a session (active run, workspace state).
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Session status data if found, null otherwise</returns>
    Task<object?> GetStatusAsync(string sessionId, CancellationToken ct = default);
}
