namespace SisyphusMaker.Services;

using System.Text.Json;
using System.Text.RegularExpressions;
using Aevatar.Agents.Cognitive.Core;
using Aevatar.Agents.Cognitive.Core.Strategies;
using Microsoft.Extensions.Options;
using SisyphusMaker.Dtos;

/// <summary>
/// Verification orchestrator: renders prompt template, builds task string,
/// invokes CognitiveStrategy with maker workflow.
/// </summary>
public sealed partial class VerificationService : IVerificationService
{
    private readonly CognitiveStrategy _cognitive;
    private readonly IPromptRenderer _renderer;
    private readonly MakerOptions _defaults;
    private readonly ILogger<VerificationService> _logger;

    public VerificationService(
        CognitiveStrategy cognitive,
        IPromptRenderer renderer,
        IOptions<MakerOptions> options,
        ILogger<VerificationService> logger)
    {
        _cognitive = cognitive;
        _renderer = renderer;
        _defaults = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<VerificationResponse> VerifyAsync(
        VerifyRequest request,
        Func<SseEvent, Task> onProgress,
        CancellationToken ct)
    {
        // 1. Render template with variables
        var variables = request.Variables ?? [];
        var userPrompt = _renderer.Render(request.Prompt.UserPromptTemplate, variables);

        // 2. Build task string for CognitiveStrategy (system prompt + rendered user prompt)
        var task = $"""
            ## System Instructions
            {request.Prompt.SystemPrompt}

            ## Task
            {userPrompt}
            """;

        // 3. Build ReasoningOptions from request config + defaults
        var config = request.Config;
        var workerCount = config?.WorkerCount ?? _defaults.WorkerCount;
        var consensusK = config?.ConsensusK ?? _defaults.ConsensusK;
        var maxRounds = config?.MaxRounds ?? _defaults.MaxRounds;
        var timeoutSeconds = config?.TimeoutSeconds ?? _defaults.TimeoutSeconds;

        var options = new ReasoningOptions
        {
            CognitiveWorkflow = "maker",
            CognitiveWorkerCount = workerCount,
            CognitiveConsensusK = consensusK,
            CognitiveMaxRounds = maxRounds,
            ProviderName = config?.Model,
            MaxDuration = TimeSpan.FromSeconds(timeoutSeconds),
        };

        // 4. Execute via CognitiveStrategy with progress forwarding
        var lastPhase = "";
        var progress = new Progress<ReasoningProgress>(rp =>
        {
            // Map CognitiveStrategy progress to SSE events
            var phase = rp.Phase ?? "";
            if (phase != lastPhase)
            {
                lastPhase = phase;
                _ = onProgress(new SseEvent("phase", new PhaseEvent(
                    phase,
                    rp.Message ?? "",
                    rp.ProgressPercent)));
            }

            // Forward vote round events
            if (rp.VoteRound > 0 && rp.StepType == "vote")
            {
                _ = onProgress(new SseEvent("vote_round", new CognitiveVoteEvent(
                    rp.VoteRound ?? 0,
                    rp.VoteK ?? 0,
                    rp.VoteCurrentVotes ?? 0,
                    rp.WinnerIsConsensus ?? false)));
            }

            // Forward worker completion events
            if (rp.Proposal is not null)
            {
                _ = onProgress(new SseEvent("worker_done", new CognitiveWorkerDoneEvent(
                    rp.TaskId ?? "worker",
                    rp.Proposal.ProposalId ?? "",
                    rp.Proposal.Success)));
            }
        });

        // MaxDuration handles the real timeout (returns partial result + deactivates coordinator).
        // CTS is a safety net: if the framework somehow hangs past MaxDuration, force-cancel.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds + 30));

        var result = await _cognitive.ExecuteAsync(task, options, progress, timeoutCts.Token);

        // 5. Build verification response from CognitiveStrategy result
        return BuildVerificationResponse(result, workerCount);
    }

    private static VerificationResponse BuildVerificationResponse(
        ReasoningResult result,
        int workerCount)
    {
        if (!result.Success)
        {
            return new VerificationResponse
            {
                IsPassed = false,
                Reason = result.Error ?? "Verification failed",
                Confidence = 0,
                FailureReason = result.Error ?? "Verification failed",
                VoteDetail = new VoteDetail
                {
                    WorkerCount = workerCount,
                    TotalRounds = 0,
                    ConsensusReached = false,
                    WorkerVotes = [],
                    Rounds = [],
                },
            };
        }

        var parsed = ParseLlmResponse(result.Content ?? "");

        // Extract voting detail from MakerTrace if available
        var (workerVotes, rounds) = ExtractVotingDetail(result.MakerTrace);
        var totalRounds = rounds.Count > 0
            ? rounds.Count
            : result.TotalLlmCalls > 0 ? result.TotalLlmCalls / workerCount : 1;

        return new VerificationResponse
        {
            IsPassed = parsed.Passed,
            Reason = parsed.Reason,
            Confidence = parsed.Confidence,
            FailureReason = parsed.Passed ? null : parsed.Reason,
            VoteDetail = new VoteDetail
            {
                WorkerCount = workerCount,
                TotalRounds = totalRounds,
                ConsensusReached = result.Success,
                WorkerVotes = workerVotes,
                Rounds = rounds,
            },
        };
    }

    /// <summary>
    /// Extracts voting detail from MakerTrace by walking the task tree
    /// and collecting all VotingSessions.
    /// </summary>
    private static (List<WorkerVote>, List<VoteRoundSummary>) ExtractVotingDetail(
        MakerTrace? trace)
    {
        if (trace?.RootTask is null)
            return ([], []);

        var sessions = new List<VotingSession>();
        CollectVotingSessions(trace.RootTask, sessions);

        if (sessions.Count == 0)
            return ([], []);

        // Build rounds from all voting sessions
        var rounds = new List<VoteRoundSummary>();
        var roundNum = 0;
        foreach (var session in sessions)
        {
            roundNum++;
            var distribution = session.Candidates
                .ToDictionary(c => c.Hash, c => c.Votes);
            rounds.Add(new VoteRoundSummary
            {
                Round = roundNum,
                Distribution = distribution,
                ConsensusReached = session.Winner is not null,
                WinnerHash = session.Winner?.Hash,
            });
        }

        // Build worker votes from the last session's candidates
        var lastSession = sessions[^1];
        var workerVotes = new List<WorkerVote>();
        var workerId = 0;
        foreach (var candidate in lastSession.Candidates)
        {
            var candidateParsed = ParseLlmResponse(candidate.Content ?? "");
            // Expand each candidate into individual worker votes
            for (var i = 0; i < candidate.Votes; i++)
            {
                workerVotes.Add(new WorkerVote
                {
                    WorkerId = workerId++,
                    VotedPass = candidateParsed.Passed,
                    Reason = candidateParsed.Reason,
                    Confidence = candidateParsed.Confidence,
                    ResponseHash = candidate.Hash,
                });
            }
        }

        return (workerVotes, rounds);
    }

    private static void CollectVotingSessions(TaskNode node, List<VotingSession> sessions)
    {
        sessions.AddRange(node.VotingSessions);
        foreach (var child in node.Children)
            CollectVotingSessions(child, sessions);
    }

    /// <summary>
    /// Parses an LLM response into structured verification data.
    /// Tries direct JSON parse, then code block extraction, then JSON search.
    /// Falls back to unparseable defaults on failure.
    /// </summary>
    internal static ParsedLlmResponse ParseLlmResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new ParsedLlmResponse(false, "(empty response)", 0.0);

        var parsed = TryParseJson(response);
        if (parsed is not null) return parsed;

        var codeBlockMatch = CodeBlockPattern().Match(response);
        if (codeBlockMatch.Success)
        {
            parsed = TryParseJson(codeBlockMatch.Groups[1].Value.Trim());
            if (parsed is not null) return parsed;
        }

        var jsonMatch = JsonObjectPattern().Match(response);
        if (jsonMatch.Success)
        {
            parsed = TryParseJson(jsonMatch.Value);
            if (parsed is not null) return parsed;
        }

        // CognitiveStrategy may return natural language — check for pass/fail keywords
        var lower = response.ToLowerInvariant();
        if (lower.Contains("\"passed\": true") || lower.Contains("\"passed\":true"))
            return new ParsedLlmResponse(true, response, 0.8);
        if (lower.Contains("\"passed\": false") || lower.Contains("\"passed\":false"))
            return new ParsedLlmResponse(false, response, 0.8);

        return new ParsedLlmResponse(false, response, 0.0);
    }

    private static ParsedLlmResponse? TryParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var passed = root.TryGetProperty("passed", out var passedProp)
                && passedProp.GetBoolean();

            var reason = root.TryGetProperty("reason", out var reasonProp)
                ? reasonProp.GetString() ?? string.Empty
                : string.Empty;

            var confidence = root.TryGetProperty("confidence", out var confProp)
                ? confProp.GetDouble()
                : 0.0;

            return new ParsedLlmResponse(passed, reason, confidence);
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex(@"```(?:json)?\s*\n?(.*?)\n?```", RegexOptions.Singleline)]
    private static partial Regex CodeBlockPattern();

    [GeneratedRegex(@"\{[^{}]*\}", RegexOptions.Singleline)]
    private static partial Regex JsonObjectPattern();

    internal sealed record ParsedLlmResponse(bool Passed, string Reason, double Confidence);
}
