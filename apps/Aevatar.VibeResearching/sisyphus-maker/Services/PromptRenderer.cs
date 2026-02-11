namespace SisyphusMaker.Services;

/// <summary>
/// Simple template renderer using string replacement for {{placeholder}} patterns.
/// Stateless and thread-safe.
/// </summary>
public sealed class PromptRenderer : IPromptRenderer
{
    /// <inheritdoc />
    public string Render(string template, Dictionary<string, string> variables)
    {
        var result = template;
        foreach (var (key, value) in variables)
        {
            result = result.Replace($"{{{{{key}}}}}", value ?? string.Empty);
        }
        return result;
    }
}
