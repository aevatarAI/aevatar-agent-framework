/*aevatar_tool
{
  "name": "file_search",
  "description": "Search for a regex pattern in text files under a directory (defaults to AEVATAR_DEMO_ROOT)",
  "category": "File",
  "version": "1.0.0",
  "tags": ["dotnet", "file", "skill", "fs", "search", "grep"],
  "parameters": {
    "required": ["pattern"],
    "items": {
      "pattern": { "type": "string", "description": "Regex pattern" },
      "root": { "type": "string", "description": "Search root (absolute, or relative to AEVATAR_DEMO_ROOT). Default: AEVATAR_DEMO_ROOT" },
      "extensions": { "type": "string", "description": "Comma-separated file extensions filter, e.g. '.cs,.md,.proto'. Default: (no filter)" },
      "ignoreCase": { "type": "boolean", "description": "Regex ignore case (default true)" },
      "maxMatches": { "type": "integer", "description": "Max matches (default 30, max 200)" },
      "maxFiles": { "type": "integer", "description": "Max scanned files (default 400, max 5000)" }
    }
  }
}
*/

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

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

var pattern = GetString(inputArgs, "pattern");
if (string.IsNullOrWhiteSpace(pattern))
{
    Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = "Parameter 'pattern' is required." }, jsonOptions));
    return;
}

var baseRoot = Environment.GetEnvironmentVariable("AEVATAR_DEMO_ROOT") ?? Directory.GetCurrentDirectory();
var root = ResolveDir(GetString(inputArgs, "root"), baseRoot);
var extensions = ParseExtensions(GetString(inputArgs, "extensions"));
if (extensions.Count == 0)
{
    // 默认只扫描常见文本文件，避免把二进制/图片也算进 maxFiles 配额里。
    extensions = DefaultTextExtensions();
}
var ignoreCase = GetBool(inputArgs, "ignoreCase", defaultValue: true);
var maxMatches = Math.Clamp(GetInt(inputArgs, "maxMatches", 30), 1, 200);
var maxFiles = Math.Clamp(GetInt(inputArgs, "maxFiles", 3000), 1, 5000);

if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
{
    Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = "Search root not found.", root }, jsonOptions));
    return;
}

Regex regex;
try
{
    regex = new Regex(pattern, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
}
catch (Exception ex)
{
    Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = $"Invalid regex: {ex.Message}", pattern }, jsonOptions));
    return;
}

var matches = new List<object>();
var filesScanned = 0;
var skipped = 0;
var truncated = false;

var stack = new Stack<string>();
stack.Push(root);

while (stack.Count > 0 && filesScanned < maxFiles && matches.Count < maxMatches)
{
    var dir = stack.Pop();

    IEnumerable<string> subDirs;
    try
    {
        subDirs = Directory.EnumerateDirectories(dir);
    }
    catch
    {
        skipped++;
        continue;
    }

    foreach (var sub in subDirs)
    {
        if (ShouldSkipDir(sub)) continue;
        stack.Push(sub);
    }

    IEnumerable<string> files;
    try
    {
        files = Directory.EnumerateFiles(dir);
    }
    catch
    {
        skipped++;
        continue;
    }

    foreach (var file in files)
    {
        if (filesScanned >= maxFiles || matches.Count >= maxMatches) break;
        if (ShouldSkipFile(file)) continue;
        if (extensions.Count > 0 && !extensions.Contains(Path.GetExtension(file))) continue;

        filesScanned++;

        try
        {
            var info = new FileInfo(file);
            if (info.Length > 1024 * 1024) // 1MB cap (demo safety)
            {
                skipped++;
                continue;
            }

            using var reader = new StreamReader(file);
            var lineNo = 0;
            while (!reader.EndOfStream && matches.Count < maxMatches)
            {
                var line = await reader.ReadLineAsync() ?? string.Empty;
                lineNo++;

                if (!regex.IsMatch(line))
                    continue;

                matches.Add(new
                {
                    file,
                    line = lineNo,
                    text = line.Length <= 260 ? line : line[..260] + "..."
                });
            }
        }
        catch
        {
            skipped++;
        }
    }
}

if (filesScanned >= maxFiles || matches.Count >= maxMatches)
{
    truncated = true;
}

var result = new
{
    success = true,
    root,
    pattern,
    ignoreCase,
    extensions = extensions.ToArray(),
    filesScanned,
    matches = matches.ToArray(),
    truncated,
    skipped
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

static bool GetBool(Dictionary<string, JsonElement> args, string key, bool defaultValue)
{
    if (!args.TryGetValue(key, out var el)) return defaultValue;

    return el.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(el.GetString(), out var parsed) => parsed,
        _ => defaultValue
    };
}

static HashSet<string> ParseExtensions(string? raw)
{
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (string.IsNullOrWhiteSpace(raw)) return set;

    foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var ext = part.StartsWith('.') ? part : "." + part;
        set.Add(ext);
    }

    return set;
}

static HashSet<string> DefaultTextExtensions()
{
    return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".slnx",
        ".md", ".txt",
        ".proto",
        ".json", ".jsonc",
        ".yaml", ".yml",
        ".props", ".targets", ".config", ".editorconfig"
    };
}

static string? ResolveDir(string? dir, string baseRoot)
{
    if (string.IsNullOrWhiteSpace(dir)) return Path.GetFullPath(baseRoot);
    if (Path.IsPathRooted(dir)) return Path.GetFullPath(dir);
    return Path.GetFullPath(dir, baseRoot);
}

static bool ShouldSkipDir(string dirPath)
{
    var name = Path.GetFileName(dirPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    return name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
           name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
           name.Equals(".idea", StringComparison.OrdinalIgnoreCase) ||
           name.Equals(".vs", StringComparison.OrdinalIgnoreCase);
}

static bool ShouldSkipFile(string filePath)
{
    var name = Path.GetFileName(filePath);
    // NOTE: avoid StartsWith(char, StringComparison) overload (not available).
    if (name.StartsWith(".", StringComparison.Ordinal)) return true;
    if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return true;
    if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) return true;
    return false;
}


