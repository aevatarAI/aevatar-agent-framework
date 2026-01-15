using AevatarKit;
using AevatarKit.Runtime.Memory;
using Google.Protobuf.WellKnownTypes;

namespace AevatarKit.Runtime.Run;

/// <summary>
/// MVP run engine: simulate a workflow and stream step events.
///
/// 说明：
/// - 真实版本会执行 GraphDefinition(IR) 编译后的 workflow（调用 Aevatar runtime）
/// - MVP 仅用于“产品形态展示”：Run Timeline、Streaming、Memory 落库形态
/// </summary>
public interface IRunEngine
{
    Task StartAsync(RunInstance run, CancellationToken ct = default);
}

public sealed class MvpRunEngine : IRunEngine
{
    private readonly IMemoryStore _memory;

    public MvpRunEngine(IMemoryStore memory)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public Task StartAsync(RunInstance run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        // Fire-and-forget: keep API response fast.
        _ = Task.Run(async () =>
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, run.Cancellation.Token);
                await ExecuteAsync(run, _memory, linkedCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await PublishAsync(
                    run,
                    stepId: "engine",
                    stepName: "engine",
                    status: WorkflowStepStatus.Failed,
                    output: string.Empty,
                    error: ex.Message).ConfigureAwait(false);
            }
            finally
            {
                run.EventChannel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    private static async Task ExecuteAsync(RunInstance run, IMemoryStore memory, CancellationToken ct)
    {
        // Step 0: receive input
        await PublishAsync(run, "input", "Input", WorkflowStepStatus.Running, "", "").ConfigureAwait(false);
        await Task.Delay(150, ct).ConfigureAwait(false);

        var input = string.IsNullOrWhiteSpace(run.Input) ? "(empty)" : run.Input.Trim();
        await AppendMemoryAsync(run, memory, role: "user", content: input, ct).ConfigureAwait(false);

        await PublishAsync(run, "input", "Input", WorkflowStepStatus.Completed, "Input received.", "").ConfigureAwait(false);

        // Step 1: "llm_call" (streaming)
        await PublishAsync(run, "llm_call", "LLM Call", WorkflowStepStatus.Running, "", "").ConfigureAwait(false);

        var chunks = new[]
        {
            "Thinking… ",
            "drafting plan… ",
            "assembling answer… "
        };

        foreach (var c in chunks)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(220, ct).ConfigureAwait(false);
            await PublishAsync(run, "llm_call", "LLM Call", WorkflowStepStatus.Streaming, c, "").ConfigureAwait(false);
        }

        var finalAnswer = "MVP output: this is where the model/tool results would appear.";
        await AppendMemoryAsync(run, memory, role: "assistant", content: finalAnswer, ct).ConfigureAwait(false);

        await PublishAsync(run, "llm_call", "LLM Call", WorkflowStepStatus.Completed, finalAnswer, "").ConfigureAwait(false);

        // Step 2: transform
        await PublishAsync(run, "transform", "Transform", WorkflowStepStatus.Running, "", "").ConfigureAwait(false);
        await Task.Delay(180, ct).ConfigureAwait(false);
        await PublishAsync(run, "transform", "Transform", WorkflowStepStatus.Completed, "Transform done.", "").ConfigureAwait(false);

        // Step 3: done
        await PublishAsync(run, "done", "Done", WorkflowStepStatus.Completed, "Run completed.", "").ConfigureAwait(false);
    }

    private static async Task PublishAsync(
        RunInstance run,
        string stepId,
        string stepName,
        WorkflowStepStatus status,
        string output,
        string error)
    {
        var evt = new WorkflowStepEvent
        {
            RunId = run.RunId,
            StepId = stepId,
            StepName = stepName,
            Status = status,
            OutputChunk = output ?? string.Empty,
            Error = error ?? string.Empty,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        // Run history (append-only)
        run.EventHistory.Enqueue(evt);

        await run.EventChannel.Writer.WriteAsync(evt).ConfigureAwait(false);
    }

    private static Task AppendMemoryAsync(RunInstance run, IMemoryStore memory, string role, string content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 1) Run-scope memory (always)
        var memoryId = $"run:{run.RunId}";
        var scope = new MemoryScope
        {
            Type = MemoryScopeType.Run,
            ScopeId = run.RunId
        };

        var entry = new MemoryEntry
        {
            MemoryId = memoryId,
            Scope = scope,
            RunId = run.RunId,
            AgentId = "mvp-agent",
            Role = role ?? "unknown",
            Content = content ?? string.Empty,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        run.MemoryEntries.Enqueue(entry);
        memory.Append(entry);

        // 2) Session-scope memory (shared) - optional for MVP demo
        if (!string.IsNullOrWhiteSpace(run.SessionId))
        {
            var sessionId = run.SessionId.Trim();
            var sessionEntry = entry.Clone();
            sessionEntry.MemoryId = $"session:{sessionId}";
            sessionEntry.Scope = new MemoryScope
            {
                Type = MemoryScopeType.Session,
                ScopeId = sessionId
            };
            memory.Append(sessionEntry);
        }

        return Task.CompletedTask;
    }
}


