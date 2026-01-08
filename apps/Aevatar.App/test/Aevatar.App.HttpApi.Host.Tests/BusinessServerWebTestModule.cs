using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.App.MongoDB;
using Aevatar.App.HttpApi.Host;
using Volo.Abp.AspNetCore.TestBase;
using Volo.Abp.Modularity;

namespace Aevatar.App;

[DependsOn(
    typeof(AbpAspNetCoreTestBaseModule),
    typeof(AppHttpApiHostModule),
    typeof(AppApplicationTestModule),
    typeof(AppMongoDbTestModule)
)]
public class AppWebTestModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var builder = new ConfigurationBuilder();
        builder.AddJsonFile("appsettings.json", false);
        builder.AddAevatarUserSecrets();
        builder.AddJsonFile("appsettings.secrets.json", true);
        context.Services.ReplaceConfiguration(builder.Build());
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ConfigureLocalizationServices(context.Services);
    }

    private static void ConfigureLocalizationServices(IServiceCollection services)
    {
        var cultures = new List<CultureInfo> { new CultureInfo("en"), new CultureInfo("tr") };
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture("en");
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
        });
    }
}
