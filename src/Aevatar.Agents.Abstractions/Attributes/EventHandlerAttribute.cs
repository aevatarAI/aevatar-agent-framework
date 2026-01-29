namespace Aevatar.Agents.Abstractions.Attributes;

/// <summary>
/// Marking event handler method.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class EventHandlerAttribute : Attribute
{
    /// <summary>
    /// Whether handle events published by current agent itself.
    /// </summary>
    public bool AllowSelfHandling { get; set; }

    /// <summary>
    /// Only handle events published by current agent itself (ignore parent/children events).
    /// 
    /// 当设置为 true 时，此 handler 只处理 agent 自己 publish 给自己的事件，
    /// 不会处理从 parent/children stream 传来的事件。
    /// 
    /// 注意：OnlySelfHandling = true 蕴含 AllowSelfHandling = true，无需重复设置。
    /// 
    /// 典型用例：HandleChatRequestEvent，确保只响应直接 publish 给自己的 ChatRequestEvent。
    /// </summary>
    public bool OnlySelfHandling { get; set; }

    /// <summary>
    /// Handler priority (The smaller the number, the higher the priority)
    /// </summary>
    public int Priority { get; set; } = 0;
}