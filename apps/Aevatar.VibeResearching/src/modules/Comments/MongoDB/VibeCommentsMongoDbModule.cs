using Aevatar.VibeResearching.Comments.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;

namespace Aevatar.VibeResearching.Comments.MongoDB;

[DependsOn(
    typeof(AbpMongoDbModule),
    typeof(VibeCommentsDomainModule)
)]
public class VibeCommentsMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Uses IMongoDatabase directly (registered by AddAevatarMongoDB in host).
        // No AddMongoDbContext needed — consistent with Sessions/Agents modules.
        context.Services.AddTransient<INodeCommentRepository, MongoNodeCommentRepository>();
    }
}
