using Volo.Abp.Data;
using Volo.Abp.MongoDB;

namespace Aevatar.VibeResearching.Sessions.MongoDB;

/// <summary>
/// MongoDB context for the Sessions module.
/// </summary>
[ConnectionStringName("Default")]
public class SessionsMongoDbContext : AbpMongoDbContext
{
    protected override void CreateModel(IMongoModelBuilder modelBuilder)
    {
        base.CreateModel(modelBuilder);
        modelBuilder.ConfigureSessions();
    }
}

/// <summary>
/// MongoDB model builder extensions for Sessions module.
/// </summary>
public static class SessionsMongoDbContextModelCreatingExtensions
{
    public static void ConfigureSessions(this IMongoModelBuilder builder)
    {
        // Entity configurations will be added as entities are migrated
        // For now, the repositories use IStateStore abstraction which handles persistence independently
    }
}
