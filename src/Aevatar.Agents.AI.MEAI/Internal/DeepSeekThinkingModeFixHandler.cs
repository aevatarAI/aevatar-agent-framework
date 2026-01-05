using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aevatar.Agents.AI.MEAI.Internal;

/// <summary>
/// DeepSeek thinking-mode fix handler.
///
/// 背景（中文 + ASCII）:
/// - deepseek-reasoner 在 tool-calls/thinking 模式下会强校验：
///   所有 role=assistant 的历史消息必须包含 `reasoning_content` 字段（允许空字符串）。
/// - OpenAI .NET SDK + Microsoft.Extensions.AI.OpenAI 的适配层不会把 ChatMessage.AdditionalProperties
///   展开成顶层 JSON 字段，导致服务端 400:
///   "Missing `reasoning_content` field in the assistant message..."
///
/// 方案:
/// - 在 HTTP 层拦截 /chat/completions 请求，解析 JSON，把缺失的 reasoning_content 补成 ""。
/// - 仅对 deepseek-reasoner 启用（由工厂按 model 决定是否挂载该 handler）。
/// </summary>
internal sealed class DeepSeekThinkingModeFixHandler : DelegatingHandler
{
    public DeepSeekThinkingModeFixHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.Content != null && IsChatCompletionsRequest(request.RequestUri))
            {
                var json = await request.Content.ReadAsStringAsync(cancellationToken);
                var patched = TryPatchRequestJson(json);
                if (patched != null)
                {
                    request.Content = new StringContent(patched, Encoding.UTF8, "application/json");
                }
            }
        }
        catch
        {
            // Best-effort only: never block the request on patch failure.
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsChatCompletionsRequest(Uri? uri)
    {
        if (uri == null)
            return false;

        // OpenAI-compatible path: /chat/completions
        // NOTE: Keep it simple and robust (no extra branches).
        return uri.AbsolutePath.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? TryPatchRequestJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch
        {
            return null;
        }

        if (rootNode is not JsonObject rootObj)
            return null;

        if (!rootObj.TryGetPropertyValue("messages", out var messagesNode) || messagesNode is not JsonArray messagesArr)
            return null;

        var changed = false;

        foreach (var node in messagesArr)
        {
            if (node is not JsonObject msgObj)
                continue;

            var role = msgObj["role"]?.GetValue<string>();
            if (!string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
                continue;

            // Ensure the field exists (DeepSeek requirement).
            if (!msgObj.ContainsKey("reasoning_content") || msgObj["reasoning_content"] is null)
            {
                msgObj["reasoning_content"] = string.Empty;
                changed = true;
            }
        }

        if (!changed)
            return null;

        // Keep output compact. (No need to preserve formatting, only semantics.)
        return rootObj.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            // Keep property names untouched (snake_case required by DeepSeek).
            PropertyNamingPolicy = null,
            DictionaryKeyPolicy = null
        });
    }
}


