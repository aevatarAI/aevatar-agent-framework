# Red-Flag 工作流程详解

## 📋 概述

Red-Flag 机制是 MAKER System 中的内容验证与恢复机制，用于检测和拒绝低质量的 LLM 输出。它在投票共识过程中起到质量保证的作用。

**核心思想**: 在投票之前，先检查每个提案的质量，拒绝有明显问题的提案，只对高质量提案进行投票。

---

## 🎯 Red-Flag 的作用

1. **质量保证**: 防止低质量、错误或格式不正确的输出进入投票环节
2. **效率提升**: 提前过滤问题提案，避免浪费投票轮次
3. **错误恢复**: 检测到 red flag 后，可以触发恢复机制，重新生成提案

---

## 🔄 Red-Flag 工作流程

### 在 MAKER Workflow 中的位置

```
vote 步骤执行流程:
  ↓
1. 并行生成提案 (fan_out)
   ├─> Worker 1 生成提案 A
   ├─> Worker 2 生成提案 B
   ├─> Worker 3 生成提案 C
   └─> ...
  ↓
2. Red-Flag 检查 (对每个提案)
   ├─> 检查提案 A → 通过/拒绝
   ├─> 检查提案 B → 通过/拒绝
   ├─> 检查提案 C → 通过/拒绝
   └─> ...
  ↓
3. 过滤通过的提案
   ├─> 只保留通过 Red-Flag 检查的提案
   └─> 被拒绝的提案不参与投票
  ↓
4. 投票共识
   ├─> 对通过的提案进行投票
   └─> First-to-ahead-by-K 机制
```

---

## 🔍 Red-Flag 检查项

### 1. 长度检查 (`max_length`)

**配置位置**: `maker.yaml` → `defaults.vote.red_flag.max_length`

**默认值**: `102400` 字符

**检查逻辑**:
```yaml
red_flag:
  max_length: 102400  # 超长判红旗
```

**触发条件**:
- 如果输出长度 > `max_length`，标记为 redFlag

**原因**: 防止 LLM 生成过长的输出，可能导致：
- 性能问题
- 解析困难
- 内容质量下降

**示例**:
```
提案长度: 150000 字符
max_length: 102400
→ Red Flag: "内容过长 (150000 > 102400 字符)"
```

---

### 2. 解析检查 (`strict_parse`)

**配置位置**: `maker.yaml` → `defaults.vote.red_flag.strict_parse`

**默认值**: `true`

**检查逻辑**:
```yaml
red_flag:
  strict_parse: true  # 解析失败视为红旗
```

**触发条件**:
- 如果 JSON 解析失败，标记为 redFlag
- 如果输出格式不符合要求，标记为 redFlag

**原因**: 确保输出格式正确，便于后续处理

**示例**:
```
期望格式: JSON
实际输出: "This is plain text"
→ Red Flag: "JSON 解析失败"
```

---

### 3. 内容质量检查 (`strategy`)

**配置位置**: `maker.yaml` → `vote.red_flag.strategy`

**可选值**: `"english"` | `"chinese"` | `"code"` | `"noop"`

**默认值**: `"english"` (在 maker.yaml 中)

**检查逻辑**:

#### Strategy: `english`

**实现位置**: `IRedFlagStrategy.cs` → `EnglishRedFlagStrategy`

**检查项**:
1. **空内容检查**:
   - 如果内容为空，标记为 redFlag

2. **长度检查**:
   - 如果长度 > `MaxContentLength`，标记为 redFlag
   - 如果长度 < `MinContentLength`，标记为 redFlag

3. **拒绝检测** (`EnableRefusalDetection`):
   - 检测 LLM 拒绝回答的模式
   - 常见模式: "I cannot", "I'm sorry", "As an AI", "I don't have the ability"
   - 如果检测到拒绝模式，标记为 redFlag

4. **退化检测** (`EnableDegenerationDetection`):
   - 检测过度重复的内容
   - 如果同一段文本重复超过阈值，标记为 redFlag

