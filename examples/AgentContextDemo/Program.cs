using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Aevatar.Agents.Core.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Runtime.Orleans.Context;
using Aevatar.Agents.Runtime.Orleans.Extensions;
using AgentContextDemo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.WriteLine("🌌 Aevatar Agent Framework - Agent Context Demo");
Console.WriteLine("=================================================\n");

// ============================================================
// Part 1: Test with Local Runtime
// ============================================================
Console.WriteLine("📦 Part 1: Testing with LOCAL Runtime");
Console.WriteLine("─────────────────────────────────────────\n");

await TestLocalRuntime();

// ============================================================
// Part 2: Test with Orleans Runtime
// ============================================================
Console.WriteLine("\n\n📦 Part 2: Testing with ORLEANS Runtime");
Console.WriteLine("─────────────────────────────────────────\n");

await TestOrleansRuntime();

Console.WriteLine("\n✅ Agent Context Demo 完成!");

// ============================================================
// Local Runtime Test
// ============================================================
async Task TestLocalRuntime()
{
    var services = new ServiceCollection();
    services.AddLogging(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    });

    services.AddAevatarAgentSystem(builder =>
    {
        builder.UseLocalRuntime();
        builder.AddAgentContext(); // Register context services
    });

    var serviceProvider = services.BuildServiceProvider();
    var factory = serviceProvider.GetRequiredService<IGAgentActorFactory>();
    var contextAccessor = serviceProvider.GetRequiredService<IAgentContextAccessor>();

    // Create agent
    Console.WriteLine("🔧 Creating ContextAwareAgent...");
    var agentId = Guid.NewGuid().ToString();
    var actor = await factory.CreateGAgentActorAsync<ContextAwareAgent>(agentId);
    Console.WriteLine($"✅ Agent created: {actor.Id}\n");

    // Set up context before publishing event
    Console.WriteLine("📝 Setting up context...");
    var context = contextAccessor.GetOrCreate();
    context.Set(AgentContextKeys.CorrelationId, "local-corr-12345");
    context.Set(AgentContextKeys.UserId, "user-local-001");
    context.Set(AgentContextKeys.Language, "zh-CN");
    context.Set(AgentContextKeys.IsCN, true);
    Console.WriteLine("   CorrelationId: local-corr-12345");
    Console.WriteLine("   UserId: user-local-001");
    Console.WriteLine("   Language: zh-CN");
    Console.WriteLine("   IsCN: true\n");

    // Publish event with context
    Console.WriteLine("📤 Publishing ProcessWithContextEvent...");
    await actor.PublishEventAsync(new ProcessWithContextEvent
    {
        RequestId = "req-local-001",
        Message = "Hello from Local Runtime!"
    });

    // Wait for processing
    await Task.Delay(500);

    // Verify context was received
    var agent = (ContextAwareAgent)actor.GetAgent();
    var (corrId, userId, lang) = agent.GetLastContext();
    
    Console.WriteLine("\n📊 Verification:");
    Console.WriteLine($"   Received CorrelationId: {corrId}");
    Console.WriteLine($"   Received UserId: {userId}");
    Console.WriteLine($"   Received Language: {lang}");
    
    var success = corrId == "local-corr-12345" && userId == "user-local-001" && lang == "zh-CN";
    Console.WriteLine($"\n   Result: {(success ? "✅ PASS - Context propagated correctly!" : "❌ FAIL - Context not propagated")}");

    await actor.DeactivateAsync();
}

// ============================================================
// Orleans Runtime Test
// ============================================================
async Task TestOrleansRuntime()
{
    var builder = Host.CreateApplicationBuilder();
    
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Information); // Show agent logs

    // Configure Orleans with correct stream provider name
    builder.Services.AddOrleans(siloBuilder =>
    {
        siloBuilder.UseLocalhostClustering();
        siloBuilder.AddMemoryGrainStorage("Default");
        siloBuilder.AddMemoryGrainStorage("PubSubStore");
        siloBuilder.AddMemoryStreams("AevatarAgents"); // Must match AevatarAgentsOrleansConstants.StreamProviderName
    });

    builder.Services.AddAevatarAgentSystem(aevatarBuilder =>
    {
        aevatarBuilder.UseOrleansRuntime();
        aevatarBuilder.AddOrleansAgentContext(); // Use Orleans-specific context
    });

    var host = builder.Build();
    await host.StartAsync();

    try
    {
        var factory = host.Services.GetRequiredService<IGAgentActorFactory>();
        var contextAccessor = host.Services.GetRequiredService<IAgentContextAccessor>();

        // Create agent
        Console.WriteLine("🔧 Creating ContextAwareAgent with Orleans...");
        var agentId = Guid.NewGuid().ToString();
        var actor = await factory.CreateGAgentActorAsync<ContextAwareAgent>(agentId);
        Console.WriteLine($"✅ Agent created: {actor.Id}\n");

        // Set up context before publishing event
        Console.WriteLine("📝 Setting up context via Orleans RequestContext Bridge...");
        var context = contextAccessor.GetOrCreate();
        context.Set(AgentContextKeys.CorrelationId, "orleans-corr-67890");
        context.Set(AgentContextKeys.UserId, "user-orleans-002");
        context.Set(AgentContextKeys.Language, "en-US");
        context.Set(AgentContextKeys.IsCN, false);
        Console.WriteLine("   CorrelationId: orleans-corr-67890");
        Console.WriteLine("   UserId: user-orleans-002");
        Console.WriteLine("   Language: en-US");
        Console.WriteLine("   IsCN: false\n");

        // Publish event with context
        Console.WriteLine("📤 Publishing ProcessWithContextEvent...");
        Console.WriteLine("   (Watch for '🎯' log line showing received context values)\n");
        await actor.PublishEventAsync(new ProcessWithContextEvent
        {
            RequestId = "req-orleans-001",
            Message = "Hello from Orleans Runtime!"
        });

        // Wait for event processing in Silo
        await Task.Delay(2000);
        
        Console.WriteLine("\n📊 Orleans Context Verification:");
        Console.WriteLine("   In Orleans mode, Agent runs inside Silo (Grain).");
        Console.WriteLine("   Context is propagated via EventEnvelope.ContextMetadata.");
        Console.WriteLine("   Check the log output above for '🎯' line to verify context values.");
        Console.WriteLine("\n   ✅ If you see context values in the log, propagation works!");

        await actor.DeactivateAsync();
    }
    finally
    {
        await host.StopAsync();
    }
}
