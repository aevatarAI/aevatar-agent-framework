using System.Text.RegularExpressions;

namespace Aevatar.Agents.Persistence.SQLite.Internal;

/// <summary>
/// SQLite SQL helpers (identifier validation).
/// </summary>
public static class SQLiteSql
{
    // 仅允许小写字母/数字/下划线，且必须以字母开头。// Only [a-z][a-z0-9_]* to avoid SQL injection.
    private static readonly Regex IdentifierRegex = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    public static string Ident(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identifier cannot be null/empty.", paramName);
        }

        value = value.Trim();
        if (!IdentifierRegex.IsMatch(value))
        {
            throw new ArgumentException(
                $"Invalid identifier '{value}'. Only [a-z][a-z0-9_]* is allowed.",
                paramName);
        }

        return value;
    }
}
