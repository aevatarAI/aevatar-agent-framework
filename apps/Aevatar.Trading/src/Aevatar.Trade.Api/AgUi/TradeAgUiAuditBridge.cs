using System.Text;
using Aevatar.Agents.AGUI;
using Aevatar.Trade;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Api.AgUi;

internal sealed class TradeAgUiAuditBridge : BackgroundService
{
    private const int PollIntervalSeconds = 2;
    private const int MaxTailBytes = 400_000;
    private const int StreamChunkSize = 24;
    private const int StreamDelayMs = 20;

    private readonly TradeAgUiHub _hub;
    private readonly TradeAuditConfig _audit;
    private readonly IHostEnvironment _env;
    private readonly ILogger<TradeAgUiAuditBridge> _logger;
    private readonly HashSet<string> _emittedCycleIds = new(StringComparer.Ordinal);
    private string? _lastFilePath;
    private DateTime _lastWriteUtc;
    private long _lastLength;

    public TradeAgUiAuditBridge(
        TradeAgUiHub hub,
        IOptions<TradeAuditConfig> audit,
        IHostEnvironment env,
        ILogger<TradeAgUiAuditBridge> logger)
    {
        _hub = hub;
        _audit = audit.Value;
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ------------------------------------------------------------
        // AGUI 事件桥：从 trade-audit/*.md -> AG-UI TextMessage
        // ------------------------------------------------------------
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "[TradeAgUi] Poll failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        if (!_audit.Enabled)
            return;

        var dir = ResolveAuditDir();
        if (dir == null || !Directory.Exists(dir))
            return;

        var latest = Directory.EnumerateFiles(dir, "trade_audit_*.md", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        if (latest == null)
            return;

        if (_lastFilePath == latest.FullName &&
            _lastWriteUtc == latest.LastWriteTimeUtc &&
            _lastLength == latest.Length)
            return;

        _lastFilePath = latest.FullName;
        _lastWriteUtc = latest.LastWriteTimeUtc;
        _lastLength = latest.Length;

        var content = ReadTailUtf8(latest.FullName, MaxTailBytes);
        if (string.IsNullOrWhiteSpace(content))
            return;

        var cycles = ParseCycles(content);
        foreach (var c in cycles)
        {
            if (string.IsNullOrWhiteSpace(c.CycleId))
                continue;
            if (!_emittedCycleIds.Add(c.CycleId))
                continue;

            await EmitCycleAsync(c, ct);
        }
    }

    private async Task EmitCycleAsync(CycleBlock cycle, CancellationToken ct)
    {
        var content = BuildChatContent(cycle);
        if (string.IsNullOrWhiteSpace(content))
            return;

        var messageId = $"msg:{TradeAgUiHub.ThreadId}:cycle:{cycle.CycleId}";
        var now = DateTimeOffset.UtcNow;

        _hub.Events.Publish(new TextMessageStartEvent
        {
            Timestamp = now.ToUnixTimeMilliseconds(),
            MessageId = messageId,
            Role = "assistant"
        });
        _hub.SetMessage(messageId, "assistant", "", name: "trade_audit");

        foreach (var chunk in ChunkText(content, StreamChunkSize))
        {
            ct.ThrowIfCancellationRequested();
            _hub.Events.Publish(new TextMessageContentEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageId = messageId,
                Delta = chunk
            });
            _hub.AppendToMessage(messageId, "assistant", chunk);
            await Task.Delay(StreamDelayMs, ct);
        }

        _hub.Events.Publish(new TextMessageEndEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = messageId
        });
    }

    private string? ResolveAuditDir()
    {
        var raw = string.IsNullOrWhiteSpace(_audit.OutputDir) ? "trade-audit" : _audit.OutputDir.Trim();
        if (Path.IsPathRooted(raw))
            return raw;

        return Path.GetFullPath(Path.Combine(_env.ContentRootPath, raw));
    }

    private static string ReadTailUtf8(string fullPath, int maxBytes)
    {
        maxBytes = Math.Clamp(maxBytes, 1_000, 2_000_000);
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var len = fs.Length;
        var start = Math.Max(0, len - maxBytes);
        fs.Seek(start, SeekOrigin.Begin);

        var buf = new byte[len - start];
        _ = fs.Read(buf, 0, buf.Length);
        return Encoding.UTF8.GetString(buf);
    }

    private static List<CycleBlock> ParseCycles(string markdown)
    {
        var result = new List<CycleBlock>();
        if (string.IsNullOrWhiteSpace(markdown))
            return result;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var buffer = new List<string>();
        string? cycleId = null;
        string? symbol = null;

        foreach (var line in lines)
        {
            if (TryParseCycleHeader(line, out var id, out var sym))
            {
                if (cycleId != null && buffer.Count > 0)
                    result.Add(new CycleBlock(cycleId, symbol ?? "", string.Join("\n", buffer)));

                cycleId = id;
                symbol = sym;
                buffer = new List<string> { line };
                continue;
            }

            if (cycleId != null)
                buffer.Add(line);
        }

        if (cycleId != null && buffer.Count > 0)
            result.Add(new CycleBlock(cycleId, symbol ?? "", string.Join("\n", buffer)));

        return result;
    }

    private static bool TryParseCycleHeader(string line, out string cycleId, out string symbol)
    {
        cycleId = "";
        symbol = "";
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var idx = line.IndexOf("## Cycle `", StringComparison.Ordinal);
        if (idx < 0)
            return false;

        var firstTick = line.IndexOf('`', idx);
        var secondTick = firstTick >= 0 ? line.IndexOf('`', firstTick + 1) : -1;
        if (firstTick < 0 || secondTick < 0 || secondTick <= firstTick)
            return false;

        cycleId = line[(firstTick + 1)..secondTick].Trim();

        var thirdTick = line.IndexOf('`', secondTick + 1);
        var fourthTick = thirdTick >= 0 ? line.IndexOf('`', thirdTick + 1) : -1;
        if (thirdTick >= 0 && fourthTick > thirdTick)
            symbol = line[(thirdTick + 1)..fourthTick].Trim();

        return cycleId.Length > 0;
    }

    private static string BuildChatContent(CycleBlock cycle)
    {
        var header = $"Cycle {cycle.CycleId}";
        if (!string.IsNullOrWhiteSpace(cycle.Symbol))
            header += $" · {cycle.Symbol}";

        var lines = cycle.RawMarkdown.Replace("\r\n", "\n").Split('\n');
        var body = string.Join("\n", lines.Skip(1)).Trim();
        if (body.Length > 2000)
            body = body[..2000] + "…";

        return string.IsNullOrWhiteSpace(body) ? header : $"{header}\n{body}";
    }

    private static IEnumerable<string> ChunkText(string text, int chunkSize)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        chunkSize = Math.Max(1, chunkSize);
        for (var i = 0; i < text.Length; i += chunkSize)
        {
            var len = Math.Min(chunkSize, text.Length - i);
            yield return text.Substring(i, len);
        }
    }

    private sealed record CycleBlock(string CycleId, string Symbol, string RawMarkdown);
}

