using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Microsoft.Extensions.DependencyInjection;
using Aevatar.VibeResearching.Knowledge.Neo4j.Services;
using Aevatar.VibeResearching.Knowledge;

namespace Aevatar.VibeResearching.Knowledge.Neo4j;

[DependsOn(
    typeof(AbpMongoDbModule),
    typeof(VibeKnowledgeDomainModule)
)]
public class VibeKnowledgeNeo4jModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // Register DAG services
        services.AddTransient<DagConsensusService>();
        services.AddTransient<IDagConsensusService>(sp => sp.GetRequiredService<DagConsensusService>());
        services.AddTransient<IDagGroundingPolicy, DefaultDagGroundingPolicy>();

        // Register DagExplainService adapter
        services.AddTransient<DagExplainServiceAdapter>();
        services.AddTransient<IDagExplainService>(sp => sp.GetRequiredService<DagExplainServiceAdapter>());

        // Register upload services
        services.AddTransient<UploadsService>();
        services.AddTransient<FileTextParser>();
        services.AddTransient<UploadExtractionService>();

        // Register repositories
        services.AddTransient<Neo4jDagRepository>();
        services.AddTransient<IDagRepository>(sp => sp.GetRequiredService<Neo4jDagRepository>());

        // Configure DagGroundingOptions
        context.Services.Configure<DagGroundingOptions>(
            context.Services.GetConfiguration().GetSection(DagGroundingOptions.SectionName));
    }
}
