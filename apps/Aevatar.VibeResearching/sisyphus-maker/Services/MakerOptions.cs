namespace SisyphusMaker.Services;

/// <summary>
/// Default configuration for the Maker consensus engine, bound from appsettings "Maker" section.
/// LLM provider configuration is handled by the framework's LLMProviders section.
/// </summary>
public sealed class MakerOptions
{
    /// <summary>Number of parallel LLM workers per round.</summary>
    public int WorkerCount { get; set; } = 3;

    /// <summary>Ahead-by-K threshold for consensus.</summary>
    public int ConsensusK { get; set; } = 2;

    /// <summary>Maximum voting rounds before best-effort.</summary>
    public int MaxRounds { get; set; } = 5;

    /// <summary>Per-request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
