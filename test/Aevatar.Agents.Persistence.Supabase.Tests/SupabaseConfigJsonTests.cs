using Aevatar.Agents.Persistence.Supabase.GAgent.Internal;
using FluentAssertions;
using Xunit;

namespace Aevatar.Agents.Persistence.Supabase.Tests;

public class SupabaseConfigJsonTests
{
    [Fact]
    public void SerializeDeserialize_ShouldRoundTrip_ProtobufConfig()
    {
        var config = new TestConfig { Setting = "hello" };

        var json = SupabaseConfigJson.Serialize(config);
        json.Should().NotBeNullOrWhiteSpace();

        var restored = SupabaseConfigJson.Deserialize<TestConfig>(json);
        restored.Should().NotBeNull();
        restored!.Setting.Should().Be("hello");
    }

    [Fact]
    public void SerializeDeserialize_ShouldRoundTrip_PlainClass()
    {
        var config = new PlainConfig { Setting = "x" };

        var json = SupabaseConfigJson.Serialize(config);
        var restored = SupabaseConfigJson.Deserialize<PlainConfig>(json);

        restored.Should().NotBeNull();
        restored!.Setting.Should().Be("x");
    }

    private sealed class PlainConfig
    {
        public string? Setting { get; set; }
    }
}


