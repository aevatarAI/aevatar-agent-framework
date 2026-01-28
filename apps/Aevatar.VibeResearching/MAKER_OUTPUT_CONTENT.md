# Maker Workflow 输出内容详解

## 📋 概述

Maker workflow 的输出通过 `ReasoningResult` 对象返回，包含执行结果、统计信息和追踪数据。在 DAG Consensus 场景中，`Content` 字段包含符合特定 JSON schema 的字符串。

---

## 🔄 输出结构

### ReasoningResult 对象

**定义位置**: `src/Aevatar.Agents.Cognitive.Core/Models/ReasoningResult.cs`

**核心字段**:

```csharp
public sealed record ReasoningResult
{
    // ─────────────────────────────────────────────────────────
    //  基本状态
    // ─────────────────────────────────────────────────────────
    
    /// <summary>是否成功</summary>
    public bool Success { get; init; }
    
    /// <summary>最终输出内容（字符串）</summary>
    public string? Content { get; init; }
    
    /// <summary>错误信息（如果失败）</summary>
    public string? Error { get; init; }
    
    /// <summary>执行时长</summary>
    public TimeSpan Duration { get; init; }
    
    // ─────────────────────────────────────────────────────────
    //  Token 统计
    // ─────────────────────────────────────────────────────────
    
    /// <summary>总 LLM 调用次数</summary>
    public int TotalLlmCalls { get; init; }
    
    /// <summary>Prompt Token 总数</summary>
    public long PromptTokens { get; init; }
    
    /// <summary>Completion Token 总数</summary>
    public long CompletionTokens { get; init; }
    
    /// <summary>Token 总数</summary>
    public long TotalTokens => PromptTokens + CompletionTokens;
    
    // ─────────────────────────────────────────────────────────
    //  MAKER 特定追踪
    // ─────────────────────────────────────────────────────────
    
    /// <summary>MAKER: 执行追踪</summary>
    public MakerTrace? MakerTrace { get; init; }
}
```

---

## 📝 Content 字段的内容

### 1. Maker Workflow 的标准输出（maker.yaml）

**定义位置**: `maker.yaml` → `output` 部分（第 298-302 行）

**标准输出结构**:
```yaml
output:
  solution: "{{atomic_solution | default: composed_solution}}"
  is_atomic: "{{atomic_check.is_atomic | default: false}}"
  subtask_count: "{{subtasks | size | default: 0}}"
```

**说明**:
- `solution`: 最终解决方案（字符串）
  - 如果是原子任务：来自 `solve_atomic` 步骤的输出
  - 如果是复杂任务：来自 `compose` 步骤的输出（合成所有子任务的结果）
- `is_atomic`: 是否为原子任务（布尔值）
- `subtask_count`: 子任务数量（整数）

**实际输出示例**（JSON 格式）:
```json
{
  "solution": "The mutation is valid and can be applied...",
  "is_atomic": false,
  "subtask_count": 3
}
```

---

### 2. DAG Consensus 场景的特殊输出

**场景**: 当 Maker workflow 用于 DAG Consensus 时

**Prompt 要求**: `BuildTaskPrompt` 方法中明确要求输出 JSON（第 254 行）

```
Output STRICT JSON ONLY (no markdown, no code fences).
```

**JSON Schema** (第 287-301 行):
```json
{
  "mutationId": "string",
  "author": "string",
  "nodes": [
    {
      "id": "string",
      "type": "axiom|theorem|assumption|hypothesis|unknown",
      "label": "string",
      "proof": "string",
      "tags": {"k": "v"}
    }
  ],
  "edges": [
    {
      "from": "string",
      "to": "string",
      "type": "depends_on"
    }
  ],
  "redFlags": ["string"],
  "rejectionReason": "string",
  "validationOutcome": {
    "proved": boolean,
    "confidence": 0~1,
    "gapDescription": "string",
    "acceptedWithCaveats": boolean
  }
}
```

**实际输出示例**（从 artifact 文件）:
```json
{
  "mutationId": "dag_h1_verification_v1",
  "author": "dag_builder",
  "nodes": [],
  "edges": [],
  "redFlags": [
    "Edge references unknown node 'plan_xxx'",
    "Edge type 'motivated_by' not standard; should be 'depends_on' unless justified"
  ],
  "rejectionReason": "Technical validation failed due to invalid references, non-standard edge types, and logical inconsistency in dependency direction.",
  "validationOutcome": {
    "proved": false,
    "confidence": 0.0,
    "gapDescription": "The mutation contains edges pointing to a node not listed in the mutation nodes, violating referential integrity...",
    "acceptedWithCaveats": false
  }
}
```

