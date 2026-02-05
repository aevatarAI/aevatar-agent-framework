using Aevatar.VibeResearching.UserProviders.Application.Services;
using Aevatar.VibeResearching.UserProviders.Services;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.UserProviders.Application;

[DependsOn(
    typeof(UserProvidersDomainModule),
    typeof(UserProvidersApplicationContractsModule))]
public class UserProvidersApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<ProviderConnectivityTester>();
        context.Services.AddTransient<IUserProviderAppService, UserProviderAppService>();
        context.Services.AddTransient<ICodexOAuthAppService, CodexOAuthAppService>();
        context.Services.AddTransient<ISessionProviderAppService, SessionProviderAppService>();
    }
}
