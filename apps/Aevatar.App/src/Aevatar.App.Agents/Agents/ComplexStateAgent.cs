using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Core;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf.WellKnownTypes;
using ComplexState.Server;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Agents.Agents;

/// <summary>
/// Agent with complex state types for testing ES CQRS projection.
/// Includes: List, Dictionary, nested objects.
/// </summary>
public class ComplexStateAgent : GAgentBase<ComplexAgentState>
{
    public ComplexStateAgent()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"Complex State Agent {Id} - Name: {State.Name}, Tags: {State.Tags.Count}, Orders: {State.Orders.Count}");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        Logger.LogInformation("🎯 ComplexStateAgent {AgentId} Activated", Id);
        
        if (State.CreatedAt == null)
        {
            State.AgentId = Id.ToString();
            State.Name = "New User";
            State.Age = 0;
            State.Balance = 0;
            State.IsActive = true;
            State.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
            State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        }
    }

    [EventHandler]
    public Task HandleUpdateProfile(UpdateProfileEvent evt)
    {
        Logger.LogInformation("📝 Updating profile: {Name}, Age: {Age}", evt.Name, evt.Age);
        
        State.Name = evt.Name;
        State.Age = evt.Age;
        
        if (evt.Address != null)
        {
            State.Address = evt.Address;
        }
        
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleAddTag(AddTagEvent evt)
    {
        Logger.LogInformation("🏷️ Adding tag: {Tag}", evt.Tag);
        
        if (!State.Tags.Contains(evt.Tag))
        {
            State.Tags.Add(evt.Tag);
        }
        
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleAddOrder(AddOrderEvent evt)
    {
        Logger.LogInformation("🛒 Adding order: {ProductName} x{Quantity}", 
            evt.Order?.ProductName, evt.Order?.Quantity);
        
        if (evt.Order != null)
        {
            State.Orders.Add(evt.Order);
        }
        
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleSetMetadata(SetMetadataEvent evt)
    {
        Logger.LogInformation("📋 Setting metadata: {Key} = {Value}", evt.Key, evt.Value);
        
        State.Metadata[evt.Key] = evt.Value;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleSetScore(SetScoreEvent evt)
    {
        Logger.LogInformation("🎮 Setting score: {Category} = {Score}", evt.Category, evt.Score);
        
        State.Scores[evt.Category] = evt.Score;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleAddLuckyNumber(AddLuckyNumberEvent evt)
    {
        Logger.LogInformation("🍀 Adding lucky number: {Number}", evt.Number);
        
        if (!State.LuckyNumbers.Contains(evt.Number))
        {
            State.LuckyNumbers.Add(evt.Number);
        }
        
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleUpdateBalance(UpdateBalanceEvent evt)
    {
        Logger.LogInformation("💰 Updating balance: +{Amount}", evt.Amount);
        
        State.Balance += evt.Amount;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Event handler for initializing test data.
    /// This allows test data initialization through the event system,
    /// which works in both Local and Orleans modes.
    /// </summary>
    [EventHandler]
    public Task HandleInitializeTestData(InitializeTestDataEvent evt)
    {
        Logger.LogInformation("🧪 Initializing test data via event");
        InitializeTestDataInternal();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Internal method to populate test data.
    /// Called by both InitializeTestDataAsync() and HandleInitializeTestData().
    /// </summary>
    private void InitializeTestDataInternal()
    {
        // 1. Profile with nested object
        State.Name = "Alice Johnson";
        State.Age = 28;
        State.Address = new AddressInfo
        {
            Street = "123 Main St",
            City = "San Francisco",
            Country = "USA",
            ZipCode = 94102
        };

        // 2. Tags (List<string>)
        State.Tags.Clear();
        State.Tags.Add("premium");
        State.Tags.Add("verified");
        State.Tags.Add("developer");

        // 3. Orders (List<nested object>)
        State.Orders.Clear();
        State.Orders.Add(new OrderItem
        {
            ProductId = "PROD-001",
            ProductName = "Laptop Pro",
            Quantity = 1,
            Price = 1299.99
        });
        State.Orders.Add(new OrderItem
        {
            ProductId = "PROD-002",
            ProductName = "Wireless Mouse",
            Quantity = 2,
            Price = 49.99
        });
        State.Orders.Add(new OrderItem
        {
            ProductId = "PROD-003",
            ProductName = "USB-C Hub",
            Quantity = 1,
            Price = 79.99
        });

        // 4. Metadata (Map<string, string>)
        State.Metadata.Clear();
        State.Metadata["theme"] = "dark";
        State.Metadata["language"] = "en-US";
        State.Metadata["timezone"] = "America/Los_Angeles";

        // 5. Scores (Map<string, int>)
        State.Scores.Clear();
        State.Scores["coding"] = 95;
        State.Scores["design"] = 78;
        State.Scores["communication"] = 88;

        // 6. Lucky numbers (List<int>)
        State.LuckyNumbers.Clear();
        State.LuckyNumbers.Add(7);
        State.LuckyNumbers.Add(13);
        State.LuckyNumbers.Add(42);

        // 7. Balance
        State.Balance = 5000.50;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);

        Logger.LogInformation("✅ Test data initialized via internal method");
    }

    /// <summary>
    /// Initialize the agent with complex data for testing.
    /// DEPRECATED: Use event-based initialization via InitializeTestDataEvent instead.
    /// This method is kept for Local mode backward compatibility.
    /// </summary>
    public async Task InitializeTestDataAsync()
    {
        Logger.LogInformation("🧪 Initializing test data (direct call - Local mode only)");
        InitializeTestDataInternal();
        
        // Manual projection trigger (since we bypassed the normal event flow)
        await OnStateChangedAsync(State, CancellationToken.None);
    }

    public Task<ComplexAgentState> GetStateAsync()
    {
        return Task.FromResult(State);
    }
}




