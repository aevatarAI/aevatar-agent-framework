using MongoDB.Driver;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;

namespace Aevatar.VibeResearching.Infrastructure.MongoDB;

[ConnectionStringName("Default")]
public class InfrastructureMongoDbContext : AbpMongoDbContext
{
    // Infrastructure module uses file-based and MongoDB stores
    // Collections are accessed via individual repository implementations

    protected override void CreateModel(IMongoModelBuilder builder)
    {
        base.CreateModel(builder);
        // Configure collections as needed
    }
}
