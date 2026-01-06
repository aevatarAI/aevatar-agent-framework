using System.Text.Json;

namespace Aevatar.Agents.AI.LLMTornado.ClaudeAgentSdk;

/// <summary>
/// Protocol helpers for the external Claude Agent SDK runner.
///
/// Runner stdout conventions (MVP):
/// - Streaming delta line:  "AEVATAR_AGENT_SDK_STREAM:{text}"
/// - Final JSON envelope:  "AEVATAR_AGENT_SDK_OUTPUT:{json}"
///
/// The JSON envelope is best-effort. We only require it to contain a "content" field for non-streaming mode.
/// </summary>
internal static class ClaudeAgentSdkProtocol
{
    public const string OutputMarker = "AEVATAR_AGENT_SDK_OUTPUT:";
    public const string StreamMarker = "AEVATAR_AGENT_SDK_STREAM:";

    public static bool TryParseStreamLine(string line, out string delta)
    {
        delta = string.Empty;
        if (string.IsNullOrEmpty(line))
            return false;

        // Allow leading whitespace (some runners may prefix with logs).
        var idx = line.IndexOf(StreamMarker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;

        delta = line[(idx + StreamMarker.Length)..];
        return true;
    }

    public static bool TryExtractOutputJsonFromLine(string line, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrEmpty(line))
            return false;

        var idx = line.IndexOf(OutputMarker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;

        json = line[(idx + OutputMarker.Length)..].Trim();
        return json.Length > 0;
    }

    public static bool TryExtractOutputJsonFromText(string stdout, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(stdout))
            return false;

        // Prefer last marker (in case stdout contains multiple runs / retries).
        var idx = stdout.LastIndexOf(OutputMarker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;

        json = stdout[(idx + OutputMarker.Length)..].Trim();
        return json.Length > 0;
    }

    public static bool TryExtractContentFromOutputJson(string json, out string content)
    {
        content = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Minimal contract: { content: "..." }
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                {
                    content = c.GetString() ?? string.Empty;
                    return true;
                }

                // Some runners may emit { text: "..." }.
                if (root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    content = t.GetString() ?? string.Empty;
                    return true;
                }
            }
        }
        catch
        {
            // ignore (best-effort)
        }

        return false;
    }
}


