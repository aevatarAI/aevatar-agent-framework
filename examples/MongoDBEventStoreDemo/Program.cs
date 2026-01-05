using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Orleans.MongoDB;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Persistence.MongoDB.GAgent;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.Runtime.Orleans.EventSourcing;
using Aevatar.Agents.Runtime.Orleans.MongoDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDBEventStoreDemo;
using Orleans.Serialization;

Console.WriteLine("🌌 Aevatar Agent Framework - OrleansEventStore + MongoDB Demo");
Console.WriteLine("==============================================================\n");
Console.WriteLine("This demo showcases:");
Console.WriteLine("  ✅ OrleansEventStore (Grain-based event coordination)");
Console.WriteLine("  ✅ MongoDB as Orleans GrainStorage backend");
Console.WriteLine("  ✅ Distributed concurrency control via Orleans");
Console.WriteLine("  ✅ Event persistence to MongoDB");
Console.WriteLine("  ✅ Automatic event replay from MongoDB\n");

// ========== MongoDB Configuration ==========
const string mongoConnectionString = "mongodb://localhost:27017";
const string mongoDatabase = "OrleansEventStore";
const string collectionPrefix = "BankAccounts";

Console.WriteLine("📊 Architecture:");
Console.WriteLine("   OrleansEventStore (IEventStore)");
Console.WriteLine("      ↓");
Console.WriteLine("   IEventStorageGrain (Orleans Grain)");
Console.WriteLine("      ↓");
Console.WriteLine("   Orleans GrainStorage (MongoDB Provider)");
Console.WriteLine("      ↓");
Console.WriteLine($"   MongoDB: {mongoDatabase}\n");

Console.WriteLine("📊 MongoDB Configuration:");
Console.WriteLine($"   Connection: {mongoConnectionString}");
Console.WriteLine($"   Database: {mongoDatabase}");
Console.WriteLine($"   Collection Prefix: {collectionPrefix}");
Console.WriteLine($"   Collection Name: {collectionPrefix}-EventStorageState\n");

// ========== Build Host with Orleans + MongoDB ==========
var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .UseOrleans((context, siloBuilder) =>
    {
        siloBuilder
            // Localhost clustering for demo
            .UseLocalhostClustering()

            // ✅ Configure MongoDB Client (REQUIRED before AddMongoDBGrainStorage)
            .UseMongoDBClient(provider =>
            {
                var settings = MongoClientSettings.FromConnectionString(mongoConnectionString);
                settings.MaxConnectionPoolSize = 100;
                settings.MinConnectionPoolSize = 10;
                return settings;
            })

            // ✅ Protobuf serialization (REQUIRED for AgentStateEvent!)
            .ConfigureServices(services =>
            {
                services.AddSerializer(serializerBuilder =>
                {
                    serializerBuilder.AddProtobufSerializer();
                });
            })
            
            .AddMemoryStreams(AevatarAgentsOrleansConstants.StreamProviderName)

            // ✅ MongoDB GrainStorage for Default (required by OrleansGAgentGrain)
            .AddMongoDBGrainStorage("Default", options =>
            {
                options.DatabaseName = mongoDatabase;
                options.CollectionPrefix = collectionPrefix;
                options.CreateShardKeyForCosmos = false;
            })
            
            // ✅ MongoDB GrainStorage for EventStore
            .AddMongoDBGrainStorage("EventStoreStorage", options =>
            {
                options.DatabaseName = mongoDatabase;
                options.CollectionPrefix = collectionPrefix;
                options.CreateShardKeyForCosmos = false;
            })

            // ✅ MongoDB GrainStorage for PubSub storage (required for streams)
            .AddMongoDBGrainStorage("PubSubStore", options =>
            {
                options.DatabaseName = mongoDatabase;
                options.CollectionPrefix = collectionPrefix;
                options.CreateShardKeyForCosmos = false;
            });
    })
    .ConfigureServices(services =>
    {
        // ✅ Register IMongoDatabase for StateStore
        services.AddSingleton<IMongoDatabase>(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            return client.GetDatabase(mongoDatabase);
        });
        
        // ✅ Register MongoDB Event Repository (auto-sharding by AgentType)
        // Collection naming: agent_events_{AgentTypeName} (automatic)
        // Each agent type gets its own collection for better query performance
        services.AddSingleton<IEventRepository>(sp =>
        {
            var mongoClient = sp.GetRequiredService<IMongoClient>();
            var logger = sp.GetRequiredService<ILogger<MongoEventRepository>>();
            
            return new MongoEventRepository(
                mongoClient, 
                new MongoEventRepositoryOptions
                {
                    DatabaseName = mongoDatabase,
                    CollectionName = "agent_events",  // Base name, actual = agent_events_{AgentType}
                    EnableDetailedLogging = true
                },
                logger);
        });
        
        // ✅ Register OrleansEventStore (uses IEventRepository)
        services.AddSingleton<IEventStore, OrleansEventStore>();
        
        // ✅ Register MongoDB StateStore for Snapshots (per-state-type collections)
        // Collection naming: agent_states_{StateTypeName}
        services.AddSingleton(typeof(IStateStore<>), typeof(MongoDBStateStore<>));
        
        // ✅ Register AIGAgentFactory for automatic EventStore injection
        services.AddSingleton<IGAgentFactory, AIGAgentFactory>();
        
        // ✅ Register Orleans Agent Factory
        services.AddOrleansActorFactory();
        
        // ✅ Register Auto-Discovery Factory Provider
        services.AddGAgentActorFactoryProvider();
    })
    .Build();

