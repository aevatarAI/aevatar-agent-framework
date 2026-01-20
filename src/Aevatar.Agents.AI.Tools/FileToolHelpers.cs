using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Tools;

internal static class FileToolHelpers
{
    public static bool IsWriteExtensionAllowed(FileToolOptions options, string path)
    {
        var ext = Path.GetExtension(path);
        var allowed = options.WriteExtensions.Count == 0
            ? new[] { ".yaml", ".yml", ".json" }
            : options.WriteExtensions;
        if (allowed.Any(x => x is "*" or ".*"))
            return true;
        return allowed.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryResolvePath(
        FileToolOptions options,
        string rawPath,
        IReadOnlyList<string> roots,
        out string fullPath,
        out string reason)
    {
        fullPath = string.Empty;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            reason = "path_empty";
            return false;
        }

        if (roots.Count == 0)
        {
            reason = "path_allowlist_empty";
            return false;
        }

        var normalized = NormalizePath(options, rawPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            reason = "path_invalid";
            return false;
        }

        foreach (var root in roots)
        {
            if (IsWithinRoot(root, normalized))
            {
                fullPath = normalized;
                return true;
            }
        }

        reason = "path_not_allowed";
        return false;
    }

    public static async Task<(string Content, bool Truncated)> ReadTextAsync(
        string path,
        int maxChars,
        CancellationToken ct)
    {
        var buffer = new char[4096];
        var sb = new StringBuilder();

        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (sb.Length < maxChars)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = Math.Min(buffer.Length, maxChars - sb.Length);
            var read = await reader.ReadAsync(buffer.AsMemory(0, remaining), ct);
            if (read <= 0)
                return (sb.ToString(), false);
            sb.Append(buffer, 0, read);
        }

        var probe = await reader.ReadAsync(buffer.AsMemory(0, 1), ct);
        var truncated = probe > 0;
        return (sb.ToString(), truncated);
    }

    public static bool TryGetString(Dictionary<string, object> parameters, string key, out string value)
    {
        value = string.Empty;
        if (!parameters.TryGetValue(key, out var raw) || raw == null)
            return false;
        value = raw.ToString() ?? string.Empty;
        return value.Trim().Length > 0;
    }

    public static bool TryGetBool(object? raw)
    {
        if (raw == null) return false;
        if (raw is bool b) return b;
        if (bool.TryParse(raw.ToString(), out var parsed)) return parsed;
        return false;
    }

    public static int ClampInt(object? raw, int fallback, int min, int max)
    {
        if (raw is int i) return Math.Clamp(i, min, max);
        if (raw is long l) return Math.Clamp((int)l, min, max);
        if (raw != null && int.TryParse(raw.ToString(), out var parsed))
            return Math.Clamp(parsed, min, max);
        return Math.Clamp(fallback, min, max);
    }

    public static Struct ToStruct(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonParser.Default.Parse<Struct>(json);
    }

    private static string NormalizePath(FileToolOptions options, string path)
    {
        var value = ExpandHome(path.Trim());
        if (!Path.IsPathRooted(value))
        {
            var baseDir = string.IsNullOrWhiteSpace(options.WorkingDirectory)
                ? Directory.GetCurrentDirectory()
                : options.WorkingDirectory;
            value = Path.Combine(baseDir, value);
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExpandHome(string path)
    {
        if (!path.StartsWith("~", StringComparison.Ordinal))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.Length == 1)
            return home;

        if (path[1] == Path.DirectorySeparatorChar || path[1] == Path.AltDirectorySeparatorChar)
            return Path.Combine(home, path[2..]);

        return path;
    }

    private static bool IsWithinRoot(string root, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(fullPath))
            return false;

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(root.Trim());
        }
        catch
        {
            return false;
        }

        var basePath = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = basePath + Path.DirectorySeparatorChar;

        return string.Equals(fullPath, basePath, StringComparison.Ordinal)
               || fullPath.StartsWith(prefix, StringComparison.Ordinal);
    }
}
