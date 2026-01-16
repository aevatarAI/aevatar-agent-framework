# Cognitive Mesh 内容加载与任务模板设计

> **核心愿景**：Load Any Content → Choose Task → Pick Strategy → Execute
>
> 将 Cognitive Mesh 从"固定项目"升级为"通用认知处理器"

---

## 🎯 设计目标

```
┌─────────────────────────────────────────────────────────────────────┐
│                     COGNITIVE MESH V2                                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐                │
│  │   CONTENT   │ → │    TASK     │ → │  STRATEGY   │                │
│  │   LOADER    │   │  TEMPLATE   │   │  SELECTOR   │                │
│  └─────────────┘   └─────────────┘   └─────────────┘                │
│        │                 │                 │                         │
│        ▼                 ▼                 ▼                         │
│   文件/文件夹       总结/评审/续写      MAKER/UoT/...               │
│   多格式支持        可自定义提示词       可靠性选择                  │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 📦 核心抽象

### 1. ContentSource - 内容来源

```csharp
/// <summary>
/// 内容来源 - 描述要加载的内容
/// </summary>
public record ContentSource
{
    /// <summary>单个文件路径</summary>
    public string? FilePath { get; init; }
    
    /// <summary>文件夹路径</summary>
    public string? DirectoryPath { get; init; }
    
    /// <summary>多个文件路径</summary>
    public IReadOnlyList<string>? FilePaths { get; init; }
    
    /// <summary>文件扩展名过滤（仅文件夹模式）</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [".md", ".txt"];
    
    /// <summary>是否递归搜索子目录</summary>
    public bool Recursive { get; init; } = false;
    
    /// <summary>直接传入的文本内容（无需文件）</summary>
    public string? DirectContent { get; init; }
    
    /// <summary>内容描述（用于提示词）</summary>
    public string? ContentDescription { get; init; }
}
```

### 2. LoadedContent - 加载结果

```csharp
/// <summary>
/// 加载后的内容
/// </summary>
public record LoadedContent
{
    /// <summary>合并后的全文</summary>
    public required string CombinedText { get; init; }
    
    /// <summary>各文件的详细信息</summary>
    public IReadOnlyList<ContentFile> Files { get; init; } = [];
    
    /// <summary>估算的 Token 数</summary>
    public int EstimatedTokens { get; init; }
    
    /// <summary>内容类型描述</summary>
    public string ContentType { get; init; } = "text";
}

public record ContentFile(
    string Path,
    string FileName,
    string Content,
    int EstimatedTokens
);
```

### 3. TaskTemplate - 任务模板

```csharp
/// <summary>
/// 预定义的任务模板
/// </summary>
public enum TaskTemplate
{
    /// <summary>总结要点 - 提取核心观点和关键信息</summary>
    Summarize,
    
    /// <summary>深度分析 - 结构化分析内容</summary>
    Analyze,
    
    /// <summary>学术评审 - 论文/报告评审</summary>
    Review,
    
    /// <summary>批评评价 - 文学作品/创意评价</summary>
    Critique,
    
    /// <summary>改写优化 - 重写/优化内容</summary>
    Rewrite,
    
    /// <summary>续写推演 - 续写故事/推演剧情</summary>
    Continue,
    
    /// <summary>信息提取 - 提取特定类型信息</summary>
    Extract,
    
    /// <summary>对比分析 - 对比多个文档</summary>
    Compare,
    
    /// <summary>问答 - 基于内容回答问题</summary>
    QA,
    
    /// <summary>翻译 - 翻译内容</summary>
    Translate,
    
    /// <summary>自定义 - 用户自定义提示词</summary>
    Custom
}
```

### 4. TaskDefinition - 任务定义

```csharp
/// <summary>
/// 完整的任务定义
/// </summary>
public record TaskDefinition
{
    /// <summary>任务模板</summary>
    public TaskTemplate Template { get; init; } = TaskTemplate.Custom;
    
    /// <summary>自定义指令（覆盖模板）</summary>
    public string? CustomInstruction { get; init; }
    
    /// <summary>任务特定参数</summary>
    public TaskParameters? Parameters { get; init; }
    
    /// <summary>输出格式要求</summary>
    public OutputFormat OutputFormat { get; init; } = OutputFormat.Markdown;
    
    /// <summary>目标语言（翻译任务）</summary>
    public string? TargetLanguage { get; init; }
    
    /// <summary>要回答的问题（QA任务）</summary>
    public string? Question { get; init; }
    
    /// <summary>提取目标（Extract任务）</summary>
    public string? ExtractionTarget { get; init; }
}

