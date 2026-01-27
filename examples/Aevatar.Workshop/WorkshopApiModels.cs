namespace Aevatar.Workshop;

public sealed record ChatRequestInput(
    string? RequestId,
    string? UserId,
    string? Message,
    Dictionary<string, string>? Context,
    string? Timestamp,
    double? Temperature,
    int? MaxTokens,
    int? StreamChunkEveryN);
public sealed record PingInput(string? SessionId, string? Content);
public sealed record AgentYamlInput(string? Yaml, bool CreateSession = true);
public sealed record RoleYamlInput(string? Yaml);
public sealed record RoleInstanceInput(string? Role, bool? LinkToRoot, bool? SetAsRoot);
public sealed record RoleInstanceRemoveInput(string? Role);
public sealed record RoleLinkInput(string? ParentRole, string? ChildRole);
public sealed record WorkflowYamlInput(string? Yaml, string? Name);
public sealed record SetApiKeyInput(string? ProviderName, string? ApiKey);
public sealed record SetDefaultProviderInput(string? ProviderName);
public sealed record RegisterDotNetToolInput(string? SessionId, string? FilePath);

public sealed record AgentSettingsInput(
    string? SessionId,
    bool? EnableHistory,
    bool? EnableCompaction,
    bool? EnableMemoryStore,
    bool? EnableSessionMemory,
    bool? EnableVectorIndex,
    bool? EnableMcp,
    bool? EnableSkills,
    bool? AllowDangerousTools,
    bool? AllowInternalTools);
