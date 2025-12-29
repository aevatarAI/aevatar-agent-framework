using Aevatar.Agents;
using Aevatar.Novel.Contracts;
using Aevatar.Novel.Sidecar.Services.Aevatar;

namespace Aevatar.Novel.Sidecar.Services.DeviationImpact;

// ============================================================
//  DeviationImpactOrchestratorHostedService
//
//  Workflow E (Aevatar bridge):
//  - SSOT chapter .txt changes -> forward event into DeviationImpactAgent
//  - Agent produces:
//    - deviation_report.md
//    - deviation_prompt.md
//    - change_impact_report.md
//    - backup_options_report.md
//    - SidecarEvent(deviation_impact_completed=StoryDeviationImpactAnalysisCompletedEvent)
// ============================================================

public sealed class DeviationImpactOrchestratorHostedService : BackgroundService
{
    private readonly ILogger<DeviationImpactOrchestratorHostedService> _logger;
    private readonly SidecarEventHub _hub;
    private readonly NovelAgentRuntime _agentRuntime;

    public DeviationImpactOrchestratorHostedService(
        ILogger<DeviationImpactOrchestratorHostedService> logger,
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
                var actor = await _agentRuntime.GetDeviationImpactActorAsync(stoppingToken);
                await actor.PublishEventAsync(fc, EventDirection.Down, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Novel] Deviation impact analysis failed unexpectedly for {Path}", fc.FullPath);
            }
        }
    }

    private static bool ShouldTrigger(SstFileChangedEvent fc)
    {
        var path = (fc.FullPath ?? string.Empty).Replace('\\', '/');
        if (!path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!path.Contains("/chapters/", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}


