namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Domain interface for skill packs sync service.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface ISkillPacksSyncService
{
    /// <summary>
    /// Whether any enabled packs are configured.
    /// </summary>
    bool HasEnabledPacks { get; }

    /// <summary>
    /// Whether the last sync attempt succeeded.
    /// </summary>
    bool LastSyncOk { get; }

    /// <summary>
    /// Timestamp of the last sync attempt (UTC).
    /// </summary>
    DateTimeOffset LastAttemptUtc { get; }

    /// <summary>
    /// Result of the last sync attempt.
    /// </summary>
    SkillPacksSyncResult? LastResult { get; }

    /// <summary>
    /// Tries to ensure skill packs are synced.
    /// </summary>
    Task<SkillPacksSyncResult> TryEnsureSyncedAsync(SkillPackSyncMode mode, CancellationToken ct);
}

/// <summary>
/// Skill pack sync mode.
/// </summary>
public enum SkillPackSyncMode
{
    Startup,
    Manual
}

/// <summary>
/// Result of skill packs sync operation.
/// </summary>
public sealed class SkillPacksSyncResult
{
    public bool Ok { get; init; }
    public List<SkillPackSyncEntry> Packs { get; init; } = new();
    public string? Error { get; init; }
}

/// <summary>
/// Result of a single skill pack sync operation.
/// </summary>
public sealed class SkillPackSyncEntry
{
    public required string Name { get; init; }
    public required string RepoUrl { get; init; }
    public required string Ref { get; init; }
    public required string RepoDir { get; init; }
    public required string SkillsRoot { get; init; }
    public string? Commit { get; init; }
    public bool? EmbeddingsIndexOk { get; init; }
    public string? EmbeddingsIndexFile { get; init; }
    public string? EmbeddingsIndexError { get; init; }
    public bool Ok { get; init; }
    public string? Error { get; init; }
}
