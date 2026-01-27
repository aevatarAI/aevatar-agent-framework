# MAKER 工作流程详解

## 📋 概述

`maker` 是 DAG Consensus 中的一种共识模式，使用 **Cognitive DSL workflow** 来验证和规范化 DAG mutation candidate。它基于论文 "Solving a Million-Step LLM Task with Zero Errors" 的 MAKER System 算法实现。

**文件位置**: `workflows/maker.yaml`

---

## 🎯 核心算法

MAKER System 包含 6 个核心步骤：

1. **分解任务 (Decomposition)** - 将复杂任务分解为子任务
2. **并行生成提案 (Fan-out)** - 多 Worker 并行 LLM
3. **投票共识 (Vote)** - First-to-ahead-by-K 共识机制
4. **Red-Flagging** - 内容验证与恢复
5. **递归处理 (Recursive)** - 子任务递归执行
6. **合成结果 (Composition)** - 聚合子任务结果

---

## 🔄 完整工作流程

### 入口点

**代码位置**: `DagConsensusRunner.cs` → `RunMakerAsync`

```csharp
var task = BuildTaskPrompt(input);
var rr = await _cognitive.ExecuteAsync(task, options, progress: input.Progress, ct: ct);
```

**输入**: `ConsensusInput`（包含 candidate mutation、current DAG、materials context）

**输出**: `ConsensusResult`（包含验证后的 mutation 或 redFlags）

---

### Step 1: 判断任务是否为原子任务

**Workflow Step**: `check_atomic`

**类型**: `llm_call`

**目的**: 判断当前任务是否需要分解

**判断标准**:

- **COMPLEX**（需要分解）:
  - 涉及多个不同方面
  - 需要综合评估（如论文评审、代码评审）
  - 示例: "Review this paper"（需要技术正确性、新颖性、清晰度等）

- **ATOMIC**（可直接解决）:
  - 聚焦单一特定问题
  - 简单查询、计算或单焦点分析
  - 示例: "Evaluate the novelty of this approach"

**输出格式**:
```json
{
  "is_atomic": true/false,
  "reasoning": "brief explanation"
}
```

**存储**: `atomic_check`

---

### Step 2: 条件分支处理

**Workflow Step**: `process_task`

**类型**: `conditional`

**条件**: `{{atomic_check.is_atomic}}`

#### 分支 A: 原子任务（直接解决）

**路径**: `if_true` → `solve_atomic`

**类型**: `vote`

**流程**:

1. **并行生成提案** (`fan_out`):
   - 多个 Worker 并行调用 LLM
   - 每个 Worker 生成一个解决方案
   - 默认并发数: 5

2. **投票共识** (`vote`):
   - 使用 **First-to-ahead-by-K** 机制
   - 默认 `k = 3`（需要领先至少 3 票）
   - 最大轮数: `max_rounds = 10`
   - 相似度阈值: `similarity = 0.85`

3. **Red-Flag 检查**:
   - 检查输出长度（`max_length: 102400`）
   - 检查 JSON 解析（`strict_parse: true`）
   - 检查内容质量（`strategy: english`）

4. **输出**: 文本格式的解决方案

**存储**: `atomic_solution`

---

#### 分支 B: 复杂任务（分解 → 递归 → 合成）

**路径**: `if_false` → 三个子步骤

##### Step 2a: 分解任务

**Workflow Step**: `decompose`

**类型**: `vote`

**目的**: 将复杂任务分解为 2-5 个独立子任务

**流程**:

1. **并行生成分解方案**:
   - 多个 Worker 并行生成分解方案
   - 每个方案包含 2-5 个子任务

2. **投票选择最佳分解方案**:
   - 使用 First-to-ahead-by-K 机制
   - 选择最合理的分解方案

3. **输出格式**:
```json
[
  {"id": "subtask_1", "description": "..."},
  {"id": "subtask_2", "description": "..."},
  ...
]
```

**存储**: `subtasks`

---

##### Step 2b: 并行递归执行子任务

**Workflow Step**: `execute_subtasks`

**类型**: `fan_out`

**目的**: 并行执行所有子任务（真正的 Actor 并行）

**流程**:

1. **遍历子任务列表** (`for_each: subtasks`)

2. **递归调用 maker workflow**:
   ```yaml
   type: workflow_call
   workflow: maker
   params:
     task: "{{item.description}}"
     context: "{{context}}"
     k: "{{k}}"
     max_depth: "{{max_depth}}"
   ```

3. **并行执行**:
   - 最大并发数: `max_concurrency: 5`
   - 每个子任务递归调用 `maker` workflow
   - 递归深度保护: `max_depth = 50`

4. **收集结果** (`reduce: collect`)

**存储**: `subtask_results`

**关键点**:
- 这是**真正的递归**：每个子任务可能再次分解
- 递归深度由 `max_depth` 限制（安全阀）
- 并行执行提高效率

---

##### Step 2c: 合成最终结果

**Workflow Step**: `compose`

**类型**: `vote`

