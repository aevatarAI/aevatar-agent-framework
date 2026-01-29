using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Sessions.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Sessions.Tests;

public sealed class SessionsTestFixture : IDisposable
{
    public ServiceProvider ServiceProvider { get; }
    public CognitiveSessionService Sessions => ServiceProvider.GetRequiredService<CognitiveSessionService>();
    public IGAgentActorManager ActorManager => ServiceProvider.GetRequiredService<IGAgentActorManager>();
    public string WorkflowsDirectory { get; }
    public string ToolsDirectory { get; }

    public SessionsTestFixture()
    {
        WorkflowsDirectory = CreateTempDirectory();
        ToolsDirectory = CreateTempDirectory();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.AddOptions();
        services.AddSingleton<IHostEnvironment>(_ =>
            new TestHostEnvironment("Aevatar.Sessions.Tests", WorkflowsDirectory));

        services.AddSingleton<ILLMProviderFactory, TestLLMProviderFactory>();
        services.Configure<LLMProvidersConfig>(options =>
        {
            options.Default = "test-provider";
            options.Providers["test-provider"] = BuildProviderConfig("test-provider");
        });

        services.AddAevatarAgentSystem(builder => builder.UseLocalRuntime());
        services.AddAevatarCognitiveSessions(options =>
        {
            options.WorkflowsDirectory = WorkflowsDirectory;
            options.LazyLoadRoles = true;
        });
        services.AddAevatarSessionRuntime(options =>
        {
            options.SystemPrompt = "test";
            options.WorkflowName = "workspace_mesh";
            options.MaxSnapshotMessages = 32;
            options.StreamChunkEveryN = 1;
        });
        services.AddAevatarSessionTooling(options =>
        {
            options.DotNetToolDirectories = new List<string> { ToolsDirectory };
            options.DotNetToolMaxFiles = 16;
        });

        ServiceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        ServiceProvider.Dispose();
        TryDeleteDirectory(WorkflowsDirectory);
        TryDeleteDirectory(ToolsDirectory);
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "aevatar-sessions-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort only
        }
    }

    private static LLMProviderConfig BuildProviderConfig(string providerName)
    {
        return new LLMProviderConfig
        {
            Name = providerName,
            ProviderType = "test",
            Model = "test-model",
            ApiKey = "test-key",
            Temperature = 0.1,
            MaxTokens = 256
        };
    }

    private sealed class TestLLMProviderFactory : ILLMProviderFactory
    {
        private readonly TestLLMProvider _provider = new();

        public IAevatarLLMProvider GetProvider(string providerName)
            => _provider;

        public IAevatarLLMProvider GetDefaultProvider()
            => _provider;

        public IReadOnlyList<string> GetAvailableProviderNames()
            => new[] { "test-provider" };

        public bool HasProvider(string providerName)
            => !string.IsNullOrWhiteSpace(providerName);

        public LLMProviderConfig GetProviderConfig(string providerName)
            => BuildProviderConfig(providerName);

        public LLMProviderConfig GetDefaultProviderConfig()
            => BuildProviderConfig("test-provider");

        public IAevatarLLMProvider CreateProvider(
            LLMProviderConfig providerConfig,
            CancellationToken cancellationToken = default)
            => _provider;

        public Task<IAevatarLLMProvider> GetProviderAsync(
            string providerName,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IAevatarLLMProvider>(_provider);

        public Task<IAevatarLLMProvider> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IAevatarLLMProvider>(_provider);

    }

    private sealed class TestLLMProvider : IAevatarLLMProvider
    {
        public Task<AevatarLLMResponse> GenerateAsync(
            AevatarLLMRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = new AevatarLLMResponse
            {
                Content = "pong",
                AevatarStopReason = AevatarStopReason.Complete,
                Usage = new AevatarTokenUsage
                {
                    PromptTokens = 4,
                    CompletionTokens = 2,
                    TotalTokens = 6
                }
            };
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
            AevatarLLMRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new AevatarLLMToken
            {
                Content = "pong",
                IsComplete = true
            };
            await Task.CompletedTask;
        }

        public Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AevatarModelInfo
            {
                Name = "test-model",
                MaxTokens = 1024,
                SupportsStreaming = true,
                SupportsFunctions = false
            });
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string name, string contentRootPath)
        {
            ApplicationName = name;
            EnvironmentName = Environments.Development;
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
