namespace SisyphusMaker.Services;

/// <summary>
/// Renders prompt templates by substituting {{placeholder}} variables.
/// </summary>
public interface IPromptRenderer
{
    /// <summary>
    /// Replaces all {{key}} placeholders in the template with values from the dictionary.
    /// Unmatched placeholders are left as-is.
    /// </summary>
    string Render(string template, Dictionary<string, string> variables);
}
