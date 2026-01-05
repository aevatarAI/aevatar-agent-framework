using System.Text.RegularExpressions;

namespace Aevatar.Agents.Persistence.Supabase.Internal;

/// <summary>
/// Supabase(Postgres) SQL-related utilities:
/// - Identifier validation (prevent SQL injection)
/// - schema.table concatenation
/// - FTS regconfig literal generation
/// </summary>
public static class SupabaseSql
{
    // Only allow: lowercase + underscore + digits, starting with letter
    // This allows us to avoid quotes, avoid case/escaping pitfalls, and completely prevent injection.
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

    public static string Table(string schema, string table, string paramNameForTable)
    {
        var s = Ident(schema, nameof(schema));
        var t = Ident(table, paramNameForTable);
        return $"{s}.{t}";
    }

    /// <summary>
    /// Generate a safe regconfig SQL string literal, e.g.: 'simple'
    /// </summary>
    public static string RegConfigLiteral(string regConfig)
    {
        var cfg = Ident(regConfig, nameof(regConfig));
        return $"'{cfg}'";
    }
}


