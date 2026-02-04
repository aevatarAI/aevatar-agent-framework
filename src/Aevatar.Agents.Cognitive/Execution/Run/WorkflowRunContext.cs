using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution.Run;

// ============================================================
//  WorkflowRunContext - runtime bag for workflow execution
//
//  中文说明：
//  - 运行时容器：thread/run/variables/events + app-specific items
//  - 不跨进程：只在本地内存中使用
// ============================================================
public static class WorkflowRunContextKeys
{
    public const string UserMessageText = "workflow.user.message_text";
    public const string UserMessageId = "workflow.user.message_id";
    public const string AssistantMessageId = "workflow.assistant.message_id";
    public const string AssistantRole = "workflow.assistant.role";
    public const string AssistantMeta = "workflow.assistant.meta";
    public const string RunResultPayload = "workflow.run.result_payload";
    public const string RunResultPayloadBuilder = "workflow.run.result_payload_builder";
}

public sealed record WorkflowRunContextOptions
{
    public required string ThreadId { get; init; }
    public required string RunId { get; init; }
    public required ILogger Logger { get; init; }
    public required TemplateEngine TemplateEngine { get; init; }

    public Dictionary<string, object>? Variables { get; init; }
    public IWorkflowRunEventSink? Events { get; init; }
    public IDictionary<string, object>? Items { get; init; }
    public SemaphoreSlim? RunLock { get; init; }
    public Action<string>? EmitAssistantDelta { get; init; }
}

public sealed class WorkflowRunContext
{
    public WorkflowRunContext(WorkflowRunContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThreadId = (options.ThreadId ?? string.Empty).Trim();
        RunId = (options.RunId ?? string.Empty).Trim();
        Logger = options.Logger ?? throw new ArgumentNullException(nameof(options.Logger));
        TemplateEngine = options.TemplateEngine ?? throw new ArgumentNullException(nameof(options.TemplateEngine));

        Variables = options.Variables ?? new Dictionary<string, object>(StringComparer.Ordinal);
        Events = options.Events ?? NullWorkflowRunEventSink.Instance;
        Items = options.Items ?? new Dictionary<string, object>(StringComparer.Ordinal);
        RunLock = options.RunLock;
        EmitAssistantDelta = options.EmitAssistantDelta ?? (_ => { });
    }

    public string ThreadId { get; }
    public string RunId { get; }
    public ILogger Logger { get; }
    public TemplateEngine TemplateEngine { get; }
    public Dictionary<string, object> Variables { get; }
    public IWorkflowRunEventSink Events { get; }
    public IDictionary<string, object> Items { get; }
    public SemaphoreSlim? RunLock { get; }
    public Action<string> EmitAssistantDelta { get; }
}
