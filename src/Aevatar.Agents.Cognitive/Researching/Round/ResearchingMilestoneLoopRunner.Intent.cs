using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Researching.Sessions;

namespace Aevatar.Agents.Cognitive.Researching.Round;

public sealed partial class ResearchingMilestoneLoopRunner
{
    // ============================================================
    //  User Intent Analysis (Dynamic User Input)
    // ============================================================

    /// <summary>
    /// Analyzes the user's intent from their interruption message.
    /// Returns the intent type and any relevant information.
    /// </summary>
    private async Task<UserIntentAnalysis> AnalyzeUserIntentAsync(
        ResearchSession session,
        InterruptionContext interruptionContext,
        string newMessage,
        string? providerOverride,
        CancellationToken ct)
    {
        try
        {
            var (verifier, verifierId) = await _runtime.GetVerifierAgentAsync(session.Id, providerOverride, ct);

            var analysisPrompt = $$"""
                # User Intent Analysis

                The user has interrupted an ongoing research session with a new message.
                Analyze the user's intent and categorize it into one of three types.

                **User's New Message**: {{newMessage}}

                **Research Progress Context**:
                - Interrupted at milestone: {{interruptionContext.InterruptedAtMilestoneIndex}}/{{interruptionContext.TotalMilestones}}
                - Completed milestones: {{interruptionContext.CompletedMilestones}}

                Respond in this exact JSON format:
                ```json
                {
                    "intentType": "direction_change" | "progress_inquiry" | "other",
                    "summary": "Brief summary of what the user wants",
                    "directionChangeDescription": "Only if intentType is direction_change: describe the new direction",
                    "suggestedNewMilestones": ["Only if direction_change: list of 1-3 new milestone descriptions"],
                    "systemReplyContent": "A friendly response to show to the user (1-2 sentences)"
                }
                ```

                **Intent Type Definitions**:
                - **direction_change**: User wants to change research focus, add new topics, modify goals, or pivot the research direction
                - **progress_inquiry**: User asks about current progress, status, what's been done, or time remaining
                - **other**: Any other message (general comments, encouragement, unrelated questions)

                Be strict in categorization. Only use "direction_change" if the user clearly wants to modify the research plan.
                """;

            var req = new Aevatar.Agents.AI.ChatRequest
            {
                Message = analysisPrompt,
                RequestId = Guid.NewGuid().ToString("N"),
                StageHint = "session:vibe:intent_analysis"
            };
            req.Context["agent_id"] = verifierId;

            var resp = await verifier.ChatAsync(req, ct);
            var content = resp.Content ?? string.Empty;

            return ParseIntentAnalysisResponse(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MilestoneLoop] Intent analysis failed, defaulting to 'other'");
            return new UserIntentAnalysis
            {
                IntentType = UserIntentType.Other,
                Summary = "Unable to analyze intent",
                SystemReplyContent = "I've received your message. Let me continue with the research."
            };
        }
    }

    private UserIntentAnalysis ParseIntentAnalysisResponse(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content[jsonStart..(jsonEnd + 1)];
                var parsed = System.Text.Json.JsonSerializer.Deserialize<IntentAnalysisJson>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed != null)
                {
                    var intentType = (parsed.IntentType ?? "other").ToLowerInvariant() switch
                    {
                        "direction_change" => UserIntentType.DirectionChange,
                        "progress_inquiry" => UserIntentType.ProgressInquiry,
                        _ => UserIntentType.Other
                    };

                    return new UserIntentAnalysis
                    {
                        IntentType = intentType,
                        Summary = parsed.Summary ?? "Intent analyzed",
                        DirectionChangeDescription = parsed.DirectionChangeDescription,
                        SuggestedNewMilestones = parsed.SuggestedNewMilestones,
                        SystemReplyContent = parsed.SystemReplyContent
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[MilestoneLoop] Failed to parse intent analysis JSON");
        }

        return new UserIntentAnalysis
        {
            IntentType = UserIntentType.Other,
            Summary = "Unable to parse intent",
            SystemReplyContent = "I've received your message and will continue with the research."
        };
    }

