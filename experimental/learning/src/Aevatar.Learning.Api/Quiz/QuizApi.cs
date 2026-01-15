using Aevatar.Learning.Notebooks;
using Aevatar.Learning.Quiz;

namespace Aevatar.Learning.Api.Quiz;

// ============================================================
//  Quiz API (notebook-scoped)
//
//  Endpoints:
//  - POST /api/notebooks/{id}/quiz:generate
//  - POST /api/notebooks/{id}/quiz:submit
//  - GET  /api/notebooks/{id}/quiz:history
// ============================================================
internal static class QuizApi
{
    public static void MapQuizApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapGenerate(app);
        MapSubmit(app);
        MapHistory(app);
    }

    private static void MapGenerate(WebApplication app)
    {
        app.MapPost("/api/notebooks/{notebookId}/quiz:generate", async (
            string notebookId,
            GenerateInDto input,
            NotebookDirectoryStore notebooks,
            QuizService quizzes,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            if (string.IsNullOrWhiteSpace(input.Topic))
                return Results.BadRequest(new { error = "topic is required" });

            try
            {
                var generated = await quizzes.GenerateAsync(
                    ws,
                    topic: input.Topic.Trim(),
                    count: input.Count ?? 10,
                    strictGrading: input.StrictGrading ?? false,
                    providerName: string.IsNullOrWhiteSpace(input.ProviderName) ? null : input.ProviderName.Trim(),
                    selectedSourceIds: input.SelectedSourceIds,
                    ct: ct);

                // Keep payload bounded: return quiz definition but omit correct answers by default.
                var quiz = generated.Quiz;
                var redacted = new
                {
                    quizId = quiz.QuizId,
                    topic = quiz.Topic,
                    createdAt = quiz.CreatedAt,
                    strictGrading = quiz.StrictGrading,
                    sourceIds = quiz.SourceIds,
                    questions = quiz.Questions.Select(q => new
                    {
                        questionId = q.QuestionId,
                        type = q.Type,
                        prompt = q.Prompt,
                        options = q.Options
                    }).ToList()
                };

                return Results.Json(new { ok = true, quiz = redacted });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static void MapSubmit(WebApplication app)
    {
        app.MapPost("/api/notebooks/{notebookId}/quiz:submit", async (
            string notebookId,
            SubmitInDto input,
            NotebookDirectoryStore notebooks,
            QuizService quizzes,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            if (string.IsNullOrWhiteSpace(input.QuizId))
                return Results.BadRequest(new { error = "quizId is required" });

            try
            {
                var result = await quizzes.SubmitAsync(
                    ws,
                    quizId: input.QuizId.Trim(),
                    answers: input.Answers ?? Array.Empty<QuizAnswer>(),
                    strictGradingOverride: input.StrictGrading,
                    providerName: string.IsNullOrWhiteSpace(input.ProviderName) ? null : input.ProviderName.Trim(),
                    ct: ct);

                // Return graded answers (includes correctAnswer) for user feedback.
                return Results.Json(new { ok = true, quizId = result.Quiz.QuizId, attempt = result.Attempt });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = "quiz not found" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static void MapHistory(WebApplication app)
    {
        app.MapGet("/api/notebooks/{notebookId}/quiz:history", async (
            string notebookId,
            int? max,
            NotebookDirectoryStore notebooks,
            QuizService quizzes,
            CancellationToken ct) =>
        {
            var ws = ResolveWorkspace(notebooks, notebookId);
            if (ws == null)
                return Results.NotFound(new { error = "notebook not found" });

            try
            {
                var items = await quizzes.ListHistoryAsync(ws, max: max ?? 30, ct: ct);
                return Results.Json(new { ok = true, count = items.Count, items });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static NotebookWorkspace? ResolveWorkspace(NotebookDirectoryStore notebooks, string notebookId)
    {
        var nb = notebooks.GetNotebook(notebookId);
        if (nb == null)
            return null;

        var rootDir = Path.GetDirectoryName(nb.DirectoryPath) ?? string.Empty;
        return new NotebookWorkspace(nb.NotebookId, rootDir, nb.DirectoryPath);
    }

    private sealed record GenerateInDto(
        string Topic,
        int? Count,
        bool? StrictGrading,
        string? ProviderName,
        IReadOnlyList<string>? SelectedSourceIds);

    private sealed record SubmitInDto(
        string QuizId,
        bool? StrictGrading,
        string? ProviderName,
        IReadOnlyList<QuizAnswer>? Answers);
}


