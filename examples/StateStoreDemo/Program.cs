using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Persistence;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace StateStoreDemo;

/// <summary>
/// Demo application showing state persistence
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Aevatar State Store Demo ===\n");

        // Create service collection
        var services = new ServiceCollection();

        // Configure agent system with default in-memory stores
        var builder = services.AddAevatarAgentSystem();
        builder.UseLocalRuntime();

        // Register logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Build service provider
        var serviceProvider = services.BuildServiceProvider();

        // Create actor factory
        var factory = serviceProvider.GetRequiredService<IGAgentActorFactory>();

        // Create a counter agent
        Console.WriteLine("Creating Counter Agent...");
        var rawId = Guid.NewGuid().ToString();
        var agentActor = await factory.CreateGAgentActorAsync<CounterAgent>(rawId);
        var agent = agentActor.GetAgent() as CounterAgent;
        // Use agentActor.Id (full ActorId format) for consistency
        var agentId = agentActor.Id;
        Console.WriteLine($"Agent created with ID: {agentId} (rawId: {rawId})\n");

        if (agent == null)
        {
            Console.WriteLine("Error: Failed to create agent!");
            return;
        }

        // Increment a few times
        Console.WriteLine("Incrementing counter...");
        var incrementEvent = new IncrementEvent { Amount = 5 };
        await agentActor.PublishEventAsync(incrementEvent);

        await Task.Delay(100); // Wait for processing

        incrementEvent = new IncrementEvent { Amount = 3 };
        await agentActor.PublishEventAsync(incrementEvent);

        await Task.Delay(100); // Wait for processing

        incrementEvent = new IncrementEvent { Amount = 2 };
        await agentActor.PublishEventAsync(incrementEvent);

        await Task.Delay(100); // Wait for processing

        Console.WriteLine($"\nCurrent count: {agent.GetState().Count}");

        // Deactivate and reactivate to show persistence
        Console.WriteLine("\n--- Deactivating and reactivating agent ---");
        await agentActor.DeactivateAsync();

        Console.WriteLine("Agent deactivated. State should persist in store.\n");

        // Reactivate - use the same rawId to get the same ActorId
        Console.WriteLine("Reactivating agent...");
        // Extract rawId from full ActorId (format: "CounterAgent:rawId")
        var rawIdFromActorId = agentId.Contains(':') ? agentId.Split(':')[1] : agentId;
        var newAgentActor = await factory.CreateGAgentActorAsync<CounterAgent>(rawIdFromActorId);
        var newAgent = newAgentActor.GetAgent() as CounterAgent;

        if (newAgent == null)
        {
            Console.WriteLine("Error: Failed to reactivate agent!");
            return;
        }

        Console.WriteLine($"State after reactivation: {newAgent.GetState().Count}");

        // Increment again - should continue from previous count
        Console.WriteLine("\nIncrementing again to show persistence:");
        incrementEvent = new IncrementEvent { Amount = 10 };
        await newAgentActor.PublishEventAsync(incrementEvent);

        await Task.Delay(100);

        // Check final count
        Console.WriteLine($"\nFinal count: {newAgent.GetState().Count}");

        Console.WriteLine("\n=== Demo completed ===");
        
        // Cleanup
        await newAgentActor.DeactivateAsync();
    }
}
