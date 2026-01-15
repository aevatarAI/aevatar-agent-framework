using System.Net;
using System.Text.RegularExpressions;
using Aevatar.Agents.Maker;

namespace MakerSystem.Projects;

// ============================================================
//  Paper Summary Strategies - Only 90 lines total!
//  Compare to 303 lines in V1
// ============================================================

/// <summary>
/// Paper content loader.
/// </summary>
public static class PaperContent
{
    public const string Title = "Solving a Million-Step LLM Task with Zero Errors";
    private const string ArticleFileName = "Solving a Million-Step LLM Task with Zero Errors.html";

    private static readonly Lazy<string> LazyFullText = new(LoadArticleText, LazyThreadSafetyMode.ExecutionAndPublication);
    public static string FullText => LazyFullText.Value;

    private static string LoadArticleText()
    {
        var path = FindArticlePath();
        if (path != null && File.Exists(path))
        {
            var html = File.ReadAllText(path);
            return ConvertHtmlToPlainText(html);
        }
        return "[Article file not found. Ensure articles/Solving a Million-Step LLM Task with Zero Errors.html exists.]";
    }

    private static string? FindArticlePath()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(current); i++)
        {
            var candidate = Path.Combine(current, "articles", ArticleFileName);
            if (File.Exists(candidate)) return candidate;
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }

    private static string ConvertHtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = Regex.Replace(html, @"<(script|style)[^>]*?>.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(p|div|br|li|h\d)[^>]*?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}

/// <summary>
/// Paper decomposition strategy.
/// </summary>
public sealed class PaperDecomposer : IDecompositionStrategy
{
    public string BuildDecompositionPrompt(string task, IReadOnlyDictionary<string, string> ctx)
    {
        var jsonFormat = """[{"step_id":"S1","description":"Summarize Section X: ..."}]""";
        return $"""
            You are a Senior Editor. Decompose the paper summary task into 4-6 logical sections.
            
            Typical sections: Abstract/Introduction, Methodology, Key findings, Discussion, Limitations.
            
            Output: JSON array {jsonFormat}
            Output ONLY JSON.
            
            [Task]
            {task}
            """;
    }

    public bool IsAtomic(string task, int depth) => depth >= 1;

    public IReadOnlyList<(string, string)> ParseDecomposition(string output)
        => new DefaultDecomposer().ParseDecomposition(output);
}

/// <summary>
/// Paper solution strategy with content injection.
/// </summary>
public sealed class PaperSolver : ISolutionStrategy
{
    public string BuildSolvePrompt(string task, IReadOnlyDictionary<string, string> ctx)
    {
        return $"""
            You are a Research Assistant. Summarize the specific section requested.
            
            Instructions:
            1. Locate the target section in the paper.
            2. Write a concise summary (100-150 words).
            3. Use markdown formatting, no global "# Summary" heading.
            
            --- PAPER CONTENT ---
            {PaperContent.FullText}
            --- END CONTENT ---
            
            [Task]
            {task}
            
            [Summary]
            """;
    }
}

