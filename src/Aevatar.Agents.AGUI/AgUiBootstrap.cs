using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;

namespace Aevatar.Agents.AGUI;

// ============================================================
//  AG-UI BOOTSTRAP (Message Snapshot)
//
//  Goals:
//  - Provide "snapshot" for SSE reconnection instead of replay (avoid token/progress explosion)
//  - Framework layer only does "collection + normalization", no business routing/naming
//
//  Input:
//  - A set of Actors (containing ActorId / ActorTypeName / LaneId / CreateAsync)
//  - Optional lane resolver: maps step_id to laneId (defined by business layer)
//
//  Output:
//  - Only returns assistant's final messages (truncated by time order)
// ============================================================

/// <summary>
/// Minimal description of a participant (Actor) in AG-UI snapshot.
/// </summary>
public sealed record AgUiActor
{
    /// <summary>
    /// Actor raw id (Local/Proto use directly; Orleans will concatenate TypeName:RawId)
    /// </summary>
    public required string ActorId { get; init; }

    /// <summary>
    /// Actor type short name (only used for Orleans style id: "{TypeName}:{RawId}")
    /// </summary>
    public string? ActorTypeName { get; init; }

    /// <summary>
    /// UI grouping key (no business semantics; meaning determined by application layer)
    /// </summary>
    public required string LaneId { get; init; }

    /// <summary>
    /// Best-effort: Used to create/restore Actor when Actor is not registered (can be null)
    /// </summary>
    public Func<IGAgentActorManager, CancellationToken, Task<IGAgentActor?>>? CreateAsync { get; init; }
}

public sealed record AgUiMessageSnapshotOptions
{
    /// <summary>
    /// Thread/Session id (for messageId generation)
    /// </summary>
    public required string ThreadId { get; init; }

    /// <summary>
    /// Keep last N complete assistant messages
    /// </summary>
    public int MaxAssistantMessages { get; init; } = 60;

    /// <summary>
    /// Business layer lane routing: determines laneId based on step_id/metadata; defaults to defaultLaneId.
    /// </summary>
    public Func<string, IDictionary<string, string>, string, string>? ResolveLaneId { get; init; }
}

public static class AgUiBootstrap
{
    private static readonly Dictionary<string, string> EmptyMetadata = new(StringComparer.Ordinal);

    public static async Task<IReadOnlyList<AgUiMessage>> CollectAssistantMessagesAsync(
        IGAgentActorManager actorManager,
        IReadOnlyList<AgUiActor> actors,
        AgUiMessageSnapshotOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actorManager);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ThreadId) || actors.Count == 0)
            return Array.Empty<AgUiMessage>();

        if (options.MaxAssistantMessages <= 0)
            return Array.Empty<AgUiMessage>();

        static string DefaultResolveLane(string stepId, IDictionary<string, string> meta, string defaultLaneId) =>
            defaultLaneId;

        var resolveLane = options.ResolveLaneId ?? DefaultResolveLane;

        var steps = new Dictionary<StepKey, StepValue>();

        for (var i = 0; i < actors.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var spec = actors[i];
            if (string.IsNullOrWhiteSpace(spec.ActorId) || string.IsNullOrWhiteSpace(spec.LaneId))
                continue;

            var actor = await TryGetOrCreateActorAsync(actorManager, spec, ct);
            if (actor == null)
                continue;

            await MergeFromStateAsync(actor, spec.LaneId, resolveLane, steps, ct);
        }

        var ordered = steps
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.AssistantResponse))
            .Select(kv => new
            {
                kv.Key.LaneId,
                kv.Key.StepId,
                kv.Value.Timestamp,
                kv.Value.AssistantResponse
            })
            .OrderBy(x => x.Timestamp)
            .ToList();

        if (ordered.Count > options.MaxAssistantMessages)
            ordered = ordered.Skip(Math.Max(0, ordered.Count - options.MaxAssistantMessages)).ToList();

        var result = new List<AgUiMessage>(capacity: ordered.Count);
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var x in ordered)
        {
            ct.ThrowIfCancellationRequested();

            var messageId = BuildMessageId(options.ThreadId, x.LaneId, x.StepId);
            if (!emitted.Add(messageId))
                continue;

            result.Add(new AgUiMessage
            {
                Id = messageId,
                Role = "assistant",
                Content = x.AssistantResponse ?? string.Empty
            });
        }

        return result;
    }

    private readonly record struct StepKey(string LaneId, string StepId);

    private sealed class StepValue
    {
        // NOTE:
        // - This value is used for "latest-wins" merge when multiple assistant messages share the same step_id.
        // - Must start from MinValue so the *first* observed message is accepted, even when its timestamp is in the past
        //   (which is the normal case when we snapshot historical messages).
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.MinValue;
        public string? AssistantResponse { get; set; }
    }

    private static string BuildMessageId(string threadId, string laneId, string stepId)
    {
        // NOTE:
        // - stepId may contain ':' so the consumer should join the tail when parsing.
        // - laneId should avoid ':' for easier parsing on the UI side.
        return $"msg:{threadId}:{laneId}:{stepId}";
    }

    private static async Task<IGAgentActor?> TryGetOrCreateActorAsync(
        IGAgentActorManager actorManager,
        AgUiActor spec,
        CancellationToken ct)
    {
        var rawId = spec.ActorId.Trim();

        IGAgentActor? actor = null;
        if (!string.IsNullOrWhiteSpace(spec.ActorTypeName))
        {
            var orleansStyleId = $"{spec.ActorTypeName}:{rawId}";
            actor = await actorManager.GetActorAsync(orleansStyleId);
        }

        actor ??= await actorManager.GetActorAsync(rawId);
        if (actor != null)
            return actor;

        if (spec.CreateAsync == null)
            return null;

        try
        {
            return await spec.CreateAsync(actorManager, ct);
        }
        catch
        {
            return null;
        }
    }

    private static async Task MergeFromStateAsync(
        IGAgentActor actor,
        string defaultLaneId,
        Func<string, IDictionary<string, string>, string, string> resolveLane,
        Dictionary<StepKey, StepValue> steps,
        CancellationToken ct)
    {
        try
        {
            var state = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
            if (state?.History == null || state.History.Count == 0)
                return;

            foreach (var m in state.History)
            {
                ct.ThrowIfCancellationRequested();
                if (m == null) continue;
                if (m.Role != AevatarChatRole.Assistant) continue;

                var stepId = TryGetMeta(m, "step_id");
                if (string.IsNullOrWhiteSpace(stepId)) continue;

                var laneId = resolveLane(stepId, m.Metadata, defaultLaneId);
                if (string.IsNullOrWhiteSpace(laneId)) laneId = defaultLaneId;

                var content = (m.Content ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(content)) continue;

                var ts = m.Timestamp?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow;
                var key = new StepKey(laneId, stepId);
                if (!steps.TryGetValue(key, out var v))
                {
                    v = new StepValue();
                    steps[key] = v;
                }

                if (ts >= v.Timestamp)
                {
                    v.Timestamp = ts;
                    v.AssistantResponse = content;
                }
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static string? TryGetMeta(AevatarChatMessage msg, string key)
    {
        try
        {
            return msg.Metadata.TryGetValue(key, out var v) ? v : null;
        }
        catch
        {
            return null;
        }
    }
}
