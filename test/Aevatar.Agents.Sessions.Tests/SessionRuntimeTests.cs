using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Sessions.Runtime;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Sessions.Tests;

public sealed class SessionRuntimeTests(SessionsTestFixture fixture) : IClassFixture<SessionsTestFixture>
{
    private readonly SessionsTestFixture _fixture = fixture;

    [Fact]
    public async Task SendChatAsync_ShouldEmitAgUiEvents()
    {
        var spec = SessionTestData.WriteWorkflow(_fixture.WorkflowsDirectory);
        var runtime = _fixture.ServiceProvider.GetRequiredService<SessionRuntime>();
        var state = await runtime.CreateSessionAsync(spec.WorkflowName, CancellationToken.None);
        var stream = await runtime.GetSessionStreamAsync(state.SessionId, CancellationToken.None);
        stream.ShouldNotBeNull();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var collectTask = CollectUntilAsync(stream!, evt => evt is RunFinishedEvent, cts.Token);

        var runId = await runtime.SendChatAsync(state.SessionId, new ChatRequestEvent { Message = "ping" }, CancellationToken.None);
        runId.ShouldNotBeNullOrWhiteSpace();

        var events = await collectTask;
        events.OfType<RunStartedEvent>().ShouldNotBeEmpty();
        events.OfType<TextMessageStartEvent>().Any(e => e.Role == "assistant").ShouldBeTrue();
        events.OfType<TextMessageContentEvent>().Any(e => (e.Delta ?? string.Empty).Contains("pong")).ShouldBeTrue();
        events.OfType<CustomEvent>().Any(e => e.Name == "SESSION_STATUS").ShouldBeTrue();
    }

    [Fact]
    public async Task SessionAgUiStream_ShouldEmitExecutionTraceRaw()
    {
        var spec = SessionTestData.WriteWorkflow(_fixture.WorkflowsDirectory);
        var runtime = _fixture.ServiceProvider.GetRequiredService<SessionRuntime>();
        var state = await runtime.CreateSessionAsync(spec.WorkflowName, CancellationToken.None);
        var stream = await runtime.GetSessionStreamAsync(state.SessionId, CancellationToken.None);
        stream.ShouldNotBeNull();

        var primary = await runtime.ResolvePrimaryAgentAsync(state.SessionId, CancellationToken.None);
        primary.ShouldNotBeNull();

        var resolver = _fixture.ServiceProvider.GetRequiredService<IAgentMessageStreamResolver>();
        var agentStream = resolver.GetStream(primary!.AgentId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var collectTask = CollectUntilAsync(stream!, evt =>
            evt is CustomEvent custom && custom.Name == "execution_trace_raw", cts.Token);

        await agentStream.ProduceAsync(new ExecutionTraceEvent
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Phase = "event.handler.start",
            Message = "trace",
            NodeId = spec.NodeId
        }, CancellationToken.None);

        var events = await collectTask;
        events.OfType<CustomEvent>().Any(e => e.Name == "execution_trace_raw").ShouldBeTrue();
    }

    private static async Task<List<AgUiEvent>> CollectUntilAsync(
        SessionAgUiStream stream,
        Func<AgUiEvent, bool> predicate,
        CancellationToken ct)
    {
        var list = new List<AgUiEvent>();
        try
        {
            await foreach (var evt in stream.SubscribeAsync(ct))
            {
                list.Add(evt);
                if (predicate(evt))
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }

        return list;
    }
}
