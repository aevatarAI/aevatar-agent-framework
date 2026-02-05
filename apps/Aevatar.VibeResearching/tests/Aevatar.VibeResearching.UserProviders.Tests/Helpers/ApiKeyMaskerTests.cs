using Aevatar.VibeResearching.UserProviders.Infrastructure.Helpers;
using Xunit;

namespace Aevatar.VibeResearching.UserProviders.Tests.Helpers;

/// <summary>
/// Unit tests for the API key masking helper.
/// </summary>
public sealed class ApiKeyMaskerTests
{
    [Fact]
    public void Mask_NormalKey_ShowsFirstAndLastFourChars()
    {
        // Arrange
        var key = "sk-proj-abc123def456ghi789";

        // Act
        var masked = ApiKeyMasker.Mask(key);

        // Assert
        Assert.StartsWith("sk-p", masked);
        Assert.EndsWith("i789", masked);
        Assert.Contains("*", masked);
    }

    [Fact]
    public void Mask_ShortKey_ReturnsFullMask()
    {
        // Keys shorter than 12 chars are fully masked
        var masked = ApiKeyMasker.Mask("short");

        Assert.Equal("****", masked);
    }

    [Fact]
    public void Mask_EmptyKey_ReturnsMask()
    {
        Assert.Equal("****", ApiKeyMasker.Mask(""));
    }

    [Fact]
    public void Mask_NullKey_ReturnsMask()
    {
        Assert.Equal("****", ApiKeyMasker.Mask(null));
    }

    [Fact]
    public void Mask_ExactlyTwelveChars_ShowsPartialMask()
    {
        var key = "123456789012"; // 12 chars

        var masked = ApiKeyMasker.Mask(key);

        Assert.StartsWith("1234", masked);
        Assert.EndsWith("9012", masked);
    }

    [Fact]
    public void Mask_LongKey_MasksMiddle()
    {
        var key = "sk-proj-very-long-api-key-that-goes-on-and-on-and-on-forever";

        var masked = ApiKeyMasker.Mask(key);

        Assert.StartsWith("sk-p", masked);
        Assert.EndsWith("ever", masked);
        Assert.Contains("*", masked);
        Assert.True(masked.Length < key.Length); // Middle is truncated to max 28 asterisks
    }
}
