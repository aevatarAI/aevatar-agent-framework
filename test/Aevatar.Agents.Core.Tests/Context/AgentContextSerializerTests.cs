using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Context;

/// <summary>
/// Unit tests for AgentContextSerializer using ContextValue protobuf type.
/// </summary>
public class AgentContextSerializerTests
{
    [Fact(DisplayName = "Should serialize string values")]
    public void Should_Serialize_String_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set(AgentContextKeys.UserId, "user123");

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("UserId");
        serialized["UserId"].StringValue.ShouldBe("user123");
    }

    [Fact(DisplayName = "Should serialize bool values")]
    public void Should_Serialize_Bool_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set(AgentContextKeys.IsCN, true);

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("IsCN");
        serialized["IsCN"].BoolValue.ShouldBe(true);
    }

    [Fact(DisplayName = "Should serialize int values")]
    public void Should_Serialize_Int_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("Count", 42);

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("Count");
        serialized["Count"].IntValue.ShouldBe(42);
    }

    [Fact(DisplayName = "Should serialize long values")]
    public void Should_Serialize_Long_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("BigNumber", 9223372036854775807L);

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("BigNumber");
        serialized["BigNumber"].IntValue.ShouldBe(9223372036854775807L);
    }

    [Fact(DisplayName = "Should serialize double values")]
    public void Should_Serialize_Double_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("Price", 3.14159);

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("Price");
        serialized["Price"].DoubleValue.ShouldBe(3.14159);
    }

    [Fact(DisplayName = "Should serialize DateTime values")]
    public void Should_Serialize_DateTime_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        var testDate = new DateTime(2024, 12, 22, 10, 30, 0, DateTimeKind.Utc);
        context.Set(AgentContextKeys.RequestTime, testDate);

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("RequestTime");
        serialized["RequestTime"].DatetimeIso.ShouldNotBeEmpty();
    }

    [Fact(DisplayName = "Should serialize Guid values")]
    public void Should_Serialize_Guid_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        var testGuid = Guid.NewGuid();
        context.Set("RequestId", testGuid);

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("RequestId");
        serialized["RequestId"].GuidString.ShouldBe(testGuid.ToString("D"));
    }

    [Fact(DisplayName = "Should deserialize string values")]
    public void Should_Deserialize_String_Values()
    {
        // Arrange
        var metadata = new Dictionary<string, ContextValue>
        {
            ["UserId"] = new ContextValue { StringValue = "user123" }
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("UserId").ShouldBe("user123");
    }

    [Fact(DisplayName = "Should deserialize bool values")]
    public void Should_Deserialize_Bool_Values()
    {
        // Arrange
        var metadata = new Dictionary<string, ContextValue>
        {
            ["IsCN"] = new ContextValue { BoolValue = true }
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("IsCN").ShouldBe(true);
    }

    [Fact(DisplayName = "Should deserialize int values")]
    public void Should_Deserialize_Int_Values()
    {
        // Arrange
        var metadata = new Dictionary<string, ContextValue>
        {
            ["Count"] = new ContextValue { IntValue = 42 }
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("Count").ShouldBe(42L); // IntValue is int64
    }

    [Fact(DisplayName = "Should deserialize double values")]
    public void Should_Deserialize_Double_Values()
    {
        // Arrange
        var metadata = new Dictionary<string, ContextValue>
        {
            ["Price"] = new ContextValue { DoubleValue = 3.14159 }
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        ((double)context.Get("Price")!).ShouldBe(3.14159, 0.00001);
    }

    [Fact(DisplayName = "Should deserialize Guid values")]
    public void Should_Deserialize_Guid_Values()
    {
        // Arrange
        var testGuid = Guid.NewGuid();
        var metadata = new Dictionary<string, ContextValue>
        {
            ["RequestId"] = new ContextValue { GuidString = testGuid.ToString("D") }
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("RequestId").ShouldBe(testGuid);
    }

    [Fact(DisplayName = "Should deserialize DateTime values")]
    public void Should_Deserialize_DateTime_Values()
    {
        // Arrange
        var testDate = new DateTime(2024, 12, 22, 10, 30, 0, DateTimeKind.Utc);
        var metadata = new Dictionary<string, ContextValue>
        {
            ["RequestTime"] = new ContextValue { DatetimeIso = testDate.ToString("O") }
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("RequestTime").ShouldBe(testDate);
    }

    [Fact(DisplayName = "Should round-trip all supported types")]
    public void Should_Round_Trip_All_Supported_Types()
    {
        // Arrange
        var originalContext = new AsyncLocalAgentContext();
        var testDate = DateTime.UtcNow;
        var testGuid = Guid.NewGuid();
        
        originalContext.Set(AgentContextKeys.CorrelationId, "corr-123");
        originalContext.Set(AgentContextKeys.UserId, "user456");
        originalContext.Set(AgentContextKeys.Language, "zh");
        originalContext.Set(AgentContextKeys.IsCN, true);
        originalContext.Set(AgentContextKeys.RequestTime, testDate);
        originalContext.Set("RequestId", testGuid);
        originalContext.Set("Count", 42);
        originalContext.Set("Price", 99.99);

        // Act - Serialize then deserialize
        var serialized = AgentContextSerializer.Serialize(originalContext);
        var restoredContext = new AsyncLocalAgentContext();
        AgentContextSerializer.Deserialize(serialized, restoredContext);

        // Assert
        restoredContext.Get(AgentContextKeys.CorrelationId).ShouldBe("corr-123");
        restoredContext.Get(AgentContextKeys.UserId).ShouldBe("user456");
        restoredContext.Get(AgentContextKeys.Language).ShouldBe("zh");
        restoredContext.Get(AgentContextKeys.IsCN).ShouldBe(true);
        restoredContext.Get("RequestId").ShouldBe(testGuid);
        restoredContext.Get("Count").ShouldBe(42L);
        ((double)restoredContext.Get("Price")!).ShouldBe(99.99, 0.001);
    }

    [Fact(DisplayName = "Should respect MaxKeys limit")]
    public void Should_Respect_MaxKeys_Limit()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        var options = new AgentContextPropagationOptions { MaxKeys = 3 };
        
        // Add more keys than MaxKeys
        for (var i = 0; i < 10; i++)
        {
            context.Set($"Key{i}", $"value{i}");
        }

        // Act
        var serialized = AgentContextSerializer.Serialize(context, options);

        // Assert
        serialized.Count.ShouldBeLessThanOrEqualTo(options.MaxKeys);
    }

    [Fact(DisplayName = "Should handle empty context")]
    public void Should_Handle_Empty_Context()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Should handle empty metadata")]
    public void Should_Handle_Empty_Metadata()
    {
        // Arrange
        var metadata = new Dictionary<string, ContextValue>();
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "Should serialize all keys by default (no allowlist)")]
    public void Should_Serialize_All_Keys_By_Default()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("CustomKey", "custom-value");
        context.Set(AgentContextKeys.UserId, "user123");

        // Act - default options (no allowlist)
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("CustomKey");
        serialized.ShouldContainKey("UserId");
    }

    [Fact(DisplayName = "Should only serialize allowlisted keys when configured")]
    public void Should_Only_Serialize_Allowlisted_Keys_When_Configured()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("NotAllowedKey", "value");
        context.Set(AgentContextKeys.UserId, "user123");

        // Use options with allowlist
        var options = new AgentContextPropagationOptions()
            .Allow("UserId");

        // Act
        var serialized = AgentContextSerializer.Serialize(context, options);

        // Assert
        serialized.ShouldNotContainKey("NotAllowedKey");
        serialized.ShouldContainKey("UserId");
    }

    [Fact(DisplayName = "Should respect denied keys")]
    public void Should_Respect_Denied_Keys()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("SensitiveKey", "secret");
        context.Set(AgentContextKeys.UserId, "user123");

        var options = new AgentContextPropagationOptions()
            .Deny("SensitiveKey");

        // Act
        var serialized = AgentContextSerializer.Serialize(context, options);

        // Assert
        serialized.ShouldNotContainKey("SensitiveKey");
        serialized.ShouldContainKey("UserId");
    }

    [Fact(DisplayName = "Should handle float values (converted to double)")]
    public void Should_Handle_Float_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("FloatValue", 3.14f);

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("FloatValue");
        serialized["FloatValue"].DoubleValue.ShouldBe(3.14, 0.01);
    }

    [Fact(DisplayName = "Should convert unsupported types to string")]
    public void Should_Convert_Unsupported_Types_To_String()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("CustomObject", new { Name = "Test" });

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("CustomObject");
        serialized["CustomObject"].ValueCase.ShouldBe(ContextValue.ValueOneofCase.StringValue);
    }

    [Fact(DisplayName = "Typed keys should round-trip with numeric conversions")]
    public void Typed_Keys_Should_Round_Trip_With_Numeric_Conversions()
    {
        // Arrange
        var countKey = new AgentContextKey<int>("Count", -1);
        var floatKey = new AgentContextKey<float>("FloatValue", -1f);

        var originalContext = new AsyncLocalAgentContext();
        originalContext.Set(countKey, 42);
        originalContext.Set(floatKey, 3.14f);

        // Act
        var serialized = AgentContextSerializer.Serialize(originalContext);
        var restoredContext = new AsyncLocalAgentContext();
        AgentContextSerializer.Deserialize(serialized, restoredContext);

        // Assert
        restoredContext.Get(countKey).ShouldBe(42);
        restoredContext.Get(floatKey).ShouldBe(3.14f, 0.01f);
    }

    [Fact(DisplayName = "MaxTotalBytes should skip oversized entry and continue")]
    public void MaxTotalBytes_Should_Skip_Oversized_Entry_And_Continue()
    {
        // Arrange - deterministic ordering: big first, small second
        var context = new OrderedContext()
            .Add("Big", new string('x', 10_000))
            .Add("Small", "ok");

        var options = new AgentContextPropagationOptions
        {
            MaxKeys = 32,
            MaxTotalBytes = 128
        };

        // Act
        var serialized = AgentContextSerializer.Serialize(context, options);

        // Assert
        serialized.ShouldNotContainKey("Big");
        serialized.ShouldContainKey("Small");
    }

    private sealed class OrderedContext : IAgentContext
    {
        private readonly List<KeyValuePair<string, object?>> _entries = new();

        public OrderedContext Add(string key, object? value)
        {
            _entries.Add(new KeyValuePair<string, object?>(key, value));
            return this;
        }

        public T? Get<T>(AgentContextKey<T> key) => throw new NotSupportedException();
        public object? Get(string key) => throw new NotSupportedException();
        public void Set<T>(AgentContextKey<T> key, T value) => throw new NotSupportedException();
        public void Set(string key, object? value) => throw new NotSupportedException();
        public void Remove(string key) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();

        public IReadOnlyDictionary<string, object?> GetAll()
        {
            // Keep insertion order (Dictionary preserves insertion order in modern .NET)
            return _entries.ToDictionary(x => x.Key, x => x.Value);
        }

        public void Import(IReadOnlyDictionary<string, object?> entries) => throw new NotSupportedException();
    }
}
