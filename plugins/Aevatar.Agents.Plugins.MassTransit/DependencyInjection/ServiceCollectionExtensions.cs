using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging; 
using Confluent.Kafka; 

namespace Aevatar.Agents.Plugins.MassTransit.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MassTransit Message Stream plugin with automatic agent assembly discovery.
    /// Scans all loaded assemblies for IGAgent implementations.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection</returns>
    public static IServiceCollection AddMassTransitStreamPlugin(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var agentAssemblies = DiscoverAgentAssemblies();
        return services.AddMassTransitStreamPlugin(configuration, agentAssemblies);
    }
    
    /// <summary>
    /// Adds MassTransit Message Stream plugin with assemblies discovered by naming pattern.
    /// Useful when agent assemblies follow a naming convention (e.g., "*.Agents.*", "MyCompany.Agents.*")
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <param name="assemblyNamePatterns">Assembly name patterns to match (supports * wildcard)</param>
    /// <returns>The service collection</returns>
    public static IServiceCollection AddMassTransitStreamPluginWithPatterns(
        this IServiceCollection services,
        IConfiguration configuration,
        params string[] assemblyNamePatterns)
    {
        var agentAssemblies = DiscoverAgentAssembliesByPattern(assemblyNamePatterns);
        return services.AddMassTransitStreamPlugin(configuration, agentAssemblies);
    }
    
    /// <summary>
    /// Discovers all loaded assemblies that contain IGAgent implementations.
    /// </summary>
    /// <returns>Array of assemblies containing agents</returns>
    public static Assembly[] DiscoverAgentAssemblies()
    {
        var agentAssemblies = new List<Assembly>();
        
        try
        {
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));
                
            foreach (var assembly in loadedAssemblies)
            {
                try
                {
                    var hasAgents = assembly.GetTypes()
                        .Any(t => typeof(IGAgent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                    
                    if (hasAgents)
                    {
                        agentAssemblies.Add(assembly);
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that fail to load types
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Failed to auto-discover agent assemblies: {ex.Message}");
        }
        return agentAssemblies.ToArray();
    }
    
    /// <summary>
    /// Discovers assemblies by name patterns (supports * wildcard).
    /// </summary>
    /// <param name="patterns">Assembly name patterns (e.g., "*.Agents.*", "MyCompany.Agents.*")</param>
    /// <returns>Array of matching assemblies</returns>
    public static Assembly[] DiscoverAgentAssembliesByPattern(params string[] patterns)
    {
        if (patterns == null || patterns.Length == 0)
            return DiscoverAgentAssemblies();
            
        var agentAssemblies = new List<Assembly>();
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));
            
        foreach (var assembly in loadedAssemblies)
        {
            var assemblyName = assembly.GetName().Name ?? string.Empty;
            
            foreach (var pattern in patterns)
            {
                if (MatchesPattern(assemblyName, pattern))
                {
                    try
                    {
                        var hasAgents = assembly.GetTypes()
                            .Any(t => typeof(IGAgent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                        
                        if (hasAgents)
                        {
                            agentAssemblies.Add(assembly);
                            break;
                        }
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        // Skip assemblies that fail to load types
                    }
                }
            }
        }
        return agentAssemblies.ToArray();
    }
    
    private static bool MatchesPattern(string input, string pattern)
    {
        // Simple wildcard matching: * matches any sequence
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    
    /// <summary>
    /// Adds MassTransit Message Stream plugin with explicit assembly list.
    /// 
    /// Configuration structure:
    /// - TopicPrefix: Default topic for Agent communication
    /// - TopicMapping: Category -> Topic routing for Producer
    /// - Producer.Enabled: Enable/disable Producer
    /// - Consumer.Enabled: Enable/disable Consumer
    /// - Consumer.Topics: Topics to subscribe (auto-scanned from [StreamTopic] if empty)
    /// - Consumer.IncludeTopicPrefix: Auto-add TopicPrefix to consumer topics
    /// - Consumer.AutoScanAgentTopics: Auto-scan [StreamTopic] attributes for consumer topics
    /// </summary>
    public static IServiceCollection AddMassTransitStreamPlugin(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] agentAssemblies)
    {
        // 1. Configure Options
        var section = configuration.GetSection("MassTransit:Stream");
        services.Configure<MassTransitStreamOptions>(section);

        var options = section.Get<MassTransitStreamOptions>() ?? new MassTransitStreamOptions();
        
        // Ensure non-null references
        var topicMapping = options.TopicMapping ?? new Dictionary<string, string>();
        options.TopicMapping = topicMapping;
        options.Producer ??= new ProducerOptions();
        options.Consumer ??= new ConsumerOptions();

        // ============================================================
        // De-duplicate Kafka bootstrap config:
        // Prefer MassTransit:Stream:Kafka:* but fallback to top-level Kafka:* when missing
        // ============================================================
        if (options.TransportType == MassTransitTransportType.Kafka)
        {
            options.Kafka ??= new KafkaOptions();
            if (string.IsNullOrWhiteSpace(options.Kafka.BootstrapServers))
            {
                options.Kafka.BootstrapServers = configuration.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:9092";
            }
        }
        
        // ============================================================
        // Collect Producer Topics (for sending messages)
        // ============================================================
        var producerTopics = new HashSet<string>();
        
        if (options.Producer.Enabled)
        {
            // Add TopicPrefix
            if (!string.IsNullOrEmpty(options.TopicPrefix))
            {
                producerTopics.Add(options.TopicPrefix);
            }
            
            // Add topics from TopicMapping
            foreach (var t in topicMapping.Values)
            {
                if (!string.IsNullOrEmpty(t)) producerTopics.Add(t);
            }
        }

        // ============================================================
        // Collect Consumer Topics (for receiving messages)
        // ============================================================
        var consumerTopics = new HashSet<string>();
        
        if (options.Consumer.Enabled)
        {
            // Add explicitly configured topics
            if (options.Consumer.Topics != null)
            {
                foreach (var t in options.Consumer.Topics)
                {
                    if (!string.IsNullOrEmpty(t)) consumerTopics.Add(t);
                }
            }
            
            // Auto-add TopicPrefix if enabled
            if (options.Consumer.IncludeTopicPrefix && !string.IsNullOrEmpty(options.TopicPrefix))
            {
                consumerTopics.Add(options.TopicPrefix);
            }
            
            // Auto-scan [StreamTopic] attributes if enabled
            if (options.Consumer.AutoScanAgentTopics && agentAssemblies != null)
            {
                foreach (var assembly in agentAssemblies)
                {
                    try 
                    {
                        var agentTypes = assembly.GetTypes()
                            .Where(t => typeof(IGAgent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

                        foreach (var type in agentTypes)
                        {
                            var attr = type.GetCustomAttribute<StreamTopicAttribute>();
                            if (attr != null)
                            {
                                var category = type.Name;
                                var topic = attr.Topic;

                                // Add to Mapping (for Producer routing)
                                if (!topicMapping.ContainsKey(category))
                                {
                                    topicMapping[category] = topic;
                                }
                                
                                // Add to Consumer topics (for Silo)
                                consumerTopics.Add(topic);
                                
                                // Also add to Producer topics
                                if (options.Producer.Enabled)
                                {
                                    producerTopics.Add(topic);
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Best-effort scan
                    }
                }
            }
        }
        
        // Log configuration
        Console.WriteLine($"[MassTransit] RuntimeName: {options.RuntimeName}");
        Console.WriteLine($"[MassTransit] Producer Enabled: {options.Producer.Enabled}, Topics: {string.Join(", ", producerTopics)}");
        Console.WriteLine($"[MassTransit] Consumer Enabled: {options.Consumer.Enabled}, Topics: {string.Join(", ", consumerTopics)}");
        
        // Update registered options with merged mapping
        services.PostConfigure<MassTransitStreamOptions>(o => 
        {
            var targetMapping = o.TopicMapping ?? new Dictionary<string, string>();
            o.TopicMapping = targetMapping;

            foreach (var kvp in topicMapping)
            {
                if (!targetMapping.ContainsKey(kvp.Key))
                {
                    targetMapping[kvp.Key] = kvp.Value;
                }
            }
        });

        // 2. Register Provider
        services.AddSingleton<MassTransitMessageStreamProvider>();
        services.AddSingleton<IMessageStreamProvider>(sp => sp.GetRequiredService<MassTransitMessageStreamProvider>());
        
        // 3. Register MassTransit
        services.AddMassTransit(x =>
        {
            switch (options.TransportType)
            {
                case MassTransitTransportType.InMemory:
                    if (options.Consumer.Enabled && consumerTopics.Count > 0)
                    {
                        x.AddConsumer<StreamMessageDispatcher>();
                    }
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.ConfigureEndpoints(context);
                    });
                    break;

                case MassTransitTransportType.Kafka:
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.ConfigureEndpoints(context);
                    });

                    x.AddRider(rider =>
                    {
                        if (options.Consumer.Enabled && consumerTopics.Count > 0)
                        {
                            rider.AddConsumer<StreamMessageDispatcher>();
                        }
                        
                        foreach (var topic in producerTopics)
                        {
                            rider.AddProducer<string, ByteArrayMessage>(topic);
                        }
                        
                        rider.UsingKafka((context, k) =>
                        {
                            if (options.Kafka != null)
                            {
                                k.Host(options.Kafka.BootstrapServers);
                            }
                            
                            k.SecurityProtocol = Confluent.Kafka.SecurityProtocol.Plaintext;

                            if (options.Consumer.Enabled)
                            {
                                foreach (var topic in consumerTopics)
                                {
                                    k.TopicEndpoint<ByteArrayMessage>(
                                        topic, 
                                        options.Kafka?.ConsumerGroupId ?? "aevatar-agents-group", 
                                        e =>
                                        {
                                            e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                                            e.UseConcurrencyLimit(50);
                                            e.PrefetchCount = 200;
                                            e.CheckpointInterval = TimeSpan.FromSeconds(5);
                                            e.CheckpointMessageCount = 100;
                                            e.CreateIfMissing(t =>
                                            {
                                                t.NumPartitions = 8;
                                                t.ReplicationFactor = 1;
                                            });
                                            e.ConfigureConsumer<StreamMessageDispatcher>(context);
                                        });
                                }
                            }
                        });
                    });
                    break;

                case MassTransitTransportType.RabbitMQ:
                    if (options.Consumer.Enabled && consumerTopics.Count > 0)
                    {
                        x.AddConsumer<StreamMessageDispatcher>();
                    }
                    x.UsingRabbitMq((context, cfg) =>
                    {
                        if (options.RabbitMQ != null)
                        {
                            cfg.Host(options.RabbitMQ.Host, h =>
                            {
                                h.Username(options.RabbitMQ.Username);
                                h.Password(options.RabbitMQ.Password);
                            });
                        }
                        
                        if (options.Consumer.Enabled && consumerTopics.Count > 0)
                        {
                            cfg.ReceiveEndpoint(options.TopicPrefix, e =>
                            {
                                e.ConfigureConsumer<StreamMessageDispatcher>(context);
                            });
                        }
                    });
                    break;
            }
        });

        return services;
    }
    
    /// <summary>
    /// Adds MassTransit Message Stream Client (Producer-only mode).
    /// Use this for Orleans Clients that only need to send messages to Kafka.
    /// </summary>
    public static IServiceCollection AddMassTransitStreamClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var agentAssemblies = DiscoverAgentAssemblies();
        return services.AddMassTransitStreamClient(configuration, agentAssemblies);
    }
    
    /// <summary>
    /// Adds MassTransit Message Stream Client (Producer-only mode) with explicit assemblies.
    /// </summary>
    public static IServiceCollection AddMassTransitStreamClient(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] agentAssemblies)
    {
        var section = configuration.GetSection("MassTransit:Stream");
        services.Configure<MassTransitStreamOptions>(section);

        var options = section.Get<MassTransitStreamOptions>() ?? new MassTransitStreamOptions();
        var topicMapping = options.TopicMapping ?? new Dictionary<string, string>();
        
        // Collect producer topics
        var producerTopics = new HashSet<string>();
        
        if (!string.IsNullOrEmpty(options.TopicPrefix))
        {
            producerTopics.Add(options.TopicPrefix);
        }

        foreach (var t in topicMapping.Values)
        {
            if (!string.IsNullOrEmpty(t)) producerTopics.Add(t);
        }

        // Scan for [StreamTopic] annotations
        if (agentAssemblies != null)
        {
            foreach (var assembly in agentAssemblies)
            {
                try 
                {
                    var agentTypes = assembly.GetTypes()
                        .Where(t => typeof(IGAgent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

                    foreach (var type in agentTypes)
                    {
                        var attr = type.GetCustomAttribute<StreamTopicAttribute>();
                        if (attr != null)
                        {
                            var category = type.Name;
                            var topic = attr.Topic;
                            
                            if (!topicMapping.ContainsKey(category))
                            {
                                topicMapping[category] = topic;
                            }
                            producerTopics.Add(topic);
                        }
                    }
                }
                catch (Exception)
                {
                    // Best-effort scan
                }
            }
        }
        
        services.PostConfigure<MassTransitStreamOptions>(o => 
        {
            foreach (var kvp in topicMapping)
            {
                if (!o.TopicMapping.ContainsKey(kvp.Key))
                {
                    o.TopicMapping[kvp.Key] = kvp.Value;
                }
            }
        });

        services.AddSingleton<MassTransitMessageStreamProvider>();
        services.AddSingleton<IMessageStreamProvider>(sp => sp.GetRequiredService<MassTransitMessageStreamProvider>());
        
        services.AddMassTransit(x =>
        {
            switch (options.TransportType)
            {
                case MassTransitTransportType.InMemory:
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.ConfigureEndpoints(context);
                    });
                    break;

                case MassTransitTransportType.Kafka:
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.ConfigureEndpoints(context);
                    });

                    x.AddRider(rider =>
                    {
                        foreach (var topic in producerTopics)
                        {
                            rider.AddProducer<string, ByteArrayMessage>(topic);
                        }
                        
                        rider.UsingKafka((context, k) =>
                        {
                            if (options.Kafka != null)
                            {
                                k.Host(options.Kafka.BootstrapServers);
                            }
                            k.SecurityProtocol = Confluent.Kafka.SecurityProtocol.Plaintext;
                        });
                    });
                    break;

                case MassTransitTransportType.RabbitMQ:
                    x.UsingRabbitMq((context, cfg) =>
                    {
                        if (options.RabbitMQ != null)
                        {
                            cfg.Host(options.RabbitMQ.Host, h =>
                            {
                                h.Username(options.RabbitMQ.Username);
                                h.Password(options.RabbitMQ.Password);
                            });
                        }
                    });
                    break;
            }
        });

        return services;
    }
}
