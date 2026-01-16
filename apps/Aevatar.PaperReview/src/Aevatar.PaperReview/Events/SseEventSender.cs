using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Aevatar.PaperReview.Models;

namespace Aevatar.PaperReview.Events;

// ============================================================
//  SSE 事件发送器
//  职责：管理 Server-Sent Events 的发送
// ============================================================

/// <summary>
/// SSE 事件发送器 - 统一管理事件推送。
/// </summary>
public sealed class SseEventSender
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    
    private readonly ConcurrentDictionary<string, Channel<string>> _channels = new();
    
    /// <summary>
    /// 创建会话的事件通道。
    /// </summary>
    public Channel<string> CreateChannel(string sessionId)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _channels[sessionId] = channel;
        return channel;
    }
    
    /// <summary>
    /// 获取会话的事件通道。
    /// </summary>
    public Channel<string>? GetChannel(string sessionId) =>
        _channels.TryGetValue(sessionId, out var ch) ? ch : null;
    
    /// <summary>
    /// 关闭会话的事件通道。
    /// </summary>
    public void CloseChannel(string sessionId)
    {
        if (_channels.TryRemove(sessionId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }
    
    /// <summary>
    /// 发送事件到指定会话。
    /// </summary>
    public void Send<T>(string sessionId, T evt) where T : class
    {
        if (!_channels.TryGetValue(sessionId, out var channel)) return;
        
        var typeName = evt.GetType().Name;
        var wrapper = new
        {
            SessionId = sessionId,
            Type = typeName,
            Timestamp = DateTimeOffset.UtcNow
        };
        
        // 合并 wrapper 和 evt 的属性
        var json = MergeJson(evt, wrapper);
        channel.Writer.TryWrite($"data: {json}\n\n");
    }
    
    /// <summary>
    /// 发送原始 JSON 事件。
    /// </summary>
    public void SendRaw(string sessionId, string json)
    {
        if (_channels.TryGetValue(sessionId, out var channel))
        {
            channel.Writer.TryWrite($"data: {json}\n\n");
        }
    }
    
    /// <summary>
    /// 异步读取事件流。
    /// </summary>
    public async IAsyncEnumerable<string> ReadEventsAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_channels.TryGetValue(sessionId, out var channel)) yield break;
        
        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }
    
    /// <summary>
    /// 快捷方法：发送进度事件。
    /// </summary>
    public void SendProgress(string sessionId, string phase, string message, float percent = 0, int? depth = null)
    {
        Send(sessionId, new ProgressEvent
        {
            SessionId = sessionId,
            Phase = phase,
            Message = message,
            ProgressPercent = (int)(percent * 100),
            Depth = depth
        });
    }
    
    /// <summary>
    /// 快捷方法：发送阶段变更事件。
    /// </summary>
    public void SendPhaseChange(string sessionId, string oldPhase, string newPhase, string? message = null, string? taskId = null)
    {
        Send(sessionId, new PhaseChangeEvent
        {
            SessionId = sessionId,
            TaskId = taskId,
            OldPhase = oldPhase,
            NewPhase = newPhase,
            Message = message
        });
    }
    
    /// <summary>
    /// 快捷方法：发送错误事件。
    /// </summary>
    public void SendError(string sessionId, string message, string? stackTrace = null)
    {
        Send(sessionId, new ErrorEvent
        {
            SessionId = sessionId,
            Message = message,
            StackTrace = stackTrace
        });
    }
    
    /// <summary>
    /// 快捷方法：发送结果事件。
    /// </summary>
    public void SendResult(string sessionId, bool success, string? content, string? error, int llmCalls, int tokens)
    {
        Send(sessionId, new ResultEvent
        {
            SessionId = sessionId,
            Success = success,
            Content = content,
            Error = error,
            TotalLlmCalls = llmCalls,
            TotalTokens = tokens
        });
    }
    
    private static string MergeJson<T>(T evt, object wrapper)
    {
        var evtJson = JsonSerializer.SerializeToElement(evt, JsonOptions);
        var wrapperJson = JsonSerializer.SerializeToElement(wrapper, JsonOptions);
        
        var merged = new Dictionary<string, JsonElement>();
        
        foreach (var prop in evtJson.EnumerateObject())
            merged[prop.Name] = prop.Value;
        
        foreach (var prop in wrapperJson.EnumerateObject())
            merged[prop.Name] = prop.Value;
        
        return JsonSerializer.Serialize(merged, JsonOptions);
    }
}
