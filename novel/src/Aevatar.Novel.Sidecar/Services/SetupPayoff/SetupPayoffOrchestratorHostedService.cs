using Aevatar.Agents;
using Aevatar.Novel.Contracts;
using Aevatar.Novel.Sidecar.Services.Aevatar;

namespace Aevatar.Novel.Sidecar.Services.SetupPayoff;

// ============================================================
//  SetupPayoffOrchestratorHostedService
//
//  Bridge:
//  - SSOT file change -> forward SstFileChangedEvent into SetupPayoffLedgerAgent (Local runtime).
//
//  IMPORTANT:
//  - Ignore derived report file changes to avoid infinite loops.
// ============================================================

public sealed class SetupPayoffOrchestratorHostedService : BackgroundService
{
    private readonly ILogger<SetupPayoffOrchestratorHostedService> _logger;
    private readonly SidecarEventHub _hub;
    private readonly NovelAgentRuntime _agentRuntime;

    public SetupPayoffOrchestratorHostedService(
        ILogger<SetupPayoffOrchestratorHostedService> logger,
        SidecarEventHub hub,
        NovelAgentRuntime agentRuntime)
    {
        _logger = logger;
        _hub = hub;
        _agentRuntime = agentRuntime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _hub.Subscribe(stoppingToken);
        await foreach (var evt in reader.ReadAllAsync(stoppingToken))
        {
            if (evt.PayloadCase != SidecarEvent.PayloadOneofCase.FileChanged)
                continue;

            var fc = evt.FileChanged;
            if (!ShouldTrigger(fc))
                continue;

            try
            {
                var actor = await _agentRuntime.GetSetupPayoffLedgerActorAsync(stoppingToken);
                await actor.PublishEventAsync(fc, EventDirection.Down, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Novel] Setup/Payoff ledger scan failed unexpectedly for {Path}", fc.FullPath);
            }
        }
    }

    private static bool ShouldTrigger(SstFileChangedEvent fc)
    {
        var path = (fc.FullPath ?? string.Empty).Replace('\\', '/');

        // Ignore derived report outputs (avoid loop).
        if (path.Contains("/artifacts/ledger/reports/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.Contains("/chapters/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.EndsWith("/artifacts/ledger/setup_payoff_ledger.md", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}


