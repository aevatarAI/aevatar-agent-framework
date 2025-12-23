using Aevatar.Agents.Abstractions.Context;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Context;

/// <summary>
/// Unit tests for AgentContextKey
/// </summary>
public class AgentContextKeyTests
{
    [Fact(DisplayName = "AgentContextKey should store name correctly")]
    public void Should_Store_Name_Correctly()
    {
        // Arrange & Act
        var key = new AgentContextKey<string>("TestKey");

        // Assert
        key.Name.ShouldBe("TestKey");
        key.DefaultValue.ShouldBeNull();
    }

    [Fact(DisplayName = "AgentContextKey should store default value")]
    public void Should_Store_Default_Value()
    {
        // Arrange & Act
        var key = new AgentContextKey<string>("Language", "en");

        // Assert
        key.Name.ShouldBe("Language");
        key.DefaultValue.ShouldBe("en");
    }

    [Fact(DisplayName = "AgentContextKey should support value types with default")]
    public void Should_Support_Value_Types_With_Default()
    {
        // Arrange & Act
        var boolKey = new AgentContextKey<bool>("IsCN", false);
        var intKey = new AgentContextKey<int>("Count", 10);

        // Assert
        boolKey.DefaultValue.ShouldBe(false);
        intKey.DefaultValue.ShouldBe(10);
    }

    [Fact(DisplayName = "AgentContextKey should implement equality correctly")]
    public void Should_Implement_Equality_Correctly()
    {
        // Arrange
        var key1 = new AgentContextKey<string>("TestKey");
        var key2 = new AgentContextKey<string>("TestKey");
        var key3 = new AgentContextKey<string>("DifferentKey");

        // Assert
        key1.Equals(key2).ShouldBeTrue();
        (key1 == key2).ShouldBeTrue();
        (key1 != key3).ShouldBeTrue();
        key1.GetHashCode().ShouldBe(key2.GetHashCode());
    }

    [Fact(DisplayName = "AgentContextKey ToString should return name")]
    public void ToString_Should_Return_Name()
    {
        // Arrange
        var key = new AgentContextKey<string>("MyKey");

        // Act & Assert
        key.ToString().ShouldBe("MyKey");
    }

    [Fact(DisplayName = "AgentContextKey should throw on null name")]
    public void Should_Throw_On_Null_Name()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new AgentContextKey<string>(null!));
    }
}

