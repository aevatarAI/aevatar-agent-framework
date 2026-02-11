namespace SisyphusMaker.Dtos;

/// <summary>
/// Per-request configuration overrides for the Maker consensus engine.
/// Null fields fall back to appsettings defaults.
/// </summary>
public sealed record MakerConfig
{
    /// <summary>Number of parallel LLM workers (1-20).</summary>
    public int? WorkerCount { get; init; }

    /// <summary>Ahead-by-K consensus threshold (1 to workerCount).</summary>
    public int? ConsensusK { get; init; }

    /// <summary>Maximum voting rounds (1-20).</summary>
    public int? MaxRounds { get; init; }

    /// <summary>Per-request timeout in seconds (10-600).</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>LLM model override. Null uses appsettings default.</summary>
    public string? Model { get; init; }
}
