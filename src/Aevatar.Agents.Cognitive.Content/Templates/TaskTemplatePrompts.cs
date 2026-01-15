using System.Text;
using Aevatar.Agents.Cognitive.Content;
using Aevatar.Agents.Cognitive.Core.Tasks;

namespace Aevatar.Agents.Cognitive.Content;

// ============================================================
//  TASK TEMPLATE PROMPTS
//  任务模板提示词生成器
// ============================================================

/// <summary>
/// 任务模板提示词生成器。
/// 根据任务模板和定义生成系统提示词和用户提示词。
/// </summary>
public static class TaskTemplatePrompts
{
    /// <summary>
    /// 获取系统提示词。
    /// </summary>
    public static string GetSystemPrompt(TaskDefinition definition)
    {
        var basePrompt = definition.Template switch
        {
            TaskTemplate.Summarize => GetSummarizePrompt(definition),
            TaskTemplate.Analyze => GetAnalyzePrompt(definition),
            TaskTemplate.Review => GetReviewPrompt(definition),
            TaskTemplate.Critique => GetCritiquePrompt(definition),
            TaskTemplate.Rewrite => GetRewritePrompt(definition),
            TaskTemplate.Continue => GetContinuePrompt(definition),
            TaskTemplate.Extract => GetExtractPrompt(definition),
            TaskTemplate.Compare => GetComparePrompt(definition),
            TaskTemplate.QA => GetQAPrompt(definition),
            TaskTemplate.Translate => GetTranslatePrompt(definition),
            TaskTemplate.CodeReview => GetCodeReviewPrompt(definition),
            TaskTemplate.Outline => GetOutlinePrompt(definition),
            TaskTemplate.Custom => definition.CustomInstruction ?? "处理以下内容：",
            _ => definition.CustomInstruction ?? "处理以下内容："
        };

        // 附加自定义指令（如果有）
        if (definition.Template != TaskTemplate.Custom && !string.IsNullOrEmpty(definition.CustomInstruction))
        {
            basePrompt += $"\n\n额外要求：\n{definition.CustomInstruction}";
        }

        // 附加输出格式要求
        basePrompt += GetOutputFormatInstructions(definition.OutputFormat);

        return basePrompt;
    }

