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

namespace Aevatar.Learning.Quiz;

// ============================================================
//  QuizService (MVP)
//
//  目标：
//  - 基于 notebook sources/context 生成测验题目（选择/判断/简答）
//  - 提交作答后：给出参考答案/解析，并记录测验结果到 quizzes/
//  - AI 判分 best-effort；可配置严格/灵活模式（MVP：strict=false 默认）
//
//  存储：
//  - quizzes/{quizId}/quiz.json         (题目集)
//  - quizzes/{quizId}/attempts.jsonl    (作答记录，append-only)
// ============================================================
public sealed class QuizService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const int MaxQuizQuestions = 20;
    private const int MaxExplanationChars = 6_000;
    private const int MaxPromptChars = 40_000;

    private readonly LearningContextBuilder _contextBuilder;
    private readonly ILLMProviderFactory _providers;
    private readonly IOptions<LLMProvidersConfig> _config;
    private readonly ILogger<QuizService> _logger;

    public QuizService(
        LearningContextBuilder contextBuilder,
        ILLMProviderFactory providers,
        IOptions<LLMProvidersConfig> config,
        ILogger<QuizService> logger)
    {
        _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ============================================================
    //  Generate
    // ============================================================

    public async Task<QuizGenerated> GenerateAsync(
        NotebookWorkspace workspace,
        string topic,
        int count = 10,
        bool strictGrading = false,
        string? providerName = null,
        IReadOnlyList<string>? selectedSourceIds = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        topic = (topic ?? string.Empty).Trim();
        if (topic.Length == 0)
            throw new ArgumentException("topic is required.", nameof(topic));

        count = Math.Clamp(count, 1, MaxQuizQuestions);

        workspace.EnsureDirectories();
        Directory.CreateDirectory(workspace.QuizzesDir);

        var ctx = await _contextBuilder.BuildAsync(
            workspace,
            query: topic,
            selectedSourceIds: selectedSourceIds,
            budget: new LearningContextBudget(MaxTotalChars: 24_000, MaxPerSourceChars: 3_000, MaxSources: 12),
            ct: ct);

        var provider = ResolveProviderName(providerName);
        var llm = _providers.GetProvider(provider);

        var req = new AevatarLLMRequest
        {
            SystemPrompt = BuildGenerateSystemPrompt(count),
            UserPrompt =
                TrimToMax(
                    $"""
                    Topic: {topic}
                    Strict grading: {strictGrading}

                    Notebook context:
                    {ctx.Text}
                    """,
                    MaxPromptChars),
            Messages = new List<Aevatar.Agents.AI.AevatarChatMessage>(),
            Settings = new AevatarLLMSettings()
        };

        _logger.LogInformation(
            "[Quiz] Generate: notebook={NotebookId}, provider={Provider}, topic={Topic}, count={Count}, sources={Sources}",
            workspace.NotebookId,
            provider,
            topic,
            count,
            ctx.Slices.Count);

        var resp = await llm.GenerateAsync(req, ct);
        var raw = (resp.Content ?? string.Empty).Trim();
        if (raw.Length == 0)
            throw new InvalidOperationException("LLM returned empty quiz.");

        var questions = TryParseQuestions(raw);
        if (questions.Count == 0)
            throw new InvalidOperationException("Failed to parse quiz JSON (expected an array of questions).");

        // Normalize + bound
        foreach (var q in questions)
        {
            q.QuestionId = q.QuestionId.Trim();
            if (q.QuestionId.Length == 0)
                q.QuestionId = $"q-{Sha256Hex($"{q.Type}\n{q.Prompt}")[..10]}";

            q.Type = NormalizeType(q.Type);
            q.Prompt = TrimToMax(q.Prompt, 2_000);
            q.Explanation = TrimToMax(q.Explanation, MaxExplanationChars);
            q.Options ??= new List<string>();

            // Bound options
            q.Options = q.Options
                .Select(o => (o ?? string.Empty).Trim())
                .Where(o => o.Length > 0)
                .Take(10)
                .ToList();

            // Ensure correctAnswer exists
            q.CorrectAnswer = (q.CorrectAnswer ?? string.Empty).Trim();

            if (q.Type == "mcq")
            {
                if (q.Options.Count < 2)
                    q.Options = new List<string> { "A", "B" };

                // Normalize answer to option index when possible
                if (!TryNormalizeMcqAnswer(q, out var normalized))
                    normalized = "0";
                q.CorrectAnswer = normalized;
            }
            else if (q.Type == "tf")
            {
                // Represent answer as "true"/"false"
                q.CorrectAnswer = NormalizeBoolAnswer(q.CorrectAnswer);
            }
        }

        var quizId = BuildQuizId(topic);
        var quizDir = Path.Combine(workspace.QuizzesDir, quizId);
        Directory.CreateDirectory(quizDir);

        var now = DateTimeOffset.UtcNow;
        var quiz = new QuizDefinition
        {
            QuizId = quizId,
            NotebookId = workspace.NotebookId,
            Topic = topic,
            ProviderName = provider,
            StrictGrading = strictGrading,
            CreatedAt = now,
            SourceIds = ctx.Slices.Select(s => s.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Questions = questions
        };

        var quizPath = Path.Combine(quizDir, "quiz.json");
        await File.WriteAllTextAsync(quizPath, JsonSerializer.Serialize(quiz, Json), new UTF8Encoding(false), ct);

        return new QuizGenerated(quiz, quizPath);
    }

    // ============================================================
    //  Submit
    // ============================================================

    public async Task<QuizAttemptResult> SubmitAsync(
        NotebookWorkspace workspace,
        string quizId,
        IReadOnlyList<QuizAnswer> answers,
        bool? strictGradingOverride = null,
        string? providerName = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        quizId = (quizId ?? string.Empty).Trim();
        if (quizId.Length == 0)
            throw new ArgumentException("quizId is required.", nameof(quizId));

        workspace.EnsureDirectories();
        Directory.CreateDirectory(workspace.QuizzesDir);

        var quiz = await LoadQuizAsync(workspace, quizId, ct);

        var strict = strictGradingOverride ?? quiz.StrictGrading;

        // Build answer map
        var byId = new Dictionary<string, QuizAnswer>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in answers ?? Array.Empty<QuizAnswer>())
        {
            if (string.IsNullOrWhiteSpace(a.QuestionId)) continue;
            byId[a.QuestionId.Trim()] = a;
        }

        // Grade deterministically when possible; use AI best-effort for short answers if strict requested.
        var graded = new List<QuizGradedAnswer>(capacity: quiz.Questions.Count);
        var correct = 0;
        foreach (var q in quiz.Questions)
        {
            ct.ThrowIfCancellationRequested();

            byId.TryGetValue(q.QuestionId, out var user);
            var userAnswer = (user?.Answer ?? string.Empty).Trim();

            var (isCorrect, rationale) = q.Type switch
            {
                "mcq" => GradeMcq(q, userAnswer),
                "tf" => GradeTf(q, userAnswer),
                _ => (null, "")
            };

            // Short answer grading: best-effort.
            if (q.Type == "short")
            {
                if (!strict)
                {
                    // Flexible mode: do not auto-fail; provide reference answer and a hint.
                    isCorrect = null;
                    rationale = "Flexible mode: no strict auto-grading for short answers.";
                }
                else
                {
                    var provider = ResolveProviderName(providerName ?? quiz.ProviderName);
                    var llm = _providers.GetProvider(provider);

                    var ai = await TryGradeShortAnswerAsync(llm, quiz, q, userAnswer, ct);
                    isCorrect = ai.IsCorrect;
                    rationale = ai.Explanation;
                }
            }

            if (isCorrect == true)
                correct++;

            graded.Add(new QuizGradedAnswer
            {
                QuestionId = q.QuestionId,
                Type = q.Type,
                Prompt = q.Prompt,
                UserAnswer = userAnswer,
                CorrectAnswer = q.CorrectAnswer,
                IsCorrect = isCorrect,
                Explanation = TrimToMax(string.IsNullOrWhiteSpace(rationale) ? q.Explanation : rationale, MaxExplanationChars),
                Options = q.Options
            });
        }

        var attempt = new QuizAttempt
        {
            AttemptId = $"a-{Guid.NewGuid():N}"[..14],
            QuizId = quiz.QuizId,
            SubmittedAt = DateTimeOffset.UtcNow,
            StrictGrading = strict,
            TotalQuestions = quiz.Questions.Count,
            CorrectCount = correct,
            Answers = graded
        };

        await AppendAttemptAsync(workspace, quizId, attempt, ct);
        return new QuizAttemptResult(quiz, attempt);
    }

    // ============================================================
    //  History
    // ============================================================

    public async Task<IReadOnlyList<QuizAttemptSummary>> ListHistoryAsync(
        NotebookWorkspace workspace,
        int max = 30,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.EnsureDirectories();
        Directory.CreateDirectory(workspace.QuizzesDir);

        max = Math.Clamp(max, 1, 200);

        var list = new List<QuizAttemptSummary>(capacity: 32);
        if (!Directory.Exists(workspace.QuizzesDir))
            return list;

        foreach (var dir in Directory.EnumerateDirectories(workspace.QuizzesDir))
        {
            ct.ThrowIfCancellationRequested();

            var quizPath = Path.Combine(dir, "quiz.json");
            if (!File.Exists(quizPath)) continue;

            QuizDefinition? quiz;
            try
            {
                var json = await File.ReadAllTextAsync(quizPath, ct);
                quiz = JsonSerializer.Deserialize<QuizDefinition>(json, Json);
            }
            catch
            {
                continue;
            }

            if (quiz == null || string.IsNullOrWhiteSpace(quiz.QuizId))
                continue;

            var attemptsPath = Path.Combine(dir, "attempts.jsonl");
            var last = await TryReadLastAttemptAsync(attemptsPath, ct);

            list.Add(new QuizAttemptSummary
            {
                QuizId = quiz.QuizId,
                Topic = quiz.Topic,
                CreatedAt = quiz.CreatedAt,
                TotalQuestions = quiz.Questions?.Count ?? 0,
                LastAttemptAt = last?.SubmittedAt,
                LastScore = last == null ? null : new QuizScore(last.CorrectCount, last.TotalQuestions)
            });
        }

        return list
            .OrderByDescending(x => x.LastAttemptAt ?? x.CreatedAt)
            .ThenBy(x => x.Topic, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    // ============================================================
    //  Storage helpers
    // ============================================================

    private static string QuizDir(NotebookWorkspace ws, string quizId) => Path.Combine(ws.QuizzesDir, quizId);

    private static async Task AppendAttemptAsync(NotebookWorkspace ws, string quizId, QuizAttempt attempt, CancellationToken ct)
    {
        Directory.CreateDirectory(QuizDir(ws, quizId));
        var attemptsPath = Path.Combine(QuizDir(ws, quizId), "attempts.jsonl");
        var line = JsonSerializer.Serialize(attempt, Json);
        await File.AppendAllTextAsync(attemptsPath, line + "\n", new UTF8Encoding(false), ct);
    }

    private static async Task<QuizAttempt?> TryReadLastAttemptAsync(string attemptsPath, CancellationToken ct)
    {
        if (!File.Exists(attemptsPath))
            return null;

        try
        {
            // Best-effort: scan last N lines by reading all if small.
            var text = await File.ReadAllTextAsync(attemptsPath, ct);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                try
                {
                    var a = JsonSerializer.Deserialize<QuizAttempt>(line, Json);
                    if (a != null && !string.IsNullOrWhiteSpace(a.AttemptId))
                        return a;
                }
                catch
                {
                    // ignore line
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<QuizDefinition> LoadQuizAsync(NotebookWorkspace ws, string quizId, CancellationToken ct)
    {
        var path = Path.Combine(QuizDir(ws, quizId), "quiz.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("quiz not found", path);

        var json = await File.ReadAllTextAsync(path, ct);
        var quiz = JsonSerializer.Deserialize<QuizDefinition>(json, Json);
        if (quiz == null || string.IsNullOrWhiteSpace(quiz.QuizId))
            throw new InvalidOperationException("invalid quiz.json");

        quiz.Questions ??= new List<QuizQuestion>();
        return quiz;
    }

    // ============================================================
    //  Grading helpers
    // ============================================================

    private static (bool? IsCorrect, string Explanation) GradeMcq(QuizQuestion q, string userAnswer)
    {
        // CorrectAnswer normalized to option index as string.
        var correctIndex = q.CorrectAnswer;
        var userIndex = NormalizeMcqUserAnswer(q, userAnswer);
        if (userIndex == null)
            return (false, "Invalid answer format. Expected option index or option label.");

        var ok = string.Equals(userIndex, correctIndex, StringComparison.OrdinalIgnoreCase);
        return (ok, ok ? "Correct." : "Incorrect.");
    }

    private static (bool? IsCorrect, string Explanation) GradeTf(QuizQuestion q, string userAnswer)
    {
        var correct = NormalizeBoolAnswer(q.CorrectAnswer);
        var user = NormalizeBoolAnswer(userAnswer);
        if (user.Length == 0)
            return (false, "Invalid answer format. Expected true/false.");

        var ok = string.Equals(user, correct, StringComparison.OrdinalIgnoreCase);
        return (ok, ok ? "Correct." : "Incorrect.");
    }

    private sealed record ShortAnswerGrade(bool IsCorrect, string Explanation);

    private async Task<ShortAnswerGrade> TryGradeShortAnswerAsync(
        IAevatarLLMProvider llm,
        QuizDefinition quiz,
        QuizQuestion q,
        string userAnswer,
        CancellationToken ct)
    {
        try
        {
            var req = new AevatarLLMRequest
            {
                SystemPrompt =
                    """
                    You grade a short answer question.

                    Output MUST be a single JSON object (no markdown fences):
                    { "isCorrect": true|false, "explanation": "string" }

                    Rules:
                    - Be strict but fair.
                    - Use the reference answer and notebook topic as ground truth.
                    - If userAnswer is empty, isCorrect must be false.
                    """,
                UserPrompt =
                    TrimToMax(
                        $"""
                        Topic: {quiz.Topic}

                        Question:
                        {q.Prompt}

                        Reference answer:
                        {q.CorrectAnswer}

                        User answer:
                        {userAnswer}
                        """,
                        16_000),
                Messages = new List<Aevatar.Agents.AI.AevatarChatMessage>(),
                Settings = new AevatarLLMSettings()
            };

            var resp = await llm.GenerateAsync(req, ct);
            var raw = (resp.Content ?? string.Empty).Trim();
            if (raw.Length == 0)
                return new ShortAnswerGrade(false, "AI grader returned empty response.");

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new ShortAnswerGrade(false, "AI grader returned invalid JSON.");

            var isCorrect = doc.RootElement.TryGetProperty("isCorrect", out var ok) && ok.ValueKind == JsonValueKind.True
                ? true
                : doc.RootElement.TryGetProperty("isCorrect", out ok) && ok.ValueKind == JsonValueKind.False
                    ? false
                    : false;

            var explanation = doc.RootElement.TryGetProperty("explanation", out var e) ? (e.GetString() ?? "") : "";
            explanation = explanation.Trim();
            if (explanation.Length == 0)
                explanation = isCorrect ? "Correct." : "Incorrect.";

            return new ShortAnswerGrade(isCorrect, explanation);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Quiz] AI grading failed (best-effort).");
            return new ShortAnswerGrade(false, "AI grading failed (best-effort).");
        }
    }

    // ============================================================
    //  Prompting / parsing
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
        return
            $"""
            You generate a learning quiz from notebook materials.

            Output MUST be a single JSON array (no markdown fences), with {count} items.

            Each item schema:
            - questionId: string (optional)
            - type: one of ["mcq","tf","short"]
            - prompt: string
            - options: string[] (required for mcq; ignored otherwise)
            - correctAnswer: string
              - mcq: option index as string, e.g. "0" for first option
              - tf: "true" or "false"
              - short: reference answer text
            - explanation: string (optional, short)

            Rules:
            - Prefer factual questions grounded in the notebook context.
            - Keep each question short and unambiguous.
            - For mcq, provide 3-5 options, only one correct.
            """;
    }

    private static List<QuizQuestion> TryParseQuestions(string raw)
    {
        raw = (raw ?? string.Empty).Trim();
        if (raw.Length == 0)
            return new List<QuizQuestion>();

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new List<QuizQuestion>();

            var list = new List<QuizQuestion>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                var type = el.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";
                var prompt = el.TryGetProperty("prompt", out var p) ? (p.GetString() ?? "") : "";
                var correct = el.TryGetProperty("correctAnswer", out var c) ? (c.GetString() ?? "") : "";
                var qid = el.TryGetProperty("questionId", out var id) ? (id.GetString() ?? "") : "";
                var explanation = el.TryGetProperty("explanation", out var e) ? (e.GetString() ?? "") : "";

                prompt = (prompt ?? string.Empty).Trim();
                if (prompt.Length == 0) continue;

                var options = new List<string>();
                if (el.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in o.EnumerateArray())
                    {
                        if (it.ValueKind != JsonValueKind.String) continue;
                        var s = (it.GetString() ?? "").Trim();
                        if (s.Length == 0) continue;
                        options.Add(s);
                        if (options.Count >= 10) break;
                    }
                }

                list.Add(new QuizQuestion
                {
                    QuestionId = (qid ?? "").Trim(),
                    Type = (type ?? "").Trim(),
                    Prompt = prompt,
                    Options = options,
                    CorrectAnswer = (correct ?? "").Trim(),
                    Explanation = (explanation ?? "").Trim()
                });

                if (list.Count >= MaxQuizQuestions) break;
            }

            return list;
        }
        catch
        {
            return new List<QuizQuestion>();
        }
    }

    private static string NormalizeType(string type)
    {
        type = (type ?? string.Empty).Trim().ToLowerInvariant();
        return type switch
        {
            "mcq" => "mcq",
            "multiple_choice" => "mcq",
            "choice" => "mcq",
            "tf" => "tf",
            "true_false" => "tf",
            "true/false" => "tf",
            "short" => "short",
            "short_answer" => "short",
            _ => "mcq"
        };
    }

    private static bool TryNormalizeMcqAnswer(QuizQuestion q, out string normalizedIndex)
    {
        normalizedIndex = "";
        var ans = (q.CorrectAnswer ?? string.Empty).Trim();
        if (ans.Length == 0) return false;

        // Already index?
        if (int.TryParse(ans, out var idx) && idx >= 0 && idx < q.Options.Count)
        {
            normalizedIndex = idx.ToString();
            return true;
        }

        // Letter label? (A/B/C...)
        var ch = char.ToUpperInvariant(ans[0]);
        if (ch >= 'A' && ch <= 'J')
        {
            idx = ch - 'A';
            if (idx >= 0 && idx < q.Options.Count)
            {
                normalizedIndex = idx.ToString();
                return true;
            }
        }

        // Match by option text
        for (var i = 0; i < q.Options.Count; i++)
        {
            if (string.Equals(q.Options[i], ans, StringComparison.OrdinalIgnoreCase))
            {
                normalizedIndex = i.ToString();
                return true;
            }
        }

        return false;
    }

    private static string? NormalizeMcqUserAnswer(QuizQuestion q, string userAnswer)
    {
        userAnswer = (userAnswer ?? string.Empty).Trim();
        if (userAnswer.Length == 0) return null;

        if (int.TryParse(userAnswer, out var idx) && idx >= 0 && idx < q.Options.Count)
            return idx.ToString();

        var ch = char.ToUpperInvariant(userAnswer[0]);
        if (ch >= 'A' && ch <= 'J')
        {
            idx = ch - 'A';
            if (idx >= 0 && idx < q.Options.Count)
                return idx.ToString();
        }

        // Option text
        for (var i = 0; i < q.Options.Count; i++)
        {
            if (string.Equals(q.Options[i], userAnswer, StringComparison.OrdinalIgnoreCase))
                return i.ToString();
        }

        return null;
    }

    private static string NormalizeBoolAnswer(string input)
    {
        input = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (input is "true" or "t" or "yes" or "y" or "1") return "true";
        if (input is "false" or "f" or "no" or "n" or "0") return "false";
        return "";
    }

    private static string BuildQuizId(string topic)
    {
        var sha8 = Sha256Hex(topic)[..8];
        return $"quiz-{sha8}";
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
// - If quiz needs to cross runtime boundaries, move to Protobuf contracts.
public sealed class QuizDefinition
{
    public string QuizId { get; set; } = "";
    public string NotebookId { get; set; } = "";
    public string Topic { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public bool StrictGrading { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<string> SourceIds { get; set; } = new();
    public List<QuizQuestion> Questions { get; set; } = new();
}

public sealed class QuizQuestion
{
    public string QuestionId { get; set; } = "";
    public string Type { get; set; } = "mcq"; // mcq/tf/short
    public string Prompt { get; set; } = "";
    public List<string> Options { get; set; } = new(); // mcq only
    public string CorrectAnswer { get; set; } = "";
    public string Explanation { get; set; } = "";
}

public sealed class QuizAnswer
{
    public string QuestionId { get; set; } = "";
    public string Answer { get; set; } = "";
}

public sealed class QuizGradedAnswer
{
    public string QuestionId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Prompt { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public string UserAnswer { get; set; } = "";
    public string CorrectAnswer { get; set; } = "";
    public bool? IsCorrect { get; set; }
    public string Explanation { get; set; } = "";
}

public sealed class QuizAttempt
{
    public string AttemptId { get; set; } = "";
    public string QuizId { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
    public bool StrictGrading { get; set; }
    public int TotalQuestions { get; set; }
    public int CorrectCount { get; set; }
    public List<QuizGradedAnswer> Answers { get; set; } = new();
}

public sealed record QuizScore(int Correct, int Total);

public sealed class QuizAttemptSummary
{
    public string QuizId { get; set; } = "";
    public string Topic { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public int TotalQuestions { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public QuizScore? LastScore { get; set; }
}

public sealed record QuizGenerated(QuizDefinition Quiz, string FilePath);

public sealed record QuizAttemptResult(QuizDefinition Quiz, QuizAttempt Attempt);


