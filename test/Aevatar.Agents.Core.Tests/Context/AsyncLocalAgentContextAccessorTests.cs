using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Context;

/// <summary>
/// Unit tests for AsyncLocalAgentContextAccessor
/// </summary>
public class AsyncLocalAgentContextAccessorTests
{
    [Fact(DisplayName = "Should store and retrieve context")]
    public void Should_Store_And_Retrieve_Context()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var context = new AsyncLocalAgentContext();
        context.Set("Key", "Value");

        // Act
        accessor.Context = context;

        // Assert
        accessor.Context.ShouldBe(context);
        accessor.Context!.Get("Key").ShouldBe("Value");
    }

    [Fact(DisplayName = "Context should be null by default")]
    public void Context_Should_Be_Null_By_Default()
    {
        // Arrange & Act
        var accessor = new AsyncLocalAgentContextAccessor();

        // Assert
        accessor.Context.ShouldBeNull();
    }

    [Fact(DisplayName = "Context should flow across async operations")]
    public async Task Context_Should_Flow_Across_Async_Operations()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var context = new AsyncLocalAgentContext();
        context.Set("AsyncKey", "AsyncValue");
        accessor.Context = context;

        // Act
        string? result = null;
        await Task.Run(async () =>
        {
            await Task.Delay(10);
            result = accessor.Context?.Get("AsyncKey")?.ToString();
        });

        // Assert
        result.ShouldBe("AsyncValue");
    }

    [Fact(DisplayName = "Context should be isolated between tasks")]
    public async Task Context_Should_Be_Isolated_Between_Tasks()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();

        // Act - Run two tasks with different contexts
        var task1Result = await Task.Run(() =>
        {
            var ctx = new AsyncLocalAgentContext();
            ctx.Set("TaskId", "Task1");
            accessor.Context = ctx;
            Thread.Sleep(50); // Simulate work
            return accessor.Context?.Get("TaskId")?.ToString();
        });

        var task2Result = await Task.Run(() =>
        {
            var ctx = new AsyncLocalAgentContext();
            ctx.Set("TaskId", "Task2");
            accessor.Context = ctx;
            Thread.Sleep(50);
            return accessor.Context?.Get("TaskId")?.ToString();
        });

        // Assert - Each task should have its own context
        task1Result.ShouldBe("Task1");
        task2Result.ShouldBe("Task2");
    }

    [Fact(DisplayName = "GetOrCreate extension should create context if null")]
    public void GetOrCreate_Should_Create_Context_If_Null()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        accessor.Context.ShouldBeNull();

        // Act
        var context = accessor.GetOrCreate();

        // Assert
        context.ShouldNotBeNull();
        accessor.Context.ShouldNotBeNull();
    }

    [Fact(DisplayName = "GetOrCreate extension should return existing context")]
    public void GetOrCreate_Should_Return_Existing_Context()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var existingContext = new AsyncLocalAgentContext();
        existingContext.Set("Existing", "Value");
        accessor.Context = existingContext;

        // Act
        var context = accessor.GetOrCreate();

        // Assert
        context.ShouldBe(existingContext);
        context.Get("Existing").ShouldBe("Value");
    }

    [Fact(DisplayName = "Concurrent access should be thread-safe")]
    public async Task Concurrent_Access_Should_Be_Thread_Safe()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var tasks = new List<Task<string?>>();

        // Act - Multiple concurrent tasks setting different values
        for (int i = 0; i < 50; i++)
        {
            var taskId = i.ToString();
            tasks.Add(Task.Run(async () =>
            {
                var ctx = new AsyncLocalAgentContext();
                ctx.Set("TaskId", taskId);
                accessor.Context = ctx;
                
                await Task.Delay(Random.Shared.Next(1, 10));
                
                return accessor.Context?.Get("TaskId")?.ToString();
            }));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - Each task should see its own value
        for (int i = 0; i < 50; i++)
        {
            results[i].ShouldBe(i.ToString());
        }
    }
}

