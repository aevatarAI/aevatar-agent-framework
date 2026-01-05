using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Tests.Fixtures;
using Aevatar.Agents.AI.Abstractions.Tests.LLMProvider;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Tests.TestAgents;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Aevatar.Agents.AI.Core.Tests.Hooks;

public class ChatStreamHooksTests(ChatStreamHooksFixture fixture) : IClassFixture<ChatStreamHooksFixture>
{
    private readonly IGAgentFactory _agentFactory = fixture.GAgentFactory;
    private readonly HookCallRecorder _recorder = fixture.ServiceProvider.GetRequiredService<HookCallRecorder>();

    private MockLLMProvider MockProvider => (MockLLMProvider)fixture.LLMProviderFactory.GetProvider("test-provider");

    [Fact]
    public async Task ChatStreamAsync_ShouldRun_BeforeLLMRequest_Hooks()
    {
        // Arrange
        _recorder.Reset();
        MockProvider.Clear();
        MockProvider.EnqueueResponse(new AevatarLLMResponse { Content = "hello world" });

        var agent = _agentFactory.CreateGAgent<TestAIGAgent>("stream-hook-1");
        await agent.InitializeAsync("test-provider");

        // Act
        var chunks = new List<string>();
        await foreach (var chunk in agent.ChatStreamAsync(ChatRequest.Create("stream test")))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Count.ShouldBeGreaterThan(0);
        _recorder.BeforeLLMRequestCount.ShouldBe(1);
        _recorder.LastBeforeRequestId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ChatStreamAsync_WhenProviderThrows_ShouldRun_OnError_Hook()
    {
        // Arrange
        _recorder.Reset();
        MockProvider.Clear();
        MockProvider.ConfigureToThrow(new InvalidOperationException("boom"));

        var agent = _agentFactory.CreateGAgent<TestAIGAgent>("stream-hook-2");
        await agent.InitializeAsync("test-provider");

        // Act
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in agent.ChatStreamAsync(ChatRequest.Create("stream throw")))
            {
                // consume
            }
        });

        // Assert
        _recorder.OnErrorCount.ShouldBe(1);
        _recorder.LastErrorRequestId.ShouldNotBeNullOrWhiteSpace();
    }
}

public sealed class ChatStreamHooksFixture : AITestFixture
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddSingleton<HookCallRecorder>();
        services.AddSingleton<IAevatarAgentHook, ProbeHook>();
    }
}

public sealed class HookCallRecorder
{
    public int BeforeLLMRequestCount { get; private set; }
    public int OnErrorCount { get; private set; }
    public string? LastBeforeRequestId { get; private set; }
    public string? LastErrorRequestId { get; private set; }

    public void RecordBefore(string requestId)
    {
        BeforeLLMRequestCount++;
        LastBeforeRequestId = requestId;
    }

    public void RecordError(string requestId)
    {
        OnErrorCount++;
        LastErrorRequestId = requestId;
    }

    public void Reset()
    {
        BeforeLLMRequestCount = 0;
        OnErrorCount = 0;
        LastBeforeRequestId = null;
        LastErrorRequestId = null;
    }
}

public sealed class ProbeHook(HookCallRecorder recorder) : IAevatarAgentHook
{
    public Task BeforeLLMRequestAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        recorder.RecordBefore(context.RequestId);
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(AevatarAgentHookContext context, Exception exception, CancellationToken cancellationToken)
    {
        recorder.RecordError(context.RequestId);
        return Task.CompletedTask;
    }
}


