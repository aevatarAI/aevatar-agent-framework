/*aevatar_tool
{
  "name": "file_read",
  "description": "Read a UTF-8 text file with optional line range (relative paths resolved against AEVATAR_DEMO_ROOT)",
  "category": "File",
  "version": "1.0.0",
  "tags": ["dotnet", "file", "skill", "fs", "read"],
  "parameters": {
    "required": ["path"],
    "items": {
      "path": { "type": "string", "description": "File path (absolute, or relative to AEVATAR_DEMO_ROOT)" },
      "startLine": { "type": "integer", "description": "1-based start line (default 1)" },
      "maxLines": { "type": "integer", "description": "Max lines to return (default 120, max 500)" }
    }
  }
}
*/

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var jsonOptions = new JsonSerializerOptions
{
    // .NET 10 file-based apps may disable reflection serialization by default.
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

var input = await Console.In.ReadToEndAsync();
var inputArgs = string.IsNullOrWhiteSpace(input)
    ? new Dictionary<string, JsonElement>()
    : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input, jsonOptions)
      ?? new Dictionary<string, JsonElement>();

var rawPath = GetString(inputArgs, "path");
var startLine = Math.Max(1, GetInt(inputArgs, "startLine", 1));
var maxLines = Math.Clamp(GetInt(inputArgs, "maxLines", 120), 1, 500);

var baseRoot = Environment.GetEnvironmentVariable("AEVATAR_DEMO_ROOT");
var fullPath = ResolvePath(rawPath, baseRoot);

if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
{
    var notFound = new
    {
        success = false,
        exists = false,
        path = fullPath ?? rawPath,
        error = "File not found"
    };
    Console.WriteLine(JsonSerializer.Serialize(notFound, jsonOptions));
    return;
}

var lines = new List<object>();
var current = 0;

try
{
    foreach (var line in File.ReadLines(fullPath))
    {
        current++;
        if (current < startLine) continue;
        if (lines.Count >= maxLines) break;
        lines.Add(new { line = current, text = line });
    }
}
catch (Exception ex)
{
    var bad = new
    {
        success = false,
        exists = true,
        path = fullPath,
        error = ex.Message
    };
    Console.WriteLine(JsonSerializer.Serialize(bad, jsonOptions));
    return;
}

var result = new
{
    success = true,
    exists = true,
    path = fullPath,
    startLine,
    returned = lines.Count,
    truncated = lines.Count >= maxLines,
    lines
};

Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));

static string? GetString(Dictionary<string, JsonElement> args, string key)
{
    return args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}

static int GetInt(Dictionary<string, JsonElement> args, string key, int defaultValue)
{
    if (!args.TryGetValue(key, out var el)) return defaultValue;

    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
    if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var parsed)) return parsed;
    return defaultValue;
}

static string? ResolvePath(string? path, string? baseRoot)
{
    if (string.IsNullOrWhiteSpace(path)) return null;
    if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

    var root = !string.IsNullOrWhiteSpace(baseRoot) ? baseRoot : Directory.GetCurrentDirectory();
    return Path.GetFullPath(path, root);
}


