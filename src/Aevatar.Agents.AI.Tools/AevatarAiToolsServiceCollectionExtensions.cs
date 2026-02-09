using Aevatar.Agents.AI.Core.ToolPacks;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.AI.Tools;

public static class AevatarAiToolsServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarAiToolsPack(this IServiceCollection services)
    {
        services.AddSingleton<IAevatarToolPack, AevatarAiToolsPack>();
        return services;
    }
}
