using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.UserProviders.HttpApi;

[DependsOn(typeof(UserProvidersApplicationContractsModule))]
public class UserProvidersHttpApiModule : AbpModule
{
}
