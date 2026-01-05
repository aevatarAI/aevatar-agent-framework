using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;

namespace Aevatar.PaperReview.Tests.Mocks;

// ============================================================
//  MOCK LLM PROVIDER FACTORY
//  用于测试的 LLM 工厂
// ============================================================

/// <summary>
/// Mock LLM Provider Factory - 返回 MockLLMProvider。
/// </summary>
public sealed class MockLLMProviderFactory : ILLMProviderFactory
{
    private readonly MockLLMProvider _provider;

    public MockLLMProviderFactory(MockLLMProvider provider)
    {
        _provider = provider;
    }

    public IAevatarLLMProvider GetProvider(string providerName) => _provider;

    public IAevatarLLMProvider GetDefaultProvider() => _provider;

    public IReadOnlyList<string> GetAvailableProviderNames() => ["mock"];

    public bool HasProvider(string providerName) => true;

    public LLMProviderConfig GetProviderConfig(string providerName) => new()
    {
        Name = "mock",
        Model = "mock-model",
        Endpoint = "http://localhost",
        ApiKey = "mock-key"
    };

    public LLMProviderConfig GetDefaultProviderConfig() => GetProviderConfig("mock");

    public IAevatarLLMProvider CreateProvider(LLMProviderConfig providerConfig, CancellationToken cancellationToken = default)
        => _provider;

    public Task<IAevatarLLMProvider> GetProviderAsync(string providerName, CancellationToken cancellationToken = default)
        => Task.FromResult<IAevatarLLMProvider>(_provider);

    public Task<IAevatarLLMProvider> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IAevatarLLMProvider>(_provider);
}

