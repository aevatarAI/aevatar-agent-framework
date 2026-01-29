# DAG 节点 ID 命名规范

## 📋 概述

DAG 节点的 ID 命名遵循特定的规则和约定，以确保唯一性、可读性和稳定性。本文档详细说明各种节点类型的 ID 命名方式。

---

## 🔧 ID 规范化函数

### SanitizeId

**代码位置**: `VibeOrchestrator.PlanDag.cs` (第 181-191 行) 和 `VibeMilestoneLoopRunner.cs` (第 652-665 行)

**功能**: 清理和规范化 ID 字符串

**规则**:
1. 将所有非字母数字字符替换为 `_`
2. 转换为小写
3. 去除首尾的 `_`
4. 最大长度：64 字符（超过则截断）

**代码**:
```csharp
private static string SanitizeId(string s)
{
    var t = (s ?? string.Empty).Trim();
    if (t.Length == 0) return string.Empty;
    var sb = new StringBuilder(t.Length);
    foreach (var ch in t)
        sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
    var outId = sb.ToString().Trim('_');
    return outId.Length <= 64 ? outId : outId[..64];
}
```

**示例**:
- `"Plan: Research Triangle"` → `"plan_research_triangle"`
- `"Thm-Pythagoras v1"` → `"thm_pythagoras_v1"`
- `"Node@123#Test"` → `"node_123_test"`

---

## 📊 节点类型和命名规则

### 1. PlanNode（计划节点）

#### 1.1 Milestone Plan Node（里程碑计划节点）

**代码位置**: `VibeMilestoneLoopRunner.cs` → `GetMilestoneNodeId` (第 642-650 行)

**格式**: `plan_{sessionId}_ms_{suffix}`

**规则**:
- `sessionId`: 会话 ID（32 字符的 GUID，例如：`da9372a500d24d4f8226b986efc9808b`）
- `ms`: 固定前缀，表示 milestone
- `suffix`: 
  - 如果 `roundIndex > 0`: `r{roundIndex}`（例如：`r1`, `r2`）
  - 否则: `i{index}`（例如：`i1`, `i2`）

**示例**:
```
plan_da9372a500d24d4f8226b986efc9808b_ms_r1
plan_da9372a500d24d4f8226b986efc9808b_ms_r2
plan_da9372a500d24d4f8226b986efc9808b_ms_i1
```

**代码**:
```csharp
private static string GetMilestoneNodeId(string sessionId, int roundIndex, int index)
{
    var suffix = roundIndex > 0 ? $"r{roundIndex}" : $"i{index}";
    return SanitizeId($"plan_{sessionId}_ms_{suffix}");
}
```

---

#### 1.2 Round Plan Node（轮次计划节点）

**代码位置**: `VibeOrchestrator.PlanDag.cs` → `BuildPlanDagMutation` (第 27 行)

**格式**: `plan_{runId}`

**规则**:
- `runId`: 运行 ID，格式为 `{sessionId}:{seq}`（例如：`da9372a500d24d4f8226b986efc9808b:1`）
- 经过 `SanitizeId` 处理后，`:` 会被替换为 `_`

**示例**:
```
plan_da9372a500d24d4f8226b986efc9808b_1
plan_da9372a500d24d4f8226b986efc9808b_2
```

**代码**:
```csharp
var nodeId = SanitizeId($"plan_{runId}");
if (nodeId.Length == 0)
    nodeId = $"plan_{Guid.NewGuid():N}";
```

---

### 2. KnowledgeNode（知识节点）

**代码位置**: `VibeDagBuilderAgent.cs` → `SystemPrompt` (第 71 行)

**生成方式**: 由 `dag_builder` 的 LLM 生成

**命名约定**:
- **格式**: `{type}_{name}_v{version}`
- **类型前缀**:
  - `thm_` = theorem（定理）
  - `def_` = definition（定义）
  - `axiom_` = axiom（公理）
  - `hypothesis_` = hypothesis（假设）
  - `assumption_` = assumption（假设）
  - `unknown_` = unknown（未知类型）

**示例**:
```
thm_pythagoras_v1
thm_h1_multiplicativity_v1
def_triangle_v1
def_z_homomorphism_v1
axiom_parallel_postulate_v1
hypothesis_h1_v1
assumption_a1_v1
```

