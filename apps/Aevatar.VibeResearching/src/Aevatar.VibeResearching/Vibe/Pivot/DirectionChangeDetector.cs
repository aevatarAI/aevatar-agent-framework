using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeResearching.Vibe.Pivot.Models;

namespace VibeResearching.Vibe.Pivot;

/// <summary>
/// Detects research direction change intent from user messages using LLM-based classification.
/// </summary>
public sealed class DirectionChangeDetector : IDirectionChangeDetector
{
    private readonly ILLMProviderFactory _llmProviderFactory;
    private readonly PivotOptions _options;
    private readonly ILogger<DirectionChangeDetector> _logger;

    private const string SystemPrompt = """
        You are an expert at analyzing user messages in scientific research sessions.
        Your task is to determine if a user wants to CHANGE their research direction/topic.

        A direction change means the user wants to:
        - Switch to a completely different research topic
        - Abandon the current line of inquiry for a new one
        - Pivot the research focus significantly

        A direction change does NOT mean:
        - Asking clarifying questions about the current topic
        - Requesting more details or depth on the current focus
        - Minor refinements or adjustments to the approach
        - Follow-up questions within the same research area

        Respond ONLY with a JSON object (no markdown, no explanation) in this exact format:
        {
          "isDirectionChange": true/false,
          "confidence": 0.0-1.0,
          "newTopic": "extracted topic or null",
          "preserveAspects": ["aspect1", "aspect2"] or [],
          "needsClarification": true/false,
          "reasoning": "brief explanation"
        }

        Important:
        - "confidence" should reflect how certain you are about the classification
        - "newTopic" should be null if not a direction change or if ambiguous
        - "preserveAspects" lists any aspects the user explicitly wants to keep
        - "needsClarification" is true when the intent is unclear and needs user confirmation
        - Support both English and Chinese (中文) input naturally
        """;

    public DirectionChangeDetector(
        ILLMProviderFactory llmProviderFactory,
        IOptions<PivotOptions> options,
        ILogger<DirectionChangeDetector> logger)
    {
        _llmProviderFactory = llmProviderFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DirectionChangeIntent> DetectAsync(
        string sessionId,
        string messageId,
        string userMessage,
        string? currentDirection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId, nameof(sessionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId, nameof(messageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage, nameof(userMessage));

        _logger.LogDebug(
            "Detecting direction change intent for session {SessionId}, message {MessageId}",
            sessionId, messageId);

        try
        {
            var provider = _llmProviderFactory.GetDefaultProvider();
            var response = await ClassifyMessageAsync(provider, userMessage, currentDirection, cancellationToken);

            var intent = ParseResponse(sessionId, messageId, response);

            _logger.LogInformation(
                "Direction change detection result for session {SessionId}: IsChange={IsChange}, Confidence={Confidence:F2}",
                sessionId, intent.IsDirectionChange, intent.Confidence);

            return intent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect direction change intent for session {SessionId}", sessionId);

            // Return a safe default on error - no direction change detected
            return new DirectionChangeIntent
            {
                SessionId = sessionId,
                MessageId = messageId,
                IsDirectionChange = false,
                Confidence = 0.0,
                NewTopic = null,
                NeedsClarification = false
            };
        }
    }

    private async Task<string> ClassifyMessageAsync(
        IAevatarLLMProvider provider,
        string userMessage,
        string? currentDirection,
        CancellationToken cancellationToken)
    {
        var contextInfo = string.IsNullOrWhiteSpace(currentDirection)
            ? "No current research direction established."
            : $"Current research direction: {currentDirection}";

        var userPrompt = $"""
            {contextInfo}

            User message to analyze:
            "{userMessage}"

            Classify whether this message indicates a research direction change.
            """;

        var request = new AevatarLLMRequest
        {
            SystemPrompt = SystemPrompt,
            UserPrompt = userPrompt,
            Settings = new AevatarLLMSettings
            {
                Temperature = 0.1,  // Low temperature for consistent classification
                MaxTokens = 500
            }
        };

        var response = await provider.GenerateAsync(request, cancellationToken);
        return response.Content;
    }

    private DirectionChangeIntent ParseResponse(string sessionId, string messageId, string llmResponse)
    {
        try
        {
            // Clean the response - remove any markdown code blocks if present
            var cleanedResponse = CleanJsonResponse(llmResponse);

            var result = JsonSerializer.Deserialize<DetectionResult>(cleanedResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                _logger.LogWarning("Failed to parse LLM response as JSON, returning no-change default");
                return CreateDefaultIntent(sessionId, messageId);
            }

            // Apply threshold logic
            var isDirectionChange = result.IsDirectionChange;
            var needsClarification = result.NeedsClarification;

            // If confidence is between clarification and auto-trigger thresholds, request clarification
            if (isDirectionChange &&
                result.Confidence >= _options.ClarificationThreshold &&
                result.Confidence < _options.ConfidenceThreshold)
            {
                needsClarification = true;
            }

            // If confidence is below clarification threshold, don't treat as direction change
            if (isDirectionChange && result.Confidence < _options.ClarificationThreshold)
            {
                isDirectionChange = false;
            }

            return new DirectionChangeIntent
            {
                SessionId = sessionId,
                MessageId = messageId,
                IsDirectionChange = isDirectionChange,
                Confidence = Math.Clamp(result.Confidence, 0.0, 1.0),
                NewTopic = isDirectionChange ? result.NewTopic : null,
                PreserveAspects = result.PreserveAspects ?? [],
                NeedsClarification = needsClarification
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON parsing error for LLM response: {Response}", llmResponse);
            return CreateDefaultIntent(sessionId, messageId);
        }
    }

    private static string CleanJsonResponse(string response)
    {
        var trimmed = response.Trim();

        // Remove markdown code blocks if present
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed[3..];
        }

        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed[..^3];
        }

        return trimmed.Trim();
    }

    private static DirectionChangeIntent CreateDefaultIntent(string sessionId, string messageId)
    {
        return new DirectionChangeIntent
        {
            SessionId = sessionId,
            MessageId = messageId,
            IsDirectionChange = false,
            Confidence = 0.0,
            NewTopic = null,
            NeedsClarification = false
        };
    }

    /// <summary>
    /// Internal class for deserializing LLM classification response.
    /// </summary>
    private sealed class DetectionResult
    {
        public bool IsDirectionChange { get; set; }
        public double Confidence { get; set; }
        public string? NewTopic { get; set; }
        public List<string>? PreserveAspects { get; set; }
        public bool NeedsClarification { get; set; }
        public string? Reasoning { get; set; }
    }
}
