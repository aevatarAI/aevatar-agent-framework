using System.Text;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Execution.Run;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Aevatar.Agents.Cognitive.Streaming;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.Cognitive.Researching.Materials;
using Aevatar.Agents.Cognitive.Researching.Sessions;

namespace Aevatar.Agents.Cognitive.Researching.Workflow;

public sealed class ResearchingWorkflowRunner
{
    private const string WorkflowName = "vibe_round";

    private readonly WorkflowRunExecutor _executor;
    private readonly IWorkflowRegistry _workflowRegistry;
    private readonly TemplateEngine _templateEngine;
    private readonly IResearchingStreamEventSinkFactory _eventSinkFactory;
    private readonly ILogger<ResearchingWorkflowRunner> _logger;

    public ResearchingWorkflowRunner(
        WorkflowRunExecutor executor,
        IWorkflowRegistry workflowRegistry,
        TemplateEngine templateEngine,
        IResearchingStreamEventSinkFactory? eventSinkFactory,
        ILogger<ResearchingWorkflowRunner> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _workflowRegistry = workflowRegistry ?? throw new ArgumentNullException(nameof(workflowRegistry));
        _templateEngine = templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));
        _eventSinkFactory = eventSinkFactory ?? NullResearchingStreamEventSinkFactory.Instance;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WorkflowRunResult> ExecuteRoundAsync(
        ResearchSession session,
        string runId,
        ResearchingInput input,
        string question,
        MaterialsSnapshot? materials,
        string? providerOverride,
        Action<string>? emitAssistantDelta,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(input);

        var workflow = _workflowRegistry.Get(WorkflowName);
        if (workflow == null)
            throw new InvalidOperationException($"Workflow '{WorkflowName}' not found.");

        var sink = new ResearchSessionEventSink(session);
        var messages = new AgUiMessageEmitter(sink);

        var assistant = new StringBuilder();
        var assistantMessageId = $"msg:{session.Id}:assistant:{runId}";
        var userMessageId = $"msg:{session.Id}:user:{runId}";

        var context = new WorkflowRunContext(new WorkflowRunContextOptions
        {
            ThreadId = session.Id,
            RunId = runId,
            Logger = _logger,
            TemplateEngine = _templateEngine,
            Events = sink,
            RunLock = session.RunLock,
            EmitAssistantDelta = delta =>
            {
                if (string.IsNullOrEmpty(delta)) return;
                assistant.Append(delta);
                messages.AppendDelta(assistantMessageId, "assistant", delta);
                emitAssistantDelta?.Invoke(delta);
            }
        });

        context.Items[ResearchingRunContextKeys.Session] = session;
        context.Items[ResearchingRunContextKeys.Input] = input;
        context.Items[ResearchingRunContextKeys.Question] = question;
        if (!string.IsNullOrWhiteSpace(providerOverride))
            context.Items[ResearchingRunContextKeys.ProviderOverride] = providerOverride!;
        if (materials != null)
            context.Items[ResearchingRunContextKeys.Materials] = materials;

        context.Items[WorkflowRunContextKeys.UserMessageText] = question;
        context.Items[WorkflowRunContextKeys.UserMessageId] = userMessageId;
        context.Items[WorkflowRunContextKeys.AssistantMessageId] = assistantMessageId;
        context.Items[WorkflowRunContextKeys.AssistantRole] = "assistant";
        context.Items[WorkflowRunContextKeys.AssistantMeta] = new
        {
            messageId = assistantMessageId,
            agent = "research_assistant",
            stepName = "vibe",
            providerName = (providerOverride ?? string.Empty).Trim()
        };

        context.Items[WorkflowRunContextKeys.RunResultPayloadBuilder] = (Func<WorkflowRunResult, object?>)(result => new
        {
            ok = result.Success,
            assistantMessageId,
            assistant = assistant.ToString()
        });

        context.Variables["question"] = question;
        context.Variables["attachments"] = input.AttachmentPaths ?? new List<string>();
        context.Variables["request_id"] = input.RequestId ?? string.Empty;
        context.Variables["provider_override"] = providerOverride ?? string.Empty;

        var prevSink = ResearchStreamEventContext.Current;
        ResearchStreamEventContext.Current = _eventSinkFactory.Create(session, runId);

        try
        {
            return await _executor.ExecuteAsync(workflow, context, new WorkflowRunOptions
            {
                StopOnFailure = false
            }, ct);
        }
        finally
        {
            ResearchStreamEventContext.Current = prevSink;
        }
    }

    private sealed class ResearchSessionEventSink : IWorkflowRunEventSink
    {
        private readonly ResearchSession _session;

        public ResearchSessionEventSink(ResearchSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public void Publish(AgUiEvent evt) => _session.Events.Publish(evt);

        public void SetMessage(string messageId, string role, string content)
            => _session.SetMessage(messageId, role, content);

        public void AppendToMessage(string messageId, string role, string delta)
            => _session.AppendToMessage(messageId, role, delta);
    }
}
