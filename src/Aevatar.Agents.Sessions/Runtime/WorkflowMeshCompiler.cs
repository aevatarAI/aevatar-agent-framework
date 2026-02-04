using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.Sessions.Abstractions.Workflows;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Sessions.Runtime;

public sealed record MeshCompileResult(
    bool Ok,
    CompiledWorkflowDefinition? Definition,
    IReadOnlyList<WorkflowValidationError> Errors,
    string? NormalizedJson)
{
    public static MeshCompileResult Success(CompiledWorkflowDefinition def, string json)
        => new(true, def, Array.Empty<WorkflowValidationError>(), json);

    public static MeshCompileResult Failed(params WorkflowValidationError[] errors)
        => new(false, null, errors ?? Array.Empty<WorkflowValidationError>(), null);
}

public sealed class WorkflowMeshCompiler
{
    private readonly ILogger<WorkflowMeshCompiler> _logger;
    private readonly GlobalAgentYamlRegistry _roles;
    private readonly IWorkflowCompiler _compiler;

    public WorkflowMeshCompiler(
        ILogger<WorkflowMeshCompiler> logger,
        GlobalAgentYamlRegistry roles,
        IWorkflowCompiler compiler)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    }

    public MeshCompileResult Compile(string raw)
    {
        var result = _compiler.Compile(new WorkflowCompileRequest(
            Raw: raw,
            KnownRoles: _roles.GetKnownRoles()));

        if (!result.Ok || result.Definition == null)
            return new MeshCompileResult(false, null, result.Errors, result.NormalizedJson);

        return new MeshCompileResult(true, result.Definition, Array.Empty<WorkflowValidationError>(), result.NormalizedJson);
    }
}