**目的**: 将子任务结果合成为最终解决方案

**流程**:

1. **并行生成合成方案**:
   - 多个 Worker 并行生成合成方案
   - 每个方案整合所有子任务结果

2. **投票选择最佳合成方案**:
   - 使用 First-to-ahead-by-K 机制
   - 选择最全面、最一致的合成方案

3. **输出**: 文本格式的最终解决方案

**存储**: `composed_solution`

---

### Step 3: 输出结果

**Workflow Output**:
```yaml
output:
  solution: "{{atomic_solution | default: composed_solution}}"
  is_atomic: "{{atomic_check.is_atomic | default: false}}"
  subtask_count: "{{subtasks | size | default: 0}}"
```

**关键点**:
- 如果是原子任务，使用 `atomic_solution`
- 如果是复杂任务，使用 `composed_solution`
- 包含任务类型和子任务数量信息

---

## 🔍 投票共识机制

### First-to-ahead-by-K

**算法**:
1. 多个 Worker 并行生成提案
2. 计算每个提案的得票数
3. 找到得票最多的提案（Winner）
4. 检查 Winner 是否领先第二名至少 K 票
5. 如果满足，达成共识；否则继续下一轮

**参数**:
- `k`: 共识所需票数（默认 3）
- `max_rounds`: 最大投票轮数（默认 10）
- `similarity`: 相似度阈值（默认 0.85）

**示例**:
```
Round 1:
  Proposal A: 5 votes
  Proposal B: 2 votes
  Proposal C: 1 vote
  
  Winner: A (5 votes)
  Runner-up: B (2 votes)
  Lead: 5 - 2 = 3 >= k (3) ✅
  
  → Consensus reached!
```

---

## 🚩 Red-Flag 机制

### 检查项

1. **长度检查** (`max_length: 102400`):
   - 如果输出超过 102400 字符，标记为 redFlag

2. **解析检查** (`strict_parse: true`):
   - 如果 JSON 解析失败，标记为 redFlag

3. **内容检查** (`strategy: english`):
   - 检查输出质量
   - 检查是否符合要求

4. **数量限制** (`max_red_flags: 20`):
   - 如果 redFlags 超过 20 个，停止处理

### Red-Flag 处理

- 如果检测到 redFlags，当前提案被拒绝
- 继续生成新提案，直到找到无 redFlags 的提案
- 如果所有提案都有 redFlags，返回失败

---

## 🧠 System Prompt 设计

**位置**: `maker.yaml` → `defaults.llm_call.system`

**核心思想**: 基于 "Holographic Polar Arithmetic" (2025) 论文

### 核心三元组

1. **Rotation (扫描 Θ)**:
   - 时间 = 深度/迭代
   - 使用无理旋转直觉（黄金比例 α=φ^{-1}）来多样化
   - 避免共振

2. **Factorization (分解)**:
   - 将事实视为生成器
   - 使用引用（如 [O1,T3]）明确依赖关系

3. **Projection/Readout (投影/读出)**:
   - 只接受封闭的论证
   - 任何缺失的引理/假设都是残差 gap δ，必须明确说明

### 判断规则

**proved=true** 的条件:
- 候选内容内部一致
- 保持在提供的公理/事实范围内
- 任何隐式步骤在物理上合理
- 允许标准推理步骤（代数/重写），只要不引入新假设

**proved=false** (gap δ) 的条件:
- 与公理/事实明确矛盾
- 存在反例
- 关键逻辑步骤在物理上不合理或矛盾
- 引入与已知事实矛盾的新假设

---

## 📊 在 DAG Consensus 中的使用

### 调用流程

**代码位置**: `DagConsensusRunner.cs` → `RunMakerAsync`

```
1. 构建 Task Prompt
   ↓
2. 调用 Cognitive DSL Workflow (maker)
   ↓
3. 解析输出
   ├─> 提取 JSON
   ├─> 反序列化为 DagMutationJson
   └─> 检查 redFlags
   ↓
4. 构建 Mutation
   ├─> 如果 redFlags 不为空 → 返回失败
   ├─> 如果 mutation 为空 → 返回失败
   └─> 否则 → 返回成功 + mutation
```

### Task Prompt 构建

**代码位置**: `DagConsensusRunner.cs` → `BuildTaskPrompt`

**内容**:
- Candidate mutation 摘要（节点数、边数、节点列表、边列表）
- Current DAG 统计信息
- Materials Context（可选）
- 输出格式要求（JSON schema）

---

## 🔄 递归执行示例

### 示例 1: 原子任务

```
输入: "Validate this DAG mutation"
  ↓
check_atomic → is_atomic: true
  ↓
solve_atomic (vote)
  ├─> Worker 1: 生成验证方案 A
  ├─> Worker 2: 生成验证方案 B
  ├─> Worker 3: 生成验证方案 C
  └─> Vote → 选择方案 A（5票，领先3票）
  ↓
输出: atomic_solution
```

### 示例 2: 复杂任务（2层递归）

