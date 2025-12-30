using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Tools.CustomTools;

/// <summary>
/// DotNet file-based tool (C# single-file "skills" executed via <c>dotnet run --file</c>).
///
/// Contract (recommended):
/// - Tool receives JSON on STDIN (parameters object).
/// - Tool prints JSON to STDOUT.
/// - If STDOUT contains "AEVATAR_TOOL_OUTPUT:" the text after the marker will be parsed as JSON.
///
/// Manifest (optional, inside the .cs file):
/// - Add a block comment starting with "/*aevatar_tool" and ending with "*\/".
/// - Put a JSON object inside; fields map to <see cref="DotNetFileSkillManifest"/>.
/// </summary>
public sealed class DotNetFileSkillTool : AevatarToolBase
{
    private readonly DotNetFileSkillDefinition _definition;
    private readonly ILogger? _logger;

    public DotNetFileSkillTool(DotNetFileSkillDefinition definition, ILogger? logger = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _logger = logger;
    }

    public override string Name => _definition.Name;
    public override string Description => _definition.Description;
    public override ToolCategory Category => _definition.Category;
    public override string Version => _definition.Version;
    public override IList<string> Tags => _definition.Tags;

    public override ToolParameters CreateParameters() => _definition.Parameters;

    protected override bool RequiresConfirmation() => _definition.RequiresConfirmation;
    protected override bool IsDangerous() => _definition.IsDangerous;
    protected override TimeSpan? GetTimeout() => _definition.Timeout;

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var effectiveLogger = logger ?? _logger;

        var inputJson = JsonSerializer.Serialize(parameters);
        var result = await DotNetFileSkillRunner.ExecuteAsync(
            _definition,
            context,
            inputJson,
            effectiveLogger,
            cancellationToken);

        return result;
    }

    /// <summary>
    /// Load a tool from a C# file (reads embedded manifest if present).
    /// </summary>
    public static async Task<DotNetFileSkillTool> LoadFromFileAsync(
        string filePath,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var def = await DotNetFileSkillLoader.LoadAsync(filePath, logger, cancellationToken);
        return new DotNetFileSkillTool(def, logger);
    }
}

/// <summary>
/// The persisted definition for a dotnet-file tool.
/// </summary>
public sealed class DotNetFileSkillDefinition
{
    public required string FilePath { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ToolParameters Parameters { get; init; }

    public ToolCategory Category { get; init; } = ToolCategory.Custom;
    public string Version { get; init; } = "1.0.0";
    public IList<string> Tags { get; init; } = new List<string>();

    public bool RequiresConfirmation { get; init; }
    public bool IsDangerous { get; init; } = true;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    public int MaxOutputChars { get; init; } = 64 * 1024;
}

internal sealed class DotNetFileSkillManifest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Category { get; set; }
    public List<string>? Tags { get; set; }
    public ToolParameters? Parameters { get; set; }
    public bool? RequiresConfirmation { get; set; }
    public bool? IsDangerous { get; set; }
    public int? TimeoutMs { get; set; }
    public int? MaxOutputChars { get; set; }
}

internal static class DotNetFileSkillLoader
{
    private const string ManifestStart = "/*aevatar_tool";

    public static async Task<DotNetFileSkillDefinition> LoadAsync(
        string filePath,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("DotNet skill file not found", filePath);
        }

        var source = await File.ReadAllTextAsync(filePath, cancellationToken);
        var manifest = TryParseManifest(source, logger);

        var toolName = !string.IsNullOrWhiteSpace(manifest?.Name)
            ? manifest!.Name!.Trim()
            : ToSnakeCase(Path.GetFileNameWithoutExtension(filePath));

        var description = !string.IsNullOrWhiteSpace(manifest?.Description)
            ? manifest!.Description!.Trim()
            : $"DotNet file skill: {Path.GetFileName(filePath)}";

        var category = ParseCategory(manifest?.Category);
        var version = !string.IsNullOrWhiteSpace(manifest?.Version) ? manifest!.Version!.Trim() : "1.0.0";

