using Aevatar.VibeResearching.HttpApi.Host.MinimalApis;
using Shouldly;

namespace VibeResearching.Api.Tests.Auth;

/// <summary>
/// Unit tests for ImageFormatValidator — magic byte detection and content type resolution.
/// Covers C-1 fix: avatar download returns correct Content-Type for JPEG, PNG, and WebP.
/// </summary>
public sealed class ImageFormatValidatorTests
{
    // ─────────────────────────────────────────────────────────────
    //  IsValidImageFormat
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsValidImageFormat_Jpeg_ReturnsTrue()
    {
        var header = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 };
        ImageFormatValidator.IsValidImageFormat(header, header.Length).ShouldBeTrue();
    }

    [Fact]
    public void IsValidImageFormat_Png_ReturnsTrue()
    {
        var header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        ImageFormatValidator.IsValidImageFormat(header, header.Length).ShouldBeTrue();
    }

    [Fact]
    public void IsValidImageFormat_WebP_ReturnsTrue()
    {
        // RIFF....WEBP
        var header = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 };
        ImageFormatValidator.IsValidImageFormat(header, header.Length).ShouldBeTrue();
    }

    [Fact]
    public void IsValidImageFormat_EmptyBytes_ReturnsFalse()
    {
        var header = new byte[12];
        ImageFormatValidator.IsValidImageFormat(header, 0).ShouldBeFalse();
    }

    [Fact]
    public void IsValidImageFormat_TooShort_ReturnsFalse()
    {
        var header = new byte[] { 0xFF, 0xD8, 0xFF };
        ImageFormatValidator.IsValidImageFormat(header, 3).ShouldBeFalse();
    }

    [Fact]
    public void IsValidImageFormat_RandomBytes_ReturnsFalse()
    {
        var header = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B };
        ImageFormatValidator.IsValidImageFormat(header, header.Length).ShouldBeFalse();
    }

    [Fact]
    public void IsValidImageFormat_GifMagic_ReturnsFalse()
    {
        // GIF89a header — not an allowed format
        var header = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0, 0, 0, 0, 0 };
        ImageFormatValidator.IsValidImageFormat(header, header.Length).ShouldBeFalse();
    }

    [Fact]
    public void IsValidImageFormat_WebPIncomplete_ReturnsFalse()
    {
        // RIFF header but missing WEBP signature
        var header = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x41, 0x56, 0x49, 0x20 };
        ImageFormatValidator.IsValidImageFormat(header, header.Length).ShouldBeFalse();
    }

    // ─────────────────────────────────────────────────────────────
    //  DetectContentType
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DetectContentType_Jpeg_ReturnsImageJpeg()
    {
        var header = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 };
        ImageFormatValidator.DetectContentType(header, header.Length).ShouldBe("image/jpeg");
    }

    [Fact]
    public void DetectContentType_Png_ReturnsImagePng()
    {
        var header = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        ImageFormatValidator.DetectContentType(header, header.Length).ShouldBe("image/png");
    }

    [Fact]
    public void DetectContentType_WebP_ReturnsImageWebp()
    {
        var header = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 };
        ImageFormatValidator.DetectContentType(header, header.Length).ShouldBe("image/webp");
    }

    [Fact]
    public void DetectContentType_UnknownFormat_DefaultsToJpeg()
    {
        var header = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B };
        ImageFormatValidator.DetectContentType(header, header.Length).ShouldBe("image/jpeg");
    }

    [Fact]
    public void DetectContentType_EmptyBytes_DefaultsToJpeg()
    {
        var header = new byte[12];
        ImageFormatValidator.DetectContentType(header, 0).ShouldBe("image/jpeg");
    }

    // ─────────────────────────────────────────────────────────────
    //  AllowedContentTypes
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("IMAGE/JPEG")] // Case-insensitive
    [InlineData("Image/Png")]
    public void AllowedContentTypes_Contains_SupportedFormats(string contentType)
    {
        ImageFormatValidator.AllowedContentTypes.Contains(contentType).ShouldBeTrue();
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("image/bmp")]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    public void AllowedContentTypes_DoesNotContain_UnsupportedFormats(string contentType)
    {
        ImageFormatValidator.AllowedContentTypes.Contains(contentType).ShouldBeFalse();
    }

    [Fact]
    public void MaxFileSizeBytes_Is2MB()
    {
        ImageFormatValidator.MaxFileSizeBytes.ShouldBe(2 * 1024 * 1024);
    }
}