**注意**: 
- Maker workflow 的输出可能包含 Markdown 代码块（```json ... ```）
- 也可能包含解释性文本
- 需要通过 `TryExtractJson` 方法提取纯 JSON 字符串

---

## 🔍 MakerTrace 追踪信息

**定义位置**: `src/Aevatar.Agents.Cognitive.Core/Models/ReasoningResult.cs`

**结构**:
```csharp
public sealed record MakerTrace
{
    /// <summary>根任务节点</summary>
    public TaskNode? RootTask { get; init; }
    
    /// <summary>总任务数</summary>
    public int TotalTasks { get; init; }
    
    /// <summary>原子任务数</summary>
    public int AtomicTasks { get; init; }
    
    /// <summary>复杂任务数</summary>
    public int ComplexTasks { get; init; }
    
    /// <summary>总投票轮次</summary>
    public int TotalVotingRounds { get; init; }
    
    /// <summary>总提案数</summary>
    public int TotalProposals { get; init; }
}
```

**TaskNode 结构**:
```csharp
public sealed record TaskNode
{
    /// <summary>任务 ID</summary>
    public required string TaskId { get; init; }
    
    /// <summary>任务描述</summary>
    public string? Description { get; init; }
    
    /// <summary>是否为原子任务</summary>
    public bool IsAtomic { get; init; }
    
    /// <summary>执行结果</summary>
    public string? Result { get; init; }
    
    /// <summary>子任务</summary>
    public IReadOnlyList<TaskNode> Children { get; init; } = [];
    
    /// <summary>投票会话</summary>
    public IReadOnlyList<VotingSession> VotingSessions { get; init; } = [];
}
```

**VotingSession 结构**:
```csharp
public sealed record VotingSession
{
    /// <summary>类型（Decomposition / Solution）</summary>
    public required string Type { get; init; }
    
    /// <summary>轮次</summary>
    public int Rounds { get; init; }
    
    /// <summary>获胜者</summary>
    public VotingCandidate? Winner { get; init; }
    
    /// <summary>所有候选</summary>
    public IReadOnlyList<VotingCandidate> Candidates { get; init; } = [];
}
```

**用途**: 
- 追踪 Maker workflow 的执行过程
- 分析任务分解、投票共识的详细信息
- 调试和性能分析

---

## 📊 完整输出示例

### 成功场景

**ReasoningResult**:
```csharp
{
    Success = true,
    Content = "{\"mutationId\":\"dag_h1_verification_v1\",\"author\":\"dag_builder\",\"nodes\":[],\"edges\":[],\"redFlags\":[...],\"rejectionReason\":\"...\",\"validationOutcome\":{...}}",
    Error = null,
    Duration = TimeSpan.FromMilliseconds(48317),
    TotalLlmCalls = 11,
    PromptTokens = 11809,
    CompletionTokens = 11809,
    TotalTokens = 23618,
    MakerTrace = {
        RootTask = {...},
        TotalTasks = 1,
        AtomicTasks = 1,
        ComplexTasks = 0,
        TotalVotingRounds = 3,
        TotalProposals = 9
    }
}
```

### 失败场景

**ReasoningResult**:
```csharp
{
    Success = false,
    Content = null,
    Error = "maker workflow execution failed: timeout",
    Duration = TimeSpan.FromMilliseconds(60000),
    TotalLlmCalls = 5,
    PromptTokens = 5000,
    CompletionTokens = 0,
    TotalTokens = 5000,
    MakerTrace = null
}
```

---

## 🎯 在 DAG Consensus 中的使用

### Step 1: 获取 ReasoningResult

**代码位置**: `DagConsensusRunner.cs` → `RunMakerAsync` (第 132-135 行)

```csharp
ReasoningResult rr;
try
{
    rr = await _cognitive.ExecuteAsync(task, options, progress: input.Progress, ct: ct);
}
```

### Step 2: 检查成功状态

**代码位置**: `DagConsensusRunner.cs` → `RunMakerAsync` (第 150-162 行)

```csharp
if (!rr.Success || string.IsNullOrWhiteSpace(rr.Content))
{
    var err = rr.Error ?? "maker failed";
    return await FailMakerAsync(..., error: err, ...);
}
```

### Step 3: 提取 JSON

**代码位置**: `DagConsensusRunner.cs` → `RunMakerAsync` (第 164-176 行)

```csharp
var raw = rr.Content!;
if (!TryExtractJson(raw, out var json))
{
    return await FailMakerAsync(..., error: "failed to extract json from maker output", ...);
}
```

### Step 4: 反序列化

**代码位置**: `DagConsensusRunner.cs` → `RunMakerAsync` (第 178-194 行)

```csharp
DagMutationJson? parsed;
try
{
    parsed = JsonSerializer.Deserialize<DagMutationJson>(json!, Json);
}
catch (Exception ex)
{
    return await FailMakerAsync(..., error: ex.Message, ...);
}
```

