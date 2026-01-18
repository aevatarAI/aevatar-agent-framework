using System.Text.RegularExpressions;

namespace Aevatar.Agents.AI.Tools;

internal static class SearchToolHelpers
{
    public static Regex BuildGlobRegex(string pattern)
    {
        var normalized = pattern.Replace('\\', '/');
        var sb = new System.Text.StringBuilder();
        sb.Append('^');

        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            if (ch == '*')
            {
                var isDouble = i + 1 < normalized.Length && normalized[i + 1] == '*';
                if (isDouble)
                {
                    sb.Append(".*");
                    i++;
                }
                else
                {
                    sb.Append("[^/]*");
                }
                continue;
            }

            if (ch == '?')
            {
                sb.Append("[^/]");
                continue;
            }

            if ("+()^$.{}[]|\\".IndexOf(ch) >= 0)
            {
                sb.Append('\\');
            }

            sb.Append(ch);
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static string NormalizePathForMatch(string path)
        => path.Replace('\\', '/');
}
