using MongoDB.Driver;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;

namespace Aevatar.VibeResearching.Agents.MongoDB;

[ConnectionStringName("Default")]
public class AgentsMongoDbContext : AbpMongoDbContext
{
    // Agents module MongoDB context
    // ReviewAgent storage is file-based, managed via individual service implementations

    protected override void CreateModel(IMongoModelBuilder builder)
    {
        base.CreateModel(builder);
    }
}
