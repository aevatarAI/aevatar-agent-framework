using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

// NOTE: This file lives in namespace Aevatar.Agents.AI.MEAI.Internal, so the
// C# compiler resolves unqualified 'ChatResponse' to the protobuf-generated
// Aevatar.Agents.AI.ChatResponse before checking 'using' directives.
// All MEAI ChatResponse/ChatResponseUpdate references MUST be fully qualified.

namespace Aevatar.Agents.AI.MEAI.Internal;

/// <summary>
/// IChatClient that calls the OpenAI Codex Responses API directly.
///
/// The standard OpenAI .NET SDK ChatClient targets /chat/completions,
/// but ChatGPT Codex OAuth requires the Responses API at:
///   POST https://chatgpt.com/backend-api/codex/responses
///
/// This client:
///   1. Converts MEAI ChatMessages → Responses API input format
///   2. POSTs with required headers (Authorization, ChatGPT-Account-Id, originator)
///   3. Parses SSE response → MEAI ChatResponse / ChatResponseUpdate
///
/// Reference: opencode (github.com/anomalyco/opencode) codex plugin.
/// </summary>
internal sealed class CodexResponsesChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string _accessToken;
    private readonly string? _accountId;
    private readonly string _originator;
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public CodexResponsesChatClient(
        LLMProviderConfig config,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _model = config.Model;
        _accessToken = config.ApiKey ?? throw new InvalidOperationException("Codex OAuth access token is required.");
        _endpoint = !string.IsNullOrWhiteSpace(config.Endpoint)
            ? config.Endpoint.TrimEnd('/')
            : "https://chatgpt.com/backend-api/codex/responses";

        // Extract headers from ProviderSpecificSettings
        var settings = config.ProviderSpecificSettings;
        _accountId = settings.TryGetValue("chatgpt-account-id", out var aid) ? aid?.ToString() : null;
        _originator = settings.TryGetValue("originator", out var orig) ? orig?.ToString() ?? "aevatar" : "aevatar";

        var timeoutMs = config.TimeoutMilliseconds > 0
            ? config.TimeoutMilliseconds
            : (int)TimeSpan.FromMinutes(10).TotalMilliseconds;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs)
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    // ================================================================
    //  Non-streaming
    // ================================================================

    public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return GetResponseCoreAsync(chatMessages, options, cancellationToken);
    }

    private async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseCoreAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        // Codex API requires stream=true — non-streaming requests return 400.
        // We consume the SSE stream and assemble a complete ChatResponse.
        _logger.LogInformation("[Codex] POST {Endpoint} (stream-to-sync, model={Model})", _endpoint, _model);

        var textBuilder = new StringBuilder();
        var functionCalls = new List<FunctionCallContent>();
        string? modelId = null;
        string? responseId = null;
        UsageDetails? usage = null;

        await foreach (var update in GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    textBuilder.Append(tc.Text);
                else if (content is FunctionCallContent fcc)
                    functionCalls.Add(fcc);
            }

            modelId ??= update.ModelId;
            responseId ??= update.ResponseId;
        }

        var messages = new List<ChatMessage>();
        if (functionCalls.Count > 0)
        {
            var contents = functionCalls.Cast<AIContent>().ToList();
            if (textBuilder.Length > 0)
                contents.Insert(0, new TextContent(textBuilder.ToString()));
            messages.Add(new ChatMessage(ChatRole.Assistant, contents));
        }
        else if (textBuilder.Length > 0)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, textBuilder.ToString()));
        }

        return new Microsoft.Extensions.AI.ChatResponse(messages)
        {
            ModelId = modelId ?? _model,
            ResponseId = responseId,
            Usage = usage
        };
    }

    // ================================================================
    //  Streaming
    // ================================================================

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(chatMessages, options, stream: true);
        using var request = CreateHttpRequest(body);

        _logger.LogInformation("[Codex] POST {Endpoint} (streaming, model={Model})", _endpoint, _model);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("[Codex] {Status}: {Body}",
                response.StatusCode, errorBody.Length > 500 ? errorBody[..500] : errorBody);
            throw new HttpRequestException($"Codex API returned {(int)response.StatusCode}: {errorBody[..Math.Min(200, errorBody.Length)]}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // SSE state: track active function calls being assembled
        string? activeFnCallId = null;
        string? activeFnCallName = null;
        var activeFnArgsBuilder = new StringBuilder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line == null) break;
            if (line.Length == 0) continue; // blank line = event separator

            // SSE format: "data: {json}" or "event: name"
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var json = line.AsSpan(6);
            if (json.SequenceEqual("[DONE]")) break;

            JsonNode? node;
            try { node = JsonNode.Parse(json.ToString()); }
            catch { continue; }

            if (node is not JsonObject obj) continue;
            var type = obj["type"]?.GetValue<string>();

            switch (type)
            {
                case "response.output_text.delta":
                {
                    var delta = obj["delta"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(delta))
                    {
                        yield return new ChatResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = [new TextContent(delta)]
                        };
                    }
                    break;
                }

                case "response.output_item.added":
                {
                    // Detect function_call start
                    var item = obj["item"]?.AsObject();
                    if (item != null && item["type"]?.GetValue<string>() == "function_call")
                    {
                        activeFnCallId = item["call_id"]?.GetValue<string>()
                                         ?? item["id"]?.GetValue<string>()
                                         ?? Guid.NewGuid().ToString("N");
                        activeFnCallName = item["name"]?.GetValue<string>() ?? string.Empty;
                        activeFnArgsBuilder.Clear();
                    }
                    break;
                }

                case "response.function_call_arguments.delta":
                {
                    var delta = obj["delta"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(delta))
                        activeFnArgsBuilder.Append(delta);
                    break;
                }

                case "response.function_call_arguments.done":
                {
                    var args = obj["arguments"]?.GetValue<string>()
                               ?? activeFnArgsBuilder.ToString();

                    if (!string.IsNullOrEmpty(activeFnCallName))
                    {
                        Dictionary<string, object?>? parsedArgs = null;
                        try { parsedArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(args); }
                        catch { /* best-effort */ }

                        yield return new ChatResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = [new FunctionCallContent(
                                activeFnCallId ?? Guid.NewGuid().ToString("N"),
                                activeFnCallName,
                                parsedArgs ?? new Dictionary<string, object?>())]
                        };
                    }

                    activeFnCallId = null;
                    activeFnCallName = null;
                    activeFnArgsBuilder.Clear();
                    break;
                }

                case "response.completed":
                {
                    // Extract usage if available
                    var respObj = obj["response"]?.AsObject();
                    var usageObj = respObj?["usage"]?.AsObject();
                    UsageDetails? usage = null;
                    if (usageObj != null)
                    {
                        usage = new UsageDetails
                        {
                            InputTokenCount = usageObj["input_tokens"]?.GetValue<int>(),
                            OutputTokenCount = usageObj["output_tokens"]?.GetValue<int>(),
                            TotalTokenCount = usageObj["total_tokens"]?.GetValue<int>()
                        };
                    }

                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [],
                        ModelId = respObj?["model"]?.GetValue<string>(),
                        ResponseId = respObj?["id"]?.GetValue<string>()
                    };
                    break;
                }
            }
        }
    }

    // ================================================================
    //  Request building
    // ================================================================

    private string BuildRequestBody(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        bool stream)
    {
        var root = new JsonObject
        {
            ["model"] = options?.ModelId ?? _model,
            ["store"] = false,
            ["stream"] = stream
        };

        // Separate system messages → instructions
        var instructions = new StringBuilder();
        var input = new JsonArray();

        foreach (var msg in chatMessages)
        {
            if (msg.Role == ChatRole.System)
            {
                if (instructions.Length > 0) instructions.Append('\n');
                instructions.Append(msg.Text ?? string.Empty);
                continue;
            }

            if (msg.Role == ChatRole.Tool)
            {
                // Tool results → function_call_output items
                foreach (var content in msg.Contents ?? [])
                {
                    if (content is FunctionResultContent frc)
                    {
                        input.Add(new JsonObject
                        {
                            ["type"] = "function_call_output",
                            ["call_id"] = frc.CallId,
                            ["output"] = frc.Result?.ToString() ?? string.Empty
                        });
                    }
                }
                continue;
            }

            if (msg.Role == ChatRole.Assistant)
            {
                // Check for function calls in assistant messages
                var hasFunctionCalls = false;
                foreach (var content in msg.Contents ?? [])
                {
                    if (content is FunctionCallContent fcc)
                    {
                        hasFunctionCalls = true;
                        var argsStr = fcc.Arguments != null
                            ? JsonSerializer.Serialize(fcc.Arguments)
                            : "{}";

                        input.Add(new JsonObject
                        {
                            ["type"] = "function_call",
                            ["call_id"] = fcc.CallId ?? Guid.NewGuid().ToString("N"),
                            ["name"] = fcc.Name ?? string.Empty,
                            ["arguments"] = argsStr
                        });
                    }
                }

                if (hasFunctionCalls) continue;
            }

            // Regular message (user or assistant text)
            var role = msg.Role == ChatRole.Assistant ? "assistant" : "user";
            var text = msg.Text;

            // Multi-part content: extract text parts
            if (string.IsNullOrEmpty(text) && msg.Contents is { Count: > 0 })
            {
                var sb = new StringBuilder();
                foreach (var c in msg.Contents)
                {
                    if (c is TextContent tc) sb.Append(tc.Text);
                }
                text = sb.ToString();
            }

            input.Add(new JsonObject
            {
                ["type"] = "message",
                ["role"] = role,
                ["content"] = text ?? string.Empty
            });
        }

        if (instructions.Length > 0)
            root["instructions"] = instructions.ToString();

        root["input"] = input;

        // Tools
        if (options?.Tools is { Count: > 0 })
        {
            var tools = new JsonArray();
            foreach (var tool in options.Tools)
            {
                if (tool is AIFunction fn)
                {
                    var toolObj = new JsonObject
                    {
                        ["type"] = "function",
                        ["name"] = fn.Name,
                        ["description"] = fn.Description ?? string.Empty,
                        ["parameters"] = JsonNode.Parse(fn.JsonSchema.GetRawText())
                    };
                    tools.Add(toolObj);
                }
            }

            if (tools.Count > 0)
                root["tools"] = tools;
        }

        // Note: Codex OAuth API does not support max_output_tokens.
        // The parameter is intentionally omitted — the API manages its own limits.

        return root.ToJsonString(JsonOpts);
    }

    private HttpRequestMessage CreateHttpRequest(string body)
    {
        // Codex API requires exactly "application/json" — .NET's StringContent
        // defaults to "application/json; charset=utf-8" which gets rejected.
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = content
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        if (!string.IsNullOrEmpty(_accountId))
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", _accountId);

        request.Headers.TryAddWithoutValidation("originator", _originator);
        request.Headers.TryAddWithoutValidation("User-Agent",
            $"aevatar/1.0 ({Environment.OSVersion.Platform}; {Environment.OSVersion.Version})");

        return request;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
