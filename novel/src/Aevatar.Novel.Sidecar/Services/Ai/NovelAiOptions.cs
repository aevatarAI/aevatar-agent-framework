namespace Aevatar.Novel.Sidecar.Services.Ai;

// ============================================================
//  NovelAiOptions
//
//  PURPOSE:
//  - Configure Smart Writing (LLM) for the sidecar.
//
//  NOTES:
//  - This is local-only configuration (not a cross-boundary type).
//  - Secrets (API keys) should come from env vars or secrets store.
// ============================================================

public sealed class NovelAiOptions
{
    public const string SectionName = "NovelAI";

    /// <summary>
    /// OpenAI-compatible endpoint (optional). Examples:
    /// - OpenAI:     https://api.openai.com/v1
    /// - DeepSeek:   https://api.deepseek.com
    /// - Ollama:     http://localhost:11434/v1 (OpenAI-compatible gateway)
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API key for the endpoint (required for hosted providers).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Model id (OpenAI-compatible). Example: "deepseek-chat", "deepseek-reasoner", "gpt-4o-mini".
    /// </summary>
    public string Model { get; set; } = "deepseek-chat";

    /// <summary>
    /// Generation temperature (0-2).
    /// </summary>
    public double Temperature { get; set; } = 0.8;

    /// <summary>
    /// Max output tokens for one continuation.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 800;

    /// <summary>
    /// Network timeout (milliseconds).
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 120_000;

    /// <summary>
    /// Default context budget (characters, best-effort).
    /// </summary>
    public int DefaultMaxContextChars { get; set; } = 60_000;

    /// <summary>
    /// Default output length hint (characters, best-effort).
    /// </summary>
    public int DefaultMaxOutputChars { get; set; } = 500;
}


