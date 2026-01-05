using System.Linq;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventRouting;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Core.MemoryGraphs;
using Aevatar.Agents.Core.Memory;
using Aevatar.Agents.Core.DependencyInjection;
using Aevatar.Agents.Core.EventRouting;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Core.Persistence;
using Aevatar.Agents.Core.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Extensions;

/// <summary>
/// ServiceCollection extensions for GAgent configuration
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core services required by the Aevatar Agent framework and exposes a fluent builder.
    /// </summary>
    public static IAevatarBuilder AddAevatarAgentSystem(
        this IServiceCollection services,
        Action<IAevatarBuilder>? configure = null)
    {
        return AddAevatarAgentSystem(services, configureStores: null, configure);
    }

    /// <summary>
    /// Registers the core services required by the Aevatar Agent framework and exposes a fluent builder.
    /// Allows overriding store implementations through <see cref="GAgentOptions"/>.
    /// </summary>
    public static IAevatarBuilder AddAevatarAgentSystem(
        this IServiceCollection services,
        Action<GAgentOptions>? configureStores,
        Action<IAevatarBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new GAgentOptions();
        configureStores?.Invoke(options);

        services.TryAddSingleton<IGAgentManager, GAgentManager>();
        services.TryAddSingleton<IGAgentActorFactoryProvider, DefaultGAgentActorFactoryProvider>();

        RegisterStateStore(services, options);
        RegisterConfigStore(services, options);
        RegisterEventStore(services, options);
        RegisterEventRouterStore(services, options);
        RegisterExecutionTraceStore(services, options);
        RegisterMemoryStore(services, options);
        RegisterMemoryVectorIndex(services, options);
        RegisterMemoryGraphStore(services, options);

        var builder = new AevatarBuilder(services);
        configure?.Invoke(builder);

        return builder;
    }

    private static void RegisterStateStore(IServiceCollection services, GAgentOptions options)
    {
        if (options.StateStoreType != null)
        {
            EnsureOpenGeneric(nameof(options.StateStoreType), typeof(IStateStore<>), options.StateStoreType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IStateStore<>), options.StateStoreType));
        }
        else
        {
            services.TryAddSingleton(typeof(IStateStore<>), typeof(InMemoryStateStore<>));
        }
    }

    private static void RegisterConfigStore(IServiceCollection services, GAgentOptions options)
    {
        if (options.ConfigStoreType != null)
        {
            EnsureOpenGeneric(nameof(options.ConfigStoreType), typeof(IConfigStore<>), options.ConfigStoreType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IConfigStore<>), options.ConfigStoreType));
        }
        else
        {
            services.TryAddSingleton(typeof(IConfigStore<>), typeof(InMemoryConfigStore<>));
        }
    }

    private static void RegisterEventStore(IServiceCollection services, GAgentOptions options)
    {
        if (options.EventStoreType != null)
        {
            EnsureConcrete(nameof(options.EventStoreType), typeof(IEventStore), options.EventStoreType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IEventStore), options.EventStoreType));
        }
        else
        {
            services.TryAddSingleton<IEventStore, InMemoryEventStore>();
        }
    }

    private static void RegisterEventRouterStore(IServiceCollection services, GAgentOptions options)
    {
        if (options.EventRouterStoreType != null)
        {
            EnsureConcrete(nameof(options.EventRouterStoreType), typeof(IEventRouterStore),
                options.EventRouterStoreType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IEventRouterStore), options.EventRouterStoreType));
        }
        else
        {
            services.TryAddSingleton<IEventRouterStore, InMemoryEventRouterStore>();
        }
    }

    private static void RegisterExecutionTraceStore(IServiceCollection services, GAgentOptions options)
    {
        if (options.ExecutionTraceStoreType != null)
        {
            EnsureConcrete(nameof(options.ExecutionTraceStoreType), typeof(IExecutionTraceStore),
                options.ExecutionTraceStoreType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IExecutionTraceStore), options.ExecutionTraceStoreType));
            return;
        }

        // Default: file output.
        // - If AEVATAR_TRACE_DIR is set: use it.
        // - Otherwise: fall back to "<repoRoot>/trace" (best-effort).
        services.TryAddSingleton<IExecutionTraceStore>(sp =>
        {
            IExecutionTraceStore baseStore;
            try
            {
                var traceRoot = FileExecutionTraceStore.GetTraceRootFromEnvironmentOrDefault();
                baseStore = new FileExecutionTraceStore(traceRoot);
            }
            catch
            {
                baseStore = NullExecutionTraceStore.Instance;
            }

            // Decorate with projection (best-effort) so trace bundles automatically produce
            // execution-scoped MemoryGraph + MemoryEntry artifacts.
            try
            {
                // If base store is null/no-op, don't project (avoid "ghost" memory without trace bundle).
                if (baseStore is NullExecutionTraceStore)
                    return baseStore;

                var memoryStore = sp.GetService<IMemoryStore>() ?? NullMemoryStore.Instance;
                var graphStore = sp.GetService<IMemoryGraphStore>() ?? NullMemoryGraphStore.Instance;
                var logger = sp.GetService<ILogger<ProjectingExecutionTraceStore>>();

                return new ProjectingExecutionTraceStore(baseStore, memoryStore, graphStore, logger);
            }
            catch
            {
                return baseStore;
            }
        });
    }

    private static void RegisterMemoryStore(IServiceCollection services, GAgentOptions options)
    {
        if (options.MemoryStoreType != null)
        {
            EnsureConcrete(nameof(options.MemoryStoreType), typeof(IMemoryStore), options.MemoryStoreType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IMemoryStore), options.MemoryStoreType));
            return;
        }

        // Default: file output (best-effort).
        // - If AEVATAR_MEMORY_DIR is set: use it.
        // - Otherwise: fall back to "<repoRoot>/memory" (best-effort).
        services.TryAddSingleton<IMemoryStore>(_ =>
        {
            try
            {
                var memoryRoot = FileMemoryStore.GetMemoryRootFromEnvironmentOrDefault();
                return new FileMemoryStore(memoryRoot);
            }
            catch
            {
                return NullMemoryStore.Instance;
            }
        });
    }

    private static void RegisterMemoryVectorIndex(IServiceCollection services, GAgentOptions options)
    {
        if (options.MemoryVectorIndexType != null)
        {
            EnsureConcrete(nameof(options.MemoryVectorIndexType), typeof(IMemoryVectorIndex), options.MemoryVectorIndexType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IMemoryVectorIndex), options.MemoryVectorIndexType));
            return;
        }

        // Default: file output (best-effort), co-located with FileMemoryStore by default.
        services.TryAddSingleton<IMemoryVectorIndex>(_ =>
        {
            try
            {
                var vectorRoot = FileMemoryVectorIndex.GetVectorRootFromEnvironmentOrDefault();
                return new FileMemoryVectorIndex(vectorRoot);
            }
            catch
            {
                return NullMemoryVectorIndex.Instance;
            }
        });
    }

    private static void RegisterMemoryGraphStore(IServiceCollection services, GAgentOptions options)
    {
        if (options.MemoryGraphStoreType != null)
        {
            EnsureConcrete(nameof(options.MemoryGraphStoreType), typeof(IMemoryGraphStore), options.MemoryGraphStoreType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IMemoryGraphStore), options.MemoryGraphStoreType));
            return;
        }

        // Default: store graphs as trace bundle artifacts under ${AEVATAR_TRACE_DIR}/<executionId>/artifacts/
        services.TryAddSingleton<IMemoryGraphStore>(_ =>
        {
            try
            {
                var traceRoot = FileExecutionTraceStore.GetTraceRootFromEnvironmentOrDefault();
                return new FileMemoryGraphStore(traceRoot);
            }
            catch
            {
                return NullMemoryGraphStore.Instance;
            }
        });
    }

    private static void EnsureOpenGeneric(string optionName, Type expectedInterface, Type candidate)
    {
        if (!candidate.IsGenericTypeDefinition)
        {
            throw new ArgumentException($"{optionName} must be an open generic type.");
        }

        var genericInterfaces = candidate.GetInterfaces()
            .Concat(candidate.IsInterface ? new[] { candidate } : Array.Empty<Type>())
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == expectedInterface);

        if (!genericInterfaces.Any())
        {
            throw new ArgumentException($"{optionName} must implement {expectedInterface.FullName}.");
        }
    }

    private static void EnsureConcrete(string optionName, Type expectedInterface, Type candidate)
    {
        if (candidate.IsAbstract || candidate.IsInterface)
        {
            throw new ArgumentException($"{optionName} must be a concrete type.");
        }

        if (!expectedInterface.IsAssignableFrom(candidate))
        {
            throw new ArgumentException($"{optionName} must implement {expectedInterface.FullName}.");
        }
    }
}