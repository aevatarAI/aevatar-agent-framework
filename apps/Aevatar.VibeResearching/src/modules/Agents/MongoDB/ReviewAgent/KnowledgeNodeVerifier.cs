using System.Diagnostics;
using System.Text.Json;
using Aevatar.Agents.Cognitive.Core;
using Aevatar.Agents.Cognitive.Core.Strategies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.VibeResearching.Agents.ReviewAgent;

namespace Aevatar.VibeResearching.Agents.MongoDB.ReviewAgent;

/// <summary>
/// Implements knowledge node verification using the CognitiveStrategy.
/// Uses the maker workflow for multi-agent consensus validation.
/// </summary>
public sealed class KnowledgeNodeVerifier : IKnowledgeNodeVerifier
{
    private const string VerificationWorkflow = "maker";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CognitiveStrategy _cognitive;
    private readonly IOptionsMonitor<ReviewAgentOptions> _optionsMonitor;
    private readonly ILogger<KnowledgeNodeVerifier> _logger;

    public KnowledgeNodeVerifier(
        CognitiveStrategy cognitive,
        IOptionsMonitor<ReviewAgentOptions> optionsMonitor,
        ILogger<KnowledgeNodeVerifier> logger)
    {
        _cognitive = cognitive ?? throw new ArgumentNullException(nameof(cognitive));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<VerificationResult> VerifyAsync(
        VerificationInput input,
        IProgress<VerificationProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var stopwatch = Stopwatch.StartNew();
        var options = _optionsMonitor.CurrentValue;

        _logger.LogDebug(
            "Starting verification for node {NodeId}: {NodeLabel}",
            input.NodeId,
            input.NodeLabel);

        // Handle empty/malformed content
        if (string.IsNullOrWhiteSpace(input.ExplanationContent))
        {
            return new VerificationResult
            {
                Passed = false,
                Result = ReviewResult.Failed,
                FailureReason = "Invalid or missing explanation content",
                Duration = stopwatch.Elapsed
            };
        }

        try
        {
            var task = BuildVerificationPrompt(input);
            var reasoningOptions = new ReasoningOptions
            {
                ProviderName = input.LLMProviderName ?? options.LLMProviderName,
                CognitiveWorkflow = VerificationWorkflow,
                StepTimeout = TimeSpan.FromSeconds(input.TimeoutSeconds ?? options.PerNodeTimeoutSeconds),
                Context = new Dictionary<string, string>
                {
                    ["node_id"] = input.NodeId,
                    ["purpose"] = "knowledge_node_verification"
                }
            };

            // Adapt progress reporting
            IProgress<ReasoningProgress>? adaptedProgress = null;
            if (progress != null)
            {
                adaptedProgress = new Progress<ReasoningProgress>(rp =>
                {
                    // Forward streaming tokens to our progress
                    // NOTE: CognitiveStrategy's StreamingToken.Token is empty, use AccumulatedContent instead
                    if (rp.StreamingToken != null)
                    {
                        // Use AccumulatedContent if Token is empty (DSL workflow behavior)
                        var tokenContent = !string.IsNullOrEmpty(rp.StreamingToken.Token)
                            ? rp.StreamingToken.Token
                            : rp.StreamingToken.AccumulatedContent ?? string.Empty;

                        if (!string.IsNullOrEmpty(tokenContent))
                        {
                            progress.Report(new VerificationProgress
                            {
                                AgentId = rp.StreamingToken.WorkerId ?? "worker",
                                AgentRole = rp.StreamingToken.WorkerId?.Contains("coordinator") == true ? "coordinator" : "worker",
                                Token = tokenContent,
                                IsComplete = rp.StreamingToken.IsLastToken
                            });
                        }
                    }
                    else if (!string.IsNullOrEmpty(rp.AssistantResponse))
                    {
                        progress.Report(new VerificationProgress
                        {
                            AgentId = "coordinator",
                            AgentRole = "coordinator",
                            Token = rp.AssistantResponse,
                            IsComplete = true
                        });
                    }
                });
            }

            var result = await _cognitive.ExecuteAsync(task, reasoningOptions, adaptedProgress, ct);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
            {
                return new VerificationResult
                {
                    Passed = false,
                    Result = ReviewResult.Failed,
                    FailureReason = result.Error ?? "Verification failed with no content",
                    VerificationContent = result.Content,
                    Error = result.Error,
                    Duration = stopwatch.Elapsed
                };
            }

            // Parse the verification response
            var verificationResponse = ParseVerificationResponse(result.Content);

            _logger.LogDebug(
                "Verification for node {NodeId} completed: {Result} in {Duration}ms",
                input.NodeId,
                verificationResponse.Passed ? "PASSED" : "FAILED",
                stopwatch.ElapsedMilliseconds);

            return new VerificationResult
            {
                Passed = verificationResponse.Passed,
                Result = verificationResponse.Passed ? ReviewResult.Passed : ReviewResult.Failed,
                FailureReason = verificationResponse.Reason,
                VerificationContent = result.Content,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            return new VerificationResult
            {
                Passed = false,
                Result = ReviewResult.Skipped,
                FailureReason = "Verification timed out or was cancelled",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verification failed for node {NodeId}", input.NodeId);
            return new VerificationResult
            {
                Passed = false,
                Result = ReviewResult.Failed,
                FailureReason = "Verification error",
                Error = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    private static string BuildVerificationPrompt(VerificationInput input)
    {
        var dependencyContext = input.DependencyLabels.Count > 0
            ? $"\n\nThis knowledge node depends on the following parent nodes:\n{string.Join("\n", input.DependencyLabels.Select((d, i) => $"- {input.DependencyIds[i]}: {d}"))}"
            : "\n\nThis is a root knowledge node with no dependencies.";

        return $$"""
            You are a knowledge validator agent. Your task is to verify whether a knowledge node in a research derivation graph is still valid and consistent.

            ## Knowledge Node to Verify
            **ID**: {{input.NodeId}}
            **Label**: {{input.NodeLabel}}

            **Explanation Content**:
            {{input.ExplanationContent}}
            {{dependencyContext}}

            ## Validation Criteria
            1. **Internal Consistency**: The explanation should be logically consistent and coherent
            2. **Factual Accuracy**: Claims should be factually supportable (if verifiable)
            3. **Dependency Alignment**: The node should be consistent with its parent dependencies
            4. **Completeness**: The explanation should adequately support the node's label/claim

            ## Response Format
            Respond with STRICT JSON only (no markdown, no code fences):
            {
              "passed": true|false,
              "reason": "Brief explanation of your decision",
              "confidence": 0.0-1.0
            }

            If PASSED: The knowledge node is valid and consistent.
            If FAILED: The knowledge node has issues that warrant deactivation.

            Analyze the knowledge node and respond:
            """;
    }

    private static VerificationResponseJson ParseVerificationResponse(string content)
    {
        // Try to extract JSON from the response
        var trimmed = content.Trim();

        // Handle markdown code blocks
        if (trimmed.StartsWith("```"))
        {
            var lines = trimmed.Split('\n');
            var jsonLines = new List<string>();
            var inJson = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("```json") || line.StartsWith("```"))
                {
                    inJson = !inJson;
                    continue;
                }
                if (inJson)
                {
                    jsonLines.Add(line);
                }
            }
            trimmed = string.Join("\n", jsonLines).Trim();
        }

        // Try direct parse
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<VerificationResponseJson>(trimmed, JsonOptions);
                if (parsed != null)
                {
                    return parsed;
                }
            }
            catch
            {
                // Fall through to extraction
            }
        }

        // Try to extract JSON from anywhere in the response
        var lastBrace = trimmed.LastIndexOf('}');
        if (lastBrace >= 0)
        {
            for (var start = lastBrace; start >= 0; start--)
            {
                if (trimmed[start] == '{')
                {
                    var candidate = trimmed.Substring(start, lastBrace - start + 1);
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<VerificationResponseJson>(candidate, JsonOptions);
                        if (parsed != null)
                        {
                            return parsed;
                        }
                    }
                    catch
                    {
                        // Continue searching
                    }
                }
            }
        }

        // Default: treat as failed if we can't parse
        return new VerificationResponseJson
        {
            Passed = false,
            Reason = "Unable to parse verification response",
            Confidence = 0
        };
    }

    private sealed class VerificationResponseJson
    {
        public bool Passed { get; init; }
        public string? Reason { get; init; }
        public float Confidence { get; init; }
    }
}
