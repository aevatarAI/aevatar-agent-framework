using Aevatar.Agents.Sessions.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Sessions.Services;

public sealed class SessionCleanupService : BackgroundService
{
    // ============================================================
    // 中文 + ASCII:
    // - 定时清理 idle session stream
    // - 避免订阅泄露与长期占用内存
    // ============================================================
    private readonly SessionRuntime _runtime;
    private readonly IOptionsMonitor<SessionRuntimeOptions> _options;
    private readonly ILogger<SessionCleanupService> _logger;

    public SessionCleanupService(
        SessionRuntime runtime,
        IOptionsMonitor<SessionRuntimeOptions> options,
        ILogger<SessionCleanupService> logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = NormalizeCleanupInterval(_options.CurrentValue.SessionCleanupIntervalSeconds);
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var idleTimeout = NormalizeIdleTimeout(_options.CurrentValue.SessionIdleTimeoutMinutes);
            try
            {
                await _runtime.CleanupIdleContextsAsync(idleTimeout, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Session cleanup failed.");
            }
        }
    }

    private static TimeSpan NormalizeCleanupInterval(int seconds)
    {
        if (seconds <= 0)
            seconds = 120;
        seconds = Math.Clamp(seconds, 10, 3600);
        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan NormalizeIdleTimeout(int minutes)
    {
        if (minutes <= 0)
            minutes = 20;
        minutes = Math.Clamp(minutes, 1, 1440);
        return TimeSpan.FromMinutes(minutes);
    }
}