**规则**:
- **稳定且简短**: ID 应该稳定（不会频繁变化）且简短
- **版本号**: 使用 `_v1`, `_v2` 等版本号区分同一知识的不同版本
- **小写字母和数字**: 只使用小写字母、数字和下划线
- **最大长度**: 建议不超过 64 字符

**提示词要求**:
```
Node.id should be stable and short (e.g. "thm_pythagoras_v1").
```

---

### 3. Placeholder Node（占位符节点）

**代码位置**: `DagStore.cs` → `ApplyMutationAsync` (第 223-254 行)

**生成方式**: 当边引用了不存在的节点时自动创建

**格式**: 直接使用被引用的节点 ID

**规则**:
- 如果引用的节点 ID 以 `plan_` 开头，**不会创建占位符**（Plan 节点只能由 brief 生成）
- 其他情况下，使用被引用的 ID 创建占位符节点

**示例**:
```
# 如果边引用了 "thm_unknown_v1" 但该节点不存在
# 会创建占位符节点，ID 为 "thm_unknown_v1"
```

---

### 4. Upload Node（上传节点）

**代码位置**: `UploadExtractionService.cs` → `CreateKnowledgeNodesFromExtractionAsync` (第 423 行)

**格式**: `upload_{timestamp}_{guid}`

**规则**:
- `timestamp`: `yyyyMMddHHmmss` 格式（例如：`20250128103045`）
- `guid`: GUID 的前 32 个字符（去除连字符）
- 总长度限制为 32 字符

**示例**:
```
upload_20250128103045_a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6
```

**代码**:
```csharp
var nodeId = $"upload_{now:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..32];
```

---

## 📝 命名规范总结

### 通用规则

1. **字符集**: 只使用小写字母、数字和下划线
2. **长度限制**: 最大 64 字符（经过 `SanitizeId` 处理后）
3. **唯一性**: 必须全局唯一（在同一个 DAG 中）
4. **稳定性**: ID 应该稳定，不会频繁变化
5. **可读性**: 应该能够从 ID 推断出节点的类型和内容

---

### 类型前缀约定

| 前缀 | 类型 | 说明 | 示例 |
|------|------|------|------|
| `plan_` | PlanNode | 计划节点 | `plan_da9372a500d24d4f8226b986efc9808b_ms_r1` |
| `thm_` | Theorem | 定理 | `thm_pythagoras_v1` |
| `def_` | Definition | 定义 | `def_triangle_v1` |
| `axiom_` | Axiom | 公理 | `axiom_parallel_postulate_v1` |
| `hypothesis_` | Hypothesis | 假设 | `hypothesis_h1_v1` |
| `assumption_` | Assumption | 假设 | `assumption_a1_v1` |
| `unknown_` | Unknown | 未知类型 | `unknown_item_v1` |
| `upload_` | Upload | 上传的知识 | `upload_20250128103045_xxx` |

---

## 🔍 ID 生成流程

### PlanNode ID 生成

```
用户输入
  ↓
research_assistant 生成 Plan
  ↓
BuildPlanDagMutation
  ↓
SanitizeId("plan_{runId}")
  ↓
PlanNode ID: plan_{sessionId}_{seq}
```

---

### KnowledgeNode ID 生成

```
Worker outputs (planner, reasoner, verifier)
  ↓
dag_builder LLM 分析输出
  ↓
LLM 生成节点定义（包括 ID）
  ↓
ParseDagBuilderOutput 解析
  ↓
DAG Consensus 验证
  ↓
ApplyMutationAsync 创建节点
  ↓
KnowledgeNode ID: {type}_{name}_v{version}
```

---

## 💡 最佳实践

### 1. KnowledgeNode ID 命名建议

**好的示例**:
```json
{
  "id": "thm_pythagoras_v1",
  "type": "theorem",
  "label": "Pythagorean Theorem"
}

{
  "id": "def_triangle_v1",
  "type": "definition",
  "label": "Triangle Definition"
}

{
  "id": "axiom_parallel_postulate_v1",
  "type": "axiom",
  "label": "Parallel Postulate"
}
```

