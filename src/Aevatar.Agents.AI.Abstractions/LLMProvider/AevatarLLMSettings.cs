namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// LLM设置
/// </summary>
public class AevatarLLMSettings
{
    /// <summary>
    /// 模型ID
    /// </summary>
    public string? ModelId { get; set; }
    
    /// <summary>
    /// 温度参数（0-2，控制输出的随机性）
    /// </summary>
    public double Temperature { get; set; } = 0.7;
    
    /// <summary>
    /// Top-P参数（核采样）
    /// </summary>
    public double TopP { get; set; } = 1.0;
    
    /// <summary>
    /// 最大生成Token数
    /// </summary>
    public int MaxTokens { get; set; } = AevatarAIDefaults.DefaultMaxOutputTokens;
    
    /// <summary>
    /// 频率惩罚（-2.0到2.0）
    /// </summary>
    public double FrequencyPenalty { get; set; } = 0;
    
    /// <summary>
    /// 存在惩罚（-2.0到2.0）
    /// </summary>
    public double PresencePenalty { get; set; } = 0;
    
    /// <summary>
    /// 停止序列
    /// </summary>
    public IList<string>? StopSequences { get; set; }
    
    /// <summary>
    /// 响应格式（如json_object）
    /// </summary>
    public AevatarResponseFormat? AevatarResponseFormat { get; set; }
    
    /// <summary>
    /// Seed（用于确定性输出）
    /// </summary>
    public int? Seed { get; set; }
}