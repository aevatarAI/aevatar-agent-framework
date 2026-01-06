using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ScientificResearchAssistant.Api.Infrastructure;

/// <summary>
/// Startup hook: best-effort sync K-Dense skills repo in background.
/// </summary>
public sealed class ClaudeScientificSkillsSyncHostedService : BackgroundService
{
    private readonly ClaudeScientificSkillsSyncService _sync;
    private readonly IOptions<ClaudeScientificSkillsSyncOptions> _options;
    private readonly ILogger<ClaudeScientificSkillsSyncHostedService> _logger;

    public ClaudeScientificSkillsSyncHostedService(
        ClaudeScientificSkillsSyncService sync,
        IOptions<ClaudeScientificSkillsSyncOptions> options,
        ILogger<ClaudeScientificSkillsSyncHostedService> logger)
    {
        _sync = sync;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = _options.Value ?? new ClaudeScientificSkillsSyncOptions();
        if (!opt.Enabled || !opt.AutoUpdateOnStartup)
            return;

        _logger.LogInformation("[SkillsSync] Startup sync enabled. RepoUrl={RepoUrl} Ref={Ref}",
            opt.RepoUrl, opt.Ref);

        // Best-effort: do not crash host.
        await _sync.TryEnsureSyncedAsync(stoppingToken);
    }
}


