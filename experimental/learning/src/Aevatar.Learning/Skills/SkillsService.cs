using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Learning.Context;
using Aevatar.Learning.Notebooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Learning.Skills;

// ============================================================
//  SkillsService (MVP)
//
//  目标：
//  - 把“学习专题 notebook”转化为可复用的 agent skill bundle
//  - 版本化写入 notebook 目录：skills/{version}/
//    - skill.md   (human-readable)
//    - skill.json (structured, optional but useful)
//    - meta.json  (version/timestamps/sourceIds/provider)
//
//  约束：
//  - no secrets
//  - bounded outputs
//  - best-effort when sources insufficient
// ============================================================
public sealed class SkillsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const int MaxBundleChars = 120_000;

    private readonly LearningContextBuilder _contextBuilder;
    private readonly ILLMProviderFactory _providers;
    private readonly IOptions<LLMProvidersConfig> _config;
    private readonly ILogger<SkillsService> _logger;

    public SkillsService(
        LearningContextBuilder contextBuilder,
        ILLMProviderFactory providers,
        IOptions<LLMProvidersConfig> config,
        ILogger<SkillsService> logger)
    {
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SkillsBundleResult> GenerateAsync(
        NotebookWorkspace workspace,
        string? theme = null,
        string? providerName = null,
        IReadOnlyList<string>? selectedSourceIds = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.EnsureDirectories();
        Directory.CreateDirectory(workspace.SkillsDir);

        var provider = ResolveProviderName(providerName);
        var llm = _providers.GetProvider(provider);

        var ctx = await _contextBuilder.BuildAsync(
            workspace,
            query: theme ?? "Generate agent skills bundle",
            selectedSourceIds: selectedSourceIds,
            budget: new LearningContextBudget(MaxTotalChars: 32_000, MaxPerSourceChars: 4_000, MaxSources: 16),
            ct: ct);

        var effectiveTheme = (theme ?? string.Empty).Trim();
        if (effectiveTheme.Length == 0)
            effectiveTheme = "Notebook Skill Bundle";

        var request = new AevatarLLMRequest
        {
            SystemPrompt = BuildSystemPrompt(),
            UserPrompt =
                $"""
                NotebookId: {workspace.NotebookId}
                Theme: {effectiveTheme}

                Task:
                Create a reusable "agent skill bundle" that other AI agents can copy.

                Output format (MUST be a single JSON object, no markdown fences):
                - name: string
                - description: string
                - systemPrompt: string
                - tools: string[]
                - usageExamples: string[]
                - knowledgeSummary: string
                - glossary: array of items (each item has: term string, definition string)
                - limitations: string[]

                Notebook context:
                {ctx.Text}
                """,
            Messages = new List<Aevatar.Agents.AI.AevatarChatMessage>(),
            Settings = new AevatarLLMSettings()
        };

        _logger.LogInformation(
            "[Skills] Generate: notebook={NotebookId}, provider={Provider}, sources={Sources}",
            workspace.NotebookId,
            provider,
            ctx.Slices.Count);

        var resp = await llm.GenerateAsync(request, ct);
        var raw = TrimToMax(resp.Content ?? string.Empty, MaxBundleChars);

        var bundle = TryParseBundle(raw) ?? new SkillsBundle
        {
            Name = effectiveTheme,
            Description = "Best-effort bundle (JSON parse failed).",
            SystemPrompt = "You are an assistant specialized for this notebook theme.",
            Tools = new List<string>(),
            UsageExamples = new List<string>(),
            KnowledgeSummary = "",
            Glossary = new List<SkillsGlossaryItem>(),
            Limitations = new List<string>()
        };

        // Persist versioned bundle
        var version = BuildVersionId(bundle.Name, raw);
        var dir = Path.Combine(workspace.SkillsDir, version);
        Directory.CreateDirectory(dir);

        var now = DateTimeOffset.UtcNow;
        var meta = new SkillsBundleMeta
        {
            NotebookId = workspace.NotebookId,
            Version = version,
            ProviderName = provider,
            Theme = effectiveTheme,
            CreatedAt = now,
            SourceIds = ctx.Slices.Select(s => s.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ParseError = bundle.ParseError ?? ""
        };

        var jsonPath = Path.Combine(dir, "skill.json");
        var mdPath = Path.Combine(dir, "skill.md");
        var metaPath = Path.Combine(dir, "meta.json");

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(bundle, Json), new UTF8Encoding(false), ct);
        await File.WriteAllTextAsync(mdPath, RenderMarkdown(bundle), new UTF8Encoding(false), ct);
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, Json), new UTF8Encoding(false), ct);

        if (!string.IsNullOrWhiteSpace(bundle.ParseError))
        {
            var rawPath = Path.Combine(dir, "raw.txt");
            await File.WriteAllTextAsync(rawPath, raw, new UTF8Encoding(false), ct);
        }

        return new SkillsBundleResult(meta, bundle, dir);
    }

    // ============================================================
    //  Helpers
    // ============================================================

    private string ResolveProviderName(string? overrideName)
    {
        var name = (overrideName ?? string.Empty).Trim();
        if (name.Length > 0)
            return name;

        name = (_config.Value.Default ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("LLMProviders:Default is not configured.");

        return name;
    }

    private static string BuildSystemPrompt()
    {
        return
            """
            You are an expert at turning learning materials into reusable AI agent skills.

            Rules:
            - Do NOT include secrets or API keys.
            - Keep prompts and outputs bounded and practical.
            - Prefer capabilities grounded in the notebook context.
            - When context is insufficient, explicitly state limitations.
            """;
    }

    private static SkillsBundle? TryParseBundle(string raw)
    {
        raw = (raw ?? string.Empty).Trim();
        if (raw.Length == 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new SkillsBundle { ParseError = "root is not object" };

            var root = doc.RootElement;
            var bundle = new SkillsBundle
            {
                Name = ReadString(root, "name"),
                Description = ReadString(root, "description"),
                SystemPrompt = ReadString(root, "systemPrompt"),
                KnowledgeSummary = ReadString(root, "knowledgeSummary"),
                Tools = ReadStringArray(root, "tools", 24),
                UsageExamples = ReadStringArray(root, "usageExamples", 12),
                Limitations = ReadStringArray(root, "limitations", 12),
                Glossary = new List<SkillsGlossaryItem>()
            };

            if (root.TryGetProperty("glossary", out var g) && g.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in g.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    var term = ReadString(it, "term");
                    var def = ReadString(it, "definition");
                    if (term.Length == 0 || def.Length == 0) continue;
                    bundle.Glossary.Add(new SkillsGlossaryItem { Term = term, Definition = def });
                    if (bundle.Glossary.Count >= 50) break;
                }
            }

            if (bundle.Name.Length == 0)
                bundle.Name = "Skill Bundle";

            return bundle;
        }
        catch (Exception ex)
        {
            return new SkillsBundle { ParseError = ex.Message };
        }
    }

    private static string ReadString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return "";
        if (!obj.TryGetProperty(name, out var p)) return "";
        return (p.GetString() ?? string.Empty).Trim();
    }

    private static List<string> ReadStringArray(JsonElement obj, string name, int max)
    {
        var list = new List<string>();
        if (obj.ValueKind != JsonValueKind.Object) return list;
        if (!obj.TryGetProperty(name, out var p)) return list;
        if (p.ValueKind != JsonValueKind.Array) return list;

        foreach (var el in p.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String) continue;
            var s = (el.GetString() ?? string.Empty).Trim();
            if (s.Length == 0) continue;
            list.Add(s);
            if (list.Count >= max) break;
        }

        return list;
    }

    private static string RenderMarkdown(SkillsBundle b)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {b.Name}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(b.Description))
        {
            sb.AppendLine(b.Description.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("## System Prompt");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine((b.SystemPrompt ?? string.Empty).Trim());
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Tools");
        sb.AppendLine();
        if (b.Tools.Count == 0) sb.AppendLine("- (none)");
        else foreach (var t in b.Tools) sb.AppendLine($"- {t}");
        sb.AppendLine();

        sb.AppendLine("## Usage Examples");
        sb.AppendLine();
        if (b.UsageExamples.Count == 0) sb.AppendLine("- (none)");
        else foreach (var ex in b.UsageExamples) sb.AppendLine($"- {ex}");
        sb.AppendLine();

        sb.AppendLine("## Knowledge Summary");
        sb.AppendLine();
        sb.AppendLine((b.KnowledgeSummary ?? string.Empty).Trim());
        sb.AppendLine();

        sb.AppendLine("## Glossary");
        sb.AppendLine();
        if (b.Glossary.Count == 0) sb.AppendLine("- (none)");
        else
        {
            foreach (var g in b.Glossary)
            {
                sb.AppendLine($"- **{g.Term}**: {g.Definition}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Limitations");
        sb.AppendLine();
        if (b.Limitations.Count == 0) sb.AppendLine("- (none)");
        else foreach (var l in b.Limitations) sb.AppendLine($"- {l}");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string BuildVersionId(string name, string raw)
    {
        var sha = Sha256Hex($"{name}\n{raw}")[..8];
        var ts = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        return $"v{ts}-{sha}";
    }

    private static string Sha256Hex(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string TrimToMax(string s, int maxChars)
    {
        s ??= string.Empty;
        s = s.Trim();
        if (s.Length <= maxChars) return s;
        return s[..maxChars].TrimEnd();
    }
}

public sealed class SkillsBundle
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public List<string> Tools { get; set; } = new();
    public List<string> UsageExamples { get; set; } = new();
    public string KnowledgeSummary { get; set; } = "";
    public List<SkillsGlossaryItem> Glossary { get; set; } = new();
    public List<string> Limitations { get; set; } = new();

    // Debug
    public string? ParseError { get; set; }
}

public sealed class SkillsGlossaryItem
{
    public string Term { get; set; } = "";
    public string Definition { get; set; } = "";
}

public sealed class SkillsBundleMeta
{
    public string NotebookId { get; set; } = "";
    public string Version { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string Theme { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<string> SourceIds { get; set; } = new();
    public string ParseError { get; set; } = "";
}

public sealed record SkillsBundleResult(SkillsBundleMeta Meta, SkillsBundle Bundle, string DirectoryPath);


