namespace Aevatar.Agents.Workspaces.Endpoints;

public sealed record RoleYamlInput(string? Yaml);
public sealed record RoleInstanceInput(string? Role, bool? LinkToRoot, bool? SetAsRoot);
public sealed record RoleInstanceRebuildInput(string? Role, bool? LinkToRoot, bool? SetAsRoot);
public sealed record RoleInstanceRemoveInput(string? Role);
public sealed record RoleLinkInput(string? ParentRole, string? ChildRole);
public sealed record RegisterRoleToolInput(string? FilePath);

public sealed record RoleChatRequestInput(
    string? RequestId,
    string? UserId,
    string? Message,
    Dictionary<string, string>? Context,
    string? Timestamp,
    double? Temperature,
    int? MaxTokens,
    int? StreamChunkEveryN);
