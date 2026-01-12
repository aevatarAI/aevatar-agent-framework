using Aevatar.Agents;
using Aevatar.Novel.Contracts;
using Aevatar.Novel.Sidecar.Services.Aevatar;

namespace Aevatar.Novel.Sidecar.Services.NarrativeTests;

// ============================================================
//  NarrativeTestsOrchestratorHostedService
//
//  Workflow F (Aevatar bridge):
//  - SSOT file change -> forward Protobuf event into Aevatar Local runtime.
//  - NarrativeTestAgent consumes it and emits UnitTestsCompletedEvent back to SidecarEventHub (SSE).
//
//  IMPORTANT:
//  - We ignore derived artifacts changes to avoid infinite loops.
// ============================================================

public sealed class NarrativeTestsOrchestratorHostedService : BackgroundService
{
    private readonly ILogger<NarrativeTestsOrchestratorHostedService> _logger;
    private readonly SidecarEventHub _eventHub;
    private readonly NovelAgentRuntime _agentRuntime;

    public NarrativeTestsOrchestratorHostedService(
        ILogger<NarrativeTestsOrchestratorHostedService> logger,
        SidecarEventHub eventHub,
        NovelAgentRuntime agentRuntime)
    {
        _logger = logger;
        _eventHub = eventHub;
        _agentRuntime = agentRuntime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _eventHub.Subscribe(stoppingToken);
        await foreach (var evt in reader.ReadAllAsync(stoppingToken))
        {
            if (evt.PayloadCase != SidecarEvent.PayloadOneofCase.FileChanged)
                continue;

            var fc = evt.FileChanged;
            if (string.IsNullOrWhiteSpace(fc.FullPath))
                continue;

            if (!NarrativeTestsWorkflow.ShouldTriggerTests(fc))
                continue;

            try
            {
                var actor = await _agentRuntime.GetNarrativeTestActorAsync(stoppingToken);
                await actor.PublishEventAsync(fc, EventDirection.Down, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Novel] Narrative tests failed unexpectedly for {Path}", fc.FullPath);
            }
        }
    }
}


