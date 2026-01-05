using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Embeddings;
using Microsoft.Extensions.AI;

namespace Aevatar.PaperReview.Tests.Mocks;

// ============================================================
//  MOCK EMBEDDING FACTORY
//  用于测试的 Embedding 工厂，返回 MockEmbeddingGenerator
// ============================================================

/// <summary>
/// Mock Embedding Factory - 返回让所有内容语义相同的 MockEmbeddingGenerator。
/// </summary>
public sealed class MockEmbeddingFactory : IAIAgentEmbeddingFactory
{
    private readonly MockEmbeddingGenerator _generator;

    public MockEmbeddingFactory()
    {
        _generator = new MockEmbeddingGenerator();
    }

    public MockEmbeddingGenerator Generator => _generator;

    public Task<IEmbeddingGenerator<string, Embedding<float>>?> CreateAsync(
        LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(_generator);
    }
}
