using Aevatar.Agents;
using Aevatar.Novel.Contracts;
using Aevatar.Novel.Sidecar.Services.Aevatar;

namespace Aevatar.Novel.Sidecar.Services.CanonGovernance;

// ============================================================
//  CanonGovernanceOrchestratorHostedService
//
//  - Bridge SSOT file change events into CanonGovernanceAgent (Local runtime)
// ============================================================

public sealed class CanonGovernanceOrchestratorHostedService : BackgroundService
{
    private readonly ILogger<CanonGovernanceOrchestratorHostedService> _logger;
    private readonly SidecarEventHub _hub;
    private readonly NovelAgentRuntime _agentRuntime;

    public CanonGovernanceOrchestratorHostedService(
        ILogger<CanonGovernanceOrchestratorHostedService> logger,
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
                var actor = await _agentRuntime.GetCanonGovernanceActorAsync(stoppingToken);
                await actor.PublishEventAsync(fc, EventDirection.Down, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Novel] Canon governance failed unexpectedly for {Path}", fc.FullPath);
            }
        }
    }

    private static bool ShouldTrigger(SstFileChangedEvent fc)
    {
        var rel = (fc.RelativePath ?? "").Replace('\\', '/');
        var full = (fc.FullPath ?? "").Replace('\\', '/');
        var p = !string.IsNullOrWhiteSpace(rel) ? rel : full;
        if (!p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!p.Contains("/artifacts/canon/", StringComparison.OrdinalIgnoreCase))
            return false;
        if (p.Contains("/artifacts/canon/changes/", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}


