using Microsoft.Extensions.Logging;
namespace Aevatar.VibeResearching.Agents.Mesh;

/// <summary>
/// Store interface for mesh definition operations.
/// Mesh definitions describe multi-agent coordination topologies.
/// </summary>
public interface IMeshDefinitionStore
{
    /// <summary>
    /// Gets the file path for the mesh definition.
    /// </summary>
    string GetMeshPath(string sessionId, string? format);

    /// <summary>
    /// Tries to load the raw mesh definition text and its format.
    /// Returns (raw content, format) or (null, null) if not found.
    /// </summary>
    Task<(string? Raw, string? Format)> TryLoadRawAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Saves the raw mesh definition.
    /// Supported formats: "yaml" or "json".
    /// </summary>
    Task SaveAsync(string sessionId, string raw, string? format, CancellationToken ct);
}
