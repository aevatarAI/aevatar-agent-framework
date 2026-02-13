using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Aevatar.VibeResearching.Agents.MongoDB.ReviewAgent;
using Aevatar.VibeResearching.Agents.ReviewAgent;
using Aevatar.VibeResearching.Agents.Tools;

namespace Aevatar.VibeResearching.Agents.MongoDB;

[DependsOn(
    typeof(AbpMongoDbModule),
    typeof(VibeAgentsDomainModule)
)]
public class VibeAgentsMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // MongoDB Context
        // Note: AddAbpDbContext skipped — Agent data is stored via File-SSoT / custom repositories.

        // ReviewAgent Domain Service (singleton — hosted service casts to concrete and wires event handlers)
        services.AddSingleton<ReviewAgentService>();
        services.AddSingleton<IReviewAgentService>(sp => sp.GetRequiredService<ReviewAgentService>());

        // ReviewAgent Storage Services (concrete + interface mappings)
        services.AddTransient<ReviewAgentIndexInitializer>();
        services.AddSingleton<ReviewAgentEventPublisher>();
        services.AddSingleton<IReviewAgentEventPublisher>(sp => sp.GetRequiredService<ReviewAgentEventPublisher>());

        // FileReviewAgentStorage requires a basePath string — use factory to resolve from IHostEnvironment
        services.AddTransient<IReviewAgentStorage>(sp =>
        {
            var basePath = ResolveReviewAgentStoragePath(sp);
            var logger = sp.GetRequiredService<ILogger<FileReviewAgentStorage>>();
            return new FileReviewAgentStorage(basePath, logger);
        });

        // ReviewAgentFileLogger requires a workspaceDirectory string — use factory
        services.AddTransient<ReviewAgentFileLogger>(sp =>
        {
            var workspaceDir = ResolveWorkspacePath(sp);
            var logger = sp.GetRequiredService<ILogger<ReviewAgentFileLogger>>();
            return new ReviewAgentFileLogger(workspaceDir, logger);
        });

        services.AddTransient<KnowledgeNodeVerifier>();
        services.AddTransient<IKnowledgeNodeVerifier, KnowledgeNodeVerifier>();

        // Agent Tools
        services.AddTransient<VibeGraphAccess>();
        services.AddTransient<IVibeGraphAccess>(sp => sp.GetRequiredService<VibeGraphAccess>());
    }

    private static string ResolveSystemRoot(IServiceProvider sp)
    {
        var cfg = sp.GetService<IConfiguration>();
        var envRoot = cfg?["Vibe:WorkspaceRoot"]
                      ?? Environment.GetEnvironmentVariable("VIBE_WORKSPACE_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            var resolved = Path.GetFullPath(envRoot.Trim());
            Directory.CreateDirectory(resolved);
            return resolved;
        }

        var env = sp.GetRequiredService<IHostEnvironment>();
        return Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", ".."));
    }

    private static string ResolveWorkspacePath(IServiceProvider sp)
        => Path.Combine(ResolveSystemRoot(sp), "workspace");

    private static string ResolveReviewAgentStoragePath(IServiceProvider sp)
        => Path.Combine(ResolveSystemRoot(sp), "workspace", "review-agent");
}
