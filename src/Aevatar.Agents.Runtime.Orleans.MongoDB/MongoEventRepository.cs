using System.Collections.Concurrent;
using Aevatar.Agents.Orleans.MongoDB;
using Aevatar.Agents.Runtime.Orleans.EventSourcing;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Aevatar.Agents.Runtime.Orleans.MongoDB;

/// <summary>
/// MongoDB document for storing a SINGLE event
/// Each event is stored as a separate document for better scalability and query performance
/// </summary>
public class EventDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty; // Format: "{agentId}_{version}"
    
    [BsonElement("agentId")]
    [BsonRepresentation(BsonType.String)]
    public Guid AgentId { get; set; }
    
    [BsonElement("version")]
    public long Version { get; set; }
    
    [BsonElement("eventData")]
    public byte[] EventData { get; set; } = Array.Empty<byte>();
    
    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [BsonElement("eventType")]
    public string EventType { get; set; } = string.Empty;
}

/// <summary>
/// MongoDB implementation of IEventRepository
/// Each event is stored as a separate document for:
/// - No document size limit (16MB per doc, but unlimited docs)
/// - Efficient version-based queries
/// - Easy cleanup of old events
/// - Better indexing and performance
/// 
/// Supports per-agent-type collections for:
/// - Better query performance (smaller index per type)
/// - Isolated scaling and optimization
/// - Business-level separation
/// </summary>
public class MongoEventRepository : IEventRepository
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<EventDocument> _defaultCollection;
    private readonly ConcurrentDictionary<string, IMongoCollection<EventDocument>> _collectionCache = new();
    private readonly ILogger<MongoEventRepository> _logger;
    private readonly string _defaultCollectionName;
    private readonly MongoEventRepositoryOptions _options;
    private static readonly SemaphoreSlim _globalIndexLock = new(1, 1);
    // IMPORTANT: EnsureIndexesAsync can be called concurrently (fire-and-forget from constructors).
    // Use a concurrent set to avoid HashSet race conditions.
    private static readonly ConcurrentDictionary<string, byte> _indexedCollections = new();

    /// <summary>
    /// Creates a new MongoEventRepository with configuration options
    /// Supports per-agent-type collections when agentTypeName is provided
    /// </summary>
    public MongoEventRepository(
        IMongoClient mongoClient,
        MongoEventRepositoryOptions options,
        ILogger<MongoEventRepository> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _database = mongoClient.GetDatabase(options.DatabaseName);
        _defaultCollectionName = options.CollectionName;
        _defaultCollection = _database.GetCollection<EventDocument>(_defaultCollectionName);
        
        if (_options.EnableDetailedLogging)
        {
            _logger.LogInformation(
                "MongoEventRepository initialized: Database={Database}, DefaultCollection={Collection}, " +
                "MaxPoolSize={MaxPoolSize}, MinPoolSize={MinPoolSize}",
                options.DatabaseName, options.CollectionName,
                options.MaxConnectionPoolSize, options.MinConnectionPoolSize);
        }
        
        // Eagerly ensure indexes on default collection (async fire-and-forget)
        _ = EnsureIndexesAsync(_defaultCollection, _defaultCollectionName);
    }
    
    /// <summary>
    /// Creates a new MongoEventRepository with simple parameters (backward compatibility)
    /// </summary>
    public MongoEventRepository(
        IMongoClient mongoClient,
        string databaseName,
        string collectionName,
        ILogger<MongoEventRepository> logger)
        : this(mongoClient, 
              new MongoEventRepositoryOptions 
              { 
                  DatabaseName = databaseName, 
                  CollectionName = collectionName 
              }, 
              logger)
    {
    }
    
    /// <summary>
    /// Get collection for a specific agent type
    /// Uses ConcurrentDictionary for thread-safe caching
    /// </summary>
    private IMongoCollection<EventDocument> GetCollection(string? agentTypeName)
    {
        if (string.IsNullOrEmpty(agentTypeName))
            return _defaultCollection;
        
        // Normalize collection name: extract short type name
        var shortTypeName = GetShortTypeName(agentTypeName);
        var collectionName = $"agent_events_{shortTypeName}";
        
        return _collectionCache.GetOrAdd(collectionName, name =>
        {
            var collection = _database.GetCollection<EventDocument>(name);
            
            _logger.LogInformation(
                "Created new event collection for agent type: {AgentType} -> {CollectionName}",
                agentTypeName, name);
            
            // Ensure indexes for new collection (async fire-and-forget)
            _ = EnsureIndexesAsync(collection, name);
            
            return collection;
        });
    }
    
    /// <summary>
    /// Extract short type name from full type name
    /// e.g., "Aevatar.Payment.Agents.PaymentRecordGAgent" -> "PaymentRecordGAgent"
    /// </summary>
    private static string GetShortTypeName(string fullTypeName)
    {
        // Handle assembly-qualified names
        var typeNamePart = fullTypeName.Split(',')[0].Trim();
        
        // Get class name only
        var lastDot = typeNamePart.LastIndexOf('.');
        return lastDot >= 0 ? typeNamePart[(lastDot + 1)..] : typeNamePart;
    }

    /// <summary>
    /// Ensure indexes are created eagerly for a collection
    /// Uses global lock to ensure each collection is indexed only once across all instances
    /// </summary>
    private async Task EnsureIndexesAsync(
        IMongoCollection<EventDocument> collection, 
        string collectionName, 
        CancellationToken ct = default)
    {
        // Check if this collection has already been indexed
        if (_indexedCollections.ContainsKey(collectionName))
            return;

        await _globalIndexLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_indexedCollections.ContainsKey(collectionName))
                return;

            var indexModels = new List<CreateIndexModel<EventDocument>>();

            // 1. Composite index: agentId + version (CRITICAL for all queries)
            // This index supports:
            //   - Find by agentId
            //   - Find by agentId + version range
            //   - Sort by version within agentId (DESC for latest queries)
            // Using DESC for version because most queries fetch latest events
            var agentIdVersionIndex = Builders<EventDocument>.IndexKeys
                .Ascending(e => e.AgentId)
                .Descending(e => e.Version);  // ✅ DESC for GetLatestVersion performance
            indexModels.Add(new CreateIndexModel<EventDocument>(
                agentIdVersionIndex,
                new CreateIndexOptions
                {
                    Name = "idx_agentId_version",
                    Unique = true,  // ✅ Enforce uniqueness: one event per (agentId, version)
                    Background = false // Create immediately on startup
                }));

            // 2. Index for timestamp-based cleanup (optional but recommended)
            var timestampIndex = Builders<EventDocument>.IndexKeys
                .Ascending(e => e.Timestamp);
            indexModels.Add(new CreateIndexModel<EventDocument>(
                timestampIndex,
                new CreateIndexOptions
                {
                    Name = "idx_timestamp",
                    Unique = false,
                    Background = false
                }));

            // 3. Index for event type queries (useful for analytics)
            var eventTypeIndex = Builders<EventDocument>.IndexKeys
                .Ascending(e => e.EventType);
            indexModels.Add(new CreateIndexModel<EventDocument>(
                eventTypeIndex,
                new CreateIndexOptions
                {
                    Name = "idx_eventType",
                    Unique = false,
                    Background = false
                }));

            // Batch create all indexes
            await collection.Indexes.CreateManyAsync(indexModels, ct);

            _indexedCollections.TryAdd(collectionName, 0);
            
            _logger.LogInformation(
                "MongoDB indexes created for collection '{Collection}': agentId+version DESC (UNIQUE), timestamp, eventType",
                collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "Failed to create indexes for collection '{Collection}' (may already exist)",
                collectionName);
            
            // Mark as indexed even on failure to avoid retry storms
            _indexedCollections.TryAdd(collectionName, 0);
        }
        finally
        {
            _globalIndexLock.Release();
        }
    }

    public async Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        var eventsList = events.ToList();
        if (!eventsList.Any()) return 0;

        // Get collection for this agent type
        var collection = GetCollection(agentTypeName);

        // Prepare event documents (events already have version assigned by Grain)
        var eventDocuments = eventsList.Select(evt => new EventDocument
        {
            Id = $"{agentId}_{evt.Version}",
            AgentId = agentId,
            Version = evt.Version,
            EventData = evt.ToByteArray(),
            Timestamp = evt.Timestamp.ToDateTime(),
            EventType = evt.EventData.TypeUrl
        }).ToList();

        // Batch insert to MongoDB
        await collection.InsertManyAsync(eventDocuments, cancellationToken: ct);

        // ✅ Optimized: Use last element (versions are sequential)
        var newVersion = eventsList[^1].Version;
        
        _logger.LogDebug(
            "Appended {Count} events for agent {AgentId} (type: {AgentType}), version range: {First}-{Last}",
            eventsList.Count, agentId, agentTypeName ?? "default", eventsList[0].Version, newVersion);

        return newVersion;
    }

    public async Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        // Get collection for this agent type
        var collection = GetCollection(agentTypeName);
        
        // Build MongoDB query
        var filterBuilder = Builders<EventDocument>.Filter;
        var filter = filterBuilder.Eq(e => e.AgentId, agentId);

        if (fromVersion.HasValue)
            filter &= filterBuilder.Gte(e => e.Version, fromVersion.Value);

        if (toVersion.HasValue)
            filter &= filterBuilder.Lte(e => e.Version, toVersion.Value);

        // Query MongoDB
        // Note: Index is { agentId: 1, version: -1 }, but MongoDB can scan in reverse for ASC sort
        var query = collection
            .Find(filter)
            .Sort(Builders<EventDocument>.Sort.Ascending(e => e.Version));

        if (maxCount.HasValue)
            query = query.Limit(maxCount.Value);

        var eventDocs = await query.ToListAsync(ct);

        // Deserialize events
        var events = eventDocs
            .Select(doc => AgentStateEvent.Parser.ParseFrom(doc.EventData))
            .ToList();

        _logger.LogDebug(
            "Retrieved {Count} events for agent {AgentId} (type: {AgentType}, version range: {From}-{To})",
            events.Count, agentId, agentTypeName ?? "default", fromVersion, toVersion);

        return events;
    }

    public async Task<long> GetLatestVersionAsync(
        Guid agentId,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        // Get collection for this agent type
        var collection = GetCollection(agentTypeName);
        
        // ✅ Perfect index usage: { agentId: 1, version: -1 } with DESC sort
        var latestEvent = await collection
            .Find(e => e.AgentId == agentId)
            .SortByDescending(e => e.Version)
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        return latestEvent?.Version ?? 0;
    }

    public async Task DeleteEventsBeforeVersionAsync(
        Guid agentId,
        long version,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        // Get collection for this agent type
        var collection = GetCollection(agentTypeName);
        
        var filter = Builders<EventDocument>.Filter.And(
            Builders<EventDocument>.Filter.Eq(e => e.AgentId, agentId),
            Builders<EventDocument>.Filter.Lt(e => e.Version, version)
        );

        var result = await collection.DeleteManyAsync(filter, ct);

        _logger.LogInformation(
            "Deleted {Count} old events for agent {AgentId} (type: {AgentType}, before version {Version})",
            result.DeletedCount, agentId, agentTypeName ?? "default", version);
    }
}

