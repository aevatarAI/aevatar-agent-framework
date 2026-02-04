using System.Collections.Concurrent;

namespace Aevatar.Agents.AI;

// ============================================================
//  StreamingContext - 基于 RequestId 的 streaming sink 注册
//
//  目标：
//  - 让 HandleChatRequestEvent 能直接把 chunks 发布到 UI
//  - 绕过 LocalMessageStream 的 await Task.WhenAll 阻塞
//
//  为什么不用 AsyncLocal：
//  - PublishEventAsync 后事件通过 stream 异步传递
//  - stream 回调在另一个 async flow 中执行
//  - AsyncLocal 值在跨 Task 边界时丢失
//
//  用法：
//  1. 上层注册 sink：StreamingContext.Register(requestId, sink)
//  2. RoleAIGAgent 获取 sink：StreamingContext.TryGet(requestId, out sink)
//  3. 请求完成后清理：StreamingContext.Unregister(requestId)
// ============================================================

/// <summary>
/// Streaming chunk sink - 接收 streaming chunks 并直接发布到 UI
/// </summary>
public interface IStreamChunkSink
{
    /// <summary>
    /// 发布一个 streaming chunk
    /// </summary>
    void EmitChunk(string requestId, string content);

    /// <summary>
    /// 标记 streaming 结束
    /// </summary>
    void EmitEnd(string requestId, string? fullContent);
}

/// <summary>
/// RequestId-based streaming sink registry (DI-friendly).
/// </summary>
public interface IStreamChunkSinkRegistry
{
    /// <summary>
    /// 注册 sink（在发送 ChatRequestEvent 之前调用）
    /// </summary>
    void Register(string requestId, IStreamChunkSink sink);

    /// <summary>
    /// 获取 sink（在 HandleChatRequestEvent 中调用）
    /// </summary>
    bool TryGet(string requestId, out IStreamChunkSink? sink);

    /// <summary>
    /// 注销 sink（在请求完成后调用）
    /// </summary>
    void Unregister(string requestId);
}

/// <summary>
/// Default in-memory registry (thread-safe).
/// </summary>
public sealed class InMemoryStreamChunkSinkRegistry : IStreamChunkSinkRegistry
{
    private readonly ConcurrentDictionary<string, IStreamChunkSink> _sinks = new(StringComparer.Ordinal);

    public void Register(string requestId, IStreamChunkSink sink)
    {
        if (string.IsNullOrEmpty(requestId))
            throw new ArgumentNullException(nameof(requestId));
        _sinks[requestId] = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public bool TryGet(string requestId, out IStreamChunkSink? sink)
    {
        if (string.IsNullOrEmpty(requestId))
        {
            sink = null;
            return false;
        }
        return _sinks.TryGetValue(requestId, out sink);
    }

    public void Unregister(string requestId)
    {
        if (!string.IsNullOrEmpty(requestId))
            _sinks.TryRemove(requestId, out _);
    }
}
