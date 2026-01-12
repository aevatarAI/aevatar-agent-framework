using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;

namespace AgenticRagDemo;

// ============================================================
//  Noop LLM Provider (Demo-only)
//
//  中文 + ASCII:
//  - 为了让 AIGAgentBase.InitializeAsync 走通，我们提供一个无需网络/密钥的 Noop Provider。
//  - AgenticRagGAgent 的默认策略在 demo 中不调用 LLM，因此这里不会影响输出。
// ============================================================

internal sealed class NoopLLMProvider : IAevatarLLMProvider
{
    public Task<AevatarLLMResponse> GenerateAsync(AevatarLLMRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AevatarLLMResponse
        {
            Content = "noop",
            Usage = new AevatarTokenUsage { PromptTokens = 0, CompletionTokens = 0, TotalTokens = 0 }
        });
    }

    public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}

internal sealed class NoopLLMProviderFactory : ILLMProviderFactory
{
    private readonly NoopLLMProvider _provider = new();
    private readonly LLMProviderConfig _config = new()
    {
        Name = "noop",
        ProviderType = "noop",
        Model = "noop"
    };

    public IAevatarLLMProvider GetProvider(string providerName) => _provider;
    public IAevatarLLMProvider GetDefaultProvider() => _provider;
    public IReadOnlyList<string> GetAvailableProviderNames() => new[] { "noop" };
    public bool HasProvider(string providerName) => true;
    public LLMProviderConfig GetProviderConfig(string providerName) => _config;
    public LLMProviderConfig GetDefaultProviderConfig() => _config;
    public IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig, CancellationToken cancellationToken = default) => _provider;
    public Task<IAevatarLLMProvider> GetProviderAsync(string providerName, CancellationToken cancellationToken = default) => Task.FromResult<IAevatarLLMProvider>(_provider);
    public Task<IAevatarLLMProvider> GetDefaultProviderAsync(CancellationToken cancellationToken = default) => Task.FromResult<IAevatarLLMProvider>(_provider);
}