public record TaskParameters
{
    /// <summary>最大输出长度</summary>
    public int? MaxOutputLength { get; init; }
    
    /// <summary>详细程度 (0-1)</summary>
    public float DetailLevel { get; init; } = 0.5f;
    
    /// <summary>是否保留原文引用</summary>
    public bool IncludeQuotes { get; init; } = true;
    
    /// <summary>是否分章节处理</summary>
    public bool ProcessBySection { get; init; } = false;
}

public enum OutputFormat
{
    Markdown,
    PlainText,
    JSON,
    HTML
}
```

---

## 🔧 任务模板提示词

### TaskTemplatePrompts.cs

```csharp
public static class TaskTemplatePrompts
{
    public static string GetSystemPrompt(TaskTemplate template, TaskDefinition definition)
    {
        return template switch
        {
            TaskTemplate.Summarize => """
                你是一个专业的内容总结专家。
                
                任务：提取并总结以下内容的核心要点。
                
                要求：
                - 识别主要观点和关键论述
                - 保持客观，不添加个人观点
                - 使用清晰的结构组织总结
                - 重要数据和事实需保留
                """,
                
            TaskTemplate.Review => """
                你是一位资深的学术评审专家。
                
                任务：对以下学术内容进行专业评审。
                
                评审维度：
                1. 结构完整性 - 论述逻辑是否清晰
                2. 论证严谨性 - 证据是否充分
                3. 创新性 - 贡献和新颖程度
                4. 写作质量 - 表达是否准确专业
                5. 格式规范 - 是否符合学术规范
                
                对于每个问题：
                - 引用原文
                - 解释问题
                - 提供具体修改建议
                """,
                
            TaskTemplate.Critique => """
                你是一位文学评论家和创意顾问。
                
                任务：评价以下创意作品的质量。
                
                评价维度：
                1. 叙事结构 - 情节编排、节奏把控
                2. 人物塑造 - 角色深度、动机合理性
                3. 语言风格 - 文笔、对话、氛围营造
                4. 主题深度 - 思想内涵、情感共鸣
                5. 原创性 - 创意新颖度
                
                既要指出不足，也要肯定亮点。
                """,
                
            TaskTemplate.Continue => """
                你是一位创意写作大师。
                
                任务：基于以下内容进行续写/推演。
                
                要求：
                - 保持原作风格和语调
                - 人物性格保持一致
                - 情节发展合乎逻辑
                - 可以有创意但不能脱离原设定
                """,
                
            TaskTemplate.Rewrite => """
                你是一位专业编辑和写作优化专家。
                
                任务：改写并优化以下内容。
                
                优化方向：
                - 提升表达清晰度
                - 增强逻辑连贯性
                - 改进语言流畅度
                - 修正错误和不准确之处
                
                保持原意的同时大幅提升质量。
                """,
                
            TaskTemplate.Analyze => """
                你是一位深度分析专家。
                
                任务：对以下内容进行结构化深度分析。
                
                分析框架：
                1. 核心主题识别
                2. 论点/观点梳理
                3. 证据/论据评估
                4. 逻辑结构解析
                5. 潜在假设揭示
                6. 优势与不足
                7. 结论与启示
                """,
                
            TaskTemplate.Extract => $"""
                你是一位信息提取专家。
                
                任务：从以下内容中提取特定信息。
                
                提取目标：{definition.ExtractionTarget ?? "关键信息"}
                
                要求：
                - 准确提取，不遗漏
                - 保持原始表述
                - 标注来源位置
                """,
                
            TaskTemplate.Compare => """
                你是一位比较分析专家。
                
                任务：对比分析以下多个文档/内容。
                
                分析维度：
                1. 相同点 - 共同主题、观点、方法
                2. 差异点 - 不同立场、方法、结论
                3. 互补性 - 各自的独特贡献
                4. 矛盾点 - 相互冲突的观点
                5. 综合评价 - 各自优劣
                """,
                
            TaskTemplate.QA => $"""
                你是一位知识问答专家。
                
                任务：基于以下内容回答问题。
                
                问题：{definition.Question}
                
                要求：
                - 答案必须基于给定内容
                - 如果内容中没有答案，明确说明
                - 引用相关原文支持答案
                """,
                
            TaskTemplate.Translate => $"""
                你是一位专业翻译。
                
                任务：将以下内容翻译为 {definition.TargetLanguage ?? "中文"}。
                
                要求：
                - 准确传达原意
                - 保持专业术语一致性
                - 符合目标语言表达习惯
                - 保留原文格式结构
                """,
                
            _ => definition.CustomInstruction ?? "处理以下内容："
        };
    }
    
