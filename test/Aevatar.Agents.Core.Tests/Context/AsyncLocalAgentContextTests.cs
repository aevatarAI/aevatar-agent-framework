using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Context;

/// <summary>
/// Unit tests for AsyncLocalAgentContext
/// </summary>
public class AsyncLocalAgentContextTests
{
    [Fact(DisplayName = "Should set and get value with typed key")]
    public void Should_Set_And_Get_Value_With_Typed_Key()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        var key = new AgentContextKey<string>("TestKey");

        // Act
        context.Set(key, "TestValue");
        var result = context.Get(key);

        // Assert
        result.ShouldBe("TestValue");
    }

    [Fact(DisplayName = "Should set and get value with string key")]
    public void Should_Set_And_Get_Value_With_String_Key()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();

        // Act
        context.Set("StringKey", "StringValue");
        var result = context.Get("StringKey");

        // Assert
        result.ShouldBe("StringValue");
    }

    [Fact(DisplayName = "Should return default value when key not found")]
    public void Should_Return_Default_Value_When_Key_Not_Found()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        var key = new AgentContextKey<string>("MissingKey", "DefaultValue");

        // Act
        var result = context.Get(key);

        // Assert
        result.ShouldBe("DefaultValue");
    }

    [Fact(DisplayName = "Should return null for string key when not found")]
    public void Should_Return_Null_For_String_Key_When_Not_Found()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();

        // Act
        var result = context.Get("MissingKey");

        // Assert
        result.ShouldBeNull();
    }

    [Fact(DisplayName = "Should remove value correctly")]
    public void Should_Remove_Value_Correctly()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("Key1", "Value1");

        // Act
        context.Remove("Key1");
        var result = context.Get("Key1");

        // Assert
        result.ShouldBeNull();
    }

    [Fact(DisplayName = "Should clear all values")]
    public void Should_Clear_All_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("Key1", "Value1");
        context.Set("Key2", "Value2");

        // Act
        context.Clear();

        // Assert
        context.Get("Key1").ShouldBeNull();
        context.Get("Key2").ShouldBeNull();
        context.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "Should get all values as dictionary")]
    public void Should_Get_All_Values_As_Dictionary()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("Key1", "Value1");
        context.Set("Key2", 42);

        // Act
        var all = context.GetAll();

        // Assert
        all.Count.ShouldBe(2);
        all["Key1"].ShouldBe("Value1");
        all["Key2"].ShouldBe(42);
    }

    [Fact(DisplayName = "Should import values from dictionary")]
    public void Should_Import_Values_From_Dictionary()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        var entries = new Dictionary<string, object?>
        {
            ["Key1"] = "Value1",
            ["Key2"] = 123
        };

        // Act
        context.Import(entries);

        // Assert
        context.Get("Key1").ShouldBe("Value1");
        context.Get("Key2").ShouldBe(123);
    }

    [Fact(DisplayName = "Should support different value types")]
    public void Should_Support_Different_Value_Types()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        var stringKey = new AgentContextKey<string>("String");
        var intKey = new AgentContextKey<int>("Int");
        var boolKey = new AgentContextKey<bool>("Bool");
        var dateKey = new AgentContextKey<DateTime>("Date");
        var guidKey = new AgentContextKey<Guid>("Guid");

        var testDate = DateTime.UtcNow;
        var testGuid = Guid.NewGuid();

        // Act
        context.Set(stringKey, "test");
        context.Set(intKey, 42);
        context.Set(boolKey, true);
        context.Set(dateKey, testDate);
        context.Set(guidKey, testGuid);

        // Assert
        context.Get(stringKey).ShouldBe("test");
        context.Get(intKey).ShouldBe(42);
        context.Get(boolKey).ShouldBe(true);
        context.Get(dateKey).ShouldBe(testDate);
        context.Get(guidKey).ShouldBe(testGuid);
    }

    [Fact(DisplayName = "Should be thread-safe for concurrent access")]
    public async Task Should_Be_Thread_Safe_For_Concurrent_Access()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        var tasks = new List<Task>();

        // Act - Concurrent writes and reads
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                context.Set($"Key{index}", index);
                var value = context.Get($"Key{index}");
                // Value might be null if another thread cleared, or the expected value
                if (value != null)
                {
                    value.ShouldBe(index);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - At least some values should exist
        context.Count.ShouldBeGreaterThan(0);
    }

    [Fact(DisplayName = "ContainsKey should work correctly")]
    public void ContainsKey_Should_Work_Correctly()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("ExistingKey", "Value");

        // Assert
        context.ContainsKey("ExistingKey").ShouldBeTrue();
        context.ContainsKey("MissingKey").ShouldBeFalse();
    }
}

