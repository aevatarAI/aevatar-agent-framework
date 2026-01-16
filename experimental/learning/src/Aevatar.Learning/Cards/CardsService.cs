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

namespace Aevatar.Learning.Cards;

// ============================================================
//  CardsService (MVP)
//
//  目标：
//  - notebook-scoped 存储：cards/{cardId}/card.json + reviews.jsonl
//  - SRS：daily queue（due + new），review 更新（确定性 SM-2 变体）
//  - AI：可选生成记忆技巧（mnemonic），失败可降级为空，不影响复习流
//
//  设计原则：
//  - Deterministic scheduling: 只依赖 card state + review grade + UTC day
//  - Bounded payloads / bounded IO（MVP best-effort）
// ============================================================
public sealed class CardsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const int MaxCardFrontChars = 4_000;
    private const int MaxCardBackChars = 12_000;
    private const int MaxMnemonicChars = 1_000;
    private const int MaxCardsGenerate = 30;

    private readonly LearningContextBuilder _contextBuilder;
    private readonly ILLMProviderFactory _providers;
    private readonly IOptions<LLMProvidersConfig> _config;
    private readonly ILogger<CardsService> _logger;

    public CardsService(
        LearningContextBuilder contextBuilder,
        ILLMProviderFactory providers,
        IOptions<LLMProvidersConfig> config,
        ILogger<CardsService> logger)
    {
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ============================================================
    //  Generate (AI)
    // ============================================================

    public async Task<IReadOnlyList<LearningCard>> GenerateAsync(
        NotebookWorkspace workspace,
        string topic,
        int count = 10,
        string? providerName = null,
        IReadOnlyList<string>? selectedSourceIds = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        topic = (topic ?? string.Empty).Trim();
        if (topic.Length == 0)
            throw new ArgumentException("topic is required.", nameof(topic));

        count = Math.Clamp(count, 1, MaxCardsGenerate);

        workspace.EnsureDirectories();
        Directory.CreateDirectory(workspace.CardsDir);

        var ctx = await _contextBuilder.BuildAsync(
            workspace,
            query: topic,
            selectedSourceIds: selectedSourceIds,
            budget: new LearningContextBudget(MaxTotalChars: 24_000, MaxPerSourceChars: 3_000, MaxSources: 12),
            ct: ct);

        var effectiveProvider = ResolveProviderName(providerName);
        var llm = _providers.GetProvider(effectiveProvider);

        var req = new AevatarLLMRequest
        {
            SystemPrompt = BuildGenerateSystemPrompt(count),
            UserPrompt =
                $"""
                Topic: {topic}

                Notebook context:
                {ctx.Text}
                """,
            Messages = new List<Aevatar.Agents.AI.AevatarChatMessage>(),
            Settings = new AevatarLLMSettings()
        };

        _logger.LogInformation(
            "[Cards] Generate: notebook={NotebookId}, provider={Provider}, topic={Topic}, count={Count}, sources={Sources}",
            workspace.NotebookId,
            effectiveProvider,
            topic,
            count,
            ctx.Slices.Count);

        var resp = await llm.GenerateAsync(req, ct);
        var raw = (resp.Content ?? string.Empty).Trim();
        if (raw.Length == 0)
            return Array.Empty<LearningCard>();

        var cards = TryParseCards(raw);
        if (cards.Count == 0)
            return Array.Empty<LearningCard>();

        // Persist (idempotent by stable cardId)
        var now = DateTimeOffset.UtcNow;
        foreach (var c in cards)
        {
            ct.ThrowIfCancellationRequested();

            c.CardId = BuildCardId(c);
            c.CreatedAt = c.CreatedAt == default ? now : c.CreatedAt;
            c.UpdatedAt = now;

            c.Front = TrimToMax(c.Front, MaxCardFrontChars);
            c.Back = TrimToMax(c.Back, MaxCardBackChars);
            c.Mnemonic = TrimToMax(c.Mnemonic, MaxMnemonicChars);

            // Initial SRS defaults
            if (c.EaseFactor <= 0) c.EaseFactor = 2.5;
            if (c.EaseFactor < 1.3) c.EaseFactor = 1.3;
            if (c.IntervalDays < 0) c.IntervalDays = 0;
            if (c.Repetitions < 0) c.Repetitions = 0;
            if (c.ReviewCount < 0) c.ReviewCount = 0;

            // Keep dueAt deterministic: new cards are due today.
            if (c.DueAt == default)
                c.DueAt = UtcDayStart(now);

            c.SourceIds = ctx.Slices.Select(s => s.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            await UpsertCardAsync(workspace, c, ct);
        }

        return cards;
    }

    // ============================================================
    //  Daily Queue
    // ============================================================

    public async Task<CardsDailyQueue> GetDailyQueueAsync(
        NotebookWorkspace workspace,
        DateTimeOffset? nowUtc = null,
        int maxDue = 20,
        int maxNew = 20,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.EnsureDirectories();
        Directory.CreateDirectory(workspace.CardsDir);

        maxDue = Math.Clamp(maxDue, 0, 200);
        maxNew = Math.Clamp(maxNew, 0, 200);

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var all = await ListCardsAsync(workspace, ct);

        var dueAll = all
            .Where(c => c.Repetitions > 0 && c.DueAt != default && c.DueAt <= now)
            .OrderBy(c => c.DueAt)
            .ThenBy(c => c.CardId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var newAll = all
            .Where(c => c.Repetitions == 0)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.CardId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var due = dueAll.Take(maxDue).ToList();
        var @new = newAll.Take(maxNew).ToList();

        return new CardsDailyQueue(
            AsOf: now,
            DueTotal: dueAll.Count,
            NewTotal: newAll.Count,
            Due: due,
            New: @new);
    }

    // ============================================================
    //  Review
    // ============================================================

    public async Task<CardsReviewResult> ReviewAsync(
        NotebookWorkspace workspace,
        string cardId,
        int grade,
        bool generateMnemonic = false,
        string? providerName = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cardId = (cardId ?? string.Empty).Trim();
        if (cardId.Length == 0)
            throw new ArgumentException("cardId is required.", nameof(cardId));

        grade = Math.Clamp(grade, 0, 5);

        var now = DateTimeOffset.UtcNow;
        var card = await GetCardAsync(workspace, cardId, ct);

        var before = card.CloneForReview();
        ApplySm2(card, grade, now);

        // Optional mnemonic generation (best-effort)
        if ((generateMnemonic || grade <= 2) && string.IsNullOrWhiteSpace(card.Mnemonic))
        {
            var mnemonic = await TryGenerateMnemonicAsync(card, providerName, ct);
            if (!string.IsNullOrWhiteSpace(mnemonic))
                card.Mnemonic = TrimToMax(mnemonic, MaxMnemonicChars);
        }

        card.UpdatedAt = now;
        await UpsertCardAsync(workspace, card, ct);

        // Append review event (best-effort; never block the main update)
        _ = AppendReviewAsync(workspace, cardId, new LearningCardReview
        {
            ReviewedAt = now,
            Grade = grade,
            DueAtBefore = before.DueAt,
            DueAtAfter = card.DueAt,
            IntervalDaysBefore = before.IntervalDays,
            IntervalDaysAfter = card.IntervalDays,
            EaseFactorBefore = before.EaseFactor,
            EaseFactorAfter = card.EaseFactor
        }, ct);

        return new CardsReviewResult(before, card);
    }

    // ============================================================
    //  Stats
    // ============================================================

    public async Task<CardsStats> GetStatsAsync(
        NotebookWorkspace workspace,
        DateTimeOffset? nowUtc = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.EnsureDirectories();
        Directory.CreateDirectory(workspace.CardsDir);

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var dayStart = UtcDayStart(now);

        var all = await ListCardsAsync(workspace, ct);

        var total = all.Count;
        var newCount = all.Count(c => c.Repetitions == 0);
        var dueCount = all.Count(c => c.Repetitions > 0 && c.DueAt != default && c.DueAt <= now);
        var reviewedToday = all.Count(c => c.LastReviewedAt != null && c.LastReviewedAt >= dayStart && c.LastReviewedAt <= now);
        var totalReviews = all.Sum(c => Math.Max(0, c.ReviewCount));

        return new CardsStats(
            AsOf: now,
            TotalCards: total,
            NewCards: newCount,
            DueCards: dueCount,
            ReviewedCardsToday: reviewedToday,
            TotalReviews: totalReviews);
    }

    // ============================================================
    //  Storage
    // ============================================================

    private static string CardDir(NotebookWorkspace ws, string cardId) => Path.Combine(ws.CardsDir, cardId);
    private static string CardPath(NotebookWorkspace ws, string cardId) => Path.Combine(CardDir(ws, cardId), "card.json");
    private static string ReviewsPath(NotebookWorkspace ws, string cardId) => Path.Combine(CardDir(ws, cardId), "reviews.jsonl");

    private static async Task AppendReviewAsync(NotebookWorkspace ws, string cardId, LearningCardReview review, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(CardDir(ws, cardId));
            var line = JsonSerializer.Serialize(review, Json);
            await File.AppendAllTextAsync(ReviewsPath(ws, cardId), line + "\n", new UTF8Encoding(false), ct);
        }
        catch
        {
            // best-effort
        }
    }

    private static async Task UpsertCardAsync(NotebookWorkspace ws, LearningCard card, CancellationToken ct)
    {
        Directory.CreateDirectory(CardDir(ws, card.CardId));
        var path = CardPath(ws, card.CardId);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(card, Json), new UTF8Encoding(false), ct);
    }

    private static async Task<LearningCard> GetCardAsync(NotebookWorkspace ws, string cardId, CancellationToken ct)
    {
        var path = CardPath(ws, cardId);
        if (!File.Exists(path))
            throw new FileNotFoundException("card not found", path);

        var json = await File.ReadAllTextAsync(path, ct);
        var card = JsonSerializer.Deserialize<LearningCard>(json, Json);
        if (card == null || string.IsNullOrWhiteSpace(card.CardId))
            throw new InvalidOperationException("invalid card.json");

        card.CardId = card.CardId.Trim();
        card.Kind = (card.Kind ?? "basic").Trim();
        card.Front = (card.Front ?? string.Empty).Trim();
        card.Back = (card.Back ?? string.Empty).Trim();
        card.Mnemonic = (card.Mnemonic ?? string.Empty).Trim();
        card.Tags ??= new List<string>();
        card.SourceIds ??= new List<string>();

        return card;
    }

    private static async Task<List<LearningCard>> ListCardsAsync(NotebookWorkspace ws, CancellationToken ct)
    {
        var list = new List<LearningCard>(capacity: 64);
        if (!Directory.Exists(ws.CardsDir))
            return list;

        foreach (var dir in Directory.EnumerateDirectories(ws.CardsDir))
        {
            ct.ThrowIfCancellationRequested();

            var path = Path.Combine(dir, "card.json");
            if (!File.Exists(path))
                continue;

            try
            {
                var json = await File.ReadAllTextAsync(path, ct);
                var card = JsonSerializer.Deserialize<LearningCard>(json, Json);
                if (card == null || string.IsNullOrWhiteSpace(card.CardId) || string.IsNullOrWhiteSpace(card.Front))
                    continue;

                card.CardId = card.CardId.Trim();
                card.Kind = (card.Kind ?? "basic").Trim();
                card.Front = (card.Front ?? string.Empty).Trim();
                card.Back = (card.Back ?? string.Empty).Trim();
                card.Mnemonic = (card.Mnemonic ?? string.Empty).Trim();
                card.Tags ??= new List<string>();
                card.SourceIds ??= new List<string>();

                list.Add(card);
            }
            catch
            {
                // best-effort
            }
        }

        return list;
    }

    // ============================================================
    //  SRS (SM-2 variant; deterministic)
    // ============================================================

    private static void ApplySm2(LearningCard card, int grade, DateTimeOffset nowUtc)
    {
        // Normalize
        card.EaseFactor = card.EaseFactor <= 0 ? 2.5 : card.EaseFactor;
        if (card.EaseFactor < 1.3) card.EaseFactor = 1.3;
        if (card.IntervalDays < 0) card.IntervalDays = 0;
        if (card.Repetitions < 0) card.Repetitions = 0;
        if (card.ReviewCount < 0) card.ReviewCount = 0;

        card.ReviewCount++;
        card.LastReviewedAt = nowUtc;

        var ef = card.EaseFactor;
        var reps = card.Repetitions;
        var interval = card.IntervalDays;

        if (grade < 3)
        {
            // Failure => reset repetition chain, schedule tomorrow.
            card.Lapses++;
            reps = 0;
            interval = 1;
        }
        else
        {
            reps += 1;
            interval = reps switch
            {
                1 => 1,
                2 => 6,
                _ => Math.Max(1, (int)Math.Round(interval * ef))
            };
        }

        // EF update (SM-2)
        var diff = 5 - grade;
        ef = ef + (0.1 - diff * (0.08 + diff * 0.02));
        if (ef < 1.3) ef = 1.3;

        card.EaseFactor = ef;
        card.Repetitions = reps;
        card.IntervalDays = interval;
        card.DueAt = UtcDayStart(nowUtc).AddDays(interval);
    }

    private static DateTimeOffset UtcDayStart(DateTimeOffset nowUtc)
    {
        var d = nowUtc.UtcDateTime.Date;
        return new DateTimeOffset(d, TimeSpan.Zero);
    }

    // ============================================================
    //  Mnemonics (best-effort)
    // ============================================================

    private async Task<string?> TryGenerateMnemonicAsync(LearningCard card, string? providerName, CancellationToken ct)
    {
        try
        {
            var effectiveProvider = ResolveProviderName(providerName);
            var llm = _providers.GetProvider(effectiveProvider);

            var req = new AevatarLLMRequest
            {
                SystemPrompt =
                    """
                    You create short, practical memory techniques for learning flashcards.

                    Output rules:
                    - Output plain text only (no markdown fences).
                    - Keep it short (1-3 sentences).
                    - Prefer mnemonics, imagery, chunking, or associations.
                    """,
                UserPrompt =
                    $"""
                    Create a memory technique for this card.

                    Front:
                    {card.Front}

                    Back:
                    {card.Back}
                    """,
                Messages = new List<Aevatar.Agents.AI.AevatarChatMessage>(),
                Settings = new AevatarLLMSettings()
            };

            var resp = await llm.GenerateAsync(req, ct);
            var text = (resp.Content ?? string.Empty).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Cards] Mnemonic generation failed (best-effort).");
            return null;
        }
    }

    // ============================================================
    //  Prompts / Parsing
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

    private static string BuildGenerateSystemPrompt(int count)
    {
        // Keep schema simple and robust.
        return
            $"""
            You generate learning flashcards for spaced repetition.

            Output MUST be a single JSON array (no markdown fences), with {count} items.

            Each item is an object with fields:
            - kind: "basic"
            - front: string
            - back: string
            - mnemonic: string (optional)
            - tags: string[] (optional)

            Rules:
            - Keep front/back concise.
            - Prefer factual cards grounded in the notebook context.
            - If context is insufficient, generate generic but safe cards and tag them as "needs_sources".
            """;
    }

    private static List<LearningCard> TryParseCards(string raw)
    {
        raw = (raw ?? string.Empty).Trim();
        if (raw.Length == 0)
            return new List<LearningCard>();

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new List<LearningCard>();

            var list = new List<LearningCard>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;

                var front = el.TryGetProperty("front", out var f) ? f.GetString() ?? "" : "";
                var back = el.TryGetProperty("back", out var b) ? b.GetString() ?? "" : "";

                front = (front ?? string.Empty).Trim();
                back = (back ?? string.Empty).Trim();
                if (front.Length == 0 || back.Length == 0)
                    continue;

                var kind = el.TryGetProperty("kind", out var k) ? k.GetString() ?? "basic" : "basic";
                var mnemonic = el.TryGetProperty("mnemonic", out var m) ? m.GetString() ?? "" : "";

                var tags = new List<string>();
                if (el.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in t.EnumerateArray())
                    {
                        if (it.ValueKind != JsonValueKind.String) continue;
                        var s = (it.GetString() ?? string.Empty).Trim();
                        if (s.Length == 0) continue;
                        tags.Add(s);
                        if (tags.Count >= 12) break;
                    }
                }

                list.Add(new LearningCard
                {
                    Kind = (kind ?? "basic").Trim(),
                    Front = front,
                    Back = back,
                    Mnemonic = (mnemonic ?? string.Empty).Trim(),
                    Tags = tags
                });

                if (list.Count >= MaxCardsGenerate)
                    break;
            }

            return list;
        }
        catch
        {
            return new List<LearningCard>();
        }
    }

    private static string BuildCardId(LearningCard c)
    {
        var kind = (c.Kind ?? "basic").Trim().ToLowerInvariant();
        var front = (c.Front ?? string.Empty).Trim();
        var back = (c.Back ?? string.Empty).Trim();

        var stable = $"{kind}\n{front}\n{back}";
        var sha = Sha256Hex(stable)[..12];
        return $"c-{sha}";
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

// NOTE:
// - These are internal storage/HTTP DTO models (directory-backed).
// - If/when cards need to cross runtime boundaries (Orleans streams / agent state),
//   move them into Protobuf contracts.
public sealed class LearningCard
{
    public string CardId { get; set; } = "";
    public string Kind { get; set; } = "basic";
    public string Front { get; set; } = "";
    public string Back { get; set; } = "";
    public string Mnemonic { get; set; } = "";

    // SRS state
    public int Repetitions { get; set; } = 0;
    public int IntervalDays { get; set; } = 0;
    public double EaseFactor { get; set; } = 2.5;
    public int Lapses { get; set; } = 0;
    public int ReviewCount { get; set; } = 0;
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }

    // bookkeeping
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> SourceIds { get; set; } = new();

    internal LearningCard ReviewClone => (LearningCard)MemberwiseClone();

    internal LearningCardReviewSnapshot CloneForReview()
    {
        return new LearningCardReviewSnapshot
        {
            CardId = CardId,
            Repetitions = Repetitions,
            IntervalDays = IntervalDays,
            EaseFactor = EaseFactor,
            Lapses = Lapses,
            ReviewCount = ReviewCount,
            DueAt = DueAt,
            LastReviewedAt = LastReviewedAt
        };
    }
}

public sealed class LearningCardReview
{
    public DateTimeOffset ReviewedAt { get; set; }
    public int Grade { get; set; }
    public DateTimeOffset DueAtBefore { get; set; }
    public DateTimeOffset DueAtAfter { get; set; }
    public int IntervalDaysBefore { get; set; }
    public int IntervalDaysAfter { get; set; }
    public double EaseFactorBefore { get; set; }
    public double EaseFactorAfter { get; set; }
}

public sealed class LearningCardReviewSnapshot
{
    public string CardId { get; set; } = "";
    public int Repetitions { get; set; }
    public int IntervalDays { get; set; }
    public double EaseFactor { get; set; }
    public int Lapses { get; set; }
    public int ReviewCount { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
}

public sealed record CardsDailyQueue(
    DateTimeOffset AsOf,
    int DueTotal,
    int NewTotal,
    IReadOnlyList<LearningCard> Due,
    IReadOnlyList<LearningCard> New);

public sealed record CardsReviewResult(
    LearningCardReviewSnapshot Before,
    LearningCard After);

public sealed record CardsStats(
    DateTimeOffset AsOf,
    int TotalCards,
    int NewCards,
    int DueCards,
    int ReviewedCardsToday,
    int TotalReviews);


