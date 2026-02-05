using Aevatar.VibeResearching.UserProviders.Constants;
using Aevatar.VibeResearching.UserProviders.MongoDB.Repositories;
using Aevatar.VibeResearching.UserProviders.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;

namespace Aevatar.VibeResearching.UserProviders.MongoDB;

[DependsOn(
    typeof(AbpMongoDbModule),
    typeof(UserProvidersDomainModule))]
public class UserProvidersMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // Register MongoClient as singleton to reuse connection pools.
        // MongoClient is thread-safe and designed to be long-lived.
        services.AddSingleton<IMongoClient>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AbpDbConnectionOptions>>();
            var connString = options.Value.ConnectionStrings.Default;

            if (string.IsNullOrWhiteSpace(connString))
                throw new InvalidOperationException("MongoDB connection string is not configured.");

            return new MongoClient(new MongoUrl(connString));
        });

        // Register the MongoDB context as singleton (stateless wrapper over IMongoDatabase)
        services.AddSingleton<UserProvidersMongoDbContext>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AbpDbConnectionOptions>>();
            var connString = options.Value.ConnectionStrings.Default;
            var mongoUrl = new MongoUrl(connString);

            var client = sp.GetRequiredService<IMongoClient>();
            var database = client.GetDatabase(mongoUrl.DatabaseName ?? "aevatar");
            return new UserProvidersMongoDbContext(database);
        });

        // Register repositories
        services.AddTransient<IUserLlmProviderRepository, MongoUserLlmProviderRepository>();
        services.AddTransient<IUserCodexTokenRepository, MongoUserCodexTokenRepository>();
    }

    public override async Task OnPostApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // Create indexes for the UserProviders collections
        try
        {
            var dbContext = context.ServiceProvider.GetRequiredService<UserProvidersMongoDbContext>();

            // user_llm_providers indexes
            await dbContext.Providers.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<Entities.UserLlmProvider>(
                    Builders<Entities.UserLlmProvider>.IndexKeys.Ascending(x => x.UserId),
                    new CreateIndexOptions { Name = "idx_userId" }),

                new CreateIndexModel<Entities.UserLlmProvider>(
                    Builders<Entities.UserLlmProvider>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Ascending(x => x.Name),
                    new CreateIndexOptions
                    {
                        Name = "idx_userId_name_unique",
                        Unique = true,
                        Collation = new Collation("en", strength: CollationStrength.Secondary)
                    }),

                new CreateIndexModel<Entities.UserLlmProvider>(
                    Builders<Entities.UserLlmProvider>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Ascending(x => x.IsDefault),
                    new CreateIndexOptions { Name = "idx_userId_isDefault" }),

                new CreateIndexModel<Entities.UserLlmProvider>(
                    Builders<Entities.UserLlmProvider>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Ascending(x => x.IsCodexOAuth),
                    new CreateIndexOptions { Name = "idx_userId_isCodexOAuth" })
            });

            // user_codex_tokens indexes
            await dbContext.CodexTokens.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<Entities.UserCodexToken>(
                    Builders<Entities.UserCodexToken>.IndexKeys.Ascending(x => x.UserId),
                    new CreateIndexOptions
                    {
                        Name = "idx_userId_unique",
                        Unique = true
                    })
            });
        }
        catch (Exception ex)
        {
            var logger = context.ServiceProvider.GetService<ILogger<UserProvidersMongoDbModule>>();
            logger?.LogWarning(ex, "Failed to create UserProviders MongoDB indexes. Queries may be slow.");
        }

        await base.OnPostApplicationInitializationAsync(context);
    }
}
