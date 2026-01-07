using System.Collections.Generic;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Embeddings;
using Microsoft.Extensions.AI;

namespace Aevatar.Agents.AI.Abstractions.Tests.Embedding;

public class MockEmbeddingFactory : IAIAgentEmbeddingFactory
{
    public Task<IEmbeddingGenerator<string, Embedding<float>>?> CreateAsync(
        LLMProviderConfig providerConfig,
        CancellationToken cancellationToken = default)
    {
        if (providerConfig.Embeddings == null)
            return Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(null);

        return Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(new MockEmbeddingGenerator());
    }

    private sealed class MockEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> inputs,
            EmbeddingGenerationOptions? options,
            CancellationToken cancellationToken)
        {
            var result = new GeneratedEmbeddings<Embedding<float>>();

            foreach (var input in inputs)
            {
                var hash = input?.Length ?? 0;
                var vector = new float[]
                {
                    hash,
                    hash % 10,
                    hash % 5
                };

                result.Add(new Embedding<float>(new ReadOnlyMemory<float>(vector)));
            }

            return Task.FromResult(result);
        }

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey)
        {
            return null;
        }
    }
}

