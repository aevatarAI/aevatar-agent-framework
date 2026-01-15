using System.Linq;
using System.Text;

namespace Aevatar.Platform.Cli.Tui;

// ============================================================
//  InputParser
//
//  说明：
//  - 解析 TUI 输入（/命令、!shell、@附件、普通消息）
//  - 仅做语法拆分，不做 IO 或业务逻辑
// ============================================================
public static class InputParser
{
    public static ParsedInput Parse(string? line)
    {
        var raw = (line ?? string.Empty).Trim();
        if (raw.Length == 0)
            return ParsedInput.Empty();

        if (raw.StartsWith('/'))
            return ParseCommand(raw[1..].Trim());

        if (raw.StartsWith('!'))
            return new ParsedInput(ParsedInputKind.Shell, raw[1..].Trim(), Array.Empty<string>(), null, null);

        return ParseMessage(raw);
    }

    private static ParsedInput ParseCommand(string raw)
    {
        if (raw.Length == 0)
            return new ParsedInput(ParsedInputKind.Command, string.Empty, Array.Empty<string>(), "help", string.Empty);

        var tokens = Tokenize(raw);
        if (tokens.Count == 0)
            return new ParsedInput(ParsedInputKind.Command, raw, Array.Empty<string>(), "help", string.Empty);

        var cmd = tokens[0];
        var args = tokens.Count > 1 ? string.Join(" ", tokens.Skip(1)) : string.Empty;
        return new ParsedInput(ParsedInputKind.Command, raw, Array.Empty<string>(), cmd, args);
    }

    private static ParsedInput ParseMessage(string raw)
    {
        var tokens = Tokenize(raw);
        var attachments = new List<string>();
        var message = new List<string>();

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token == "@")
            {
                if (i + 1 < tokens.Count)
                {
                    attachments.Add(TrimQuotes(tokens[++i]));
                }
                continue;
            }

            if (token.StartsWith('@'))
            {
                var attach = token[1..];
                if (attach.Length > 0)
                    attachments.Add(TrimQuotes(attach));
                continue;
            }

            message.Add(token);
        }

        var text = string.Join(" ", message).Trim();
        return new ParsedInput(ParsedInputKind.Message, text, attachments, null, null);
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return tokens;

        var sb = new StringBuilder();
        var quote = '\0';

        foreach (var ch in text)
        {
            if (quote == '\0')
            {
                if (ch is '"' or '\'')
                {
                    quote = ch;
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    FlushToken(tokens, sb);
                    continue;
                }
            }
            else if (ch == quote)
            {
                quote = '\0';
                continue;
            }

            sb.Append(ch);
        }

        FlushToken(tokens, sb);
        return tokens;
    }

    private static void FlushToken(ICollection<string> tokens, StringBuilder sb)
    {
        if (sb.Length == 0)
            return;

        tokens.Add(sb.ToString());
        sb.Clear();
    }

    private static string TrimQuotes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var t = text.Trim();
        if (t.Length >= 2 && t[0] == t[^1] && t[0] is '"' or '\'')
            return t[1..^1];

        return t;
    }
}

public enum ParsedInputKind
{
    Empty = 0,
    Command = 1,
    Shell = 2,
    Message = 3
}

public sealed record ParsedInput(
    ParsedInputKind Kind,
    string Text,
    IReadOnlyList<string> Attachments,
    string? Command,
    string? CommandArgs)
{
    public static ParsedInput Empty()
        => new(ParsedInputKind.Empty, string.Empty, Array.Empty<string>(), null, null);
}


