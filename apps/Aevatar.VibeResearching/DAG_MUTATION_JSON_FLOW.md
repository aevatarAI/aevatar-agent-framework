# DagMutationJson 获取流程详解

## 📋 概述

`DagMutationJson` 是通过解析 **maker workflow** 的输出得到的。整个流程涉及多个步骤：从构建 prompt、执行 Cognitive DSL workflow、提取 JSON、到反序列化为 C# 对象。

---

## 🔄 完整流程

### Step 1: 构建 Task Prompt

**代码位置**: `DagConsensusRunner.cs` → `BuildTaskPrompt` (第 229-303 行)

**方法**:
```csharp
private static string BuildTaskPrompt(ConsensusInput input)
```

**作用**: 构建发送给 maker workflow 的 prompt，包含：
- Candidate mutation 摘要
- 当前 DAG 统计信息
- Materials context（如果有）
- **输出 JSON schema**（定义了期望的输出格式）

**关键部分 - JSON Schema**:
```json
{
  "mutationId": "string",
  "author": "string",
  "nodes": [...],
  "edges": [...],
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

---

### Step 2: 执行 Maker Workflow

**代码位置**: `DagConsensusRunner.cs` → `RunMakerAsync` (第 132-135 行)

**方法**:
```csharp
rr = await _cognitive.ExecuteAsync(task, options, progress: input.Progress, ct: ct);
```

**参数**:
- `task`: Step 1 构建的 prompt 字符串
- `options`: `ReasoningOptions`，包含：
  - `CognitiveWorkflow = "maker"`（指定使用 maker workflow）
  - `ProviderName`: LLM 提供者名称
  - `CognitiveConsensusK`: 共识 K 值
  - `CognitiveMaxRounds`: 最大轮数
  - `CognitiveWorkerCount`: Worker 数量
  - `CognitiveMaxDepth`: 最大深度

**返回**: `ReasoningResult` (`rr`)，包含：
- `Success`: 是否成功
- `Content`: **Maker workflow 的原始输出**（字符串）
- `Error`: 错误信息（如果有）
- `Duration`: 执行时长
- `TotalLlmCalls`: LLM 调用次数
- `PromptTokens`, `CompletionTokens`, `TotalTokens`: Token 统计

**Maker Workflow 执行过程**:
1. 加载 `maker.yaml` workflow 定义
2. 执行 Cognitive DSL workflow（包含 decompose、fan_out、vote、compose 等步骤）
3. 最终输出一个 JSON 字符串（符合 Step 1 中定义的 schema）

---

### Step 3: 提取 JSON 字符串

**代码位置**: `DagConsensusRunner.cs` → `RunMakerAsync` (第 164-176 行)

**方法**:
```csharp
var raw = rr.Content!;
if (!TryExtractJson(raw, out var json))
{
    // 失败处理
}
```

**`TryExtractJson` 方法** (第 508-543 行):

**作用**: 从 maker workflow 的原始输出中提取 JSON 字符串

**算法**:
1. **简单情况**: 如果整个字符串就是 JSON（以 `{` 或 `[` 开头，以 `}` 或 `]` 结尾），直接返回
2. **复杂情况**: 
   - 从后往前查找最后一个 `}` 或 `]`
   - 从该位置往前查找匹配的 `{` 或 `[`
   - 提取候选 JSON 字符串
   - 使用 `IsValidJson` 验证是否为有效 JSON
   - 返回第一个有效的 JSON 字符串

**为什么需要提取**:
- Maker workflow 的输出可能包含：
  - Markdown 代码块（```json ... ```）
  - 解释性文本
  - 多个 JSON 对象
  - 其他格式的内容

**示例**:
```
Here is the validated mutation:

```json
{
  "mutationId": "dag_h1_verification_v1",
  "author": "dag_builder",
  "nodes": [],
  "edges": [],
  "redFlags": [...]
}
```

This mutation was blocked due to...
```

`TryExtractJson` 会提取中间的 JSON 部分。

---

### Step 4: 反序列化为 DagMutationJson

**代码位置**: `DagConsensusRunner.cs` → `RunMakerAsync` (第 178-194 行)

**方法**:
```csharp
DagMutationJson? parsed;
try
{
    parsed = JsonSerializer.Deserialize<DagMutationJson>(json!, Json);
}
catch (Exception ex)
{
    // 失败处理
}
```

**`DagMutationJson` 类定义** (第 558-567 行):
```csharp
private sealed class DagMutationJson
{
    public string? MutationId { get; init; }
    public string? Author { get; init; }
    public List<NodeJson>? Nodes { get; init; }
    public List<EdgeJson>? Edges { get; init; }
    public List<string>? RedFlags { get; init; }
    public string? RejectionReason { get; init; }
    public ValidationOutcomeJson? ValidationOutcome { get; init; }
}
```

**`ValidationOutcomeJson` 类定义** (第 569-575 行):
```csharp
private sealed class ValidationOutcomeJson
{
    public bool? Proved { get; init; }
    public double? Confidence { get; init; }
    public string? GapDescription { get; init; }
    public bool? AcceptedWithCaveats { get; init; }
}
```

**`JsonSerializerOptions`** (`Json` 字段，第 43-48 行):
```csharp
private static readonly JsonSerializerOptions Json = new()
{
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
```

**关键特性**:
- `PropertyNameCaseInsensitive = true`: JSON 属性名大小写不敏感
- `AllowTrailingCommas = true`: 允许尾随逗号
- `ReadCommentHandling = JsonCommentHandling.Skip`: 跳过注释
- `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`: 正确处理 Unicode 字符

**反序列化过程**:
1. `System.Text.Json.JsonSerializer.Deserialize` 将 JSON 字符串解析为 `DagMutationJson` 对象
2. 如果 JSON 中的字段在 `DagMutationJson` 类中不存在，会被忽略（不会报错）
3. 如果 JSON 中的字段类型不匹配，会抛出异常

---

### Step 5: 使用 DagMutationJson

**代码位置**: `DagConsensusRunner.cs` → `RunMakerAsync` (第 196-210 行)

**用途**:

#### 5.1 提取 RedFlags

```csharp
var redFlags = (parsed?.RedFlags ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
```

如果 `redFlags` 不为空，说明 mutation 被阻止。

#### 5.2 构建 SraDagMutation

```csharp
var mutation = BuildMutation(ws.SessionId, input.Candidate, parsed);
```

**`BuildMutation` 方法** (第 305-377 行):

**作用**: 将 `DagMutationJson` 转换为 `SraDagMutation`（用于应用到 DAG）

**转换过程**:
1. 创建 `SraDagMutation` 对象
2. 设置 `MutationId`（从 `parsed.MutationId` 或生成新的）
3. 设置 `AuthorAgent`（从 `parsed.Author` 或使用 `MakerWorkflow`）
4. 转换 `Nodes`:
   - 遍历 `parsed.Nodes`
   - 创建 `SraDagNode` 对象
   - 设置 `Id`, `Type`（通过 `ParseNodeType` 解析）, `Label`（最多 200 字符）, `Proof`（最多 2000 字符）, `Tags`（每个值最多 200 字符）
5. 转换 `Edges`:
   - 遍历 `parsed.Edges`
   - 创建 `SraDagEdge` 对象
   - 设置 `FromId`, `ToId`, `Type`（默认为 `"depends_on"`）

#### 5.3 保存到 Artifact

```csharp
var artifact = await WriteArtifactAsync(ws, MakerWorkflow, input, rr, extractedJson: json, parsed, redFlags: [], error: null, ct);
```

**`WriteArtifactAsync` 方法** (第 400-470 行):

**作用**: 将 consensus 结果保存到 artifact 文件

**保存的内容**:
- `sessionId`, `runId`, `workflow`
- `status` (accepted/blocked)
- `error`, `redFlags`
- `stats` (LLM 统计信息)
- `candidate` (原始 candidate 摘要)
- `extractedJson` (提取的 JSON 字符串)
- `parsed` (反序列化后的 `DagMutationJson` 对象)
- **`rejectionReason`** (从 `parsed.RejectionReason` 提取)
- **`validationOutcome`** (从 `parsed.ValidationOutcome` 提取)
- `rawOutput` (原始输出，最多 30,000 字符)

---

## 📊 流程图

```
┌─────────────────────────────────────────────────────────────┐
│ Step 1: BuildTaskPrompt                                     │
│ - 构建包含 JSON schema 的 prompt                             │
│ - 包含 candidate mutation 摘要和 DAG 统计                    │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ Step 2: ExecuteAsync (Maker Workflow)                       │
│ - 执行 Cognitive DSL workflow "maker"                        │
│ - 返回 ReasoningResult (包含 Content 字符串)               │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ Step 3: TryExtractJson                                      │
│ - 从原始输出中提取 JSON 字符串                              │
│ - 处理 Markdown 代码块、多个 JSON 等情况                    │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ Step 4: JsonSerializer.Deserialize<DagMutationJson>         │
│ - 将 JSON 字符串反序列化为 DagMutationJson 对象             │
│ - 使用 PropertyNameCaseInsensitive 等选项                   │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│ Step 5: 使用 DagMutationJson                                │
│ - 提取 RedFlags                                             │
│ - 构建 SraDagMutation (BuildMutation)                      │
│ - 保存到 Artifact (WriteArtifactAsync)                      │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔍 关键点

### 1. JSON Schema 定义

**位置**: `BuildTaskPrompt` 方法中的 prompt

**作用**: 指导 LLM 生成符合预期格式的 JSON

**包含字段**:
- `mutationId`, `author`
- `nodes[]`, `edges[]`
- `redFlags[]`
- `rejectionReason` ⭐ (新增)
- `validationOutcome` ⭐ (新增)

### 2. JSON 提取算法

**位置**: `TryExtractJson` 方法

**特点**:
- 容错性强：处理 Markdown 代码块、多个 JSON、解释性文本
- 从后往前查找：找到最后一个完整的 JSON 对象
- 验证有效性：使用 `JsonDocument.Parse` 验证

### 3. 反序列化配置

**位置**: `Json` 静态字段

**关键选项**:
- `PropertyNameCaseInsensitive = true`: 允许 `mutationId` 和 `MutationId` 都能匹配
- `AllowTrailingCommas = true`: 允许 JSON 中有尾随逗号
- `ReadCommentHandling = JsonCommentHandling.Skip`: 跳过 JSON 注释
- `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`: 正确处理 Unicode（如中文）

### 4. 字段映射

**JSON → C#**:
- `mutationId` → `MutationId`
- `author` → `Author`
- `nodes` → `Nodes` (List<NodeJson>)
- `edges` → `Edges` (List<EdgeJson>)
- `redFlags` → `RedFlags` (List<string>)
- `rejectionReason` → `RejectionReason` ⭐
- `validationOutcome` → `ValidationOutcome` (ValidationOutcomeJson) ⭐

### 5. 数据流

```
Maker Workflow Output (字符串)
    ↓
TryExtractJson
    ↓
JSON 字符串
    ↓
JsonSerializer.Deserialize<DagMutationJson>
    ↓
DagMutationJson 对象
    ↓
提取字段 (RedFlags, RejectionReason, ValidationOutcome)
    ↓
构建 SraDagMutation (BuildMutation)
    ↓
保存到 Artifact (WriteArtifactAsync)
```

---

## ⚠️ 错误处理

### 1. JSON 提取失败

**条件**: `TryExtractJson` 返回 `false`

**处理**: 
```csharp
return await FailMakerAsync(..., redFlags: [ErrorJsonParseFailed], error: "failed to extract json from maker output", ...);
```

**原因**:
- Maker workflow 输出格式不符合预期
- 输出中没有有效的 JSON

### 2. JSON 反序列化失败

**条件**: `JsonSerializer.Deserialize` 抛出异常

**处理**:
```csharp
catch (Exception ex)
{
    return await FailMakerAsync(..., redFlags: [ErrorJsonDeserializeFailed], error: ex.Message, ...);
}
```

**原因**:
- JSON 格式错误（语法错误）
- 字段类型不匹配
- 缺少必需的字段（如果标记为 required）

### 3. RedFlags 不为空

**条件**: `parsed.RedFlags` 不为空

**处理**:
```csharp
if (redFlags.Count > 0)
{
    return await FailMakerAsync(..., redFlags, error: ErrorBlockedByRedFlags, ...);
}
```

**原因**:
- Maker workflow 检测到 mutation 有问题
- 设置了 `redFlags` 来阻止 mutation

---

## 📝 示例

### Maker Workflow 输出示例

```
Here is the validated mutation:

```json
{
  "mutationId": "dag_h1_verification_v1",
  "author": "dag_builder",
  "nodes": [],
  "edges": [],
  "redFlags": [
    "Edge references unknown node 'plan_xxx'",
    "Edge type 'motivated_by' not standard"
  ],
  "rejectionReason": "Technical validation failed due to invalid references and non-standard edge types.",
  "validationOutcome": {
    "proved": false,
    "confidence": 0.0,
    "gapDescription": "The mutation contains edges pointing to a node not listed in the mutation nodes...",
    "acceptedWithCaveats": false
  }
}
```

This mutation was blocked due to the issues listed above.
```

### 提取后的 JSON 字符串

```json
{
  "mutationId": "dag_h1_verification_v1",
  "author": "dag_builder",
  "nodes": [],
  "edges": [],
  "redFlags": [
    "Edge references unknown node 'plan_xxx'",
    "Edge type 'motivated_by' not standard"
  ],
  "rejectionReason": "Technical validation failed due to invalid references and non-standard edge types.",
  "validationOutcome": {
    "proved": false,
    "confidence": 0.0,
    "gapDescription": "The mutation contains edges pointing to a node not listed in the mutation nodes...",
    "acceptedWithCaveats": false
  }
}
```

### 反序列化后的 DagMutationJson 对象

```csharp
DagMutationJson {
    MutationId = "dag_h1_verification_v1",
    Author = "dag_builder",
    Nodes = [],
    Edges = [],
    RedFlags = [
        "Edge references unknown node 'plan_xxx'",
        "Edge type 'motivated_by' not standard"
    ],
    RejectionReason = "Technical validation failed due to invalid references and non-standard edge types.",
    ValidationOutcome = ValidationOutcomeJson {
        Proved = false,
        Confidence = 0.0,
        GapDescription = "The mutation contains edges pointing to a node not listed in the mutation nodes...",
        AcceptedWithCaveats = false
    }
}
```

---

## 🎯 总结

`DagMutationJson` 的获取流程：

1. **构建 Prompt**: 包含 JSON schema 定义
2. **执行 Maker Workflow**: 生成符合 schema 的 JSON 字符串
3. **提取 JSON**: 从原始输出中提取 JSON 字符串
4. **反序列化**: 将 JSON 字符串转换为 `DagMutationJson` 对象
5. **使用**: 提取字段、构建 mutation、保存到 artifact

**关键点**:
- JSON schema 定义了期望的输出格式
- `TryExtractJson` 处理各种输出格式
- `JsonSerializer.Deserialize` 将 JSON 转换为 C# 对象
- `DagMutationJson` 类定义了反序列化的目标结构

---

*最后更新: 2025-01-28*
