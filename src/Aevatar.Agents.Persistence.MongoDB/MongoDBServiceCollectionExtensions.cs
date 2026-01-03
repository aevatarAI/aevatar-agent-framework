using System;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Aevatar.Agents.Persistence.MongoDB;

/// <summary>
/// DI extension methods for MongoDB persistence
/// </summary>
public static class MongoDBServiceCollectionExtensions
{
    private static bool _serializersConfigured;
    private static readonly object _lock = new();

    /// <summary>
    /// Configure MongoDB BSON serialization globally (call at startup)
    /// </summary>
    public static void ConfigureBsonSerializers()
    {
        if (_serializersConfigured) return;

        lock (_lock)
        {
            if (_serializersConfigured) return;

            // Register Guid serializer with Standard representation
            // to avoid "GuidRepresentation is Unspecified" errors
            try
            {
                BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            }
            catch (BsonSerializationException)
            {
                // Already registered
            }

            _serializersConfigured = true;
        }
    }


    /// <summary>
    /// Add MongoDB services with proper connection management
    /// 
    /// Usage:
    /// <code>
    /// services.AddAevatarMongoDB("mongodb://localhost:27017", "aevatar");
    /// </code>
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="connectionString">MongoDB connection string</param>
    /// <param name="databaseName">Database name (default: "aevatar")</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAevatarMongoDB(
        this IServiceCollection services,
        string connectionString,
        string databaseName = "aevatar")
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        // Configure BSON serializers (once globally)
        ConfigureBsonSerializers();

        // Register IMongoClient as singleton (connection pool is managed internally)
        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));

        // Register IMongoDatabase as singleton (bound to the single client)
        services.AddSingleton(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

        return services;
    }

    /// <summary>
    /// Add MongoDB services with custom client settings
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="settings">MongoDB client settings</param>
    /// <param name="databaseName">Database name (default: "aevatar")</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAevatarMongoDB(
        this IServiceCollection services,
        MongoClientSettings settings,
        string databaseName = "aevatar")
    {
        ArgumentNullException.ThrowIfNull(settings);

        services.AddSingleton<IMongoClient>(_ => new MongoClient(settings));
        services.AddSingleton(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

        return services;
    }

}

