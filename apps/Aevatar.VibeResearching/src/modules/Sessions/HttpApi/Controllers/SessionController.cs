using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Aevatar.VibeResearching.Sessions.DTOs;
using Aevatar.VibeResearching.Sessions.Services;

namespace Aevatar.VibeResearching.Sessions.Controllers;

/// <summary>
/// Research session management controller.
/// </summary>
[ApiController]
[Route("api/sessions")]
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
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateSessionDto input,
        CancellationToken ct = default)
    {
        var session = await _sessionAppService.CreateAsync(input, ct);
        return Ok(new { ok = true, sessionId = session.SessionId });
    }

    /// <summary>
    /// Gets all research sessions.
    /// GET /api/sessions
    /// </summary>
    [HttpGet]
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
    /// DELETE /api/sessions/{sessionId}
    /// </summary>
    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> DeleteAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        await _sessionAppService.DeleteAsync(sessionId, ct);
        return Ok(new { ok = true, sessionId });
    }

    /// <summary>
    /// Submits user input to a session.
    /// POST /api/sessions/{sessionId}/input
    /// </summary>
    [HttpPost("{sessionId}/input")]
    public async Task<IActionResult> SubmitInputAsync(
        [FromRoute] string sessionId,
        [FromBody] SessionInputDto input,
        CancellationToken ct = default)
    {
        var runId = await _sessionAppService.SubmitInputAsync(sessionId, input, ct);
        return Accepted($"/api/sessions/{sessionId}", new { ok = true, sessionId, runId });
    }

    /// <summary>
    /// Triggers MCP reconnection for a session.
    /// POST /api/sessions/{sessionId}/mcp/reconnect
    /// </summary>
    [HttpPost("{sessionId}/mcp/reconnect")]
    public async Task<IActionResult> ReconnectMcpAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        await _sessionAppService.ReconnectMcpAsync(sessionId, ct);
        return Accepted($"/api/sessions/{sessionId}", new { ok = true });
    }

    /// <summary>
    /// Gets the tools snapshot for a session.
    /// GET /api/sessions/{sessionId}/tools
    /// </summary>
    [HttpGet("{sessionId}/tools")]
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
    public async Task<IActionResult> GetStatusAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var status = await _sessionAppService.GetStatusAsync(sessionId, ct);
        if (status == null)
            return NotFound(new { error = "session not found" });

        return Ok(status);
    }
}
