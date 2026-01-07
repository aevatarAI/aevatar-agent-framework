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

namespace Aevatar.Learning.Encyclopedia;

// ============================================================
//  EncyclopediaService (MVP)
//
//  Goals:
//  - Build/update notebook-scoped encyclopedia entries from sources
//    (MVP storage: encyclopedia/entries.jsonl + meta.json)
//  - Query: symptom/body description -> structured output:
//      - recommendedItems (name/location/effects)
//      - whyItWorks
//      - massageTechniques
//      - relatedTheory
//
//  Notes:
//  - This is a generic "encyclopedia" layer; domain-specific templates can be
//    layered on top by notebook theme (later tasks).
//  - Output is best-effort; if JSON parsing fails, we keep raw text.
// ============================================================
public sealed class EncyclopediaService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaxRawChars = 120_000;

    private readonly LearningContextBuilder _contextBuilder;
    private readonly ILLMProviderFactory _providers;
    private readonly IOptions<LLMProvidersConfig> _config;
    private readonly ILogger<EncyclopediaService> _logger;

    public EncyclopediaService(
        LearningContextBuilder contextBuilder,
        ILLMProviderFactory providers,
        IOptions<LLMProvidersConfig> config,
        ILogger<EncyclopediaService> logger)
    {
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ============================================================
    //  Build / Update
    // ============================================================

    public async Task<EncyclopediaBuildResult> BuildAsync(
        NotebookWorkspace workspace,
        string? providerName = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.EnsureDirectories();
        Directory.CreateDirectory(workspace.EncyclopediaDir);

        var provider = ResolveProviderName(providerName);
        var llm = _providers.GetProvider(provider);

        // Build a larger context snapshot for encyclopedia building.
        var ctx = await _contextBuilder.BuildAsync(
            workspace,
            query: "Build encyclopedia entries",
            selectedSourceIds: null,
            budget: new LearningContextBudget(MaxTotalChars: 40_000, MaxPerSourceChars: 6_000, MaxSources: 16),
            ct: ct);

        var req = new AevatarLLMRequest
        {
            SystemPrompt = BuildBuildSystemPrompt(),
            UserPrompt =
                $"""
                NotebookId: {workspace.NotebookId}

                Task:
                - Extract key concepts/terms from the notebook context.
                - Produce a JSON array of 10-30 entries.

                Output JSON schema:
                - Root: JSON array
                - Each item: object with fields:
                  - id: string (stable, lowercase, no spaces)
                  - title: string
                  - summary: string (<= 240 chars)
                  - keywords: string[]
                  - tags: string[]

                Notebook context:
                {ctx.Text}
                """,
            Messages = new List<Aevatar.Agents.AI.AevatarChatMessage>(),
            Settings = new AevatarLLMSettings()
        };

        _logger.LogInformation("[Encyclopedia] Build: notebook={NotebookId}, provider={Provider}, sources={Sources}",
            workspace.NotebookId, provider, ctx.Slices.Count);

        var resp = await llm.GenerateAsync(req, ct);
        var raw = TrimToMax(resp.Content ?? string.Empty, MaxRawChars);

        var entries = TryParseEntries(raw, out var parseError);

        // Persist
        var entriesPath = Path.Combine(workspace.EncyclopediaDir, "entries.jsonl");
        var metaPath = Path.Combine(workspace.EncyclopediaDir, "meta.json");

        var now = DateTimeOffset.UtcNow;
        var meta = new EncyclopediaMeta
        {
            NotebookId = workspace.NotebookId,
            ProviderName = provider,
            BuiltAt = now,
            SourceIds = ctx.Slices.Select(s => s.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            EntryCount = entries.Count,
            ParseError = parseError ?? ""
        };

        // Write entries.jsonl (overwrite for deterministic rebuild)
        await using (var fs = new FileStream(entriesPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        await using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
        {
            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                await sw.WriteLineAsync(JsonSerializer.Serialize(e, Json));
            }
        }

        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, Json), new UTF8Encoding(false), ct);

        // If parsing failed, keep raw response for debugging.
        if (!string.IsNullOrWhiteSpace(parseError))
        {
            var rawPath = Path.Combine(workspace.EncyclopediaDir, "build_raw.txt");
            await File.WriteAllTextAsync(rawPath, raw, new UTF8Encoding(false), ct);
        }

        return new EncyclopediaBuildResult(meta, entriesPath);
    }

    // ============================================================
    //  Query
    // ============================================================

    public async Task<EncyclopediaQueryResult> QueryAsync(
        NotebookWorkspace workspace,
        string query,
        string? providerName = null,
        IReadOnlyList<string>? selectedSourceIds = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0)
            throw new ArgumentException("query is required.", nameof(query));

        workspace.EnsureDirectories();
        Directory.CreateDirectory(workspace.EncyclopediaDir);

        var provider = ResolveProviderName(providerName);
        var llm = _providers.GetProvider(provider);

        var ctx = await _contextBuilder.BuildAsync(
            workspace,
            query: query,
            selectedSourceIds: selectedSourceIds,
            budget: new LearningContextBudget(MaxTotalChars: 18_000, MaxPerSourceChars: 3_000, MaxSources: 10),
            ct: ct);

        // Include existing entries (best-effort) as an extra hint.
        var entriesHint = await ReadEntriesHintAsync(workspace, maxChars: 6_000, ct);

        var req = new AevatarLLMRequest
        {
            SystemPrompt = BuildQuerySystemPrompt(),
            UserPrompt =
                $"""
                Input:
                {query}

                Notebook context:
                {ctx.Text}

                Encyclopedia entries (optional, best-effort):
                {entriesHint}
                """,
            Messages = new List<Aevatar.Agents.AI.AevatarChatMessage>(),
            Settings = new AevatarLLMSettings()
        };

        _logger.LogInformation("[Encyclopedia] Query: notebook={NotebookId}, provider={Provider}, sources={Sources}",
            workspace.NotebookId, provider, ctx.Slices.Count);

        var resp = await llm.GenerateAsync(req, ct);
        var raw = TrimToMax(resp.Content ?? string.Empty, MaxRawChars);

        var parsed = TryParseQueryResult(raw);
        parsed.RawText = raw;
        parsed.Query = query;
        parsed.ProviderName = provider;
        parsed.SourceIds = ctx.Slices.Select(s => s.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return parsed;
    }

    // ============================================================
    //  Prompting
    // ============================================================

    private static string BuildBuildSystemPrompt()
    {
        return
            """
            You are building a compact notebook encyclopedia.

            Output rules:
            - Output MUST be a single JSON array. No markdown fences.
            - Keep entries small and practical.
            - If the notebook context is insufficient, still output a minimal list of generic entries.
            """;
    }

    private static string BuildQuerySystemPrompt()
    {
        // This schema is designed to support the "meridians/acupoints" example,
        // but it can also be used for other domains as a structured answer template.
        return
            """
            You are an AI-assisted encyclopedia for a learning notebook.

            Output MUST be a single JSON object with this schema (no markdown fences):
            {
              "recommendedItems": [
                { "name": "string", "location": "string", "effects": "string" }
              ],
              "whyItWorks": "string",
              "massageTechniques": ["string", "..."],
              "relatedTheory": "string",
              "cautions": ["string", "..."],
              "relatedConcepts": ["string", "..."]
            }

            Grounding rules:
            - Prefer notebook context. When unsure, say so in whyItWorks/cautions.
            - Keep output bounded and actionable.
            """;
    }

    // ============================================================
    //  Parsing / Storage
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

    private static List<EncyclopediaEntry> TryParseEntries(string raw, out string? error)
    {
        error = null;
        raw = (raw ?? string.Empty).Trim();
        if (raw.Length == 0)
            return new List<EncyclopediaEntry>();

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "root is not an array";
                return new List<EncyclopediaEntry>();
            }

            var list = new List<EncyclopediaEntry>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;

                var title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var id = el.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                var summary = el.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(title))
                    continue;

                id = NormalizeId(id.Length == 0 ? title : id);

                list.Add(new EncyclopediaEntry
                {
                    Id = id,
                    Title = title.Trim(),
                    Summary = TrimToMax(summary, 240),
                    Keywords = ReadStringArray(el, "keywords", max: 12),
                    Tags = ReadStringArray(el, "tags", max: 12)
                });
            }

            return list;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return new List<EncyclopediaEntry>();
        }
    }

    private static EncyclopediaQueryResult TryParseQueryResult(string raw)
    {
        raw = (raw ?? string.Empty).Trim();

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new EncyclopediaQueryResult();

            var root = doc.RootElement;
            var result = new EncyclopediaQueryResult
            {
                WhyItWorks = ReadString(root, "whyItWorks"),
                RelatedTheory = ReadString(root, "relatedTheory"),
                MassageTechniques = ReadStringArray(root, "massageTechniques", 12),
                Cautions = ReadStringArray(root, "cautions", 12),
                RelatedConcepts = ReadStringArray(root, "relatedConcepts", 12),
                RecommendedItems = new List<EncyclopediaRecommendedItem>()
            };

            if (root.TryGetProperty("recommendedItems", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in items.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;

                    result.RecommendedItems.Add(new EncyclopediaRecommendedItem
                    {
                        Name = ReadString(it, "name"),
                        Location = ReadString(it, "location"),
                        Effects = ReadString(it, "effects")
                    });

                    if (result.RecommendedItems.Count >= 12)
                        break;
                }
            }

            return result;
        }
        catch
        {
            // Fallback: raw text only.
            return new EncyclopediaQueryResult();
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

    private static string TrimToMax(string s, int maxChars)
    {
        s ??= string.Empty;
        s = s.Trim();
        if (s.Length <= maxChars) return s;
        return s[..maxChars].TrimEnd();
    }

    private static string NormalizeId(string input)
    {
        input = (input ?? string.Empty).Trim();
        if (input.Length == 0) return "entry";

        // Prefer stable hash-based id.
        var sha = Sha256Hex(input)[..10];
        return $"e-{sha}";
    }

    private static string Sha256Hex(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> ReadEntriesHintAsync(NotebookWorkspace ws, int maxChars, CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(ws.EncyclopediaDir, "entries.jsonl");
            if (!File.Exists(path))
                return "(none)";

            var sb = new StringBuilder(capacity: Math.Min(maxChars, 4096));
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                line = line.Trim();
                if (line.Length == 0) continue;

                if (sb.Length + line.Length + 1 > maxChars)
                    break;

                sb.AppendLine(line);
            }

            return sb.Length == 0 ? "(empty)" : sb.ToString();
        }
        catch
        {
            return "(unavailable)";
        }
    }
}

public sealed class EncyclopediaEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Keywords { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

public sealed class EncyclopediaMeta
{
    public string NotebookId { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public DateTimeOffset BuiltAt { get; set; }
    public int EntryCount { get; set; }
    public List<string> SourceIds { get; set; } = new();
    public string ParseError { get; set; } = "";
}

public sealed record EncyclopediaBuildResult(EncyclopediaMeta Meta, string EntriesPath);

public sealed class EncyclopediaRecommendedItem
{
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string Effects { get; set; } = "";
}

public sealed class EncyclopediaQueryResult
{
    public string Query { get; set; } = "";
    public string ProviderName { get; set; } = "";

    public List<EncyclopediaRecommendedItem> RecommendedItems { get; set; } = new();
    public string WhyItWorks { get; set; } = "";
    public List<string> MassageTechniques { get; set; } = new();
    public string RelatedTheory { get; set; } = "";
    public List<string> Cautions { get; set; } = new();
    public List<string> RelatedConcepts { get; set; } = new();

    // Debug/trace
    public List<string> SourceIds { get; set; } = new();
    public string RawText { get; set; } = "";
}


