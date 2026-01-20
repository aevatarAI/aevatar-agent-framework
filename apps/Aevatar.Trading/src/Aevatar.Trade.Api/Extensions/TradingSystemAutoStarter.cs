using Aevatar.Trade;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Api.Extensions;

/// <summary>
/// Auto-start trading system on API boot (optional).
/// </summary>
internal sealed class TradingSystemAutoStarter : IHostedService
{
    private readonly TradingSystem _tradingSystem;
    private readonly TradingStartupConfig _startup;
    private readonly TradingConfig _trading;
    private readonly ILogger<TradingSystemAutoStarter> _logger;

    public TradingSystemAutoStarter(
        TradingSystem tradingSystem,
        IOptions<TradingStartupConfig> startup,
        IOptions<TradingConfig> trading,
        ILogger<TradingSystemAutoStarter> logger)
    {
        _tradingSystem = tradingSystem;
        _startup = startup.Value;
        _trading = trading.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_startup.AutoStart)
            return;

        _logger.LogInformation("[AutoStart] Requested.");

        if (_trading.ExecutionMode == TradeExecutionMode.Live && !_startup.AutoStartAllowLive)
        {
            _logger.LogWarning(
                "[AutoStart] ExecutionMode=Live but AutoStartAllowLive=false. Skipped.");
            return;
        }

        try
        {
            _logger.LogInformation("[AutoStart] Initialize...");
            await _tradingSystem.InitializeAsync(cancellationToken);
            _logger.LogInformation("[AutoStart] Start...");
            await _tradingSystem.StartAsync(cancellationToken);
            _logger.LogInformation("[AutoStart] Trading system started.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[AutoStart] Failed to start trading system.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