### Step 5: 保存到 Artifact

**代码位置**: `DagConsensusRunner.cs` → `WriteArtifactAsync` (第 420-464 行)

**保存的内容**:
- `rr.Content`: 原始输出（最多 30,000 字符）
- `rr.Success`: 执行是否成功
- `rr.Duration`: 执行时长
- `rr.TotalLlmCalls`: LLM 调用次数
- `rr.PromptTokens`, `rr.CompletionTokens`, `rr.TotalTokens`: Token 统计

---

## 📋 输出内容总结

### 1. Content 字段（主要输出）

**标准 Maker 场景**:
- JSON 对象，包含 `solution`, `is_atomic`, `subtask_count`

**DAG Consensus 场景**:
- JSON 字符串，符合 DAG mutation schema
- 包含 `mutationId`, `author`, `nodes`, `edges`, `redFlags`, `rejectionReason`, `validationOutcome`

**格式**:
- 可能是纯 JSON 字符串
- 可能包含在 Markdown 代码块中（```json ... ```）
- 可能包含解释性文本

### 2. 统计信息

- `Success`: 执行是否成功
- `Duration`: 执行时长
- `TotalLlmCalls`: LLM 调用次数
- `PromptTokens`: Prompt tokens
- `CompletionTokens`: Completion tokens
- `TotalTokens`: 总 tokens

### 3. 追踪信息（可选）

- `MakerTrace`: MAKER 执行追踪
  - 任务分解树
  - 投票会话详情
  - 提案和共识信息

### 4. 错误信息（如果失败）

- `Error`: 错误描述

---

## 🔍 关键点

### 1. Content 字段是字符串

- `ReasoningResult.Content` 是 `string?` 类型
- 即使输出是 JSON，也是以字符串形式存储
- 需要手动解析（`JsonSerializer.Deserialize`）

### 2. 输出格式取决于 Prompt

- Maker workflow 的输出格式由 prompt 中的要求决定
- DAG Consensus 场景中，prompt 明确要求输出 JSON
- 标准 Maker 场景中，输出是 workflow 定义的格式

### 3. 需要提取 JSON

- Maker workflow 的输出可能包含 Markdown 代码块
- 需要使用 `TryExtractJson` 方法提取纯 JSON
- 提取算法从后往前查找最后一个完整的 JSON 对象

### 4. 追踪信息可选

- `MakerTrace` 只在 MAKER 策略中使用
- 其他策略（如 UoT）有不同的追踪结构
- 追踪信息用于调试和性能分析

---

## 💡 实际使用示例

### 查看 Maker 输出

```csharp
var rr = await _cognitive.ExecuteAsync(task, options, ...);

// 检查成功
if (!rr.Success)
{
    Console.WriteLine($"Maker failed: {rr.Error}");
    return;
}

// 查看输出内容
Console.WriteLine($"Content: {rr.Content}");

// 查看统计信息
Console.WriteLine($"Duration: {rr.Duration.TotalMilliseconds}ms");
Console.WriteLine($"LLM Calls: {rr.TotalLlmCalls}");
Console.WriteLine($"Tokens: {rr.TotalTokens}");

// 查看追踪信息（如果有）
if (rr.MakerTrace != null)
{
    Console.WriteLine($"Total Tasks: {rr.MakerTrace.TotalTasks}");
    Console.WriteLine($"Voting Rounds: {rr.MakerTrace.TotalVotingRounds}");
}
```

### 提取和解析 JSON

```csharp
var raw = rr.Content!;

// 提取 JSON
if (!TryExtractJson(raw, out var json))
{
    Console.WriteLine("Failed to extract JSON");
    return;
}

// 解析 JSON
var parsed = JsonSerializer.Deserialize<DagMutationJson>(json!);
Console.WriteLine($"MutationId: {parsed.MutationId}");
Console.WriteLine($"RedFlags: {parsed.RedFlags.Count}");
```

---

## 📝 总结

Maker workflow 的输出包含：

1. **Content** (主要输出):
   - 标准场景：`{solution, is_atomic, subtask_count}`
   - DAG Consensus：符合 mutation schema 的 JSON

2. **统计信息**:
   - Success, Duration, TotalLlmCalls
   - PromptTokens, CompletionTokens, TotalTokens

3. **追踪信息** (可选):
   - MakerTrace（任务分解树、投票会话）

4. **错误信息** (如果失败):
   - Error

**关键点**:
- Content 是字符串，需要手动解析
- 输出格式取决于 prompt 要求
- 可能需要提取 JSON（处理 Markdown 代码块）
- 追踪信息用于调试和分析

---

*最后更新: 2025-01-28*
