using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.VibeResearching.Infrastructure.Controllers;

/// <summary>
/// Session workspace file operations controller.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId}")]
[Authorize]
public class WorkspaceController : AbpControllerBase
{
    private readonly IWorkspaceAppService _workspaceAppService;

    public WorkspaceController(IWorkspaceAppService workspaceAppService)
    {
        _workspaceAppService = workspaceAppService;
    }

    /// <summary>
    /// Lists all files in the session workspace.
    /// GET /api/sessions/{sessionId}/workspace
    /// </summary>
    [HttpGet("workspace")]
    [AllowAnonymous]
    public async Task<IActionResult> ListFilesAsync(
        [FromRoute] string sessionId,
        CancellationToken ct = default)
    {
        var workspace = await _workspaceAppService.ListFilesAsync(sessionId, ct);
        return Ok(new { ok = true, sessionId, workspace });
    }

    /// <summary>
    /// Reads a file from the session workspace.
    /// GET /api/sessions/{sessionId}/files
    /// </summary>
    [HttpGet("files")]
    [AllowAnonymous]
    public async Task<IActionResult> ReadFileAsync(
        [FromRoute] string sessionId,
        [FromQuery] string path,
        CancellationToken ct = default)
    {
        var content = await _workspaceAppService.ReadFileAsync(sessionId, path, ct);
        return Ok(new { ok = true, sessionId, file = new { path, content } });
    }

    /// <summary>
    /// Saves a file to the session workspace.
    /// PUT /api/sessions/{sessionId}/files
    /// </summary>
    [HttpPut("files")]
    public async Task<IActionResult> SaveFileAsync(
        [FromRoute] string sessionId,
        [FromBody] SaveFileDto input,
        CancellationToken ct = default)
    {
        await _workspaceAppService.SaveFileAsync(sessionId, input, ct);
        return Ok(new { ok = true, sessionId, file = new { path = input.Path } });
    }

    /// <summary>
    /// Deletes a file from the session workspace.
    /// DELETE /api/sessions/{sessionId}/files
    /// </summary>
    [HttpDelete("files")]
    public async Task<IActionResult> DeleteFileAsync(
        [FromRoute] string sessionId,
        [FromQuery] string path,
        CancellationToken ct = default)
    {
        await _workspaceAppService.DeleteFileAsync(sessionId, path, ct);
        return Ok(new { ok = true, sessionId, path });
    }

    /// <summary>
    /// Gets file tree structure.
    /// GET /api/sessions/{sessionId}/files/tree
    /// </summary>
    [HttpGet("files/tree")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFileTreeAsync(
        [FromRoute] string sessionId,
        [FromQuery] string? dir = null,
        [FromQuery] int? depth = null,
        CancellationToken ct = default)
    {
        var tree = await _workspaceAppService.ListFilesAsync(sessionId, ct);
        return Ok(new { ok = true, sessionId, tree });
    }
}