    public static string GetUserPrompt(LoadedContent content, TaskDefinition definition)
    {
        var sb = new StringBuilder();
        
        // 内容描述
        if (content.Files.Count > 1)
        {
            sb.AppendLine($"以下是 {content.Files.Count} 个文件的内容：");
            sb.AppendLine();
            
            foreach (var file in content.Files)
            {
                sb.AppendLine($"═══════════════════════════════════════");
                sb.AppendLine($"文件: {file.FileName}");
                sb.AppendLine($"═══════════════════════════════════════");
                sb.AppendLine(file.Content);
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("内容");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine(content.CombinedText);
        }
        
        return sb.ToString();
    }
}
```

---

## 🌊 执行流程

```
┌─────────────────────────────────────────────────────────────────────┐
│                        EXECUTION FLOW                                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  1. CREATE PROJECT                                                   │
│     ┌─────────────────────────────────────────────┐                 │
│     │ POST /api/projects                          │                 │
│     │ {                                           │                 │
│     │   "name": "我的项目",                        │                 │
│     │   "content": {                              │                 │
│     │     "directoryPath": "/path/to/docs",       │                 │
│     │     "extensions": [".md", ".txt"],          │                 │
│     │     "recursive": true                       │                 │
│     │   },                                        │                 │
│     │   "task": {                                 │                 │
│     │     "template": "Summarize",                │                 │
│     │     "parameters": { "detailLevel": 0.8 }    │                 │
│     │   },                                        │                 │
│     │   "strategy": "Maker",                      │                 │
│     │   "options": { "reliability": "High" }      │                 │
│     │ }                                           │                 │
│     └─────────────────────────────────────────────┘                 │
│                          │                                           │
│                          ▼                                           │
│  2. LOAD CONTENT                                                     │
│     ┌─────────────────────────────────────────────┐                 │
│     │ ContentLoader.LoadAsync(source)             │                 │
│     │   → Scan files                              │                 │
│     │   → Read contents                           │                 │
│     │   → Estimate tokens                         │                 │
│     │   → Return LoadedContent                    │                 │
│     └─────────────────────────────────────────────┘                 │
│                          │                                           │
│                          ▼                                           │
│  3. BUILD TASK                                                       │
│     ┌─────────────────────────────────────────────┐                 │
│     │ TaskTemplatePrompts.GetSystemPrompt()       │                 │
│     │ TaskTemplatePrompts.GetUserPrompt()         │                 │
│     │   → Inject content into task                │                 │
│     └─────────────────────────────────────────────┘                 │
│                          │                                           │
│                          ▼                                           │
│  4. EXECUTE STRATEGY                                                 │
│     ┌─────────────────────────────────────────────┐                 │
│     │ strategy.ExecuteAsync(task, options)        │                 │
│     │   → MAKER: multi-agent consensus            │                 │
│     │   → C-UoT: analogical thinking              │                 │
│     │   → E-UoT: explore outside thoughts         │                 │
│     │   → T-UoT: challenge hidden rules           │                 │
│     │   → Simple: single LLM call                 │                 │
│     └─────────────────────────────────────────────┘                 │
│                          │                                           │
│                          ▼                                           │
│  5. STREAM RESULTS                                                   │
│     ┌─────────────────────────────────────────────┐                 │
│     │ SSE: /api/projects/{id}/events              │                 │
│     │   → Progress updates                        │                 │
│     │   → Streaming output                        │                 │
│     │   → Final result                            │                 │
│     └─────────────────────────────────────────────┘                 │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 📡 增强的 API

### 创建项目（增强版）

```http
POST /api/projects
Content-Type: application/json

{
  "name": "论文审稿",
  "description": "审阅我的 AI 论文",
  
  "content": {
    "filePath": "/path/to/paper.md"
  },
  
  "task": {
    "template": "Review",
    "parameters": {
      "detailLevel": 0.9,
      "includeQuotes": true
    }
  },
  
  "strategy": "Maker",
  
  "options": {
    "reliability": "High",
    "maxLlmCalls": 100
  }
}
```

### 上传文件

```http
POST /api/upload
Content-Type: multipart/form-data

files[]: paper.md
files[]: appendix.md
```

Response:
```json
{
  "uploadId": "abc123",
  "files": [
    { "name": "paper.md", "path": "/uploads/abc123/paper.md" },
    { "name": "appendix.md", "path": "/uploads/abc123/appendix.md" }
  ]
}
```

### 基于上传创建项目

```http
POST /api/projects
Content-Type: application/json

{
  "name": "论文审稿",
  "content": {
    "uploadId": "abc123"
  },
  "task": { "template": "Review" },
  "strategy": "Maker"
}
```

---

## 🎨 前端 UI 设计

```
┌─────────────────────────────────────────────────────────────────────┐
│                    COGNITIVE MESH WORKBENCH                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │  📁 CONTENT                                                  │    │
│  │  ┌─────────────────────────────────────────────────────────┐│    │
│  │  │ [拖拽文件到此处] 或 [选择文件] [选择文件夹]              ││    │
│  │  │                                                         ││    │
│  │  │ 已加载: paper.md (12,345 tokens)                        ││    │
│  │  │         appendix.md (3,456 tokens)                      ││    │
│  │  │         ─────────────────                               ││    │
│  │  │         总计: 15,801 tokens                             ││    │
│  │  └─────────────────────────────────────────────────────────┘│    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                      │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │  📋 TASK                                                     │    │
│  │  ┌─────────────────────────────────────────────────────────┐│    │
│  │  │ [总结要点] [深度分析] [学术评审] [文学批评]              ││    │
│  │  │ [改写优化] [续写推演] [信息提取] [对比分析]              ││    │
│  │  │ [问答]     [翻译]     [自定义]                           ││    │
│  │  │                                                         ││    │
│  │  │ ☑ 选中: 学术评审                                        ││    │
│  │  │                                                         ││    │
│  │  │ 自定义指令 (可选):                                       ││    │
│  │  │ ┌───────────────────────────────────────────────────┐   ││    │
│  │  │ │ 重点关注方法论部分的严谨性...                      │   ││    │
│  │  │ └───────────────────────────────────────────────────┘   ││    │
│  │  └─────────────────────────────────────────────────────────┘│    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                      │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │  ⚙️ STRATEGY                                                 │    │
│  │  ┌─────────────────────────────────────────────────────────┐│    │
│  │  │ ○ Simple      单次 LLM 调用，快速但可能有遗漏           ││    │
│  │  │ ● MAKER       多 Agent 共识，高可靠性                   ││    │
│  │  │ ○ C-UoT       类比思维，适合创意问题                    ││    │
│  │  │ ○ E-UoT       探索域外思想，发现新视角                  ││    │
│  │  │ ○ T-UoT       挑战假设，颠覆性思考                      ││    │
│  │  │                                                         ││    │
│  │  │ 可靠性: [低] [中] [●高]                                  ││    │
│  │  │ 最大调用: [100]  预估 Token: ~500K                      ││    │
│  │  └─────────────────────────────────────────────────────────┘│    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                      │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │                    [ 🚀 START PROCESSING ]                   │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 🔮 策略选择建议

| 任务 | 推荐策略 | 原因 |
|------|---------|------|
| 论文评审 | MAKER (High) | 需要高可靠性，多角度审视 |
| 小说评价 | Simple 或 MAKER (Low) | 主观性任务，单次足够 |
| 文章总结 | Simple | 简单任务，快速完成 |
| 复杂分析 | MAKER (Medium) | 需要多步骤，防止遗漏 |
| 创意续写 | C-UoT | 需要类比思维，激发创意 |
| 商业创新 | E-UoT / T-UoT | 需要跳出框架思考 |
| 翻译 | Simple | 单一任务，LLM 直接完成 |
| 信息提取 | Simple 或 MAKER (Low) | 结构化任务 |

---

## 📁 新增文件结构

```
Aevatar.Agents.Cognitive.Core/
├── Tasks/
│   ├── TaskTemplate.cs           # 任务模板枚举
│   └── TaskDefinition.cs         # 任务定义
└── (existing files...)

Aevatar.Agents.Cognitive.Content/
├── Content/
│   ├── ContentSource.cs          # 内容来源定义
│   ├── LoadedContent.cs          # 加载结果
│   └── IContentLoader.cs         # 加载器接口
├── Templates/
│   └── TaskTemplatePrompts.cs    # 模板提示词
├── Loaders/
│   └── ContentLoader.cs          # 内容加载器实现
└── Stores/
    └── ProjectStore.cs           # 项目存储
```

---

## 🚀 实现优先级

| 阶段 | 内容 | 优先级 |
|------|------|--------|
| P0 | ContentSource + LoadedContent 抽象 | 🔴 必须 |
| P0 | TaskTemplate 枚举 + 提示词 | 🔴 必须 |
| P0 | ContentLoader 实现 | 🔴 必须 |
| P1 | API 增强 | 🟡 重要 |
| P1 | 前端 UI 升级 | 🟡 重要 |
| P2 | Simple 策略（单次 LLM） | 🟢 有价值 |
| P2 | 文件上传接口 | 🟢 有价值 |

---

*Last Updated: 2025-12-04*

