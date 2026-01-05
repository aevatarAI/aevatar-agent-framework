namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 函数调用
/// </summary>
public class AevatarFunctionCall
{
    /// <summary>
    /// 工具调用 ID（用于将 tool result 绑定回模型返回的 tool call）。
    /// <para/>
    /// 注意：不同 Provider 的字段名不同（OpenAI: tool_call_id / id，MEAI: CallId），这里统一为 CallId。
    /// </summary>
    public string CallId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
}