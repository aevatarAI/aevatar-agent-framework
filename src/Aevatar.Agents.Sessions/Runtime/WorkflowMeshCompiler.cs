using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.CognitiveMesh.Dsl;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Options;
using Aevatar.CognitiveMesh.Dsl.Validation;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Sessions.Runtime;

public sealed record MeshCompileResult(
    bool Ok,
    MeshDefinition? Definition,
    IReadOnlyList<DslValidationError> Errors,
    string? NormalizedJson)
{
    public static MeshCompileResult Success(MeshDefinition def, string json) => new(true, def, Array.Empty<DslValidationError>(), json);
    public static MeshCompileResult Failed(params DslValidationError[] errors) => new(false, null, errors ?? Array.Empty<DslValidationError>(), null);
}

public sealed class WorkflowMeshCompiler
{
    private readonly ILogger<WorkflowMeshCompiler> _logger;
    private readonly GlobalAgentYamlRegistry _roles;

    public WorkflowMeshCompiler(ILogger<WorkflowMeshCompiler> logger, GlobalAgentYamlRegistry roles)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    public MeshCompileResult Compile(string raw)
    {
        raw = (raw ?? string.Empty).Replace("\r", "").Trim();
        if (raw.Length == 0)
        {
            return MeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.empty",
                Message: "workflow YAML 为空。",
                Path: null));
        }

        string json;
        IReadOnlySet<string> nodeTypes;
        try
        {
            (json, nodeTypes) = MeshInputCoercer.CoerceToJson(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow YAML -> JSON failed.");
            return MeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.coerce_failed",
                Message: ex.Message,
                Path: null));
        }

        var options = CognitiveDslOptions.Default.With(
            allowedAgentTypes: MergeAllowedAgentTypes(nodeTypes),
            allowedConstraintTypes: CognitiveDslOptions.Default.AllowedConstraintTypes,
            metaAgentTypeName: "meta");

        var compiler = new CognitiveDslCompiler(options);
        try
        {
            var def = compiler.Compile(json);
            return MeshCompileResult.Success(def, json);
        }
        catch (DslCompilationException ex)
        {
            var errors = ex.Errors?.ToArray() ?? Array.Empty<DslValidationError>();
            if (errors.Length == 0)
            {
                errors =
                [
                    new DslValidationError("mesh.compile_failed", ex.Message, null)
                ];
            }

            return new MeshCompileResult(false, null, errors, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow compile failed.");
            return MeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.compile_exception",
                Message: ex.Message,
                Path: null));
        }
    }

    private IReadOnlySet<string> MergeAllowedAgentTypes(IReadOnlySet<string> nodeTypes)
    {
        var set = new HashSet<string>(CognitiveDslOptions.Default.AllowedAgentTypes, StringComparer.OrdinalIgnoreCase);
        foreach (var role in _roles.GetKnownRoles())
            set.Add(role);
        foreach (var t in nodeTypes)
            set.Add(t);
        set.Add("meta");
        return set;
    }
}
