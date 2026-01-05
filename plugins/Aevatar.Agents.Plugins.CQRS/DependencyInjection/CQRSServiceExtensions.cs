using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Core.CQRS;
using Aevatar.Agents.Plugins.CQRS.Batching;
using Aevatar.Agents.Plugins.CQRS.Elasticsearch;
using Aevatar.Agents.Plugins.CQRS.Forwarding;
using Aevatar.Agents.Plugins.CQRS.Logging;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Plugins.CQRS;

/// <summary>
/// Extension methods for configuring CQRS services.
/// </summary>
public static class CQRSServiceExtensions
{
    /// <summary>
    /// Adds CQRS services to the service collection.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddCQRS(
        this IServiceCollection services,
        Action<CQRSOptionsBuilder> configure)
    {
        var builder = new CQRSOptionsBuilder(services);
        configure(builder);
        return services;
    }

    /// <summary>
    /// Adds Elasticsearch state projection with default settings.
    /// </summary>
    public static IServiceCollection AddElasticsearchCQRS(
        this IServiceCollection services,
        string elasticsearchUrl = "http://localhost:9200",
        string indexPrefix = "aevatar-state")
    {
        return services.AddCQRS(options =>
        {
            options.UseElasticsearch(es =>
            {
                es.Url = elasticsearchUrl;
                es.IndexPrefix = indexPrefix;
            });
            options.UseDirectProjection();
        });
    }

    /// <summary>
    /// Adds logging-only state projection (for development/debugging).
    /// </summary>
    public static IServiceCollection AddLoggingCQRS(this IServiceCollection services)
    {
        return services.AddCQRS(options =>
        {
            options.UseLoggingProjection();
        });
    }

    /// <summary>
    /// Adds batched Elasticsearch state projection for high-throughput scenarios.
    /// </summary>
    public static IServiceCollection AddBatchedElasticsearchCQRS(
        this IServiceCollection services,
        string elasticsearchUrl = "http://localhost:9200",
        string indexPrefix = "aevatar-state",
        Action<BatchProjectorOptions>? configureBatch = null)
    {
        return services.AddCQRS(options =>
        {
            options.UseElasticsearch(es =>
            {
                es.Url = elasticsearchUrl;
                es.IndexPrefix = indexPrefix;
            });
            options.UseBatchedProjection(configureBatch);
        });
    }
}

/// <summary>
/// Builder for configuring CQRS options.
/// </summary>
public class CQRSOptionsBuilder
{
    private readonly IServiceCollection _services;
    private bool _elasticsearchConfigured;

    public CQRSOptionsBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Gets whether Elasticsearch has been configured.
    /// </summary>
    public bool IsElasticsearchConfigured => _elasticsearchConfigured;

    /// <summary>
    /// Configures Elasticsearch for state indexing.
    /// </summary>
    public CQRSOptionsBuilder UseElasticsearch(Action<ElasticsearchOptions> configure)
    {
        var options = new ElasticsearchOptions();
        configure(options);

        // Register Elasticsearch client
        _services.AddSingleton(sp =>
        {
            var settings = new ElasticsearchClientSettings(new Uri(options.Url));
            return new ElasticsearchClient(settings);
        });

        // Register options
        _services.AddSingleton(options);

        // Register index service
        _services.AddSingleton<IStateIndexService, ElasticsearchStateIndexService>();

        // Register query facade (framework-level) once ES query is available.
        // Callers (HTTP/tools) should depend on IStateQueryService rather than IStateIndexService directly.
        _services.TryAddSingleton<IStateQueryService, StateQueryService>();

        _elasticsearchConfigured = true;
        return this;
    }

    /// <summary>
    /// Configures direct projection to Elasticsearch.
    /// State changes are written directly to ES without going through a stream.
    /// </summary>
    public CQRSOptionsBuilder UseDirectProjection()
    {
        if (!_elasticsearchConfigured)
        {
            throw new InvalidOperationException(
                "Elasticsearch must be configured before using direct projection. " +
                "Call UseElasticsearch() first.");
        }

        _services.AddSingleton<IStateProjector, ElasticsearchStateProjector>();
        return this;
    }

    /// <summary>
    /// Configures batched projection to Elasticsearch.
    /// State changes are accumulated and flushed in batches for better throughput.
    /// Optimized for high-volume scenarios.
    /// </summary>
    /// <param name="configure">Optional batch configuration action</param>
    public CQRSOptionsBuilder UseBatchedProjection(Action<BatchProjectorOptions>? configure = null)
    {
        if (!_elasticsearchConfigured)
        {
            throw new InvalidOperationException(
                "Elasticsearch must be configured before using batched projection. " +
                "Call UseElasticsearch() first.");
        }

        // Configure batch options
        var options = new BatchProjectorOptions();
        configure?.Invoke(options);

        // Register options for IOptions<T> pattern
        _services.Configure<BatchProjectorOptions>(o =>
        {
            o.BatchSize = options.BatchSize;
            o.BatchTimeoutSeconds = options.BatchTimeoutSeconds;
            o.MaxBatchSize = options.MaxBatchSize;
            o.MinBatchSize = options.MinBatchSize;
            o.HighMemoryThreshold = options.HighMemoryThreshold;
            o.MaxRetryCount = options.MaxRetryCount;
            o.RetryBaseDelaySeconds = options.RetryBaseDelaySeconds;
            o.MaxRetryDelaySeconds = options.MaxRetryDelaySeconds;
            o.FlushMinPeriodInMs = options.FlushMinPeriodInMs;
        });

        // Register batched projector
        _services.AddSingleton<IStateProjector, BatchedStateProjector>();
        return this;
    }

    /// <summary>
    /// Configures stream forwarding projection.
    /// State changes are forwarded to a message stream (determined by IMessageStreamProvider).
    /// The actual stream implementation (Orleans/MassTransit) depends on Silo configuration.
    /// </summary>
    public CQRSOptionsBuilder UseStreamForwarding()
    {
        _services.AddSingleton<IStateProjector, StreamForwardingProjector>();
        return this;
    }

    /// <summary>
    /// Configures logging-only projection for development/debugging.
    /// </summary>
    public CQRSOptionsBuilder UseLoggingProjection()
    {
        _services.AddSingleton<IStateProjector, LoggingStateProjector>();
        return this;
    }

    /// <summary>
    /// Configures composite projection with multiple projectors.
    /// </summary>
    public CQRSOptionsBuilder UseCompositeProjection(params Type[] projectorTypes)
    {
        // Register individual projectors
        foreach (var type in projectorTypes)
        {
            if (!typeof(IStateProjector).IsAssignableFrom(type))
            {
                throw new ArgumentException(
                    $"Type {type.Name} does not implement IStateProjector");
            }

            _services.AddSingleton(type);
        }

        // Register composite projector
        _services.AddSingleton<IStateProjector>(sp =>
        {
            var projectors = projectorTypes.Select(t => (IStateProjector)sp.GetRequiredService(t));
            var logger = sp.GetRequiredService<ILogger<CompositeStateProjector>>();
            return new CompositeStateProjector(projectors, logger);
        });

        return this;
    }

    /// <summary>
    /// Adds a custom projector type.
    /// </summary>
    public CQRSOptionsBuilder AddProjector<T>() where T : class, IStateProjector
    {
        _services.AddSingleton<IStateProjector, T>();
        return this;
    }

    /// <summary>
    /// Adds a custom projector instance.
    /// </summary>
    public CQRSOptionsBuilder AddProjector(IStateProjector projector)
    {
        _services.AddSingleton(projector);
        return this;
    }
}

