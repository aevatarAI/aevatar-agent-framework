using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Authorization;
using Aevatar.VibeResearching.Sessions.DTOs;
using Aevatar.VibeResearching.Sessions.Permissions;
using Aevatar.VibeResearching.Sessions.Services;

namespace Aevatar.VibeResearching.Sessions.Controllers;

/// <summary>
/// Research session management controller.
/// </summary>
[ApiController]
[Route("api/sessions")]
[Authorize]
public class SessionController : AbpControllerBase
{
    private readonly ISessionAppService _sessionAppService;

    public SessionController(ISessionAppService sessionAppService)
    {
        _sessionAppService = sessionAppService;
    }

    /// <summary>
    /// Creates a new research session.
    /// POST /api/sessions
    /// </summary>
    [HttpPost]
    [Authorize(SessionsPermissions.Sessions.Create)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateSessionDto input,
        CancellationToken ct = default)
    {
        var session = await _sessionAppService.CreateAsync(input, ct);
        return Ok(new { ok = true, sessionId = session.SessionId });
    }

    /// <summary>
    /// Gets all research sessions (excludes archived by default).
    /// Anonymous users can browse sessions.
    /// GET /api/sessions
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetListAsync(CancellationToken ct = default)
    {
        var sessions = await _sessionAppService.GetListAsync(ct);
        return Ok(new { count = sessions.Count, sessions });
    }

    /// <summary>
    /// Gets a specific session by ID.
    /// GET /api/sessions/{sessionId}
    /// </summary>
    [HttpGet("{sessionId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var session = await _sessionAppService.GetAsync(sessionId, ct);
        if (session == null)
            return NotFound(new { error = "session not found" });

        return Ok(new { ok = true, session });
    }

    /// <summary>
    /// Deletes a research session.
    /// Only the session owner or an admin can delete.
    /// Returns 404 for unauthorized access to hide resource existence.
    /// DELETE /api/sessions/{sessionId}
    /// </summary>
    [HttpDelete("{sessionId}")]
    [Authorize(SessionsPermissions.Sessions.Delete)]
    public async Task<IActionResult> DeleteAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        if (!await IsOwnerOrAdminAsync(sessionId, ct))
            return NotFound(new { error = "session not found" });

        await _sessionAppService.DeleteAsync(sessionId, ct);
        return Ok(new { ok = true, sessionId });
    }