    /// <summary>
    /// Handles the analyzed user intent by emitting appropriate system replies
    /// and taking action based on intent type.
    /// </summary>
    private async Task HandleUserIntentAsync(
        ResearchSession session,
        InterruptionContext interruptionContext,
        UserIntentAnalysis intentAnalysis,
        string? providerOverride,
        Action<string> emitAssistantDelta,
        CancellationToken ct)
    {
        long Ts() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        switch (intentAnalysis.IntentType)
        {
            case UserIntentType.DirectionChange:
                _logger.LogInformation("[MilestoneLoop] User requested direction change: {Desc}",
                    intentAnalysis.DirectionChangeDescription);

                // Emit system reply acknowledging direction change
                session.Events.Publish(new CustomEvent
                {
                    Timestamp = Ts(),
                    Name = "aevatar.scientific.system_reply",
                    Value = new
                    {
                        sessionId = session.Id,
                        messageType = "direction_change",
                        content = intentAnalysis.SystemReplyContent ??
                            "Got it! I'll adjust the research direction based on your feedback."
                    }
                });

                // Modify pending milestones based on user's new direction
                await ModifyPendingMilestonesAsync(
                    session,
                    interruptionContext,
                    intentAnalysis,
                    providerOverride,
                    emitAssistantDelta,
                    ct);

                break;

            case UserIntentType.ProgressInquiry:
                _logger.LogInformation("[MilestoneLoop] User requested progress inquiry");

                // Generate progress summary
                var progressContent = GenerateProgressSummary(interruptionContext);

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = Ts(),
                    Name = "aevatar.scientific.system_reply",
                    Value = new
                    {
                        sessionId = session.Id,
                        messageType = "progress_inquiry",
                        content = progressContent
                    }
                });

                emitAssistantDelta($"\n## Progress Update\n\n{progressContent}\n\n");
                break;

            case UserIntentType.Other:
            default:
                _logger.LogInformation("[MilestoneLoop] User message categorized as 'other', continuing research");

                session.Events.Publish(new CustomEvent
                {
                    Timestamp = Ts(),
                    Name = "aevatar.scientific.system_reply",
                    Value = new
                    {
                        sessionId = session.Id,
                        messageType = "other",
                        content = intentAnalysis.SystemReplyContent ??
                            "Thanks for your message! I haven't detected a specific research direction change, so I'll continue with the current plan."
                    }
                });
                break;
        }
    }

    private static string GenerateProgressSummary(InterruptionContext ctx)
    {
        var completed = ctx.CompletedMilestones;
        var total = ctx.TotalMilestones;
        var percent = total > 0 ? (completed * 100 / total) : 0;

        return $"**Research Progress**: {completed}/{total} milestones completed ({percent}% done)\n" +
               $"Currently working on milestone {ctx.InterruptedAtMilestoneIndex}.\n" +
               "The research will continue from where it left off.";
    }

    private sealed class IntentAnalysisJson
    {
        public string? IntentType { get; init; }
        public string? Summary { get; init; }
        public string? DirectionChangeDescription { get; init; }
        public List<string>? SuggestedNewMilestones { get; init; }
        public string? SystemReplyContent { get; init; }
    }
}

/// <summary>
/// User intent type detected from interruption message.
/// </summary>
internal enum UserIntentType
{
    /// <summary>User wants to change research direction, modify or add milestones.</summary>
    DirectionChange,

    /// <summary>User wants to know about current progress.</summary>
    ProgressInquiry,

    /// <summary>Other intent that doesn't affect research execution.</summary>
    Other
}

/// <summary>
/// Result of analyzing user intent from an interruption message.
/// </summary>
internal sealed class UserIntentAnalysis
{
    public UserIntentType IntentType { get; init; } = UserIntentType.Other;
    public string Summary { get; init; } = string.Empty;
    public string? DirectionChangeDescription { get; init; }
    public List<string>? SuggestedNewMilestones { get; init; }
    public string? SystemReplyContent { get; init; }
}

