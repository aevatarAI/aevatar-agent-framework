using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Execution.Run;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

public sealed class WorkflowRunExecutorLifecycleTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldEmitRunAndStepEventsInOrder()
    {
        var events = new List<AgUiEvent>();
        var sink = new RecordingEventSink(events);
        var context = new WorkflowRunContext(new WorkflowRunContextOptions
        {
            ThreadId = "t1",
            RunId = "r1",
            Logger = NullLogger.Instance,
            TemplateEngine = new TemplateEngine(),
            Events = sink
        });

        context.Variables["task"] = "hello";

        var workflow = new WorkflowDefinition
        {
            Name = "test",
            Inputs =
            [
                new InputParameter { Name = "task", Type = "string", Required = true }
            ],
            Steps =
            [
                new StepDefinition { Id = "step-1", Type = "test_step" },
                new StepDefinition { Id = "step-2", Type = "test_step" }
            ]
        };

        var executor = new WorkflowRunExecutor(
            new TemplateEngine(),
            new IWorkflowRunStepModule[] { new TestStepModule() },
            NullLogger<WorkflowRunExecutor>.Instance);

        var result = await executor.ExecuteAsync(workflow, context);
        result.Success.ShouldBeTrue();

        var ordered = events.Select(e => e.GetType().Name).ToList();
        ordered.ShouldBe(new[]
        {
            nameof(RunStartedEvent),
            nameof(StepStartedEvent),
            nameof(StepFinishedEvent),
            nameof(StepStartedEvent),
            nameof(StepFinishedEvent),
            nameof(RunFinishedEvent)
        });
    }

    [Fact]
    public async Task ExecuteAsync_WhenCanceled_ShouldEmitRunError()
    {
        var events = new List<AgUiEvent>();
        var sink = new RecordingEventSink(events);
        var context = new WorkflowRunContext(new WorkflowRunContextOptions
        {
            ThreadId = "t2",
            RunId = "r2",
            Logger = NullLogger.Instance,
            TemplateEngine = new TemplateEngine(),
            Events = sink
        });

        context.Variables["task"] = "cancel";

        var workflow = new WorkflowDefinition
        {
            Name = "cancel-test",
            Inputs =
            [
                new InputParameter { Name = "task", Type = "string", Required = true }
            ],
            Steps =
            [
                new StepDefinition { Id = "step-1", Type = "test_step" }
            ]
        };

        var executor = new WorkflowRunExecutor(
            new TemplateEngine(),
            new IWorkflowRunStepModule[] { new TestStepModule() },
            NullLogger<WorkflowRunExecutor>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(workflow, context, ct: cts.Token));

        events.OfType<RunStartedEvent>().Count().ShouldBe(1);
        events.OfType<RunErrorEvent>().Count().ShouldBe(1);
        events.OfType<RunFinishedEvent>().Count().ShouldBe(0);
    }

    private sealed class TestStepModule : WorkflowRunStepModuleBase
    {
        public override string Name => "test_step";
        public override string StepType => "test_step";

        public override Task<PrimitiveResult> ExecuteAsync(
            WorkflowRunContext context,
            StepDefinition step,
            string? preRenderedPrompt,
            string? preRenderedSystem,
            CancellationToken ct)
        {
            return Task.FromResult(PrimitiveResult.Ok("ok"));
        }
    }

    private sealed class RecordingEventSink : IWorkflowRunEventSink
    {
        private readonly List<AgUiEvent> _events;

        public RecordingEventSink(List<AgUiEvent> events)
        {
            _events = events;
        }

        public void Publish(AgUiEvent evt) => _events.Add(evt);

        public void SetMessage(string messageId, string role, string content)
        {
        }

        public void AppendToMessage(string messageId, string role, string delta)
        {
        }
    }
}