**不好的示例**:
```json
{
  "id": "node1",  // ❌ 太通用，无法推断类型
  "id": "theorem_about_pythagorean_theorem_stating_that_in_a_right_triangle_the_square_of_the_hypotenuse_equals_the_sum_of_squares_of_the_other_two_sides_v1",  // ❌ 太长
  "id": "Thm-Pythagoras",  // ❌ 包含大写字母和连字符（会被规范化，但不够规范）
  "id": "thm_pythagoras",  // ⚠️ 缺少版本号（如果同一知识有多个版本会有冲突）
}
```

---

### 2. 版本号使用

**场景**: 同一知识有多个版本或修订

**示例**:
```
thm_pythagoras_v1  # 第一个版本
thm_pythagoras_v2  # 修订版本
thm_pythagoras_v3  # 进一步修订
```

**规则**:
- 使用 `_v1`, `_v2`, `_v3` 等版本号
- 版本号从 `v1` 开始
- 每次重大修订时递增版本号

---

### 3. 重用现有 ID

**代码位置**: `VibeDagBuilderAgent.cs` → `SystemPrompt` (第 66 行)

**提示词**:
```
- You MAY call graph_get_snapshot to see existing node ids/types and reuse them.
- Do not invent node ids that are not necessary; prefer reusing existing ids when possible.
```

**规则**:
- 如果知识已经存在，应该重用现有的节点 ID
- 避免创建重复的节点
- 如果需要更新，可以考虑使用新版本号（`_v2`）

---

## 🔍 查看节点 ID

### 1. DAG API

```bash
# 查看所有节点 ID
curl http://localhost:5678/api/dag/global | jq '.dag.nodes[] | .id'

# 查看特定类型的节点 ID
curl http://localhost:5678/api/dag/global | \
  jq '.dag.nodes[] | select(.id | startswith("thm_")) | .id'

# 查看 Plan 节点 ID
curl http://localhost:5678/api/dag/global | \
  jq '.dag.nodes[] | select(.id | startswith("plan_")) | .id'
```

---

### 2. Snapshot 文件

```bash
# 查看所有节点 ID
cat workspace/dags/global/artifacts/dag/snapshot.json | jq '.nodes[].id'

# 查看特定类型的节点 ID
cat workspace/dags/global/artifacts/dag/snapshot.json | \
  jq '.nodes[] | select(.id | startswith("thm_")) | .id'
```

---

### 3. Neo4j 数据库

```cypher
// 查看所有 KnowledgeNode ID
MATCH (n:KnowledgeNode)
RETURN n.Id
ORDER BY n.Id

// 查看所有 PlanNode ID
MATCH (n:PlanNode)
RETURN n.Id
ORDER BY n.Id

// 查看特定前缀的节点
MATCH (n)
WHERE n.Id STARTS WITH 'thm_'
RETURN n.Id
```

---

## 📊 ID 命名示例

### 完整示例

**PlanNode**:
```
plan_da9372a500d24d4f8226b986efc9808b_ms_r1
plan_da9372a500d24d4f8226b986efc9808b_ms_r2
plan_da9372a500d24d4f8226b986efc9808b_1
```

**KnowledgeNode**:
```
thm_pythagoras_v1
thm_h1_multiplicativity_v1
def_triangle_v1
def_z_homomorphism_v1
axiom_parallel_postulate_v1
hypothesis_h1_v1
assumption_a1_v1
```

**Upload Node**:
```
upload_20250128103045_a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6
```

---

## 💡 总结

### 命名规则

1. **PlanNode**: 
   - Milestone: `plan_{sessionId}_ms_{suffix}`
   - Round: `plan_{runId}`

2. **KnowledgeNode**: 
   - 由 LLM 生成
   - 格式: `{type}_{name}_v{version}`
   - 类型前缀: `thm_`, `def_`, `axiom_`, `hypothesis_`, `assumption_`

3. **Placeholder Node**: 
   - 使用被引用的节点 ID

4. **Upload Node**: 
   - `upload_{timestamp}_{guid}`

### 规范化

- 所有 ID 都经过 `SanitizeId` 处理
- 只包含小写字母、数字和下划线
- 最大长度：64 字符

### 查看位置

- DAG API: `GET /api/dag/global`
- Snapshot 文件: `workspace/dags/{dagId}/artifacts/dag/snapshot.json`
- Neo4j 数据库: 直接查询 `Id` 字段

---

*最后更新: 2025-01-28*
