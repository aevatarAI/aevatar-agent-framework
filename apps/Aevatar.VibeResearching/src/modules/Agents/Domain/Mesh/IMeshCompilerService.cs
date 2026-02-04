using Aevatar.VibeResearching.Agents.Mesh.Services;

namespace Aevatar.VibeResearching.Agents.Mesh;

/// <summary>
/// Domain interface for mesh compiler service.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface IMeshCompilerService
{
    /// <summary>
    /// Compiles mesh definition from raw DSL string.
    /// </summary>
    MeshCompileResult Compile(string dsl);
}
