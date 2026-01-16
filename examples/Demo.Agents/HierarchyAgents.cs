using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

// Manager agent
public class ManagerAgent : GAgentBase<ManagerState>
{
    
    [AllEventHandler]
    public Task HandleManagementEvent(EventEnvelope envelope)
    {
        // EventsReceived field does not exist in proto, using TeamSize instead
        State.TeamSize++;
        Logger?.LogInformation("Manager {Id} handling management event", Id);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Manager {Id}: Department {State.Department}, Team size: {State.TeamSize}");
    }
}

// ManagerState is defined in demo_messages.proto

// Employee agent
public class EmployeeAgent : GAgentBase<EmployeeState>
{
    
    [AllEventHandler]
    public Task HandleWorkEvent(EventEnvelope envelope)
    {
        // TasksCompleted field does not exist in proto
        State.Role = "Working";
        Logger?.LogInformation("Employee {Id} working on task", Id);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Employee {Id}: {State.Name}, Role: {State.Role}");
    }
}

// EmployeeState is defined in demo_messages.proto

// Hierarchy agent
public class HierarchyAgent : GAgentBase<HierarchyState>
{
    
    [EventHandler]
    public Task HandleHierarchyMessage(HierarchyMessage message)
    {
        // MessagesReceived and LastMessageDirection fields do not exist in proto
        State.Level++;
        Logger?.LogInformation("HierarchyAgent {Id} received hierarchy message: {Content}", 
            Id, message.Content);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"HierarchyAgent {Id}: Level {State.Level}, Children: {State.Children.Count}");
    }
}

// HierarchyState is defined in demo_messages.proto
