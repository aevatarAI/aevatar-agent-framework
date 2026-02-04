using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Tests.Fixtures;
using Aevatar.Agents.AI.Abstractions.Tests.LLMProvider;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Tests.TestAgents;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Aevatar.Agents.AI.Core.Tests.Hooks;

public class SessionHooksTests(SessionHooksFixture fixture) : IClassFixture<SessionHooksFixture>
{
    private readonly IGAgentFactory _agentFactory = fixture.GAgentFactory;
    private readonly SessionHookRecorder _recorder = fixture.ServiceProvider.GetRequiredService<SessionHookRecorder>();

    private MockLLMProvider MockProvider => (MockLLMProvider)fixture.LLMProviderFactory.GetProvider("test-provider");

    [Fact(DisplayName = "ChatAsync runs session start/stop/end hooks")]
    public async Task ChatAsync_ShouldRun_SessionHooks()
    {
        _recorder.Reset();
        MockProvider.Clear();
        MockProvider.EnqueueResponse(new AevatarLLMResponse { Content = "hello world" });

        var agent = _agentFactory.CreateGAgent<TestAIGAgent>("session-hook-chat");
        await agent.InitializeAsync("test-provider");

        var response = await agent.ChatAsync(ChatRequest.Create("hi"));

        response.Content.ShouldNotBeNullOrWhiteSpace();
        _recorder.SessionStartCount.ShouldBe(1);
        _recorder.StopCount.ShouldBe(1);
        _recorder.SessionEndCount.ShouldBe(1);
        _recorder.LastStopStatus.ShouldBe(AevatarAgentHookStopStatus.Completed);
        _recorder.LastIsStreaming.ShouldNotBeNull();
        _recorder.LastIsStreaming.Value.ShouldBeFalse();
    }

    [Fact(DisplayName = "ChatStreamAsync runs session start/stop/end hooks")]
    public async Task ChatStreamAsync_ShouldRun_SessionHooks()
    {
        _recorder.Reset();
        MockProvider.Clear();
        MockProvider.EnqueueResponse(new AevatarLLMResponse { Content = "stream ok" });

        var agent = _agentFactory.CreateGAgent<TestAIGAgent>("session-hook-stream");
        await agent.InitializeAsync("test-provider");

        await foreach (var _ in agent.ChatStreamAsync(ChatRequest.Create("stream hi")))
        {
            // consume
        }

        _recorder.SessionStartCount.ShouldBe(1);
        _recorder.StopCount.ShouldBe(1);
        _recorder.SessionEndCount.ShouldBe(1);
        _recorder.LastStopStatus.ShouldBe(AevatarAgentHookStopStatus.Completed);
        _recorder.LastIsStreaming.ShouldNotBeNull();
        _recorder.LastIsStreaming.Value.ShouldBeTrue();
    }
}

public sealed class SessionHooksFixture : AITestFixture
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddSingleton<SessionHookRecorder>();
        services.AddSingleton<IAevatarAgentHook, SessionProbeHook>();
    }
}

public sealed class SessionHookRecorder
{
    public int SessionStartCount { get; private set; }
    public int StopCount { get; private set; }
    public int SessionEndCount { get; private set; }
    public AevatarAgentHookStopStatus? LastStopStatus { get; private set; }
    public bool? LastIsStreaming { get; private set; }

    public void RecordStart(bool isStreaming)
    {
        SessionStartCount++;
        LastIsStreaming = isStreaming;
    }

    public void RecordStop(AevatarAgentHookStopStatus? status)
    {
        StopCount++;
        LastStopStatus = status;
    }

    public void RecordEnd()
    {
        SessionEndCount++;
    }

    public void Reset()
    {
        SessionStartCount = 0;
        StopCount = 0;
        SessionEndCount = 0;
        LastStopStatus = null;
        LastIsStreaming = null;
    }
}

public sealed class SessionProbeHook(SessionHookRecorder recorder) : IAevatarAgentHook
{
    public Task OnSessionStartAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        recorder.RecordStart(context.IsStreaming);
        return Task.CompletedTask;
    }

    public Task OnStopAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        recorder.RecordStop(context.StopStatus);
        return Task.CompletedTask;
    }

    public Task OnSessionEndAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
    {
        recorder.RecordEnd();
        return Task.CompletedTask;
    }
}