// Start Orleans Silo
Console.WriteLine("🚀 Starting Orleans Silo with MongoDB backend...\n");
await host.StartAsync();

try
{
    var factory = host.Services.GetRequiredService<IGAgentActorFactory>();

    Console.WriteLine("✅ Orleans Silo started successfully!\n");

    // ============================================================
    // Part 1: Create Account and Execute Transactions
    // ============================================================
    Console.WriteLine("📍 Part 1: Creating Account and Transactions");
    Console.WriteLine("══════════════════════════════════════════════\n");

    var accountId = Guid.NewGuid().ToString();
    Console.WriteLine($"📊 Agent ID: {accountId}\n");

    // ✅ Create Actor (EventSourcing is automatically enabled via IEventStore registration)
    var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(accountId);

    // ✅ Use RPC proxy to call Agent methods (works across Orleans boundaries!)
    var agent = actor.As<IBankAccountAgent>();

    // Create account
    await agent.CreateAccountAsync("Alice Smith", 100m);
    
    Console.WriteLine($"✅ Account created");
    Console.WriteLine($"   Holder: {await agent.GetAccountHolderAsync()}");
    Console.WriteLine($"   Balance: ${await agent.GetBalanceAsync():F2}");
    Console.WriteLine($"   Version: v{await agent.GetCurrentVersionAsync()}\n");

    // Individual transactions
    Console.WriteLine("💰 Individual Transactions:");
    Console.WriteLine("────────────────────────────");
    await agent.DepositAsync(1000m, "Salary");
    Console.WriteLine($"  ✓ Deposited $1000 (Salary) - Balance: ${await agent.GetBalanceAsync():F2}");

    await agent.DepositAsync(500m, "Bonus");
    Console.WriteLine($"  ✓ Deposited $500 (Bonus) - Balance: ${await agent.GetBalanceAsync():F2}");

    await agent.WithdrawAsync(300m, "Rent");
    Console.WriteLine($"  ✓ Withdrew $300 (Rent) - Balance: ${await agent.GetBalanceAsync():F2}\n");

    Console.WriteLine($"💵 Current Balance: ${await agent.GetBalanceAsync():F2}");
    Console.WriteLine($"📈 Current Version: v{await agent.GetCurrentVersionAsync()}\n");

    // ============================================================
    // Part 2: More Transactions (simulating batch)
    // ============================================================
    Console.WriteLine("📍 Part 2: Additional Transactions");
    Console.WriteLine("══════════════════════════════════════════════════\n");

    Console.WriteLine("⚡ Executing 3 more transactions:");
    Console.WriteLine("────────────────────────────────────────────");
    
    await agent.DepositAsync(200m, "Freelance payment");
    Console.WriteLine($"  ✓ Deposited $200 (Freelance) - Balance: ${await agent.GetBalanceAsync():F2}");
    
    await agent.WithdrawAsync(150m, "Groceries");
    Console.WriteLine($"  ✓ Withdrew $150 (Groceries) - Balance: ${await agent.GetBalanceAsync():F2}");
    
    await agent.DepositAsync(100m, "Gift");
    Console.WriteLine($"  ✓ Deposited $100 (Gift) - Balance: ${await agent.GetBalanceAsync():F2}\n");

    Console.WriteLine($"💵 Current Balance: ${await agent.GetBalanceAsync():F2}");
    Console.WriteLine($"📈 Current Version: v{await agent.GetCurrentVersionAsync()} (Snapshot will trigger at v10)\n");

    // ============================================================
    // Part 2.5: More Transactions to Trigger Snapshot
    // ============================================================
    Console.WriteLine("📍 Part 2.5: More Transactions (Testing Snapshot)");
    Console.WriteLine("══════════════════════════════════════════════════\n");

    Console.WriteLine("💰 Adding 5 more transactions to trigger snapshot...");
    Console.WriteLine("────────────────────────────────────────────");
    
    await agent.DepositAsync(300m, "Investment return");
    Console.WriteLine($"  ✓ Deposited $300 (Investment) - Balance: ${await agent.GetBalanceAsync():F2}, Version: v{await agent.GetCurrentVersionAsync()}");

    await agent.WithdrawAsync(100m, "Utilities");
    Console.WriteLine($"  ✓ Withdrew $100 (Utilities) - Balance: ${await agent.GetBalanceAsync():F2}, Version: v{await agent.GetCurrentVersionAsync()}");

    await agent.DepositAsync(50m, "Cashback");
    Console.WriteLine($"  ✓ Deposited $50 (Cashback) - Balance: ${await agent.GetBalanceAsync():F2}, Version: v{await agent.GetCurrentVersionAsync()}");

    await agent.WithdrawAsync(200m, "Dining");
    Console.WriteLine($"  ✓ Withdrew $200 (Dining) - Balance: ${await agent.GetBalanceAsync():F2}, Version: v{await agent.GetCurrentVersionAsync()}");

    await agent.DepositAsync(150m, "Gift received");
    Console.WriteLine($"  ✓ Deposited $150 (Gift) - Balance: ${await agent.GetBalanceAsync():F2}, Version: v{await agent.GetCurrentVersionAsync()}");

    Console.WriteLine($"\n📸 Snapshot should be saved at version 10!");
    Console.WriteLine($"💵 Final Balance: ${await agent.GetBalanceAsync():F2}");
    Console.WriteLine($"📈 Final Version: v{await agent.GetCurrentVersionAsync()}\n");

    var balanceBeforeRecovery = await agent.GetBalanceAsync();
    var versionBeforeRecovery = await agent.GetCurrentVersionAsync();

    // ============================================================
    // Part 3: Event Replay with Snapshot (Crash Recovery Simulation)
    // ============================================================
    Console.WriteLine("📍 Part 3: Event Replay with Snapshot from MongoDB");
    Console.WriteLine("══════════════════════════════════════════════════\n");

    Console.WriteLine("🔄 Simulating grain deactivation and reactivation...");
    Console.WriteLine("   (Creating new actor instance with same ID)\n");

    // Create new actor instance (simulating grain reactivation)
    // Event sourcing automatically replays events from storage when the Agent handles events
    var actor2 = await factory.CreateGAgentActorAsync<BankAccountAgent>(accountId);

    // ✅ Use RPC proxy for recovered Agent
    var agent2 = actor2.As<IBankAccountAgent>();

    Console.WriteLine("✅ Grain reactivated and state recovered from MongoDB!");
    Console.WriteLine($"   Holder: {await agent2.GetAccountHolderAsync()}");
    Console.WriteLine($"   Balance: ${await agent2.GetBalanceAsync():F2}");
    Console.WriteLine($"   Transactions: {await agent2.GetTransactionCountAsync()}");
    Console.WriteLine($"   Version: v{await agent2.GetCurrentVersionAsync()}");
    Console.WriteLine($"   📸 Recovery used Snapshot + incremental events!\n");

    // Verify consistency
    var recoveredBalance = await agent2.GetBalanceAsync();
    var recoveredVersion = await agent2.GetCurrentVersionAsync();
    if (Math.Abs(recoveredBalance - balanceBeforeRecovery) < 0.01 && 
        recoveredVersion == versionBeforeRecovery)
    {
        Console.WriteLine("✅ State consistency verified!");
        Console.WriteLine($"   Balance matches: ${balanceBeforeRecovery:F2}");
        Console.WriteLine($"   Version matches: v{versionBeforeRecovery}\n");
    }
    else
    {
        Console.WriteLine("❌ State mismatch detected!");
        Console.WriteLine($"   Expected: ${balanceBeforeRecovery:F2}, Got: ${recoveredBalance:F2}");
        Console.WriteLine($"   Expected version: v{versionBeforeRecovery}, Got: v{recoveredVersion}\n");
    }

    // ============================================================
    // Part 4: Final Statistics (via RPC)
    // ============================================================
    Console.WriteLine("📍 Part 4: Final Statistics (from MongoDB via RPC)");
    Console.WriteLine("══════════════════════════════════════════════════\n");

    Console.WriteLine($"📊 Final Statistics:");
    Console.WriteLine($"   Account Holder: {await agent2.GetAccountHolderAsync()}");
    Console.WriteLine($"   Final Balance: ${await agent2.GetBalanceAsync():F2}");
    Console.WriteLine($"   Total Transactions: {await agent2.GetTransactionCountAsync()}");
    Console.WriteLine($"   Event Version: v{await agent2.GetCurrentVersionAsync()}\n");

    // ============================================================
    // MongoDB Collection Information
    // ============================================================
    Console.WriteLine("📍 MongoDB Collection Details");
    Console.WriteLine("══════════════════════════════════════════════════\n");
    
    Console.WriteLine("🗄️  Collection Location:");
    Console.WriteLine($"   Database: {mongoDatabase}");
    Console.WriteLine($"   Collection: {collectionPrefix}-EventStorageState");
    Console.WriteLine($"   Document ID: {accountId}");
    Console.WriteLine($"\n💡 View data using:");
    Console.WriteLine($"   - MongoDB Compass: {mongoConnectionString}");
    Console.WriteLine($"   - Mongo Shell: mongosh {mongoDatabase}");
    Console.WriteLine($"   - Query: db['{collectionPrefix}-EventStorageState'].find().pretty()\n");

    Console.WriteLine("✨ Demo completed successfully!");
    Console.WriteLine("\n🎯 Key Architecture Points:");
    Console.WriteLine("   1. OrleansEventStore wraps IEventStorageGrain");
    Console.WriteLine("   2. IEventStorageGrain provides Orleans concurrency control");
    Console.WriteLine("   3. Orleans GrainStorage persists to MongoDB");
    Console.WriteLine("   4. Collection name = CollectionPrefix + '-EventStorageState'");
    Console.WriteLine($"   5. This demo uses: '{collectionPrefix}-EventStorageState'");
    Console.WriteLine("   6. Orleans handles serialization automatically (Protobuf)");
    Console.WriteLine("   7. Grain deactivation/reactivation is transparent\n");
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error: {ex.Message}");
    Console.WriteLine($"   Type: {ex.GetType().Name}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    }
    Console.WriteLine($"\n💡 Make sure MongoDB is running:");
    Console.WriteLine($"   docker run -d -p 27017:27017 --name mongodb mongo:7.0");
    Console.WriteLine($"   OR: cd examples/MongoDBEventStoreDemo && docker-compose up -d\n");
    
    // Print stack trace for debugging
    Console.WriteLine($"\n📋 Stack Trace:");
    Console.WriteLine(ex.StackTrace);
}
finally
{
    Console.WriteLine("\n🛑 Shutting down Orleans Silo...");
    await host.StopAsync();
    Console.WriteLine("✅ Orleans Silo stopped.\n");
}
