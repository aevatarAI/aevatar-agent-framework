using Aevatar.VibeResearching.Comments.DTOs;
using Aevatar.VibeResearching.Comments.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.VibeResearching.Comments.Controllers;

/// <summary>
/// REST controller for managing comments on DAG nodes within a session.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId}/nodes/{nodeId}/comments")]
public class CommentsController : AbpControllerBase
{
    private readonly ICommentAppService _commentAppService;

    public CommentsController(ICommentAppService commentAppService)
    {
        _commentAppService = commentAppService;
    }

    /// <summary>
    /// Gets a paged list of comments for a node.
    /// GET /api/sessions/{sessionId}/nodes/{nodeId}/comments
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetListAsync(
        [FromRoute] string sessionId,
        [FromRoute] string nodeId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Min(take, 100);
        var items = await _commentAppService.GetListAsync(sessionId, nodeId, skip, take, ct);
        var totalCount = await _commentAppService.GetCountAsync(sessionId, nodeId, ct);
        return Ok(new { items, totalCount });
    }

    /// <summary>
    /// Gets a preview of the latest comments and total count for a node.
    /// GET /api/sessions/{sessionId}/nodes/{nodeId}/comments/preview
    /// </summary>
    [HttpGet("preview")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPreviewAsync(
        [FromRoute] string sessionId,
        [FromRoute] string nodeId,
        CancellationToken ct = default)
    {
        var result = await _commentAppService.GetPreviewAsync(sessionId, nodeId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Creates a new comment on a node.
    /// POST /api/sessions/{sessionId}/nodes/{nodeId}/comments
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateAsync(
        [FromRoute] string sessionId,
        [FromRoute] string nodeId,
        [FromBody] CreateCommentDto input,
        CancellationToken ct = default)
    {
        var result = await _commentAppService.CreateAsync(sessionId, nodeId, input, ct);
        return Ok(result);
    }

    /// <summary>
    /// Updates an existing comment.
    /// PUT /api/sessions/{sessionId}/nodes/{nodeId}/comments/{commentId}
    /// </summary>
    [HttpPut("{commentId:guid}")]
    [Authorize]
    public async Task<IActionResult> UpdateAsync(
        [FromRoute] string sessionId,
        [FromRoute] string nodeId,
        [FromRoute] Guid commentId,
        [FromBody] UpdateCommentDto input,
        CancellationToken ct = default)
    {
        var result = await _commentAppService.UpdateAsync(commentId, input, ct);
        return Ok(result);
    }

    /// <summary>
    /// Soft-deletes a comment.
    /// DELETE /api/sessions/{sessionId}/nodes/{nodeId}/comments/{commentId}
    /// </summary>
    [HttpDelete("{commentId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteAsync(
        [FromRoute] string sessionId,
        [FromRoute] string nodeId,
        [FromRoute] Guid commentId,
        CancellationToken ct = default)
    {
        await _commentAppService.DeleteAsync(commentId, ct);
        return Ok(new { id = commentId });
    }
}