        var parameters = manifest?.Parameters ?? new ToolParameters
        {
            Required = new List<string> { "input" },
            Items = new Dictionary<string, ToolParameter>
            {
                ["input"] = new ToolParameter
                {
                    Type = "object",
                    Description = "Tool input object (passed to the C# file via STDIN as JSON)",
                    Required = true
                }
            }
        };

        var tags = manifest?.Tags != null && manifest.Tags.Count > 0
            ? manifest.Tags
            : new List<string> { "dotnet", "file", "skill" };

        return new DotNetFileSkillDefinition
        {
            FilePath = Path.GetFullPath(filePath),
            Name = toolName,
            Description = description,
            Parameters = parameters,
            Category = category,
            Version = version,
            Tags = tags,
            RequiresConfirmation = manifest?.RequiresConfirmation ?? false,
            IsDangerous = manifest?.IsDangerous ?? true,
            Timeout = manifest?.TimeoutMs is > 0 ? TimeSpan.FromMilliseconds(manifest.TimeoutMs.Value) : TimeSpan.FromSeconds(60),
            MaxOutputChars = manifest?.MaxOutputChars is > 0 ? manifest.MaxOutputChars.Value : 64 * 1024
        };
    }

    private static DotNetFileSkillManifest? TryParseManifest(string source, ILogger? logger)
    {
        var start = source.IndexOf(ManifestStart, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        var end = source.IndexOf("*/", start, StringComparison.Ordinal);
        if (end < 0)
        {
            logger?.LogWarning("DotNet skill manifest start found but no closing */");
            return null;
        }

        var body = source.Substring(start + ManifestStart.Length, end - (start + ManifestStart.Length)).Trim();
        var jsonStart = body.IndexOf('{');
        var jsonEnd = body.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            logger?.LogWarning("DotNet skill manifest block found but JSON object not found");
            return null;
        }

        var json = body.Substring(jsonStart, jsonEnd - jsonStart + 1);
        try
        {
            return JsonSerializer.Deserialize<DotNetFileSkillManifest>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                });
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to parse dotnet skill manifest JSON");
            return null;
        }
    }

    private static ToolCategory ParseCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ToolCategory.Custom;

        return System.Enum.TryParse<ToolCategory>(value, ignoreCase: true, out var category)
            ? category
            : ToolCategory.Custom;
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "skill";

        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && name[i - 1] != '_' && !char.IsUpper(name[i - 1]))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

internal static class DotNetFileSkillRunner
{
    private const string OutputMarker = "AEVATAR_TOOL_OUTPUT:";

