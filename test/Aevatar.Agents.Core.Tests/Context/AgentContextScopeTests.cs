using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Context;

/// <summary>
/// Unit tests for AgentContextScope
/// </summary>
public class AgentContextScopeTests
{
    [Fact(DisplayName = "Scope should set new context")]
    public void Scope_Should_Set_New_Context()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var newContext = new AsyncLocalAgentContext();
        newContext.Set("Key", "NewValue");

        // Act
        using (new AgentContextScope(accessor, newContext))
        {
            // Assert - Inside scope, new context is active
            accessor.Context.ShouldBe(newContext);
            accessor.Context!.Get("Key").ShouldBe("NewValue");
        }
    }

    [Fact(DisplayName = "Scope should restore previous context on dispose")]
    public void Scope_Should_Restore_Previous_Context_On_Dispose()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var previousContext = new AsyncLocalAgentContext();
        previousContext.Set("Key", "PreviousValue");
        accessor.Context = previousContext;

        var newContext = new AsyncLocalAgentContext();
        newContext.Set("Key", "NewValue");

        // Act
        using (new AgentContextScope(accessor, newContext))
        {
            accessor.Context!.Get("Key").ShouldBe("NewValue");
        }

        // Assert - After scope, previous context is restored
        accessor.Context.ShouldBe(previousContext);
        accessor.Context!.Get("Key").ShouldBe("PreviousValue");
    }

    [Fact(DisplayName = "Scope should restore null context")]
    public void Scope_Should_Restore_Null_Context()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        accessor.Context = null; // No previous context

        var newContext = new AsyncLocalAgentContext();
        newContext.Set("Key", "Value");

        // Act
        using (new AgentContextScope(accessor, newContext))
        {
            accessor.Context.ShouldNotBeNull();
        }

        // Assert - After scope, null is restored
        accessor.Context.ShouldBeNull();
    }

    [Fact(DisplayName = "Nested scopes should work correctly")]
    public void Nested_Scopes_Should_Work_Correctly()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var context1 = new AsyncLocalAgentContext();
        context1.Set("Level", "1");
        var context2 = new AsyncLocalAgentContext();
        context2.Set("Level", "2");
        var context3 = new AsyncLocalAgentContext();
        context3.Set("Level", "3");

        accessor.Context = context1;

        // Act & Assert
        accessor.Context!.Get("Level").ShouldBe("1");

        using (new AgentContextScope(accessor, context2))
        {
            accessor.Context!.Get("Level").ShouldBe("2");

            using (new AgentContextScope(accessor, context3))
            {
                accessor.Context!.Get("Level").ShouldBe("3");
            }

            // Back to level 2
            accessor.Context!.Get("Level").ShouldBe("2");
        }

        // Back to level 1
        accessor.Context!.Get("Level").ShouldBe("1");
    }

    [Fact(DisplayName = "Scope should work with async operations")]
    public async Task Scope_Should_Work_With_Async_Operations()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var outerContext = new AsyncLocalAgentContext();
        outerContext.Set("Outer", "value");
        accessor.Context = outerContext;

        var innerContext = new AsyncLocalAgentContext();
        innerContext.Set("Inner", "value");

        // Act
        using (new AgentContextScope(accessor, innerContext))
        {
            accessor.Context!.Get("Inner").ShouldBe("value");
            accessor.Context!.Get("Outer").ShouldBeNull();

            // Async operation
            await Task.Delay(10);

            // Context should still be the inner one
            accessor.Context!.Get("Inner").ShouldBe("value");
        }

        // Assert - After scope and async
        accessor.Context!.Get("Outer").ShouldBe("value");
        accessor.Context!.Get("Inner").ShouldBeNull();
    }

    [Fact(DisplayName = "Default scope should not change context")]
    public void Default_Scope_Should_Not_Change_Context()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var originalContext = new AsyncLocalAgentContext();
        originalContext.Set("Key", "Original");
        accessor.Context = originalContext;

        // Act - Using default struct (when condition is false)
        var scope = default(AgentContextScope);
        scope.Dispose(); // Should be safe to dispose default

        // Assert
        accessor.Context.ShouldBe(originalContext);
    }

    [Fact(DisplayName = "Scope should restore previous values for ambient accessor")]
    public void Scope_Should_Restore_Previous_Values_For_Ambient_Accessor()
    {
        // Arrange - accessor does NOT store context by reference (Orleans-like bridge semantics)
        var accessor = new AmbientAccessor();
        accessor.Context!.Set("Key", "PreviousValue");

        var newContext = new AsyncLocalAgentContext();
        newContext.Set("Key", "NewValue");

        // Act
        using (new AgentContextScope(accessor, newContext))
        {
            accessor.Context!.Get("Key").ShouldBe("NewValue");
        }

        // Assert - restored by snapshot
        accessor.Context!.Get("Key").ShouldBe("PreviousValue");
    }

    private sealed class AmbientAccessor : IAgentContextAccessor
    {
        private readonly AsyncLocalAgentContext _ambient = new();

        public IAgentContext? Context
        {
            get => _ambient;
            set
            {
                _ambient.Clear();
                if (value != null)
                {
                    _ambient.Import(value.GetAll());
                }
            }
        }
    }
}

