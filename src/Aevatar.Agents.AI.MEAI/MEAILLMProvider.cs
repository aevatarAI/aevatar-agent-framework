using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Collections;
using System.Text.Json.Nodes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.Telemetry;
using Aevatar.Agents.AI.WithTool;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

// ReSharper disable InconsistentNaming
namespace Aevatar.Agents.AI.MEAI;

public sealed class MEAILLMProvider : AevatarLLMProviderBase
{
    private readonly IChatClient _chatClient;
    private readonly LLMProviderConfig _config;
    private readonly ILogger _logger;
    private readonly LLMCallPolicy _policy;
    
    // ------------------------------------------------------------
    // DeepSeek thinking-mode compatibility
    //
    // DeepSeek `deepseek-reasoner` requires `reasoning_content` to be
    // present on assistant messages when using tool calls. If missing,
    // the API returns 400:
    //   "Missing `reasoning_content` field in the assistant message..."
    //
    // We keep this provider-specific to avoid breaking standard OpenAI.
    // ------------------------------------------------------------
    private bool ForceAssistantReasoningContentField =>
        !string.IsNullOrWhiteSpace(_config.Model) &&
        _config.Model.Contains("deepseek-reasoner", StringComparison.OrdinalIgnoreCase);

    public MEAILLMProvider(
        IChatClient chatClient,
        LLMProviderConfig config,
        ILogger logger,
        LLMCallPolicy? policy = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policy = policy ?? LLMCallPolicy.Default;
    }

    protected override LLMCallPolicy Policy => _policy;
    protected override ILogger? Logger => _logger;
    protected override string ProviderName => $"MEAI:{_config.Model}";

