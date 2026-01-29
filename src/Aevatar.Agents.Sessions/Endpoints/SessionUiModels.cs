namespace Aevatar.Agents.Sessions.Endpoints;

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
public sealed record RegisterDotNetToolInput(string? SessionId, string? FilePath);
public sealed record WorkflowYamlInput(string? Yaml, string? Name);
public sealed record WorkflowRunInput(
    string? RequestId,
    string? WorkflowName,
    string? Message,
    string? Mode,
    Dictionary<string, object?>? Variables);

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
