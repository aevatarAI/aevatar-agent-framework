using Aevatar.Agents.Knowledge.Graph.Storage;
using Aevatar.Agents.Knowledge.Graph.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Minio;

namespace Aevatar.Agents.Knowledge.Graph;

/// <summary>
/// Extension methods for registering knowledge graph services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the knowledge graph services with optional S3 storage.
    /// Requires an IGraphClient to already be registered (e.g., via AddAevatarGraphInMemory or AddAevatarGraphNeo4j).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // Setup with in-memory storage (no S3)
    /// services.AddAevatarGraphInMemory();
    /// services.AddKnowledgeGraph();
    ///
    /// // Or with S3 storage
    /// services.AddAevatarGraphInMemory();
    /// services.AddKnowledgeGraph(options =>
    /// {
    ///     options.Endpoint = "play.min.io";
    ///     options.AccessKey = "your-access-key";
    ///     options.SecretKey = "your-secret-key";
    ///     options.BucketName = "knowledge-graph";
    /// });
    ///
    /// // Usage - create session-scoped clients
    /// var factory = sp.GetRequiredService&lt;IKnowledgeGraphClientFactory&gt;();
    /// var client = factory.CreateClient("session-123");
    /// await client.AddNodeAsync("axiom-1", KnowledgeNodeType.MathAxiom,
    ///     "Base case axiom", "The foundational axiom for the proof.");
    /// </code>
    /// </example>
    public static IServiceCollection AddKnowledgeGraph(this IServiceCollection services)
    {
        // Use NullFileStorage by default (no S3)
        services.TryAddSingleton<IFileStorage, NullFileStorage>();
        services.TryAddSingleton<IKnowledgeGraphStore, GraphClientBackedStore>();
        services.TryAddSingleton<IKnowledgeGraphClientFactory, KnowledgeGraphClientFactory>();
        return services;
    }

    /// <summary>
    /// Adds the knowledge graph services with S3 storage configuration.
    /// Requires an IGraphClient to already be registered (e.g., via AddAevatarGraphInMemory or AddAevatarGraphNeo4j).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure S3 storage options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKnowledgeGraph(
        this IServiceCollection services,
        Action<S3StorageOptions> configureOptions)
    {
        var options = new S3StorageOptions();
        configureOptions(options);

        // Register options
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        // Register Minio client
        services.AddSingleton<IMinioClient>(_ =>
        {
            var builder = new MinioClient()
                .WithEndpoint(options.Endpoint)
                .WithCredentials(options.AccessKey, options.SecretKey);

            if (!string.IsNullOrWhiteSpace(options.Region))
            {
                builder = builder.WithRegion(options.Region);
            }

            if (options.UseSsl)
            {
                builder = builder.WithSSL();
            }

            return builder.Build();
        });

        // Register logger if not already registered
        services.TryAddSingleton<ILogger<S3FileStorage>>(_ => NullLogger<S3FileStorage>.Instance);

        services.AddSingleton<IFileStorage, S3FileStorage>();
        services.TryAddSingleton<IKnowledgeGraphStore, GraphClientBackedStore>();
        services.TryAddSingleton<IKnowledgeGraphClientFactory, KnowledgeGraphClientFactory>();
        return services;
    }
}
