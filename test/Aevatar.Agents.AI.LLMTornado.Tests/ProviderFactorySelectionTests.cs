using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.LLMTornado.ClaudeAgentSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.LLMTornado.Tests;

public class ProviderFactorySelectionTests
{
    [Fact]
    public void Factory_ShouldCreate_ClaudeAgentSdkProvider_WhenProviderTypeMatches()
    {
        var providerConfig = new LLMProviderConfig
        {
            Name = "claude",
            ProviderType = "claude_agent_sdk",
            Model = "sonnet",
            ProviderSpecificSettings = new Dictionary<string, object>
            {
                ["runnerCommand"] = "node",
                ["runnerArgs"] = new[] { "/abs/runner.mjs" }
            }
        };

        var cfg = new LLMProvidersConfig
        {
            Default = "claude",
            Providers = new Dictionary<string, LLMProviderConfig>
            {
                ["claude"] = providerConfig
            }
        };

        var services = new ServiceCollection();
        services.AddLogging();

        var sp = services.BuildServiceProvider();
        var options = Options.Create(cfg);
        var logger = sp.GetRequiredService<ILogger<LLMTornadoProviderFactory>>();

        var factory = new LLMTornadoProviderFactory(options, logger, sp);
        var provider = factory.CreateProvider(providerConfig);

        provider.ShouldBeOfType<ClaudeAgentSdkProvider>();
    }
}


