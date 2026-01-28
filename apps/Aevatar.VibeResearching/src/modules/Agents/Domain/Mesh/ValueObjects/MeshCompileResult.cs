using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Validation;

namespace Aevatar.VibeResearching.Agents.Mesh.ValueObjects;

/// <summary>
/// Result of compiling a mesh definition from DSL.
/// </summary>
public sealed record MeshCompileResult(
    bool Ok,
    MeshDefinition? Definition,
    IReadOnlyList<DslValidationError> Errors)
{
    public static MeshCompileResult Success(MeshDefinition def) =>
        new(true, def, Array.Empty<DslValidationError>());

    public static MeshCompileResult Failed(params DslValidationError[] errors) =>
        new(false, null, errors ?? Array.Empty<DslValidationError>());
}
