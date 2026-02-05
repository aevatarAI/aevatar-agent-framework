using Aevatar.VibeResearching.UserProviders.Infrastructure.Codex;
using Aevatar.VibeResearching.UserProviders.Infrastructure.Encryption;
using Aevatar.VibeResearching.UserProviders.Infrastructure.Resolution;
using Aevatar.VibeResearching.UserProviders.Services;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.UserProviders.Infrastructure;

[DependsOn(typeof(UserProvidersDomainModule))]
public class UserProvidersInfrastructureModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = services.GetConfiguration();

        // Master key: appsettings.secrets.json > env var
        var masterKey = configuration["UserProviders:MasterKey"]
                       ?? Environment.GetEnvironmentVariable("AEVATAR_USER_PROVIDER_MASTER_KEY");

        if (string.IsNullOrWhiteSpace(masterKey))
        {
            throw new InvalidOperationException(
                "User provider encryption master key is not configured. "
                + "Set 'UserProviders:MasterKey' in appsettings.secrets.json "
                + "or AEVATAR_USER_PROVIDER_MASTER_KEY as an environment variable. "
                + "Generate with: openssl rand -base64 32");
        }

        services.Configure<UserProviderEncryptionOptions>(opt =>
        {
            opt.MasterKeyBase64 = masterKey;
        });

        // Codex OAuth configuration
        services.Configure<CodexOAuthOptions>(configuration.GetSection("CodexOAuth"));

        // Register services
        services.AddSingleton<IUserProviderEncryptionService, Aes256GcmEncryptionService>();
        services.AddTransient<IProviderResolutionService, ProviderResolutionService>();
        services.AddTransient<ICodexOAuthService, CodexOAuthService>();
        services.AddTransient<PkceStateStore>();

        // Register HttpClient for Codex OAuth token exchange
        services.AddHttpClient("CodexOAuth", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }
}
