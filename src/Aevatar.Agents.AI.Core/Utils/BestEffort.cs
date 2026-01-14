using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Best-effort execution helper.
///
/// 中文 + ASCII:
/// - 目的：统一散落的 try/catch + LogDebug/LogWarning 模式，写死“失败不影响主路径”的语义
/// - 注意：默认吞掉所有异常（包括 OperationCanceledException），以保持现有 best-effort 行为一致
/// </summary>
internal static class BestEffort
{
    internal static async Task TryAsync(
        Func<Task> action,
        ILogger logger,
        LogLevel level,
        string message)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.Log(level, ex, message);
        }
    }

    internal static async Task<T?> TryAsync<T>(
        Func<Task<T>> action,
        ILogger logger,
        LogLevel level,
        string message,
        T? fallback = default)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            logger.Log(level, ex, message);
            return fallback;
        }
    }
}


