using Aevatar.VibeResearching.Comments.Entities;
using MongoDB.Driver;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;

namespace Aevatar.VibeResearching.Comments.MongoDB;

/// <summary>
/// MongoDB context for the Comments module.
/// </summary>
[ConnectionStringName("Default")]
public class CommentsMongoDbContext : AbpMongoDbContext
{
    /// <summary>
    /// NodeComments collection.
    /// </summary>
    public IMongoCollection<NodeComment> NodeComments => Collection<NodeComment>();

    protected override void CreateModel(IMongoModelBuilder modelBuilder)
    {
        base.CreateModel(modelBuilder);

        modelBuilder.Entity<NodeComment>(b =>
        {
            b.CollectionName = "NodeComments";
        });
    }
}
