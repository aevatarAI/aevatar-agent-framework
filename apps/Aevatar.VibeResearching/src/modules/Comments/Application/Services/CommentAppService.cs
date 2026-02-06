using System.Text.RegularExpressions;
using Aevatar.VibeResearching.Comments.DTOs;
using Aevatar.VibeResearching.Comments.Entities;
using Aevatar.VibeResearching.Comments.Permissions;
using Aevatar.VibeResearching.Comments.Repositories;
using Aevatar.VibeResearching.Comments.Services;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Users;

namespace Aevatar.VibeResearching.Comments.Application.Services;

/// <summary>
/// Application service for managing node comments.
/// </summary>
public partial class CommentAppService : ApplicationService, ICommentAppService
{
    private readonly INodeCommentRepository _repository;
    private readonly IAuthorizationService _authorizationService;

    /// <summary>
    /// Maximum number of comments that can be retrieved in a single request.
    /// </summary>
    private const int MaxTake = 100;

    public CommentAppService(
        INodeCommentRepository repository,
        IAuthorizationService authorizationService)
    {
        _repository = repository;
        _authorizationService = authorizationService;
    }

    /// <inheritdoc/>
    public async Task<List<CommentDto>> GetListAsync(
        string sessionId, string nodeId, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        take = Math.Min(take, MaxTake);

        var comments = await _repository.GetListByNodeIdAsync(sessionId, nodeId, skip, take, ct);
        return ObjectMapper.Map<List<NodeComment>, List<CommentDto>>(comments);
    }

    /// <inheritdoc/>
    public async Task<long> GetCountAsync(
        string sessionId, string nodeId, CancellationToken ct = default)
    {
        return await _repository.GetCountByNodeIdAsync(sessionId, nodeId, ct);
    }

    /// <inheritdoc/>
    public async Task<CommentPreviewResultDto> GetPreviewAsync(
        string sessionId, string nodeId, CancellationToken ct = default)
    {
        var totalCount = await _repository.GetCountByNodeIdAsync(sessionId, nodeId, ct);
        var previews = await _repository.GetPreviewByNodeIdAsync(
            sessionId, nodeId, CommentsConsts.MaxPreviewCount, ct);

        return new CommentPreviewResultDto
        {
            TotalCount = totalCount,
            Comments = ObjectMapper.Map<List<NodeComment>, List<CommentPreviewDto>>(previews)
        };
    }

    /// <inheritdoc/>
    public async Task<CommentDto> CreateAsync(
        string sessionId, string nodeId, CreateCommentDto input, CancellationToken ct = default)
    {
        if (!CurrentUser.IsAuthenticated)
            throw new AbpAuthorizationException("Authentication is required to create a comment.");

        var comment = new NodeComment
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            NodeId = nodeId,
            Content = SanitizeHtml(input.Content),
            AuthorId = CurrentUser.GetId(),
            AuthorDisplayName = CurrentUser.Name ?? CurrentUser.UserName ?? "Unknown",
            ParentCommentId = input.ParentCommentId,
            MentionedUserIds = input.MentionedUserIds ?? [],
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        await _repository.InsertAsync(comment, ct);
        return ObjectMapper.Map<NodeComment, CommentDto>(comment);
    }

    /// <inheritdoc/>
    public async Task<CommentDto> UpdateAsync(
        Guid commentId, UpdateCommentDto input, CancellationToken ct = default)
    {
        var comment = await _repository.GetAsync(commentId, ct)
            ?? throw new UserFriendlyException("Comment not found.");

        if (comment.IsDeleted)
            throw new UserFriendlyException("Cannot edit a deleted comment.");

        await EnsureOwnershipOrPermissionAsync(comment.AuthorId, CommentsPermissions.Comments.EditAny);

        comment.Content = SanitizeHtml(input.Content);
        comment.MentionedUserIds = input.MentionedUserIds ?? comment.MentionedUserIds;
        comment.EditedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(comment, ct);
        return ObjectMapper.Map<NodeComment, CommentDto>(comment);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid commentId, CancellationToken ct = default)
    {
        var comment = await _repository.GetAsync(commentId, ct)
            ?? throw new UserFriendlyException("Comment not found.");

        if (comment.IsDeleted)
            return;

        await EnsureOwnershipOrPermissionAsync(comment.AuthorId, CommentsPermissions.Comments.DeleteAny);

        comment.IsDeleted = true;
        comment.DeletedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(comment, ct);
    }

    /// <summary>
    /// Ensures the current user is the owner or has the specified admin permission.
    /// </summary>
    private async Task EnsureOwnershipOrPermissionAsync(Guid authorId, string adminPermission)
    {
        if (CurrentUser.GetId() == authorId)
            return;

        await AuthorizationService.CheckAsync(adminPermission);
    }

    /// <summary>
    /// Strips dangerous HTML tags and attributes from content to prevent XSS.
    /// Removes script, iframe, embed, object, svg tags, event handler attributes,
    /// javascript/data URIs, and other common XSS vectors.
    /// </summary>
    private static string SanitizeHtml(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // Remove dangerous tags and their content
        content = DangerousTagsRegex().Replace(content, string.Empty);
        // Remove event handler attributes (double-quoted, single-quoted, and unquoted)
        content = EventHandlerRegex().Replace(content, string.Empty);
        // Remove javascript: and data: URIs
        content = DangerousUriRegex().Replace(content, string.Empty);

        return content;
    }

    [GeneratedRegex(@"<\s*(script|iframe|embed|object|svg)\b[^<]*(?:(?!</\s*\1\s*>)<[^<]*)*<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DangerousTagsRegex();

    [GeneratedRegex(@"\bon\w+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerRegex();

    [GeneratedRegex(@"(?:javascript|data)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex DangerousUriRegex();
}
