namespace Aevatar.AxiomReasoning.LlmRecorder;

// ============================================================
//  LLM TRANSCRIPT OPTIONS
//
//  目标：
//  - 把每次 LLM 对话过程落盘，便于“复盘 + AI 分析 + 人类审阅”
//  - 默认写入 output/{sessionId}/llm/
//
//  设计原则：
//  - 默认开启（本项目是调试/研究型 UI）
//  - JSONL 为机器可读主格式；Markdown 为人类可读辅助格式
//  - 任何输出都要有安全阀（上限），避免意外把磁盘/内存打爆
// ============================================================

public sealed class LlmTranscriptOptions
{
    public const string SectionName = "AxiomReasoning:LlmTranscript";

    /// <summary>是否启用本地对话记录</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>是否写入 JSONL（强烈建议保留）</summary>
    public bool WriteJsonlTranscript { get; set; } = true;

    /// <summary>是否写入 Markdown 回放（给人看）</summary>
    public bool WriteMarkdownReview { get; set; } = true;

    /// <summary>单条 prompt 最大字符数（超过则截断并标注）</summary>
    public int MaxPromptChars { get; set; } = 200_000;

    /// <summary>单条 assistant response 最大字符数（超过则截断并标注）</summary>
    public int MaxResponseChars { get; set; } = 800_000;

    /// <summary>Markdown 中每段内容最大字符数（避免 review.md 巨大不可用）</summary>
    public int MaxMarkdownCharsPerMessage { get; set; } = 20_000;
}

