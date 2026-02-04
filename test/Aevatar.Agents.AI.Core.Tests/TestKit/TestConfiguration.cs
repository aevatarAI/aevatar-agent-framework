using Microsoft.Extensions.Configuration;

namespace Aevatar.Agents.AI.Core.Tests.TestKit;

internal static class TestConfiguration
{
    public static IConfiguration Build(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs)
        {
            dict[k] = v;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }
}

