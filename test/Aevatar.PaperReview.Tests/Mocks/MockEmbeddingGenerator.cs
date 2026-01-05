using Microsoft.Extensions.AI;

namespace Aevatar.PaperReview.Tests.Mocks;

// ============================================================
//  MOCK EMBEDDING GENERATOR
//  让所有内容返回相同的 embedding，从而保证语义聚类总是达成共识
// ============================================================

/// <summary>
/// Mock Embedding Generator - 返回相同的 embedding 向量，
/// 这样 VoteEngine 的语义聚类会将所有响应视为相同，第一次就达成共识。
/// </summary>
public sealed class MockEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly float[] _fixedEmbedding;
    private int _callCount;

    public int CallCount => _callCount;

    public MockEmbeddingGenerator(int dimensions = 1536)
    {
        // 创建一个固定的 embedding 向量（全 0.1）
        _fixedEmbedding = new float[dimensions];
        Array.Fill(_fixedEmbedding, 0.1f);
    }

    public EmbeddingGeneratorMetadata Metadata => new("mock-embedding");

    public TService? GetService<TService>(object? key = null) where TService : class
        => this as TService;

    public object? GetService(Type serviceType, object? key = null)
        => serviceType.IsAssignableFrom(GetType()) ? this : null;

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        
        // 模拟一点延迟
        await Task.Delay(1, cancellationToken);

        var embeddings = values.Select(v => new Embedding<float>(_fixedEmbedding)).ToList();
        
        return new GeneratedEmbeddings<Embedding<float>>(embeddings)
        {
            Usage = new UsageDetails
            {
                InputTokenCount = values.Sum(v => v.Length / 4),
                TotalTokenCount = values.Sum(v => v.Length / 4)
            }
        };
    }

    public void Dispose() { }
}
