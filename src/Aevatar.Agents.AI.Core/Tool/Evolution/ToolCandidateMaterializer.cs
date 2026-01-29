using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Messages;
using Aevatar.Agents.AI.Tool.Tools.CustomTools;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tool.Evolution;

// ============================================================
//  ToolCandidateMaterializer
//
//  中文 + ASCII:
//  - 将候选代码落盘为 file-skill，复用现有运行时。
//  - 通过 manifest 保持工具元数据一致。
// ============================================================
/// <summary>
/// Materialize tool candidates into file-based tools.
/// </summary>
public static class ToolCandidateMaterializer
{
    public static async Task<IAevatarTool> MaterializeAsync(
        ToolCandidate candidate,
        ToolEvolutionOptions options,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        options ??= new ToolEvolutionOptions();

        if (string.IsNullOrWhiteSpace(candidate.SourceCode))
            throw new InvalidOperationException("Tool candidate source_code is empty.");

        var language = (candidate.Language ?? "dotnet").Trim().ToLowerInvariant();
        var directory = ExpandHomePath(options.ToolStorageDirectory);
        Directory.CreateDirectory(directory);

        var filePath = BuildFilePath(directory, candidate, language);
        var source = EnsureManifest(candidate, language);
        await File.WriteAllTextAsync(filePath, source, Encoding.UTF8, cancellationToken);

        if (language == "python" || language == "py")
            return await PythonFileSkillTool.LoadFromFileAsync(filePath, logger, cancellationToken);

        return await DotNetFileSkillTool.LoadFromFileAsync(filePath, logger, cancellationToken);
    }

    private static string EnsureManifest(ToolCandidate candidate, string language)
    {
        var source = candidate.SourceCode ?? string.Empty;
        if (HasManifest(source, language))
            return source;

        var manifestJson = BuildManifestJson(candidate);
        if (language == "python" || language == "py")
        {
            return $"\"\"\"aevatar_tool\n{manifestJson}\n\"\"\"\n\n{source}";
        }

        return $"/*aevatar_tool\n{manifestJson}\n*/\n\n{source}";
    }

    private static bool HasManifest(string source, string language)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (language == "python" || language == "py")
        {
            return source.Contains("\"\"\"aevatar_tool", StringComparison.OrdinalIgnoreCase) ||
                   source.Contains("'''aevatar_tool", StringComparison.OrdinalIgnoreCase);
        }

        return source.Contains("/*aevatar_tool", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildManifestJson(ToolCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ManifestJson))
            return candidate.ManifestJson.Trim();

        ToolParameters? parameters = null;
        if (!string.IsNullOrWhiteSpace(candidate.ParametersJson))
        {
            try
            {
                parameters = JsonSerializer.Deserialize<ToolParameters>(
                    candidate.ParametersJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                // best-effort only
            }
        }

        var manifest = new Dictionary<string, object?>
        {
            ["name"] = string.IsNullOrWhiteSpace(candidate.ToolName) ? "evolved_tool" : candidate.ToolName,
            ["description"] = candidate.Description ?? string.Empty,
            ["version"] = string.IsNullOrWhiteSpace(candidate.ToolVersion) ? "1.0.0" : candidate.ToolVersion
        };

        if (parameters != null)
        {
            manifest["parameters"] = parameters;
        }

        return JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildFilePath(string directory, ToolCandidate candidate, string language)
    {
        var ext = language == "python" || language == "py" ? ".py" : ".cs";
        var name = SanitizeFileToken(candidate.ToolName);
        var version = SanitizeFileToken(candidate.ToolVersion);
        var candidateId = SanitizeFileToken(candidate.CandidateId);
        if (string.IsNullOrWhiteSpace(candidateId))
            candidateId = Guid.NewGuid().ToString("N")[..8];

        var fileName = $"{name}_{version}_{candidateId}{ext}";
        return Path.Combine(directory, fileName);
    }

    private static string SanitizeFileToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "tool";

        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                sb.Append(c);
            else
                sb.Append('_');
        }

        return sb.ToString();
    }

    private static string ExpandHomePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Path.Combine(Path.GetTempPath(), "aevatar", "tools");

        path = path.Trim();
        if (!path.StartsWith("~/", StringComparison.Ordinal))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var suffix = path[2..];
        return Path.Combine(home, suffix);
    }
}
