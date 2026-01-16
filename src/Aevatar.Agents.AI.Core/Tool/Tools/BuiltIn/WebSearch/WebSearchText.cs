namespace Aevatar.Agents.AI.Tool.Tools.BuiltIn;

internal static class WebSearchText
{
    public static string Truncate(string? s, int maxChars)
    {
        var t = s ?? string.Empty;
        if (maxChars <= 0) return string.Empty;
        if (t.Length <= maxChars) return t;
        return t[..maxChars] + "...";
    }

    public static string NormalizeWhitespace(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;
        return string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}


