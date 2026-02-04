namespace Aevatar.VibeResearching.HttpApi.Host.MinimalApis;

/// <summary>
/// Validates image file format using magic byte signatures.
/// Supports JPEG, PNG, and WebP formats.
/// </summary>
public static class ImageFormatValidator
{
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] WebpRiff = [0x52, 0x49, 0x46, 0x46]; // "RIFF"

    public static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2MB

    /// <summary>
    /// Validates whether the given header bytes match a supported image format (JPEG, PNG, or WebP).
    /// </summary>
    public static bool IsValidImageFormat(byte[] header, int length)
    {
        if (length < 4) return false;

        // JPEG: FF D8 FF
        if (header[0] == JpegMagic[0] && header[1] == JpegMagic[1] && header[2] == JpegMagic[2])
            return true;

        // PNG: 89 50 4E 47
        if (header[0] == PngMagic[0] && header[1] == PngMagic[1] &&
            header[2] == PngMagic[2] && header[3] == PngMagic[3])
            return true;

        // WebP: RIFF....WEBP
        if (length >= 12 &&
            header[0] == WebpRiff[0] && header[1] == WebpRiff[1] &&
            header[2] == WebpRiff[2] && header[3] == WebpRiff[3] &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return true;

        return false;
    }

    /// <summary>
    /// Detects the content type from the given header bytes.
    /// Falls back to "image/jpeg" if the format cannot be determined.
    /// </summary>
    public static string DetectContentType(byte[] header, int length)
    {
        if (length >= 4)
        {
            if (header[0] == PngMagic[0] && header[1] == PngMagic[1] &&
                header[2] == PngMagic[2] && header[3] == PngMagic[3])
                return "image/png";

            if (length >= 12 &&
                header[0] == WebpRiff[0] && header[1] == WebpRiff[1] &&
                header[2] == WebpRiff[2] && header[3] == WebpRiff[3] &&
                header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
                return "image/webp";
        }

        // Default to JPEG (most common profile picture format)
        return "image/jpeg";
    }
}
