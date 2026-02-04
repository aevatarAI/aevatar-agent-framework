using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.MCP.Abstractions;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Aevatar.Agents.AI.Core.Tests.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core.Tests.Runtime.Mcp;

public sealed class McpRuntimeTests
{
    [Fact(DisplayName = "RegisterFromConfigurationBestEffortAsync registers once per server key")]
    public async Task RegisterFromConfigurationBestEffortAsync_ShouldRegisterOncePerServer()
    {
        // Arrange
        var cfg = TestConfiguration.Build(
            ("MCP:mcpServers:github:enabled", "true"),
            ("MCP:mcpServers:github:url", "http://example.invalid"));

        var host = new Host(cfg);
        var factory = new FakeMcpClientFactory();
        var clock = new FakeClock();

        var rt = new McpRuntime(host, factory, clock);

        // Act
        (await rt.RegisterFromConfigurationBestEffortAsync(isRetry: false)).Should().BeTrue();
        (await rt.RegisterFromConfigurationBestEffortAsync(isRetry: false)).Should().BeFalse(); // already connected

        // Assert
        factory.CreateCalls.Should().Be(1);
        host.RegisterCalls.Should().Be(1);
    }

    [Fact(DisplayName = "Retry registration is throttled by minimum interval")]
    public async Task RegisterFromConfigurationBestEffortAsync_Retry_ShouldThrottleByMinInterval()
    {
        // Arrange
        var cfg = TestConfiguration.Build(
            ("MCP:retryMinIntervalSeconds", "60"),
            ("MCP:mcpServers:github:enabled", "true"),
            ("MCP:mcpServers:github:url", "http://example.invalid"));

        var host = new Host(cfg) { McpRetryOnEachChat = true };
        var factory = new FakeMcpClientFactory();
        var clock = new FakeClock(new DateTimeOffset(2026, 02, 03, 0, 0, 0, TimeSpan.Zero));

        var rt = new McpRuntime(host, factory, clock);

        // Act + Assert
        // First attempt: connects.
        (await rt.RegisterFromConfigurationBestEffortAsync(isRetry: true)).Should().BeTrue();
        factory.CreateCalls.Should().Be(1);

        // Second attempt within interval: throttled.
        clock.Now = clock.Now.AddSeconds(10);
        (await rt.RegisterFromConfigurationBestEffortAsync(isRetry: true)).Should().BeFalse();
        factory.CreateCalls.Should().Be(1);
    }

    [Fact(DisplayName = "DisposeBestEffortAsync disposes all created clients")]
    public async Task DisposeBestEffortAsync_ShouldDisposeAllClients()
    {
        // Arrange
        var cfg = TestConfiguration.Build(
            ("MCP:mcpServers:github:enabled", "true"),
            ("MCP:mcpServers:github:url", "http://example.invalid"));

        var host = new Host(cfg);
        var factory = new FakeMcpClientFactory();
        var clock = new FakeClock();
        var rt = new McpRuntime(host, factory, clock);

        (await rt.RegisterFromConfigurationBestEffortAsync(isRetry: false)).Should().BeTrue();

        // Act
        await rt.DisposeBestEffortAsync();

        // Assert
        factory.Clients.Single().Disposed.Should().BeTrue();
    }

    private sealed class Host(IConfiguration cfg) : IMcpRuntimeHost
    {
        public Microsoft.Extensions.Logging.ILogger Logger { get; } = NullLogger.Instance;
        public bool EnableMcpServers { get; set; } = true;
        public bool McpRetryOnEachChat { get; set; } = true;
        public TimeSpan McpRetryMinInterval { get; set; } = TimeSpan.FromSeconds(30);
        public IConfiguration? HostConfiguration { get; } = cfg;

        public IAevatarToolManager ToolManager { get; } = new NoopToolManager();
        public int RegisterCalls { get; private set; }

        public Task InitializeToolsAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RefreshToolCachesAsync(CancellationToken ct) => Task.CompletedTask;

        public Task RegisterMcpToolsAsync(string serverKey, IMCPClient mcpClient, string toolNamePrefix,
            MCPServerConfig config, CancellationToken ct)
        {
            RegisterCalls++;
            return Task.CompletedTask;
        }
    }

}

