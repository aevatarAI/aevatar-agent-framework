using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;

namespace ClaudeAgentSdkProviderDemo;

/// <summary>
/// A tiny offline baseline provider for comparison.
///
/// Design intent:
/// - In-process (no runner)
/// - Deterministic output
/// - Does NOT read `.claude/*` or load plugins
/// - Does NOT implement function calling (AevatarFunctionCall always null)
/// </summary>
public sealed class BaselineDeterministicProvider : IAevatarLLMProvider
{
    public Task<AevatarLLMResponse> GenerateAsync(AevatarLLMRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lastUser = request.Messages
            .LastOrDefault(m => m.Role == AevatarChatRole.User)?.Content ?? request.UserPrompt;

        var content =
            "== Baseline Provider (in-process, deterministic) ==\n" +
            "This provider does NOT read .claude, does NOT load plugins, and has no runner-side permission system.\n" +
            $"systemPrompt={(string.IsNullOrWhiteSpace(request.SystemPrompt) ? "(none)" : "(set)")}\n" +
            $"lastUser={lastUser}\n" +
            $"functionsProvided={(request.Functions is { Count: > 0 } ? "yes" : "no")}\n";

        return Task.FromResult(new AevatarLLMResponse
        {
            Content = content,
            ModelName = request.Settings?.ModelId ?? "baseline",
            AevatarStopReason = AevatarStopReason.Complete,
            AevatarFunctionCall = null
        });
    }

    public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resp = await GenerateAsync(request, cancellationToken);
        yield return new AevatarLLMToken
        {
            Index = 0,
            Content = resp.Content,
            IsComplete = false,
            AevatarFunctionCall = null
        };

        yield return new AevatarLLMToken
        {
            Index = 1,
            Content = string.Empty,
            IsComplete = true,
            AevatarFunctionCall = null
        };
    }
}


