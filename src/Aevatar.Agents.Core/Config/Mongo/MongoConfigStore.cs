using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Core.Secrets;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Aevatar.Agents.Core.Config.Mongo;

internal class MongoConfigDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty; // "config" or "secrets"

    public string Data { get; set; } = string.Empty; // JSON content

    public DateTime LastUpdated { get; set; }
}

public class MongoConfigStore
{
    private readonly AevatarUserSecretsOptions _options;
    private IMongoCollection<MongoConfigDocument>? _collection;
    private readonly object _lock = new();
    private bool _initialized;

    public MongoConfigStore(AevatarUserSecretsOptions options)
    {
        _options = options;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;

            var connStr = _options.MongoConnectionString;
            if (string.IsNullOrWhiteSpace(connStr))
            {
                 // Try env vars
                 connStr = Environment.GetEnvironmentVariable("AEVATAR_MONGODB_CONNECTION_STRING");
                 if (string.IsNullOrWhiteSpace(connStr))
                    connStr = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
            }

            if (string.IsNullOrWhiteSpace(connStr))
            {
                // Not configured
                _initialized = true;
                return;
            }

            try 
            {
                var client = new MongoClient(connStr);
                var dbName = _options.MongoDatabaseName;
                if (string.IsNullOrWhiteSpace(dbName)) dbName = "aevatar_config";
                
                var db = client.GetDatabase(dbName);
                _collection = db.GetCollection<MongoConfigDocument>("user_config");
            }
            catch
            {
                // Best effort - if mongo fails, we just don't use it
            }
            _initialized = true;
        }
    }

    public bool IsAvailable 
    {
        get 
        {
            EnsureInitialized();
            return _collection != null;
        }
    }

    public string? LoadConfig() => Load("config");
    public string? LoadSecrets() => Load("secrets");

    public void SaveConfig(string json) => Save("config", json);
    public void SaveSecrets(string json) => Save("secrets", json);

    private string? Load(string id)
    {
        if (!IsAvailable) return null;
        try
        {
            var filter = Builders<MongoConfigDocument>.Filter.Eq(x => x.Id, id);
            var doc = _collection!.Find(filter).FirstOrDefault();
            return doc?.Data;
        }
        catch
        {
            return null;
        }
    }

    private void Save(string id, string json)
    {
        if (!IsAvailable) return;
        try
        {
            var filter = Builders<MongoConfigDocument>.Filter.Eq(x => x.Id, id);
            var update = Builders<MongoConfigDocument>.Update
                .Set(x => x.Data, json)
                .Set(x => x.LastUpdated, DateTime.UtcNow);
            
            _collection!.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
        }
        catch
        {
            // Ignore write errors
        }
    }
}
