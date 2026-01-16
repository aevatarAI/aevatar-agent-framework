using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Learning.Contracts;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Learning.Agents;

// ============================================================
//  LearningNotebookAgent (MVP Skeleton)
//
//  Goals:
//  - Provide a minimal runnable Agent with Protobuf state.
//  - Initialize State in OnActivateAsync (never replace State).
//  - Keep implementation tiny and deterministic.
//
//  Notes:
//  - This is only the "business shell". Real AI execution & AG-UI streaming
//    are wired by the API/runtime layer tasks (sessions + chat services).
// ============================================================
public sealed class LearningNotebookAgent : GAgentBase<LearningNotebookState>
{
    // Parameterless constructor is required by the framework activation pipeline.
    public LearningNotebookAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        var notebookId = string.IsNullOrWhiteSpace(State.NotebookId) ? "(none)" : State.NotebookId.Trim();
        var historyCount = State.History?.Count ?? 0;
        var dueCards = State.Progress?.DueCards ?? 0;
        var newCards = State.Progress?.NewCards ?? 0;

        return Task.FromResult($"LearningNotebookAgent: notebook={notebookId}, history={historyCount}, due={dueCards}, new={newCards}");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        // NOTE: Never replace State; only mutate its properties.
        if (State.CreatedAt == null || State.CreatedAt.Seconds == 0)
            State.CreatedAt = now;

        State.UpdatedAt = now;
        State.Progress ??= new LearningProgressSummary();
        State.Progress.LastActivity ??= now;
    }

    // ============================================================
    //  Event Handlers
    // ============================================================

    [EventHandler]
    public Task HandleUserInputAsync(LearningUserInputEvent evt)
    {
        if (evt == null) return Task.CompletedTask;

        var message = (evt.Message ?? string.Empty).Trim();
        if (message.Length == 0) return Task.CompletedTask;

        if (!string.IsNullOrWhiteSpace(evt.NotebookId))
            State.NotebookId = evt.NotebookId.Trim();

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        State.UpdatedAt = now;

        State.Progress ??= new LearningProgressSummary();
        State.Progress.LastActivity = now;

        // Append to conversation history (bounded window/compaction is handled later).
        var mid = string.IsNullOrWhiteSpace(evt.RunId)
            ? $"msg:{Id:N}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}:user"
            : $"msg:{evt.RunId}:user";

        State.History.Add(new LearningChatMessage
        {
            Id = mid,
            Role = LearningChatRole.User,
            Content = message,
            Timestamp = now
        });

        return Task.CompletedTask;
    }
}


