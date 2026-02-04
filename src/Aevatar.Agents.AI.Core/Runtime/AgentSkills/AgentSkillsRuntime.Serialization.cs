using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core;

internal sealed partial class AgentSkillsRuntime
{
    internal static Struct ToStruct(object obj)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        return JsonParser.Default.Parse<Struct>(json);
    }

    private static bool TryGetBool(object? v, bool fallback)
    {
        if (v is bool b) return b;
        if (v is string s && bool.TryParse(s, out var parsed)) return parsed;
        if (v is JsonElement je && je.ValueKind is JsonValueKind.True or JsonValueKind.False) return je.GetBoolean();
        return fallback;
    }

    private static int ClampInt(object? v, int fallback, int min, int max)
    {
        if (!TryParseInt(v, out var i))
            i = fallback;
        return Math.Clamp(i, min, max);
    }

    private static bool TryParseInt(object? v, out int i)
    {
        i = 0;
        if (v == null) return false;
        if (v is int ii) { i = ii; return true; }
        if (v is long ll) { i = (int)Math.Clamp(ll, int.MinValue, int.MaxValue); return true; }
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n)) { i = n; return true; }
            if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var s)) { i = s; return true; }
            return false;
        }
        return int.TryParse(v.ToString(), out i);
    }

    private static List<string> ParseStringArray(object? v)
    {
        var list = new List<string>();
        if (v == null)
            return list;

        if (v is string s)
        {
            // Best-effort: split by whitespace.
            foreach (var part in s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                list.Add(part);
            return list;
        }

        if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in je.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var str = el.GetString();
                    if (!string.IsNullOrWhiteSpace(str))
                        list.Add(str.Trim());
                }
                else
                {
                    var raw = el.ToString();
                    if (!string.IsNullOrWhiteSpace(raw))
                        list.Add(raw.Trim());
                }
            }
        }

        return list;
    }

    private static string ResolvePythonBin(string? pythonBinOverride)
    {
        if (!string.IsNullOrWhiteSpace(pythonBinOverride))
            return pythonBinOverride.Trim();

        var env = (Environment.GetEnvironmentVariable("AEVATAR_PYTHON_BIN") ?? string.Empty).Trim();
        if (env.Length > 0)
            return env;

        // Compatibility with existing SRA tool env var.
        env = (Environment.GetEnvironmentVariable("SRA_PYTHON_BIN") ?? string.Empty).Trim();
        if (env.Length > 0)
            return env;

        return "python3";
    }
}