    /// <summary>
    /// Submits user input to a session.
    /// Only the session owner or an admin can submit input.
    /// Returns 404 for unauthorized access to hide resource existence.
    /// POST /api/sessions/{sessionId}/input
    /// </summary>
    [HttpPost("{sessionId}/input")]
    public async Task<IActionResult> SubmitInputAsync(
        [FromRoute] string sessionId,
        [FromBody] SessionInputDto input,
        CancellationToken ct = default)
    {
        if (!await IsOwnerOrAdminAsync(sessionId, ct))
            return NotFound(new { error = "session not found" });

        try
        {
            var runId = await _sessionAppService.SubmitInputAsync(sessionId, input, ct);
            return Accepted($"/api/sessions/{sessionId}", new { ok = true, sessionId, runId });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "SessionNotActive", message = ex.Message });
        }
    }

    /// <summary>
    /// Pauses an active session.
    /// Only the session owner or an admin can pause.
    /// Returns 404 for unauthorized access to hide resource existence.
    /// POST /api/sessions/{sessionId}/pause
    /// </summary>
    [HttpPost("{sessionId}/pause")]
    [Authorize(SessionsPermissions.Sessions.Pause)]
    public async Task<IActionResult> PauseAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        if (!await IsOwnerOrAdminAsync(sessionId, ct))
            return NotFound(new { error = "session not found" });

        try
        {
            await _sessionAppService.PauseAsync(sessionId, ct);
            return Ok(new { ok = true, sessionId });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "InvalidStateTransition", message = ex.Message });
        }
    }

    /// <summary>
    /// Resumes a paused session.
    /// Only the session owner or an admin can resume.
    /// Returns 404 for unauthorized access to hide resource existence.
    /// POST /api/sessions/{sessionId}/resume
    /// </summary>
    [HttpPost("{sessionId}/resume")]
    [Authorize(SessionsPermissions.Sessions.Resume)]
    public async Task<IActionResult> ResumeAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        if (!await IsOwnerOrAdminAsync(sessionId, ct))
            return NotFound(new { error = "session not found" });

        try
        {
            var result = await _sessionAppService.ResumeAsync(sessionId, ct);
            return Ok(new { ok = true, sessionId, autoResumed = result.AutoResumed, runId = result.RunId });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "InvalidStateTransition", message = ex.Message });
        }
    }

    /// <summary>
    /// Terminates (archives) a session permanently.
    /// Only the session owner or an admin can terminate.
    /// Returns 404 for unauthorized access to hide resource existence.
    /// POST /api/sessions/{sessionId}/terminate
    /// </summary>
    [HttpPost("{sessionId}/terminate")]
    [Authorize(SessionsPermissions.Sessions.Terminate)]
    public async Task<IActionResult> TerminateAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        if (!await IsOwnerOrAdminAsync(sessionId, ct))
            return NotFound(new { error = "session not found" });

        try
        {
            await _sessionAppService.TerminateAsync(sessionId, ct);
            return Ok(new { ok = true, sessionId });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "InvalidStateTransition", message = ex.Message });
        }
    }

    /// <summary>
    /// Triggers MCP reconnection for a session.
    /// Only the session owner or an admin can reconnect MCP.
    /// Returns 404 for unauthorized access to hide resource existence.
    /// POST /api/sessions/{sessionId}/mcp/reconnect
    /// </summary>
    [HttpPost("{sessionId}/mcp/reconnect")]
    public async Task<IActionResult> ReconnectMcpAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        if (!await IsOwnerOrAdminAsync(sessionId, ct))
            return NotFound(new { error = "session not found" });

        await _sessionAppService.ReconnectMcpAsync(sessionId, ct);
        return Accepted($"/api/sessions/{sessionId}", new { ok = true });
    }

    /// <summary>
    /// Gets the tools snapshot for a session.
    /// GET /api/sessions/{sessionId}/tools
    /// </summary>
    [HttpGet("{sessionId}/tools")]
    [AllowAnonymous]
    public async Task<IActionResult> GetToolsAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var tools = await _sessionAppService.GetToolsAsync(sessionId, ct);
        return Ok(tools);
    }

    /// <summary>
    /// Gets real-time status of a session (running steps, agents, tools).
    /// GET /api/sessions/{sessionId}/status
    /// </summary>
    [HttpGet("{sessionId}/status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStatusAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var status = await _sessionAppService.GetStatusAsync(sessionId, ct);
        if (status == null)
            return NotFound(new { error = "session not found" });

        return Ok(status);
    }

    /// <summary>
    /// Checks if the current user is the session owner or has admin privileges.
    /// Returns false if the session does not exist or the user is not authorized,
    /// enabling the caller to return 404 to hide resource existence from unauthorized users.
    /// </summary>
    /// <param name="sessionId">The session ID to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the session exists and the user is owner or admin; false otherwise.</returns>
    private async Task<bool> IsOwnerOrAdminAsync(string sessionId, CancellationToken ct)
    {
        var session = await _sessionAppService.GetAsync(sessionId, ct);
        if (session == null)
            return false;

        // Legacy sessions without an owner are accessible by any authenticated user
        if (string.IsNullOrWhiteSpace(session.OwnerId))
            return true;

        // Check if the current user is the owner
        var currentUserId = CurrentUser.Id?.ToString();
        if (!string.IsNullOrWhiteSpace(currentUserId) &&
            string.Equals(currentUserId, session.OwnerId, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if the user has admin permission
        try
        {
            await AuthorizationService.CheckAsync(SessionsPermissions.Sessions.Admin);
            return true;
        }
        catch (AbpAuthorizationException)
        {
            return false;
        }
    }
}