    private static readonly JsonSerializerOptions JsonOutOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<IMessage> ExecuteAsync(
        DotNetFileSkillDefinition definition,
        ToolContext context,
        string inputJson,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var (stdout, stderr, exitCode, timedOut) = await RunProcessAsync(
            definition,
            context,
            inputJson,
            logger,
            cancellationToken);

        var parsed = TryExtractJson(stdout, out var jsonCandidate);
        JsonElement? data = null;

        if (parsed && jsonCandidate != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonCandidate);
                data = doc.RootElement.Clone();
            }
            catch
            {
                data = null;
            }
        }

        var resultObj = new Dictionary<string, object?>
        {
            ["success"] = !timedOut && exitCode == 0,
            ["exitCode"] = timedOut ? -1 : exitCode,
            ["toolName"] = definition.Name,
            ["file"] = definition.FilePath,
            ["data"] = data.HasValue
                ? data.Value
                : new Dictionary<string, object?>
                {
                    ["text"] = stdout?.TrimEnd() ?? string.Empty
                },
            ["stderr"] = string.IsNullOrWhiteSpace(stderr) ? null : stderr.TrimEnd()
        };

        if (timedOut)
        {
            resultObj["error"] = "Tool execution timed out";
        }

        var json = JsonSerializer.Serialize(resultObj, JsonOutOptions);
        return JsonParser.Default.Parse<Struct>(json);
    }

    private static async Task<(string StdOut, string StdErr, int ExitCode, bool TimedOut)> RunProcessAsync(
        DotNetFileSkillDefinition definition,
        ToolContext context,
        string inputJson,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var workingDir = Path.GetDirectoryName(definition.FilePath);
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            workingDir = Environment.CurrentDirectory;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // dotnet run --file <file>
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--file");
        psi.ArgumentList.Add(definition.FilePath);
        psi.ArgumentList.Add("--no-launch-profile");
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("q");

        // Reduce noise
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        // Context for the skill file (read via Environment.GetEnvironmentVariable)
        psi.Environment["AEVATAR_AGENT_ID"] = context.AgentId ?? string.Empty;
        psi.Environment["AEVATAR_AGENT_TYPE"] = context.AgentType ?? string.Empty;
        psi.Environment["AEVATAR_SESSION_ID"] = context.GetSessionIdCallback?.Invoke() ?? string.Empty;
        psi.Environment["AEVATAR_TOOL_NAME"] = definition.Name;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to start dotnet run --file process");
            throw;
        }

        // Write input JSON then close stdin
        await process.StandardInput.WriteAsync(inputJson.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();

        var stdoutTask = ReadAllWithLimitAsync(process.StandardOutput, definition.MaxOutputChars, cancellationToken);
        var stderrTask = ReadAllWithLimitAsync(process.StandardError, definition.MaxOutputChars, cancellationToken);

        using var timeoutCts = new CancellationTokenSource(definition.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                TryKill(process, logger);
                throw;
            }

            // Timeout
            TryKill(process, logger);

            var stdout = await SafeAwaitAsync(stdoutTask);
            var stderr = await SafeAwaitAsync(stderrTask);
            return (stdout, stderr, -1, true);
        }

        var stdOut = await SafeAwaitAsync(stdoutTask);
        var stdErr = await SafeAwaitAsync(stderrTask);
        return (stdOut, stdErr, process.ExitCode, false);
    }

    private static void TryKill(Process process, ILogger? logger)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to kill dotnet skill process (best-effort)");
        }
    }

    private static async Task<string> SafeAwaitAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<string> ReadAllWithLimitAsync(
        StreamReader reader,
        int maxChars,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder(capacity: Math.Min(maxChars, 16 * 1024));
        var buffer = new char[4096];

        // IMPORTANT:
        // - We MUST keep draining stdout/stderr until EOF.
        // - If we stop reading once `maxChars` is reached, the child process can block on a full pipe,
        //   never exit, and we'll end up timing out (this happens on endpoints that return big payloads
        //   like market/tickers or market/contracts).
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
                break;

            var remaining = maxChars - sb.Length;
            if (remaining <= 0)
            {
                // Discard remaining output but keep draining to avoid deadlock.
                continue;
            }

            sb.Append(buffer, 0, Math.Min(read, remaining));
        }

        return sb.ToString();
    }

    private static bool TryExtractJson(string stdout, out string? json)
    {
        json = null;
        if (string.IsNullOrWhiteSpace(stdout))
            return false;

        var markerIdx = stdout.LastIndexOf(OutputMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx >= 0)
        {
            json = stdout[(markerIdx + OutputMarker.Length)..].Trim();
            return !string.IsNullOrWhiteSpace(json);
        }

        var trimmed = stdout.Trim();
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            json = trimmed;
            return true;
        }

        // Best-effort: try parse the last JSON object/array in the output
        var lastEnd = trimmed.LastIndexOf('}');
        if (lastEnd < 0)
        {
            lastEnd = trimmed.LastIndexOf(']');
            if (lastEnd < 0) return false;
        }

        for (var start = lastEnd; start >= 0; start--)
        {
            if (trimmed[start] is '{' or '[')
            {
                var candidate = trimmed.Substring(start, lastEnd - start + 1);
                if (IsValidJson(candidate))
                {
                    json = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsValidJson(string candidate)
    {
        try
        {
            using var _ = JsonDocument.Parse(candidate);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