**示例**:
```
输出: "I cannot help with this task."
→ Red Flag: "检测到LLM拒绝: 'I cannot'"
```

#### Strategy: `chinese`

**实现位置**: `IRedFlagStrategy.cs` → `ChineseRedFlagStrategy`

**检查项**:
- 类似 `english`，但使用中文拒绝模式
- 常见模式: "我无法", "我不能", "抱歉", "对不起", "作为AI"

**示例**:
```
输出: "我无法完成这个任务。"
→ Red Flag: "检测到LLM拒绝: '我无法'"
```

#### Strategy: `code`

**实现位置**: `IRedFlagStrategy.cs` → `CodeAwareRedFlagStrategy`

**特点**:
- 放宽长度限制（`MaxContentLength: 50000`）
- 禁用拒绝检测（代码注释可能包含 "I cannot"）
- 放宽退化检测（代码有合理的重复）

#### Strategy: `noop`

**实现位置**: `IRedFlagStrategy.cs` → `NoOpRedFlagStrategy`

**特点**:
- 不进行任何检查
- 接受所有内容
- 用于禁用 red flag 检查

---

### 4. 数量限制 (`max_red_flags`)

**配置位置**: `maker.yaml` → `defaults.vote.red_flag.max_red_flags`

**默认值**: `20`

**检查逻辑**:
```yaml
red_flag:
  max_red_flags: 20  # 红旗上限
```

**触发条件**:
- 如果 redFlags 数量 > `max_red_flags`，停止处理

**原因**: 防止无限循环，如果所有提案都被拒绝，停止尝试

---

## 📊 Red-Flag 在不同场景中的使用

### 场景 1: maker workflow 中的 vote 步骤

**配置示例**:
```yaml
- id: solve_atomic
  type: vote
  red_flag:
    strategy: english
    max_length: 102400
    strict_parse: true
    max_red_flags: 20
  generator:
    type: llm_call
    max_length: 102400
    strict_parse: true
```

**工作流程**:
1. `generator` (llm_call) 生成提案
2. `red_flag` 检查提案质量
3. 如果通过，进入投票环节
4. 如果被拒绝，重新生成（直到 `max_red_flags` 上限）

---

### 场景 2: verifier-quorum 中的 redFlags

**代码位置**: `DagConsensusRunner.Quorum.cs`

**检查逻辑**:
```csharp
var redFlags = (parsed?.RedFlags ?? [])
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => x.Trim())
    .ToList();

// 软警告过滤（不阻塞）
var soft = redFlags.Where(IsSoftMissingEvidenceFlag).ToList();
redFlags = redFlags.Where(x => !IsSoftMissingEvidenceFlag(x)).ToList();

// 如果 approve=true 但 redFlags 存在，视为阻塞
if (redFlags.Count > 0)
    approve = false;
```

**特点**:
- Verifier 在输出中明确列出 `redFlags`
- 区分软警告（missing evidence）和硬阻塞（hard blockers）
- 只有硬阻塞才会导致投票失败

**软警告示例**:
- "Missing evidence for claim X"（缺少证据，但不阻塞）

**硬阻塞示例**:
- "Edge type 'motivated_by' is not a standard dependency type"
- "Node 'xxx' has type 'Unknown' which is not valid"

---

### 场景 3: maker 输出中的 redFlags

**代码位置**: `DagConsensusRunner.cs` → `RunMakerAsync`

**检查逻辑**:
```csharp
var redFlags = (parsed?.RedFlags ?? [])
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => x.Trim())
    .ToList();

if (redFlags.Count > 0)
{
    return await FailMakerAsync(
        ws, input, rr, extractedJson: json, parsed,
        redFlags, error: ErrorBlockedByRedFlags, ct);
}
```

**特点**:
- Maker workflow 的输出中包含 `redFlags` 字段
- 如果 `redFlags` 不为空，整个 consensus 失败
- 返回 `ConsensusResult` 标记为 `Blocked=true`

