using Microsoft.Extensions.Logging;
using Aevatar.CognitiveMesh.Dsl;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Options;
using Aevatar.CognitiveMesh.Dsl.Validation;
using Aevatar.Agents.AI.Core.Configuration;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace Aevatar.VibeResearching.Agents.Mesh.Services;

// ============================================================
//  MeshCompilerService
//
//  Purpose:
//  - Compile + validate MeshDefinition JSON using CognitiveDslCompiler.
//  - Enforce SRA-specific allowlists for node types and constraint types.
//  - Return structured errors (do not throw to callers).
// ============================================================

public sealed record MeshCompileResult(bool Ok, MeshDefinition? Definition, IReadOnlyList<DslValidationError> Errors)
{
    public static MeshCompileResult Success(MeshDefinition def) => new(true, def, Array.Empty<DslValidationError>());
    public static MeshCompileResult Failed(params DslValidationError[] errors) => new(false, null, errors ?? Array.Empty<DslValidationError>());
}

public sealed class MeshCompilerService : IMeshCompilerService
{
    private readonly ILogger<MeshCompilerService> _logger;
    private readonly GlobalAgentYamlRegistry _roles;

    public MeshCompilerService(ILogger<MeshCompilerService> logger, GlobalAgentYamlRegistry roles)
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
                Message: "mesh 内容为空。",
                Path: null));
        }

        // Configure compiler guardrails for SRA mesh orchestration.
        // NOTE:
        // - CognitiveDslCompiler enforces allowedAgentTypes. We extend it with user-defined roles discovered
        //   from ~/.aevatar/agents/*.yaml (cross-app convention).
        var options = CognitiveDslOptions.Default.With(
            allowedAgentTypes: MergeAllowedNodeTypesWithGlobalYamlRoles(),
            allowedConstraintTypes: SraMeshMappings.AllowedConstraintTypes,
            metaAgentTypeName: "meta"); // unused for SRA; keep placeholder stable

        var compiler = new CognitiveDslCompiler(options);

        string json;
        try
        {
            json = MeshInputCoercer.CoerceToJson(raw).Json;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mesh coercion failed (yaml->json).");
            return MeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.coerce_failed",
                Message: ex.Message,
                Path: null));
        }

        try
        {
            var def = compiler.Compile(json);
            return MeshCompileResult.Success(def);
        }
        catch (DslCompilationException ex)
        {
            // Structured errors from compiler.
            var errors = ex.Errors?.ToArray() ?? Array.Empty<DslValidationError>();
            if (errors.Length == 0)
            {
                var preview = json.Length <= 240 ? json : json[..240];
                errors =
                [
                    new DslValidationError("mesh.compile_failed", ex.Message, null),
                    new DslValidationError("mesh.compile_input_preview", preview, null)
                ];
            }

            return new MeshCompileResult(false, null, errors);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mesh compile failed (unexpected).");
            return MeshCompileResult.Failed(new DslValidationError(
                Code: "mesh.compile_exception",
                Message: ex.Message,
                Path: null));
        }
    }

    private IReadOnlySet<string> MergeAllowedNodeTypesWithGlobalYamlRoles()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var x in SraMeshMappings.AllowedNodeTypes)
            set.Add(x);

        foreach (var x in _roles.GetKnownRoles())
            set.Add(x);

        // Keep placeholder stable for DSL option (even if unused by SRA).
        set.Add("meta");

        return set;
    }
}


