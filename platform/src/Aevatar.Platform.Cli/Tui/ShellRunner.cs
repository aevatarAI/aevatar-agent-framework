using System.Diagnostics;
using System.Linq;
using System.Text;
using Aevatar.Platform.Core.Tools;
using Spectre.Console;

namespace Aevatar.Platform.Cli.Tui;

// ============================================================
//  ShellRunner
//
//  说明：
//  - 执行 !shell（受 PlatformToolPolicy 约束）
// ============================================================
public static class ShellRunner
{
    public static async Task RunAsync(string command, PlatformToolPolicy policy, ITuiOutput output, CancellationToken ct)
    {
        var text = (command ?? string.Empty).Trim();
        if (text.Length == 0)
            return;

        if (!policy.TryValidateCommand(text, out var reason))
        {
            output.MarkupLine($"Shell denied: [red]{Markup.Escape(reason)}[/]");
            return;
        }

        var (file, args) = SplitCommand(text);
        if (file.Length == 0)
        {
            output.MarkupLine("Shell denied: invalid command.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                output.MarkupLine("Shell failed: process start error.");
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (policy.TryGetToolTimeout("shell", out var timeout))
                cts.CancelAfter(timeout);

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cts.Token);

            var stdout = await outputTask;
            var stderr = await errorTask;

            if (!string.IsNullOrWhiteSpace(stdout))
                output.WriteLine(stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr))
                output.MarkupLine($"[red]{Markup.Escape(stderr.TrimEnd())}[/]");
        }
        catch (OperationCanceledException)
        {
            output.MarkupLine("Shell canceled.");
        }
        catch (Exception ex)
        {
            output.MarkupLine($"Shell failed: {Markup.Escape(ex.Message)}");
        }
    }

    private static (string File, string Args) SplitCommand(string text)
    {
        var parts = new List<string>();
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
                    Flush(parts, sb);
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

        Flush(parts, sb);

        if (parts.Count == 0)
            return (string.Empty, string.Empty);

        var file = parts[0];
        var args = parts.Count > 1 ? string.Join(" ", parts.Skip(1)) : string.Empty;
        return (file, args);
    }

    private static void Flush(ICollection<string> parts, StringBuilder sb)
    {
        if (sb.Length == 0)
            return;

        parts.Add(sb.ToString());
        sb.Clear();
    }
}