---

## 🔄 Red-Flag 处理流程

### 在 Vote 步骤中

```
1. 生成提案
   ↓
2. Red-Flag 检查
   ├─> 通过 → 进入投票池
   └─> 拒绝 → 记录 redFlag 原因
   ↓
3. 检查 redFlag 数量
   ├─> 如果 redFlag 数量 < max_red_flags
   │     └─> 重新生成提案（回到步骤 1）
   └─> 如果 redFlag 数量 >= max_red_flags
         └─> 停止处理，返回失败
   ↓
4. 投票共识（只对通过的提案）
   ├─> First-to-ahead-by-K
   └─> 选择最佳提案
```

---

### 在 Maker Workflow 输出中

```
1. Maker workflow 执行完成
   ↓
2. 解析输出 JSON
   ├─> 提取 redFlags 字段
   └─> 检查 redFlags 是否为空
   ↓
3. 决策
   ├─> 如果 redFlags 为空
   │     └─> 构建 mutation，返回成功
   └─> 如果 redFlags 不为空
         └─> 返回失败，包含 redFlags 列表
```

---

## 📝 Red-Flag 配置详解

### 默认配置 (`maker.yaml`)

```yaml
defaults:
  vote:
    red_flag:
      max_length: 102400      # 超长判红旗
      strict_parse: true      # 解析失败视为红旗
      max_red_flags: 20       # 红旗上限
  llm_call:
    max_length: 102400        # 超长判红旗
    strict_parse: true        # 解析失败视为红旗
```

### 步骤级配置（可覆盖）

```yaml
- id: solve_atomic
  type: vote
  red_flag:
    strategy: english         # 内容质量检查策略
    max_length: 102400       # 覆盖默认值
    strict_parse: true       # 覆盖默认值
    max_red_flags: 20        # 覆盖默认值
```

---

## 🎯 Red-Flag 策略对比

| 策略 | 长度检查 | 拒绝检测 | 退化检测 | 适用场景 |
|------|---------|---------|---------|---------|
| **english** | ✅ | ✅ | ✅ | 英文文本、通用内容 |
| **chinese** | ✅ | ✅ (中文) | ✅ | 中文文本 |
| **code** | ✅ (放宽) | ❌ | ✅ (放宽) | 代码内容 |
| **noop** | ❌ | ❌ | ❌ | 禁用检查 |

---

## 🔍 Red-Flag 检测示例

### 示例 1: 长度超限

```
提案内容: [150000 字符的文本]
max_length: 102400
→ Red Flag: "内容过长 (150000 > 102400 字符)"
→ 处理: 拒绝提案，重新生成
```

### 示例 2: JSON 解析失败

```
期望格式: JSON
实际输出: "This is a plain text response"
→ Red Flag: "JSON 解析失败"
→ 处理: 拒绝提案，重新生成
```

### 示例 3: LLM 拒绝回答

```
输出: "I'm sorry, but I cannot help with this task."
拒绝模式: "I'm sorry"
→ Red Flag: "检测到LLM拒绝: 'I'm sorry'"
→ 处理: 拒绝提案，重新生成
```

### 示例 4: 内容退化（过度重复）

```
输出: "The answer is yes. The answer is yes. The answer is yes..."
重复次数: 50 次
阈值: 10 次
→ Red Flag: "检测到内容退化: 过度重复"
→ 处理: 拒绝提案，重新生成
```

### 示例 5: Maker 输出中的 redFlags

```json
{
  "mutationId": "dag_h1_verification_v1",
  "author": "dag_builder",
  "nodes": [],
  "edges": [],
  "redFlags": [
    "Edge type 'motivated_by' is not a standard dependency type",
    "Node 'def_z_mapping_v1' has type 'Unknown' which is not valid"
  ]
}
```

