using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

/// <summary>
/// Team leader agent - parent node
/// </summary>
public class TeamLeaderAgent : GAgentBase<TeamLeaderState>
{
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.Name = "Team Leader";
    }
    
    // Public state access method
    public new TeamLeaderState GetState() => State;
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Team Leader: {State.Name}, Managing {State.AssignedTasks.Count} tasks");
    }
    
    // Assign task
    public async Task AssignTask(string taskId, string assignTo, string description)
    {
        var evt = new TaskAssignedEvent
        {
            TaskId = taskId,
            AssignedTo = assignTo,
            Description = description
        };
        
        State.AssignedTasks.Add($"{taskId} -> {assignTo}");
        
        // 向下广播任务分配事件（发送到自己的stream，所有子节点会收到）
        await PublishAsync(evt, EventDirection.Down);
        Logger.LogInformation("Leader assigned task {TaskId} to {AssignTo}", taskId, assignTo);
    }
    
    // 处理任务完成事件（来自子节点的向上事件）
    [EventHandler]
    public async Task HandleTaskCompleted(TaskCompletedEvent evt)
    {
        State.CompletedTasks.Add($"{evt.TaskId} by {evt.CompletedBy}");
        Logger.LogInformation("Leader received: Task {TaskId} completed by {CompletedBy}", 
            evt.TaskId, evt.CompletedBy);
        await Task.CompletedTask;
    }
    
    // 处理团队消息（来自子节点的向上事件）
    [EventHandler]
    public async Task HandleTeamMessage(TeamMessageEvent evt)
    {
        Logger.LogInformation("Leader received message from {From}: {Message}", 
            evt.From, evt.Message);
        await Task.CompletedTask;
    }
}

/// <summary>
/// 团队成员Agent - 子节点
/// 使用类型约束，只处理特定的事件类型
/// </summary>
public class TeamMemberAgent : GAgentBase<TeamMemberState>
{
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        // Name will be set later via SetName
    }
    
    public void SetName(string name)
    {
        State.Name = name;
    }
    
    // 公开State访问方法
    public new TeamMemberState GetState() => State;
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Team Member: {State.Name}, Tasks: {State.MyTasks.Count}");
    }
    
    // 处理任务分配（从父节点stream广播来的事件）
    [EventHandler]
    public async Task HandleTaskAssigned(TaskAssignedEvent evt)
    {
        if (evt.AssignedTo == State.Name)
        {
            State.MyTasks.Add(evt.TaskId);
            Logger.LogInformation("{Name} received task: {TaskId} - {Description}", 
                State.Name, evt.TaskId, evt.Description);
                
            // 自动回复确认
            await SendMessageToTeam($"Got it! I'll work on {evt.TaskId}");
        }
        else
        {
            Logger.LogInformation("{Name} saw {AssignedTo} got task {TaskId}", 
                State.Name, evt.AssignedTo, evt.TaskId);
        }
    }
    
    // 完成任务
    public async Task CompleteTask(string taskId)
    {
        if (State.MyTasks.Contains(taskId))
        {
            var evt = new TaskCompletedEvent
            {
                TaskId = taskId,
                CompletedBy = State.Name
            };
            
            // 向上发布完成事件（发送到父节点stream，会广播给所有兄弟节点）
            await PublishAsync(evt, EventDirection.Up);
            Logger.LogInformation("{Name} completed task {TaskId}", State.Name, taskId);
        }
    }
    
    // 发送团队消息
    public async Task SendMessageToTeam(string message)
    {
        var evt = new TeamMessageEvent
        {
            From = State.Name,
            Message = message
        };
        
        // 向上发布消息（发送到父节点stream，实现组内广播）
        await PublishAsync(evt, EventDirection.Up);
        Logger.LogInformation("{Name} sent message: {Message}", State.Name, message);
    }
    
    // 处理团队消息（从父节点stream广播来的，包括其他成员的消息）
    [EventHandler]
    public async Task HandleTeamMessage(TeamMessageEvent evt)
    {
        if (evt.From != State.Name)
        {
            State.Messages.Add($"{evt.From}: {evt.Message}");
            Logger.LogInformation("{Name} received message from {From}: {Message}", 
                State.Name, evt.From, evt.Message);
        }
        await Task.CompletedTask;
    }
    
    // 处理任务完成事件（从父节点stream广播来的其他成员的完成事件）
    [EventHandler]
    public async Task HandleTaskCompleted(TaskCompletedEvent evt)
    {
        if (evt.CompletedBy != State.Name)
        {
            Logger.LogInformation("{Name} saw {CompletedBy} completed task {TaskId}", 
                State.Name, evt.CompletedBy, evt.TaskId);
        }
        await Task.CompletedTask;
    }
}

