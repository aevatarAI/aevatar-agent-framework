using Aevatar.VibeResearching.UserProviders.ValueObjects;
using Xunit;

namespace Aevatar.VibeResearching.UserProviders.Tests.ValueObjects;

/// <summary>
/// Unit tests for the ProviderNamespace value object.
/// Covers parsing of user, platform, and unprefixed (backward compat) namespaces.
/// </summary>
public sealed class ProviderNamespaceTests
{
    [Fact]
    public void Parse_UserPrefix_ReturnsUserScope()
    {
        // Act
        var ns = ProviderNamespace.Parse("user:abc123");

        // Assert
        Assert.True(ns.IsUser);
        Assert.False(ns.IsPlatform);
        Assert.Equal("user", ns.Scope);
        Assert.Equal("abc123", ns.Identifier);
    }

    [Fact]
    public void Parse_PlatformPrefix_ReturnsPlatformScope()
    {
        // Act
        var ns = ProviderNamespace.Parse("platform:openai-gpt4");

        // Assert
        Assert.True(ns.IsPlatform);
        Assert.False(ns.IsUser);
        Assert.Equal("platform", ns.Scope);
        Assert.Equal("openai-gpt4", ns.Identifier);
    }

    [Fact]
    public void Parse_Unprefixed_DefaultsToPlatform()
    {
        // Backward compatibility: unprefixed = platform
        var ns = ProviderNamespace.Parse("openai-gpt4");

        Assert.True(ns.IsPlatform);
        Assert.Equal("platform", ns.Scope);
        Assert.Equal("openai-gpt4", ns.Identifier);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsPlatformWithEmptyIdentifier()
    {
        var ns = ProviderNamespace.Parse("");

        Assert.True(ns.IsPlatform);
        Assert.Equal("", ns.Identifier);
    }

    [Fact]
    public void Parse_Null_ReturnsPlatformWithEmptyIdentifier()
    {
        var ns = ProviderNamespace.Parse(null!);

        Assert.True(ns.IsPlatform);
        Assert.Equal("", ns.Identifier);
    }

    [Fact]
    public void ToString_FormatsCorrectly()
    {
        var ns = ProviderNamespace.ForUser("abc123");
        Assert.Equal("user:abc123", ns.ToString());

        var platformNs = ProviderNamespace.ForPlatform("openai-gpt4");
        Assert.Equal("platform:openai-gpt4", platformNs.ToString());
    }

    [Fact]
    public void ForUser_CreatesUserNamespace()
    {
        var ns = ProviderNamespace.ForUser("myId");

        Assert.True(ns.IsUser);
        Assert.Equal("myId", ns.Identifier);
    }

    [Fact]
    public void ForPlatform_CreatesPlatformNamespace()
    {
        var ns = ProviderNamespace.ForPlatform("myProvider");

        Assert.True(ns.IsPlatform);
        Assert.Equal("myProvider", ns.Identifier);
    }

    [Fact]
    public void Parse_WithWhitespace_TrimsInput()
    {
        var ns = ProviderNamespace.Parse("  user:abc123  ");

        Assert.True(ns.IsUser);
        Assert.Equal("abc123", ns.Identifier);
    }
}
