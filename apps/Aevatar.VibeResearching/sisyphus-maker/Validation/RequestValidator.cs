namespace SisyphusMaker.Validation;

using SisyphusMaker.Dtos;

/// <summary>
/// Centralized request validation. Returns null if valid, or an error message string.
/// </summary>
public static class RequestValidator
{
    public static string? Validate(VerifyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt.SystemPrompt))
            return "prompt.systemPrompt must not be empty.";

        if (string.IsNullOrWhiteSpace(request.Prompt.UserPromptTemplate))
            return "prompt.userPromptTemplate must not be empty.";

        return ValidateConfig(request.Config);
    }

    public static string? ValidateConfig(MakerConfig? config)
    {
        if (config is null) return null;

        if (config.WorkerCount.HasValue && (config.WorkerCount < 1 || config.WorkerCount > 20))
            return "config.workerCount must be between 1 and 20.";

        if (config.ConsensusK.HasValue && config.ConsensusK < 1)
            return "config.consensusK must be >= 1.";

        if (config.ConsensusK.HasValue && config.WorkerCount.HasValue
            && config.ConsensusK > config.WorkerCount)
            return "config.consensusK must not exceed config.workerCount.";

        if (config.MaxRounds.HasValue && (config.MaxRounds < 1 || config.MaxRounds > 20))
            return "config.maxRounds must be between 1 and 20.";

        if (config.TimeoutSeconds.HasValue && (config.TimeoutSeconds < 10 || config.TimeoutSeconds > 600))
            return "config.timeoutSeconds must be between 10 and 600.";

        if (config.Model is not null && string.IsNullOrWhiteSpace(config.Model))
            return "config.model must not be an empty string.";

        return null;
    }
}