/// <summary>
/// 测试场景
/// </summary>
public static class HierarchicalStreamingTestScenario
{
    public static async Task RunDemo(
        IGAgentActorManager actorManager,
        ILogger logger)
    {
        logger.LogInformation("\n=== Hierarchical Streaming Demo ===\n");
        
        // 创建团队领导
        var leaderId = Guid.NewGuid().ToString();
        var leaderActor = await actorManager.CreateAndRegisterAsync<TeamLeaderAgent>(
            leaderId, 
            ct: default);
        
        // 创建3个团队成员
        var member1Id = Guid.NewGuid().ToString();
        var member1Actor = await actorManager.CreateAndRegisterAsync<TeamMemberAgent>(
            member1Id,
            ct: default);
        (member1Actor.GetAgent() as TeamMemberAgent)?.SetName("Alice");
            
        var member2Id = Guid.NewGuid().ToString();  
        var member2Actor = await actorManager.CreateAndRegisterAsync<TeamMemberAgent>(
            member2Id,
            ct: default);
        (member2Actor.GetAgent() as TeamMemberAgent)?.SetName("Bob");
            
        var member3Id = Guid.NewGuid().ToString();
        var member3Actor = await actorManager.CreateAndRegisterAsync<TeamMemberAgent>(
            member3Id,
            ct: CancellationToken.None);
        (member3Actor.GetAgent() as TeamMemberAgent)?.SetName("Charlie");
        
        // 建立父子关系（关键：这会触发子节点订阅父节点的stream）
        // Use actor.Id (full GrainKey format) for Manager operations
        await actorManager.LinkParentChildAsync(leaderActor.Id, member1Actor.Id);
        await actorManager.LinkParentChildAsync(leaderActor.Id, member2Actor.Id);
        await actorManager.LinkParentChildAsync(leaderActor.Id, member3Actor.Id);
        
        logger.LogInformation("Team structure established: 1 leader, 3 members\n");
        
        // 场景1：领导分配任务（向下广播）
        var leader = leaderActor.GetAgent() as TeamLeaderAgent;
        await leader!.AssignTask("TASK-001", "Alice", "Implement login feature");
        await Task.Delay(100); // 等待事件传播
        
        await leader.AssignTask("TASK-002", "Bob", "Write unit tests");
        await Task.Delay(100);
        
        // 场景2：成员发送消息（向上发布，广播给全组）
        var member1 = member1Actor.GetAgent() as TeamMemberAgent;
        await member1!.SendMessageToTeam("Starting work on the login feature");
        await Task.Delay(100);
        
        // 场景3：成员完成任务（向上发布）
        await member1.CompleteTask("TASK-001");
        await Task.Delay(100);
        
        var member2 = member2Actor.GetAgent() as TeamMemberAgent;
        await member2!.SendMessageToTeam("I need help with the test framework");
        await Task.Delay(100);
        
        var member3 = member3Actor.GetAgent() as TeamMemberAgent;
        await member3!.SendMessageToTeam("I can help with that!");
        await Task.Delay(100);
        
        // 场景4：另一个成员完成任务
        await member2.CompleteTask("TASK-002");
        await Task.Delay(100);
        
        logger.LogInformation("\n=== Demo Results ===");
        // 使用公开方法获取状态信息
        var leaderState = leader?.GetState();
        var member1State = member1?.GetState();
        var member2State = member2?.GetState();
        logger.LogInformation("Leader's completed tasks: {Count}", leaderState?.CompletedTasks.Count ?? 0);
        logger.LogInformation("Alice's messages received: {Count}", member1State?.Messages.Count ?? 0);
        logger.LogInformation("Bob's messages received: {Count}", member2State?.Messages.Count ?? 0);
        
        logger.LogInformation("\n=== Hierarchical Streaming Demo Completed ===\n");
    }
}
