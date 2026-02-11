namespace SisyphusMaker.Services;

using SisyphusMaker.Dtos;

/// <summary>
/// Orchestrates generic verification: renders prompt template variables,
/// invokes CognitiveStrategy maker workflow, parses LLM results.
/// </summary>
public interface IVerificationService
{
    /// <summary>
    /// Executes a generic verification using the provided request.
    /// </summary>
    Task<VerificationResponse> VerifyAsync(
        VerifyRequest request,
        Func<SseEvent, Task> onProgress,
        CancellationToken ct);
}
