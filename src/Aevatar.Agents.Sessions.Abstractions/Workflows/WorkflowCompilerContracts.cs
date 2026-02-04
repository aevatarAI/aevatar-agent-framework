using System.Text.Json;

namespace Aevatar.Agents.Sessions.Abstractions.Workflows;

public sealed record WorkflowValidationError(
    string Code,
    string Message,
    string? Path = null);

public sealed record WorkflowNodeSpec(
    string Id,
    string Type,
    IReadOnlyDictionary<string, JsonElement>? Params);

public sealed record WorkflowEdgeSpec(
    string From,
    string To,
    string Channel);

public sealed record WorkflowGoalSpec(
    string? Name);

public sealed record CompiledWorkflowDefinition(
    WorkflowGoalSpec? Goal,
    IReadOnlyList<WorkflowNodeSpec> Nodes,
    IReadOnlyList<WorkflowEdgeSpec> Edges);

public sealed record WorkflowCompileRequest(
    string Raw,
    IReadOnlyCollection<string> KnownRoles,
    string MetaAgentTypeName = "meta");

public sealed record WorkflowCompileResult(
    bool Ok,
    CompiledWorkflowDefinition? Definition,
    IReadOnlyList<WorkflowValidationError> Errors,
    string? NormalizedJson)
{
    public static WorkflowCompileResult Success(CompiledWorkflowDefinition def, string normalizedJson)
        => new(true, def, Array.Empty<WorkflowValidationError>(), normalizedJson);

    public static WorkflowCompileResult Failed(string normalizedJson, params WorkflowValidationError[] errors)
        => new(false, null, errors ?? Array.Empty<WorkflowValidationError>(), normalizedJson);
}

public interface IWorkflowCompiler
{
    WorkflowCompileResult Compile(WorkflowCompileRequest request);
}