    /// <summary>
    /// Get model info - MEAI supports streaming for most models.
    /// </summary>
    public override Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AevatarModelInfo
        {
            Name = _config.Model,
            MaxTokens = _config.MaxTokens,
            SupportsStreaming = true,
            SupportsFunctions = true
        });
    }

    protected override async Task<AevatarLLMResponse> GenerateCoreAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = LLMTelemetry.StartGeneration(_config.Model);
        var sw = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("[MEAI] Calling model: {Model}, Endpoint: {Endpoint}",
                _config.Model, _config.Endpoint ?? "default");
            LLMTelemetry.RequestCount.Add(1, new KeyValuePair<string, object?>("model", _config.Model));

            var messageHistory = request.Messages?.Select(m => (
                Role: m.Role == AevatarChatRole.User ? "user" : "assistant",
                Content: m.Content ?? ""
            ));
            LLMTelemetry.RecordRequest(activity, request.SystemPrompt, request.UserPrompt, messageHistory);

            var messages = BuildChatMessages(request);
            var options = BuildChatOptions(request);

            _logger.LogInformation("[MEAI] Sending request with {MsgCount} messages", messages.Count);
            var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
            _logger.LogInformation("[MEAI] Got response: Text='{Text}', MsgCount={MsgCount}",
                response.Text?.Substring(0, Math.Min(100, response.Text?.Length ?? 0)) ?? "(null)",
                response.Messages?.Count ?? 0);

            sw.Stop();
            activity?.SetTag("llm.duration_ms", sw.ElapsedMilliseconds);
            LLMTelemetry.ResponseTime.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("model", _config.Model));

            var result = CreateAevatarLLMResponse(response);
            LLMTelemetry.RecordResponse(activity, result.Content);

            if (result.Usage != null)
            {
                activity?.SetTag("llm.tokens.prompt", result.Usage.PromptTokens);
                activity?.SetTag("llm.tokens.completion", result.Usage.CompletionTokens);
                LLMTelemetry.InputTokens.Add(result.Usage.PromptTokens, new KeyValuePair<string, object?>("model", _config.Model));
                LLMTelemetry.OutputTokens.Add(result.Usage.CompletionTokens, new KeyValuePair<string, object?>("model", _config.Model));
            }

            return result;
        }
        catch (OperationCanceledException ex)
        {
            // This can be:
            // - Provider/network timeout
            // - External cancellation (HTTP request aborted, shutdown, etc.)
            //
            // Keep it low-noise: cancellation is often user-driven (client disconnect) and will be
            // surfaced upstream anyway.
            _logger.LogDebug(ex, "[MEAI] Model call canceled/timeout: {Model} - {Message}", _config.Model, ex.Message);
            LLMTelemetry.RecordError(activity, ex);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MEAI] Error calling model {Model}: {Message}", _config.Model, ex.Message);
            LLMTelemetry.RecordError(activity, ex);
            throw;
        }
    }

    /// <summary>
    /// Maps Aevatar chat role to Microsoft.Extensions.AI chat role.
    /// </summary>
    private static ChatRole MapToMEAIChatRole(AevatarChatRole role) => role switch
    {
        AevatarChatRole.System => ChatRole.System,
        AevatarChatRole.User => ChatRole.User,
        AevatarChatRole.Assistant => ChatRole.Assistant,
        AevatarChatRole.Tool => ChatRole.Tool,
        _ => ChatRole.User
    };

    /// <summary>
    /// Builds chat messages from request.
    /// </summary>
    private List<ChatMessage> BuildChatMessages(AevatarLLMRequest request)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));

        if (request.Messages?.Count > 0)
        {
            // ------------------------------------------------------------
            //  Tool calling protocol guard (OpenAI-compatible)
            //
            //  Some providers enforce strict ordering:
            //  - A tool message must reference a preceding assistant message with tool_calls.
            //
            //  When history is compacted/truncated, it's possible to end up with an orphan
            //  tool result message (role=tool) without its matching tool_calls message.
            //  That yields HTTP 400:
            //    "Messages with role 'tool' must be a response to a preceding message with 'tool_calls'"
            //
            //  We keep this best-effort and only skip clearly invalid tool messages.
            // ------------------------------------------------------------
            HashSet<string>? pendingToolCallIds = null;

            foreach (var msg in request.Messages)
            {
                var role = MapToMEAIChatRole(msg.Role);

                if (role == ChatRole.Assistant && msg.ToolCalls.Count > 0)
                {
                    pendingToolCallIds = new HashSet<string>(
                        msg.ToolCalls
                            .Select(tc => (tc?.Id ?? string.Empty).Trim())
                            .Where(id => id.Length > 0),
                        StringComparer.Ordinal);

                    messages.Add(CreateChatMessage(msg));
                    continue;
                }

                if (role == ChatRole.Tool)
                {
                    var callId = (msg.ToolResult?.ToolCallId ?? string.Empty).Trim();
                    if (callId.Length == 0 ||
                        pendingToolCallIds == null ||
                        !pendingToolCallIds.Contains(callId))
                    {
                        _logger.LogWarning(
                            "[MEAI] Skipping orphan tool message (tool_call_id={ToolCallId}). Missing matching preceding tool_calls.",
                            callId);
                        continue;
                    }

                    // Consume the id (supports multi-tool-call in one assistant message).
                    pendingToolCallIds.Remove(callId);

                    messages.Add(CreateChatMessage(msg));
                    continue;
                }

                // Any other role breaks the "tool_calls -> tool results" block.
                pendingToolCallIds = null;
                messages.Add(CreateChatMessage(msg));
            }
        }

        if (!string.IsNullOrEmpty(request.UserPrompt))
            messages.Add(new ChatMessage(ChatRole.User, request.UserPrompt));

        return messages;
    }

    /// <summary>
    /// Creates a ChatMessage and populates additional properties (like reasoning_content) if present.
    /// </summary>
    private ChatMessage CreateChatMessage(AevatarChatMessage msg)
    {
        var role = MapToMEAIChatRole(msg.Role);
        
        // ------------------------------------------------------------
        //  Tool calling: preserve structured tool call / tool result
        //
        //  Without this, the model may not associate tool results with
        //  its original tool call, and can get stuck requesting the same
        //  tool repeatedly (infinite tool loop).
        // ------------------------------------------------------------
        ChatMessage chatMsg;
        if (role == ChatRole.Assistant && msg.ToolCalls.Count > 0)
        {
            var contents = new List<AIContent>();
            foreach (var tc in msg.ToolCalls)
            {
                var callId = !string.IsNullOrWhiteSpace(tc.Id)
                    ? tc.Id
                    : Guid.NewGuid().ToString("N");

                var toolName = tc.ToolName ?? string.Empty;

                Dictionary<string, object?> args;
                if (string.IsNullOrWhiteSpace(tc.Arguments))
                {
                    args = new Dictionary<string, object?>();
                }
                else
                {
                    try
                    {
                        args = JsonSerializer.Deserialize<Dictionary<string, object?>>(tc.Arguments) ??
                               new Dictionary<string, object?>();
                    }
                    catch
                    {
                        // Best-effort: if arguments can't be parsed, still surface the tool call.
                        args = new Dictionary<string, object?>();
                    }
                }

                contents.Add(new FunctionCallContent(callId, toolName, args));
            }

            chatMsg = new ChatMessage(role, contents);
        }
        else if (role == ChatRole.Tool && msg.ToolResult != null && !string.IsNullOrWhiteSpace(msg.ToolResult.ToolCallId))
        {
            var contents = new List<AIContent>
            {
                new FunctionResultContent(msg.ToolResult.ToolCallId, msg.ToolResult.Content ?? string.Empty)
            };

            chatMsg = new ChatMessage(role, contents);
        }
        else
        {
            chatMsg = new ChatMessage(role, msg.Content ?? string.Empty);
        }
        
        // If caller captured reasoning_content, pass it through (including empty string if present).
        if (msg.Metadata?.TryGetValue("reasoning_content", out var reasoning) == true)
        {
            chatMsg.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            chatMsg.AdditionalProperties["reasoning_content"] = reasoning ?? string.Empty;
        }

        // DeepSeek `deepseek-reasoner` tool-calls mode requires the field to exist on assistant messages,
        // even when empty. We only enforce this for DeepSeek to avoid breaking OpenAI-compatible servers
        // that reject unknown fields.
        if (ForceAssistantReasoningContentField && role == ChatRole.Assistant)
        {
            chatMsg.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            if (!chatMsg.AdditionalProperties.ContainsKey("reasoning_content"))
            {
                chatMsg.AdditionalProperties["reasoning_content"] = string.Empty;
            }
        }

        return chatMsg;
    }

    /// <summary>
    /// Build chat options including temperature, max tokens, and tools.
    /// </summary>
    private ChatOptions BuildChatOptions(AevatarLLMRequest request)
    {
        var options = new ChatOptions
        {
            MaxOutputTokens = request.Settings?.MaxTokens ?? _config.MaxTokens,
            ModelId = _config.Model
        };

        if (request.Functions is { Count: > 0 })
        {
            var aiTools = ConvertFunctionsToAITools(request.Functions);
            if (aiTools.Count > 0)
            {
                options.Tools = aiTools;
                _logger.LogInformation("Added {Count} tools to ChatOptions", aiTools.Count);
            }
        }

        return options;
    }

    /// <summary>
    /// Convert Aevatar function definitions to Microsoft.Extensions.AI tools.
    /// </summary>
    private List<AITool> ConvertFunctionsToAITools(IList<AevatarFunctionDefinition> functions)
    {
        var aiTools = new List<AITool>();
        foreach (var func in functions)
        {
            var schema = ConvertParametersToJsonSchema(func.Parameters);
            var aiFunc = new DelegatingAIFunction(
                func.Name,
                func.Description,
                schema,
                (_, _) => Task.FromResult<object>(string.Format(ToolConstants.FunctionCalledMessageFormat, func.Name)));

            aiTools.Add(aiFunc);
        }

        return aiTools;
    }

    /// <summary>
    /// Custom AIFunction implementation that wraps our handler and exposes custom JsonSchema
    /// </summary>
    private sealed class DelegatingAIFunction : AIFunction
    {
        private readonly Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<object>> _handler;
        private readonly string _name;
        private readonly string _description;
        private readonly JsonElement _jsonSchema;

        public DelegatingAIFunction(
            string name,
            string description,
            JsonElement jsonSchema,
            Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<object>> handler)
        {
            _handler = handler;
            _name = name;
            _description = description;
            _jsonSchema = jsonSchema;
        }

        public override string Name => _name;
        public override string Description => _description;
        public override JsonElement JsonSchema => _jsonSchema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            return await _handler(arguments, cancellationToken);
        }
    }

    private static JsonElement ConvertParametersToJsonSchema(Dictionary<string, AevatarParameterDefinition> parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var param in parameters)
        {
            var paramDef = param.Value;
            var property = new JsonObject
            {
                ["type"] = paramDef.Type,
                ["description"] = paramDef.Description
            };

            if (paramDef.Enum != null && paramDef.Enum.Count > 0)
            {
                var enumArray = new JsonArray();
                foreach (var val in paramDef.Enum)
                {
                    enumArray.Add(val);
                }

                property["enum"] = enumArray;
            }

            properties[param.Key] = property;

            if (paramDef.Required)
            {
                required.Add(param.Key);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };

        return JsonSerializer.Deserialize<JsonElement>(schema.ToJsonString());
    }

    /// <summary>
    /// Unwraps function arguments that may be wrapped in a "_" key.
    /// </summary>
    private IDictionary<string, object?>? UnwrapFunctionArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || arguments.Count != 1 || !arguments.TryGetValue("_", out var wrappedValue))
            return arguments;

        if (wrappedValue is JsonElement wrappedElement &&
            wrappedElement.ValueKind == JsonValueKind.Object)
        {
            _logger.LogDebug("Unwrapping arguments from '_' key (JsonElement)");
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(wrappedElement.GetRawText());
        }

        if (wrappedValue is Dictionary<string, object?> wrappedDict)
        {
            _logger.LogDebug("Unwrapping arguments from '_' key (Dictionary)");
            return wrappedDict;
        }

        return arguments;
    }

    /// <summary>
    /// Processes function calls from the chat response.
    /// </summary>
    private AevatarFunctionCall? ProcessFunctionCalls(Microsoft.Extensions.AI.ChatResponse response)
    {
        if (response.Messages?.Count == 0)
            return null;

        foreach (var message in response.Messages!)
        {
            if (message.Contents?.Count == 0)
                continue;

            foreach (var content in message.Contents!)
            {
                if (content is not FunctionCallContent functionCall)
                    continue;

                _logger.LogDebug("Found function call: {FunctionName} with {ArgCount} arguments",
                    functionCall.Name, functionCall.Arguments?.Count ?? 0);

                var unwrappedArguments = UnwrapFunctionArguments(functionCall.Arguments);

                return new AevatarFunctionCall
                {
                    CallId = functionCall.CallId,
                    Name = functionCall.Name,
                    Arguments = unwrappedArguments != null
                        ? JsonSerializer.Serialize(unwrappedArguments)
                        : "{}"
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Creates AevatarLLMResponse from Microsoft.Extensions.AI ChatResponse.
    /// </summary>
    private AevatarLLMResponse CreateAevatarLLMResponse(Microsoft.Extensions.AI.ChatResponse response)
    {
        var content = response.Text;

        _logger.LogInformation(
            "[MEAI] ChatResponse structure: Text='{Text}', MessageCount={MsgCount}, ModelId={ModelId}",
            content ?? "(null)",
            response.Messages?.Count ?? 0,
            response.ModelId ?? "(null)");

        if (response.Messages?.Count > 0)
        {
            try
            {
                var messageList = response.Messages.ToList();
                _logger.LogInformation("[MEAI] Materialized {Count} messages", messageList.Count);

                for (var i = 0; i < messageList.Count; i++)
                {
                    var msg = messageList[i];
                    _logger.LogInformation(
                        "[MEAI] Message[{Index}]: Role={Role}, Text='{Text}', ContentsCount={ContentsCount}",
                        i,
                        msg.Role,
                        msg.Text ?? "(null)",
                        msg.Contents?.Count ?? 0);

                    if (msg.Contents != null)
                    {
                        var contentList = msg.Contents.ToList();
                        for (var j = 0; j < contentList.Count; j++)
                        {
                            var c = contentList[j];
                            var rawRepr = c.RawRepresentation?.ToString() ?? "(no raw)";
                            _logger.LogInformation("[MEAI] Content[{Index}]: Type={Type}, Text={Text}, Raw={Raw}",
                                j,
                                c.GetType().Name,
                                c is TextContent tc ? tc.Text ?? "(null)" : "(not text)",
                                rawRepr.Length > 200 ? rawRepr[..200] + "..." : rawRepr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MEAI] Error iterating messages: {Message}", ex.Message);
            }
        }

        if (string.IsNullOrEmpty(content) && response.Messages?.Count > 0)
        {
            content = ExtractTextFromMessages(response.Messages.ToList());
            _logger.LogInformation("[MEAI] Fallback extraction result: '{Content}'", content ?? "(null)");
        }

        var result = new AevatarLLMResponse
        {
            Content = content ?? string.Empty,
            ModelName = response.ModelId ?? _config.Model,
            AevatarStopReason = AevatarStopReason.Complete,
            Usage = CreateTokenUsage(
                (int?)response.Usage?.InputTokenCount,
                (int?)response.Usage?.OutputTokenCount,
                (int?)response.Usage?.TotalTokenCount)
        };

        var functionCall = ProcessFunctionCalls(response);
        if (functionCall != null)
        {
            result.AevatarFunctionCall = functionCall;
            result.Content = string.Format(ToolConstants.FunctionCalledMessageFormat, functionCall.Name);
        }

        return result;
    }

    /// <summary>
    /// Extract text content from ChatMessage collection.
    /// </summary>
    private static string? ExtractTextFromMessages(IReadOnlyList<ChatMessage> messages)
    {
        var sb = new StringBuilder();

        foreach (var message in messages)
        {
            if (message.Role != ChatRole.Assistant)
                continue;

            if (!string.IsNullOrEmpty(message.Text))
            {
                sb.Append(message.Text);
                continue;
            }

            if (message.Contents == null)
                continue;

            foreach (var part in message.Contents)
            {
                if (part is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    sb.Append(textContent.Text);
                }
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    protected override async IAsyncEnumerable<AevatarLLMToken> GenerateStreamCoreAsync(
        AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = LLMTelemetry.StartStreaming(_config.Model);
        var sw = Stopwatch.StartNew();
        var chunkIndex = 0;
        var totalTokens = 0;
        var firstTokenReceived = false;
        var responseBuilder = new StringBuilder();

        _logger.LogDebug("Generating streaming response using MEAI provider: {Model}", _config.Model);
        LLMTelemetry.RequestCount.Add(1,
            new KeyValuePair<string, object?>("model", _config.Model),
            new KeyValuePair<string, object?>("streaming", true));

        var messageHistory = request.Messages?.Select(m => (
            Role: m.Role == AevatarChatRole.User ? "user" : "assistant",
            Content: m.Content ?? ""
        ));
        LLMTelemetry.RecordRequest(activity, request.SystemPrompt, request.UserPrompt, messageHistory);

        var messages = BuildChatMessages(request);
        var options = BuildChatOptions(request);

        await foreach (var chatUpdate in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            // ------------------------------------------------------------
            //  Streaming + tools (IMPORTANT)
            //
            //  Some providers (e.g., DeepSeek via OpenAI-compatible API) may emit a FunctionCall
            //  in streaming updates. If we don't surface it as AevatarFunctionCall, the agent will
            //  stop mid-answer (model expects tool execution).
            //
            //  We detect function calls best-effort and hand control back to AIGAgentBase:
            //    - AIGAgentBase.ChatStreamAsync will execute the tool loop non-streaming
            //    - then emit the final answer as a single chunk
            // ------------------------------------------------------------
            var functionCall = TryExtractStreamingFunctionCall(chatUpdate);
            if (functionCall != null)
            {
                yield return new AevatarLLMToken
                {
                    AevatarFunctionCall = functionCall,
                    Content = string.Empty,
                    IsComplete = false
                };

                yield break;
            }

            var chunk = ExtractStreamingText(chatUpdate);
            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }

            if (!firstTokenReceived)
            {
                firstTokenReceived = true;
                var ttft = sw.ElapsedMilliseconds;
                LLMTelemetry.RecordFirstToken(activity, ttft);
                _logger.LogDebug("First token received in {TTFT}ms", ttft);
            }

            var estimatedTokens = Math.Max(1, chunk.Length / 4);
            totalTokens += estimatedTokens;
            chunkIndex++;

            var reasoning = ExtractReasoningContent(chatUpdate);
            
            responseBuilder.Append(chunk);
            LLMTelemetry.AddStreamingChunk(activity, chunkIndex, chunk.Length);

            yield return new AevatarLLMToken
            {
                Content = chunk,
                ReasoningContent = reasoning,
                IsComplete = false
            };
        }

        sw.Stop();
        LLMTelemetry.CompleteStreaming(activity, chunkIndex, totalTokens, sw.ElapsedMilliseconds);
        LLMTelemetry.RecordStreamingResponse(activity, responseBuilder.ToString());
        _logger.LogDebug("Streaming complete: {Chunks} chunks, ~{Tokens} tokens in {Duration}ms",
            chunkIndex, totalTokens, sw.ElapsedMilliseconds);

        yield return new AevatarLLMToken { Content = string.Empty, IsComplete = true };
    }

    /// <summary>
    /// Best-effort extraction of function call from streaming updates.
    /// </summary>
    private AevatarFunctionCall? TryExtractStreamingFunctionCall(object chatUpdate)
    {
        try
        {
            // ------------------------------------------------------------
            //  Common shapes (OpenAI-compatible streaming)
            // ------------------------------------------------------------

            // 1) update.Message.Contents
            var fromMessage = TryExtractFunctionCallFromMessage(StreamingPropertyCache.GetValue(chatUpdate, "Message"));
            if (fromMessage != null) return fromMessage;

            // 2) update.Delta (some SDKs surface tool calls on Delta)
            var delta = StreamingPropertyCache.GetValue(chatUpdate, "Delta");
            var fromDelta = TryExtractFunctionCallFromAny(delta);
            if (fromDelta != null) return fromDelta;

            // 3) update.Choices[*].Delta / update.Choices[*].Message
            var choices = StreamingPropertyCache.GetValue(chatUpdate, "Choices") as IEnumerable;
            if (choices != null)
            {
                foreach (var choice in choices)
                {
                    if (choice == null) continue;
                    var cDelta = StreamingPropertyCache.GetValue(choice, "Delta");
                    var cMsg = StreamingPropertyCache.GetValue(choice, "Message");

                    var fromChoiceDelta = TryExtractFunctionCallFromAny(cDelta);
                    if (fromChoiceDelta != null) return fromChoiceDelta;

                    var fromChoiceMsg = TryExtractFunctionCallFromMessage(cMsg);
                    if (fromChoiceMsg != null) return fromChoiceMsg;
                }
            }

            // 4) update.ToolCalls / update.FunctionCall
            var fromToolCalls = TryExtractFunctionCallFromEnumerable(StreamingPropertyCache.GetValue(chatUpdate, "ToolCalls") as IEnumerable);
            if (fromToolCalls != null) return fromToolCalls;

            var fromDirect = TryExtractFunctionCallFromRaw(StreamingPropertyCache.GetValue(chatUpdate, "FunctionCall"));
            if (fromDirect != null) return fromDirect;

            // 5) Some update types may directly expose Content/Contents as parts.
            var fromTopLevelParts = TryExtractFunctionCallFromEnumerable(
                (StreamingPropertyCache.GetValue(chatUpdate, "Contents") as IEnumerable) ??
                (StreamingPropertyCache.GetValue(chatUpdate, "Content") as IEnumerable));
            if (fromTopLevelParts != null) return fromTopLevelParts;
        }
        catch
        {
            // ignore (best-effort)
        }

        return null;
    }

    private AevatarFunctionCall? TryExtractFunctionCallFromAny(object? candidate)
    {
        if (candidate == null)
            return null;

        // Direct part / raw tool call
        var direct = TryExtractFunctionCallFromRaw(candidate);
        if (direct != null)
            return direct;

        // candidate.Message
        var fromMessage = TryExtractFunctionCallFromMessage(StreamingPropertyCache.GetValue(candidate, "Message"));
        if (fromMessage != null)
            return fromMessage;

        // candidate.ToolCalls
        var fromToolCalls = TryExtractFunctionCallFromEnumerable(StreamingPropertyCache.GetValue(candidate, "ToolCalls") as IEnumerable);
        if (fromToolCalls != null)
            return fromToolCalls;

        // candidate.Contents / candidate.Content
        var fromParts = TryExtractFunctionCallFromEnumerable(
            (StreamingPropertyCache.GetValue(candidate, "Contents") as IEnumerable) ??
            (StreamingPropertyCache.GetValue(candidate, "Content") as IEnumerable));
        if (fromParts != null)
            return fromParts;

        // candidate.FunctionCall
        var fromFunctionCall = TryExtractFunctionCallFromRaw(StreamingPropertyCache.GetValue(candidate, "FunctionCall"));
        if (fromFunctionCall != null)
            return fromFunctionCall;

        return null;
    }

    private AevatarFunctionCall? TryExtractFunctionCallFromMessage(object? message)
    {
        if (message == null)
            return null;

        var parts = (StreamingPropertyCache.GetValue(message, "Contents") as IEnumerable)
            ?? (StreamingPropertyCache.GetValue(message, "Content") as IEnumerable);

        return TryExtractFunctionCallFromEnumerable(parts);
    }

    private AevatarFunctionCall? TryExtractFunctionCallFromEnumerable(IEnumerable? parts)
    {
        if (parts == null)
            return null;

        foreach (var part in parts)
        {
            if (part == null)
                continue;

            // Strong-typed path (Microsoft.Extensions.AI)
            if (part is FunctionCallContent functionCall)
            {
                var unwrapped = UnwrapFunctionArguments(functionCall.Arguments);
                var argsJson = unwrapped != null ? JsonSerializer.Serialize(unwrapped) : "{}";
                return new AevatarFunctionCall
                {
                    CallId = functionCall.CallId,
                    Name = functionCall.Name,
                    Arguments = argsJson
                };
            }

            // Raw representation (OpenAI.Chat.ChatToolCall) - best effort via reflection.
            var raw = StreamingPropertyCache.GetValue(part, "RawRepresentation");
            var fromRaw = TryExtractFunctionCallFromRaw(raw);
            if (fromRaw != null)
                return fromRaw;

            // Some streaming types may surface tool call directly as the part itself.
            var fromPart = TryExtractFunctionCallFromRaw(part);
            if (fromPart != null)
                return fromPart;
        }

        return null;
    }

    private static AevatarFunctionCall? TryExtractFunctionCallFromRaw(object? raw)
    {
        if (raw == null)
            return null;

        var callId = (StreamingPropertyCache.GetValue(raw, "CallId") as string)
                     ?? (StreamingPropertyCache.GetValue(raw, "Id") as string)
                     ?? (StreamingPropertyCache.GetValue(raw, "ToolCallId") as string)
                     ?? (StreamingPropertyCache.GetValue(raw, "ToolCallID") as string)
                     ?? string.Empty;

        var name = (StreamingPropertyCache.GetValue(raw, "Name") as string)
                   ?? (StreamingPropertyCache.GetValue(raw, "ToolName") as string)
                   ?? (StreamingPropertyCache.GetValue(raw, "FunctionName") as string)
                   ?? string.Empty;

        name = name.Trim();
        if (name.Length == 0)
            return null;

        var argsObj = StreamingPropertyCache.GetValue(raw, "Arguments")
                     ?? StreamingPropertyCache.GetValue(raw, "FunctionArguments");

        var argsJson = "{}";
        switch (argsObj)
        {
            case null:
                break;
            case string s:
                argsJson = string.IsNullOrWhiteSpace(s) ? "{}" : s;
                break;
            case JsonElement el:
                argsJson = el.ValueKind == JsonValueKind.String
                    ? (el.GetString() ?? "{}")
                    : el.GetRawText();
                break;
            case IDictionary<string, object?> dict:
                argsJson = JsonSerializer.Serialize(dict);
                break;
            default:
                // Best-effort: try to serialize unknown argument container
                argsJson = JsonSerializer.Serialize(argsObj);
                break;
        }

        // ToolArgumentsJson expects an object - keep it safe.
        var trimmed = argsJson.Trim();
        if (trimmed.Length == 0 || (!trimmed.StartsWith("{", StringComparison.Ordinal) && !trimmed.StartsWith("[", StringComparison.Ordinal)))
        {
            argsJson = "{}";
        }

        return new AevatarFunctionCall
        {
            CallId = callId,
            Name = name,
            Arguments = argsJson
        };
    }

    /// <summary>
    /// Extracts streaming text from chat update using reflection.
    /// </summary>
    private string ExtractStreamingText(object chatUpdate)
    {
        if (chatUpdate == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        var reasoningContent = ExtractReasoningContent(chatUpdate);
        if (!string.IsNullOrEmpty(reasoningContent))
        {
            _logger.LogDebug("[REASONING] {Content}", reasoningContent);
        }

        var text = StreamingPropertyCache.GetValue(chatUpdate, "TextDelta") as string;
        if (string.IsNullOrEmpty(text))
        {
            text = StreamingPropertyCache.GetValue(chatUpdate, "Text") as string;
        }

        if (!string.IsNullOrEmpty(text))
        {
            sb.Append(text);
            return sb.ToString();
        }

        var message = StreamingPropertyCache.GetValue(chatUpdate, "Message");
        if (message == null)
        {
            return sb.ToString();
        }

        text = StreamingPropertyCache.GetValue(message, "Text") as string;
        if (!string.IsNullOrEmpty(text))
        {
            sb.Append(text);
            return sb.ToString();
        }

        var content = (StreamingPropertyCache.GetValue(message, "Contents") as System.Collections.IEnumerable)
            ?? (StreamingPropertyCache.GetValue(message, "Content") as System.Collections.IEnumerable);
        if (content == null)
        {
            return sb.ToString();
        }

        foreach (var part in content)
        {
            if (part == null) continue;
            var partText = StreamingPropertyCache.GetValue(part, "Text") as string;
            if (!string.IsNullOrEmpty(partText))
            {
                sb.Append(partText);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extract reasoning_content from DeepSeek-reasoner or similar reasoning models.
    /// </summary>
    private static string? ExtractReasoningContent(object chatUpdate)
    {
        var reasoning = StreamingPropertyCache.GetValue(chatUpdate, "ReasoningContent") as string;
        if (!string.IsNullOrEmpty(reasoning))
        {
            return reasoning;
        }

        var delta = StreamingPropertyCache.GetValue(chatUpdate, "Delta");
        if (delta != null)
        {
            reasoning = StreamingPropertyCache.GetValue(delta, "ReasoningContent") as string;
            if (!string.IsNullOrEmpty(reasoning))
            {
                return reasoning;
            }

            reasoning = StreamingPropertyCache.GetValue(delta, "reasoning_content") as string;
            if (!string.IsNullOrEmpty(reasoning))
            {
                return reasoning;
            }
        }

        var message = StreamingPropertyCache.GetValue(chatUpdate, "Message");
        if (message != null)
        {
            reasoning = StreamingPropertyCache.GetValue(message, "ReasoningContent") as string;
            if (!string.IsNullOrEmpty(reasoning))
            {
                return reasoning;
            }
        }

        return null;
    }
}
