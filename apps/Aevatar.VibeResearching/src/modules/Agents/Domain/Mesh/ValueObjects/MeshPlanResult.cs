using Aevatar.CognitiveMesh.Dsl.Validation;

namespace Aevatar.VibeResearching.Agents.Mesh.ValueObjects;

/// <summary>
/// Result of planning a mesh execution.
/// </summary>
public sealed record MeshPlanResult(
    bool Ok,
    MeshExecutionPlan? Plan,
    IReadOnlyList<DslValidationError> Errors)
{
    public static MeshPlanResult Success(MeshExecutionPlan plan) =>
        new(true, plan, Array.Empty<DslValidationError>());

    public static MeshPlanResult Failed(params DslValidationError[] errors) =>
        new(false, null, errors ?? Array.Empty<DslValidationError>());
}