**处理**:
- 检测到 `redFlags` 不为空
- 返回 `ConsensusResult`，标记为 `Blocked=true`
- 保存 artifact 文件，包含 redFlags 列表

---

## 🔧 Red-Flag 恢复机制

### 在 Vote 步骤中

**代码位置**: `IRedFlagHandler.cs` → `DefaultRedFlagHandler`

**恢复策略**:

1. **重新生成提案**:
   - 如果 redFlag 数量 < `max_red_flags`
   - 重新调用 `generator` (llm_call)
   - 最多尝试 `max_red_flags` 次

2. **接受最佳努力结果**:
   - 如果达到最大尝试次数
   - 且有 `BestCandidate`，接受它
   - 返回 `RecoveryAction.AcceptBestEffort`

3. **中止处理**:
   - 如果达到最大尝试次数
   - 且没有 `BestCandidate`
   - 返回 `RecoveryAction.Abort`

---

## 📊 Red-Flag 在 Artifact 文件中的记录

### Maker Artifact

```json
{
  "workflow": "maker",
  "status": "blocked",
  "redFlags": [
    "Edge type 'motivated_by' is not a standard dependency type",
    "Node 'def_z_mapping_v1' has type 'Unknown' which is not valid"
  ],
  "parsed": {
    "RedFlags": [
      "Edge type 'motivated_by' is not a standard dependency type",
      "Node 'def_z_mapping_v1' has type 'Unknown' which is not valid"
    ]
  }
}
```

### Verifier-Quorum Artifact

```json
{
  "workflow": "verifier-quorum",
  "decision": "blocked",
  "votes": [
    {
      "approve": false,
      "redFlags": [
        "Edge type 'motivated_by' is not a standard dependency type"
      ],
      "notes": "..."
    }
  ]
}
```

---

## 💡 最佳实践

### 1. 合理设置 max_length

- **过小**: 可能误杀正常的长文本输出
- **过大**: 可能允许过长的低质量输出
- **建议**: 根据实际需求调整（默认 102400 字符）

### 2. 启用 strict_parse

- **原因**: 确保输出格式正确，便于后续处理
- **注意**: 如果输出不是 JSON，需要禁用或调整策略

### 3. 选择合适的 strategy

- **英文内容**: 使用 `english`
- **中文内容**: 使用 `chinese`
- **代码内容**: 使用 `code`
- **禁用检查**: 使用 `noop`

### 4. 设置合理的 max_red_flags

- **过小**: 可能过早放弃
- **过大**: 可能浪费资源
- **建议**: 默认 20 次，根据实际情况调整

---

## 🔍 调试 Red-Flag

### 查看 Artifact 文件

**位置**: `workspace/sessions/{sessionId}/artifacts/dag/consensus/*.json`

**关键字段**:
- `redFlags`: Red flag 列表
- `status`: "accepted" | "blocked"
- `parsed.RedFlags`: 解析后的 redFlags

### 日志

**关键日志**:
```
[Cognitive] Red flag detected: {Reason}
[Cognitive] Proposal rejected: {ProposalId}, reason: {Reason}
[Cognitive] Retrying proposal generation (attempt {Attempt}/{MaxAttempts})
```

---

## 📝 总结

### Red-Flag 工作流程

1. **生成提案** → LLM 生成内容
2. **Red-Flag 检查** → 检查长度、解析、内容质量
3. **决策** → 通过/拒绝
4. **恢复** → 如果拒绝，重新生成（最多 `max_red_flags` 次）
5. **投票** → 只对通过的提案进行投票

### 关键配置

- `max_length`: 长度限制
- `strict_parse`: 解析检查
- `strategy`: 内容质量检查策略
- `max_red_flags`: 最大重试次数

### 应用场景

- **maker workflow**: Vote 步骤中的提案质量检查
- **verifier-quorum**: Verifier 输出中的 redFlags
- **maker 输出**: 最终 mutation 中的 redFlags

---

*最后更新: 2025-01-16*
