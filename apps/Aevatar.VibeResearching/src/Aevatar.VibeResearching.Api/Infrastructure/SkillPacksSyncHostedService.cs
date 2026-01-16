using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VibeResearching.Api.Infrastructure;

/// <summary>
/// Startup hook: best-effort sync Git-based skill packs in background.
/// </summary>
public sealed class SkillPacksSyncHostedService : BackgroundService
{
    private readonly SkillPacksSyncService _sync;
    private readonly ILogger<SkillPacksSyncHostedService> _logger;

    public SkillPacksSyncHostedService(
        SkillPacksSyncService sync,
        ILogger<SkillPacksSyncHostedService> logger)
    {
        _sync = sync;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[SkillPacksSync] Startup sync: begin (best-effort).");
        // If packs are configured, always try (best-effort).
        _ = await _sync.TryEnsureSyncedAsync(SkillPackSyncMode.Manual, stoppingToken);
        _logger.LogInformation("[SkillPacksSync] Startup sync: finished (best-effort).");
    }
}