```
输入: "Review this DAG mutation comprehensively"
  ↓
check_atomic → is_atomic: false
  ↓
decompose (vote)
  └─> 分解为: [subtask_1, subtask_2, subtask_3]
  ↓
execute_subtasks (fan_out)
  ├─> subtask_1 → maker (递归)
  │     ├─> check_atomic → is_atomic: true
  │     └─> solve_atomic → solution_1
  │
  ├─> subtask_2 → maker (递归)
  │     ├─> check_atomic → is_atomic: false
  │     ├─> decompose → [subtask_2a, subtask_2b]
  │     ├─> execute_subtasks → [solution_2a, solution_2b]
  │     └─> compose → solution_2
  │
  └─> subtask_3 → maker (递归)
        ├─> check_atomic → is_atomic: true
        └─> solve_atomic → solution_3
  ↓
compose (vote)
  └─> 合成 [solution_1, solution_2, solution_3] → final_solution
  ↓
输出: composed_solution
```

---

## ⚙️ 配置参数

### 输入参数

- `task` (string, required): 任务描述
- `context` (object, optional): 额外上下文信息
- `k` (int, default: 3): 共识所需票数
- `max_rounds` (int, default: 10): 最大投票轮数
- `max_depth` (int, default: 50): 最大递归深度（安全阀）

### 默认策略

- `vote.k`: 3
- `vote.max_rounds`: 10
- `vote.similarity`: 0.85
- `vote.red_flag.max_length`: 102400
- `vote.red_flag.strict_parse`: true
- `vote.red_flag.max_red_flags`: 20
- `llm_call.max_length`: 102400
- `llm_call.strict_parse`: true

---

## 🎯 关键特性

### 1. 自适应分解

- 自动判断任务复杂度
- 复杂任务自动分解为子任务
- 原子任务直接解决

### 2. 并行执行

- 多个 Worker 并行生成提案
- 子任务并行递归执行
- 提高处理效率

### 3. 共识机制

- First-to-ahead-by-K 投票
- 确保结果质量
- 避免单一 LLM 的错误

### 4. 递归处理

- 支持多层递归分解
- 深度保护机制（`max_depth`）
- 自动合成结果

### 5. Red-Flag 保护

- 内容质量检查
- 解析错误检测
- 长度限制保护

---

## 📝 输出格式

### 成功输出

```json
{
  "mutationId": "maker_normalized_xxx",
  "author": "maker",
  "nodes": [
    {
      "id": "thm_1",
      "type": "theorem",
      "label": "...",
      "proof": "...",
      "tags": {...}
    }
  ],
  "edges": [
    {
      "from": "axiom_1",
      "to": "thm_1",
      "type": "depends_on"
    }
  ],
  "redFlags": []
}
```

### 失败输出

```json
{
  "mutationId": "...",
  "author": "...",
  "nodes": [],
  "edges": [],
  "redFlags": [
    "Edge type 'motivated_by' is not a standard dependency type",
    "Node 'xxx' has type 'Unknown' which is not valid"
  ]
}
```

---

## 🔍 调试和监控

### Artifact 文件

**位置**: `workspace/sessions/{sessionId}/artifacts/dag/consensus/{timestamp}_{guid}.json`

**内容**:
- `workflow`: "maker"
- `status`: "accepted" | "blocked"
- `stats`: LLM 调用统计（调用次数、tokens、耗时）
- `extractedJson`: 提取的 JSON
- `parsed`: 解析后的 mutation
- `rawOutput`: 原始输出（最多 30,000 字符）
- `redFlags`: 拒绝原因列表

### 日志

**关键日志**:
```
[Cognitive] Executing workflow: maker
[Cognitive] Step: check_atomic
[Cognitive] Step: process_task
[Cognitive] Step: solve_atomic / decompose / execute_subtasks / compose
[DagConsensus] Maker workflow completed: success={Success}, durationMs={DurationMs}
```

---

## 💡 总结

### 优势

1. **智能分解**: 自动判断任务复杂度，合理分解
2. **并行处理**: 多 Worker 并行，提高效率
3. **共识保证**: First-to-ahead-by-K 机制确保质量
4. **递归支持**: 支持多层递归，处理复杂任务
5. **质量保护**: Red-Flag 机制防止低质量输出

### 适用场景

- **复杂验证任务**: 需要多角度评估的 DAG mutation
- **规范化任务**: 需要统一格式和标准的 mutation
- **质量保证**: 需要高可靠性的共识结果

### 与 verifier-quorum 的对比

| 特性 | maker | verifier-quorum |
|------|-------|----------------|
| **复杂度** | 高（完整 workflow） | 低（简单投票） |
| **智能度** | 高（分解、递归、合成） | 中（并行验证） |
| **性能** | 慢（多步骤、递归） | 快（并行投票） |
| **适用场景** | 复杂任务、规范化 | 简单验证、快速决策 |
| **输出** | 规范化后的 mutation | 投票结果 |

---

*最后更新: 2025-01-16*