    /// <summary>
    /// 获取用户提示词（包含内容）。
    /// </summary>
    public static string GetUserPrompt(LoadedContent content, TaskDefinition definition)
    {
        var sb = new StringBuilder();

        // 内容描述
        if (!string.IsNullOrEmpty(content.SourceDescription))
        {
            sb.AppendLine($"内容说明：{content.SourceDescription}");
            sb.AppendLine();
        }

        // 根据文件数量决定格式
        if (content.Files.Count > 1)
        {
            sb.AppendLine($"以下是 {content.Files.Count} 个文件的内容（共约 {content.EstimatedTokens:N0} tokens）：");
            sb.AppendLine();

            foreach (var file in content.Files)
            {
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine($"📄 文件: {file.FileName}");
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine(file.Content);
                sb.AppendLine();
            }
        }
        else if (content.Files.Count == 1)
        {
            var file = content.Files[0];
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine($"📄 文件: {file.FileName}（约 {file.EstimatedTokens:N0} tokens）");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine(file.Content);
        }
        else
        {
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("内容");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine(content.CombinedText);
        }

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────
    //  各模板提示词
    // ─────────────────────────────────────────────────────────

    private static string GetSummarizePrompt(TaskDefinition def)
    {
        var detail = def.Parameters.DetailLevel switch
        {
            < 0.3f => "极简摘要，只保留最核心的3-5个要点",
            < 0.6f => "中等详细度的总结，涵盖主要观点和关键细节",
            _ => "详尽的总结，包含所有重要观点、论据和细节"
        };

        return $"""
            你是一位专业的内容总结专家。
            
            任务：对以下内容进行{detail}。
            
            要求：
            1. 识别并提取核心主题和主要观点
            2. 保持客观中立，不添加个人解读
            3. 使用清晰的层级结构组织总结
            4. 重要数据、事实和结论必须保留
            5. 按重要性排序，最重要的内容放在前面
            """;
    }

    private static string GetAnalyzePrompt(TaskDefinition def)
    {
        var focus = string.IsNullOrEmpty(def.Parameters.FocusAreas)
            ? ""
            : $"\n\n重点关注：{def.Parameters.FocusAreas}";

        return $"""
            你是一位深度分析专家。
            
            任务：对以下内容进行结构化深度分析。
            
            分析框架：
            1. **核心主题**：识别中心议题和关键概念
            2. **论点梳理**：列出主要论点和观点
            3. **证据评估**：分析支撑论点的证据质量
            4. **逻辑结构**：评估论证的逻辑严密性
            5. **潜在假设**：揭示隐含的前提假设
            6. **优势与不足**：客观评价内容的强弱项
            7. **结论与启示**：总结核心发现和实际意义
            {focus}
            """;
    }

    private static string GetReviewPrompt(TaskDefinition def)
    {
        var quoteInstr = def.Parameters.IncludeQuotes
            ? "对于每个问题，请引用原文相关段落"
            : "指出问题位置（如章节名或段落描述）";

        return $"""
            你是一位资深的学术评审专家。
            
            任务：对以下学术内容进行专业评审。
            
            评审维度：
            1. **结构完整性**：论述逻辑是否清晰，章节安排是否合理
            2. **论证严谨性**：论点是否有充分证据支撑，推理是否严密
            3. **创新性**：贡献程度，相对于已有工作的新颖之处
            4. **写作质量**：表达是否准确、专业、流畅
            5. **格式规范**：是否符合学术写作规范
            
            对于每个发现的问题：
            - {quoteInstr}
            - 清晰解释问题所在
            - 提供具体、可操作的修改建议
            
            最后给出整体评价和改进优先级建议。
            """;
    }

    private static string GetCritiquePrompt(TaskDefinition def)
    {
        return """
            你是一位资深的文学评论家和创意顾问。
            
            任务：对以下创意作品进行专业评价。
            
            评价维度：
            1. **叙事结构**：情节编排、节奏把控、悬念设置
            2. **人物塑造**：角色深度、动机合理性、成长弧线
            3. **语言风格**：文笔质量、对话自然度、氛围营造
            4. **主题深度**：思想内涵、情感共鸣、价值传达
            5. **原创性**：创意新颖度、独特视角
            
            评价要求：
            - 既要指出不足，也要肯定亮点
            - 具体引用文本说明问题
            - 给出建设性的改进建议
            - 最后给出整体评分（1-10）和总评
            """;
    }

    private static string GetRewritePrompt(TaskDefinition def)
    {
        return """
            你是一位专业编辑和写作优化专家。
            
            任务：改写并优化以下内容。
            
            优化方向：
            1. **清晰度**：消除歧义，简化复杂表述
            2. **连贯性**：增强段落间的逻辑连接
            3. **流畅度**：改善句子结构，提升可读性
            4. **准确性**：修正事实错误和表述不当
            5. **风格一致**：保持整体语调统一
            
            要求：
            - 保持原意不变的前提下大幅提升质量
            - 对重大修改给出简要说明
            - 输出完整的改写版本
            """;
    }

    private static string GetContinuePrompt(TaskDefinition def)
    {
        var hint = string.IsNullOrEmpty(def.ContinuationHint)
            ? ""
            : $"\n\n续写方向提示：{def.ContinuationHint}";

        return $"""
            你是一位创意写作大师。
            
            任务：基于以下内容进行续写/推演。
            
            要求：
            1. **风格一致**：保持原作的叙事风格和语调
            2. **人物连贯**：角色性格和行为符合已建立的设定
            3. **情节合理**：发展要合乎逻辑，不能脱离原设定
            4. **创意发挥**：在合理范围内加入新颖元素
            5. **节奏把控**：保持与原文相近的叙事节奏
            {hint}
            
            请自然地从原文结束处继续，不要重复已有内容。
            """;
    }

    private static string GetExtractPrompt(TaskDefinition def)
    {
        var target = def.ExtractionTarget ?? "关键信息";

        return $"""
            你是一位信息提取专家。
            
            任务：从以下内容中提取「{target}」。
            
            要求：
            1. **完整性**：不遗漏任何符合条件的信息
            2. **准确性**：保持原始表述，不改写
            3. **来源标注**：标明信息来自哪个部分
            4. **结构化**：按逻辑分类组织提取结果
            
            如果内容中不存在目标信息，请明确说明。
            """;
    }

    private static string GetComparePrompt(TaskDefinition def)
    {
        return """
            你是一位比较分析专家。
            
            任务：对比分析以下多个文档/内容。
            
            分析维度：
            1. **相同点**：共同的主题、观点、方法
            2. **差异点**：不同的立场、方法、结论
            3. **互补性**：各自的独特贡献
            4. **矛盾点**：相互冲突的观点或数据
            5. **综合评价**：各自的优势和局限
            
            输出格式：
            - 先分别简述每份内容的核心
            - 然后进行系统性对比
            - 最后给出综合结论
            """;
    }

    private static string GetQAPrompt(TaskDefinition def)
    {
        return $"""
            你是一位知识问答专家。
            
            任务：基于以下内容回答问题。
            
            问题：{def.Question}
            
            要求：
            1. 答案必须基于给定内容，不要引入外部知识
            2. 如果内容中没有答案，请明确说明"内容中未提及"
            3. 引用相关原文支持你的答案
            4. 如果问题有多个方面，分点回答
            """;
    }

    private static string GetTranslatePrompt(TaskDefinition def)
    {
        var lang = def.TargetLanguage ?? "中文";

        return $"""
            你是一位专业翻译。
            
            任务：将以下内容翻译为 {lang}。
            
            要求：
            1. **准确性**：准确传达原文含义
            2. **专业性**：保持专业术语一致性
            3. **地道性**：符合目标语言表达习惯
            4. **格式保留**：保持原文的结构和格式
            
            对于无法直译的习语或文化特定表达，请在翻译后用括号注明原文。
            """;
    }

    private static string GetCodeReviewPrompt(TaskDefinition def)
    {
        return """
            你是一位资深软件工程师和代码审查专家。
            
            任务：对以下代码进行专业审查。
            
            审查维度：
            1. **正确性**：逻辑错误、边界条件、异常处理
            2. **可读性**：命名、注释、代码结构
            3. **性能**：算法效率、资源使用
            4. **安全性**：潜在安全漏洞
            5. **最佳实践**：设计模式、SOLID 原则
            6. **可维护性**：代码复杂度、测试覆盖
            
            对于每个问题：
            - 指出具体代码位置
            - 解释问题及其影响
            - 提供修改建议（最好包含示例代码）
            """;
    }

    private static string GetOutlinePrompt(TaskDefinition def)
    {
        return """
            你是一位内容组织专家。
            
            任务：为以下内容生成结构化大纲。
            
            要求：
            1. 识别主要章节/部分
            2. 提取每部分的核心要点
            3. 使用层级结构（最多3层）
            4. 每个条目简明扼要
            5. 标注关键概念和术语
            
            输出格式：
            使用 Markdown 格式的有序/无序列表
            """;
    }

    private static string GetOutputFormatInstructions(OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Markdown => "\n\n输出格式：请使用 Markdown 格式，合理使用标题、列表、引用等。",
            OutputFormat.PlainText => "\n\n输出格式：请使用纯文本，不使用任何格式标记。",
            OutputFormat.JSON => "\n\n输出格式：请输出有效的 JSON 格式。",
            OutputFormat.HTML => "\n\n输出格式：请使用 HTML 格式。",
            _ => ""
        };
    }
}

