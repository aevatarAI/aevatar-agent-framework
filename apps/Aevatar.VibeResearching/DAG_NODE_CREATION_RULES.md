# DAG 节点创建规则

## 📋 概述

程序运行时，DAG 节点由多个来源创建，每个来源都有特定的规则和条件。本文档详细说明什么内容会被创建为 DAG 节点，以及创建规则。

---

## 🔷 节点类型

### 1. PlanNode（计划节点）

**创建者**: `research_assistant` agent

**创建时机**:
- **BRIEF 模式**: 创建 Milestone Plan Node（里程碑计划节点）
- **PLAN 模式**: 创建 Round Plan Node（轮次计划节点）

**节点 ID 格式**:
- Milestone: `plan_{sessionId}_ms_{suffix}`
- Round: `plan_{runId}`

**创建规则**:
- 每个 milestone 创建一个 Plan 节点
- 每个 research round 创建一个 Plan 节点
- Plan 节点由系统自动创建，不由 `dag_builder` 创建

---

### 2. KnowledgeNode（知识节点）

**创建者**: `dag_builder` agent（通过 DAG Consensus）

**创建时机**: 在每个 research round 的 worker phase 结束后

**节点 ID 格式**: `{type}_{name}_v{version}`（由 LLM 生成）

**类型前缀**:
- `thm_` = theorem（定理）
- `def_` = definition（定义）
- `axiom_` = axiom（公理）
- `hypothesis_` = hypothesis（假设）
- `assumption_` = assumption（假设）
- `unknown_` = unknown（未知类型）

---

## 📊 KnowledgeNode 创建规则

### 规则 1: 从 Verifier 输出提取（最高优先级）

**代码位置**: `VibeDagBuilderAgent.cs` → `SystemPrompt` (第 49-67 行)

**提取内容**:
- ✅ **AXIOM 节点**: Verifier 输出中提到的已验证公理或基础陈述
- ✅ **THEOREM 节点**: Verifier 输出中提到的已验证定理或已证明陈述
- ✅ **ASSUMPTION/DEFINITION 节点**: Verifier 输出中提到的已验证定义或假设

**提取规则**:
1. **优先提取标记为 "VERIFIED" 的知识项**
2. **使用 verifier 输出中的确切陈述作为 "label"**
3. **根据 verifier 的分类设置 "type"**:
   - `axiom` - 如果 verifier 标记为公理
   - `theorem` - 如果 verifier 标记为定理
   - `assumption` - 如果 verifier 标记为假设
   - `definition` - 如果 verifier 标记为定义
4. **添加验证标签**:
   ```json
   {
     "verification_status": "verified",
     "verified_by": "verifier"
   }
   ```
5. **提取验证方法或证明**（如果可用）放入 "proof" 字段

**示例**:
```json
{
  "id": "thm_pythagoras_v1",
  "type": "theorem",
  "label": "Pythagorean Theorem: In a right triangle, the square of the hypotenuse equals the sum of squares of the other two sides.",
  "proof": "Verified by verifier using geometric proof...",
  "tags": {
    "verification_status": "verified",
    "verified_by": "verifier"
  }
}
```

---

### 规则 2: 从 Reasoner 输出提取

**代码位置**: `VibeDagBuilderAgent.cs` → `SystemPrompt` (第 62-63 行)

**提取内容**:
- Reasoner 列出的 axioms、theorems、definitions
- 与 verifier 输出交叉引用
- 使用 reasoner 的完整陈述，verifier 的验证状态

**提取规则**:
1. **提取 reasoner 明确列出的知识项**
2. **与 verifier 输出交叉引用**:
   - 如果 verifier 验证了 reasoner 列出的项，使用 verifier 的验证状态
   - 如果 verifier 没有验证，仍然可以创建节点，但标记为未验证
3. **使用 reasoner 的完整陈述作为 "label"**

**示例**:
```json
{
  "id": "def_triangle_v1",
  "type": "definition",
  "label": "Triangle: A polygon with three edges and three vertices.",
  "proof": "From reasoner output...",
  "tags": {
    "source": "reasoner"
  }
}
```

---

### 规则 3: 从 Librarian 输出提取

**代码位置**: `VibeDagBuilderAgent.cs` → `SystemPrompt` (第 46-47 行)

**提取内容**:
- Librarian 提供的 "trusted axioms"（可信公理）
- 带有 citation 和 sourcePath 的公理

**提取规则**:
1. **必须包含 citation 和 sourcePath**
2. **标记为 AXIOM 节点**
3. **不需要 verifier 验证**（因为是可信来源）
4. **添加标签**:
   ```json
   {
     "sourcePath": "...",
     "citation": "...",
     "trusted": "paper"
   }
   ```

**示例**:
```json
{
  "id": "axiom_parallel_postulate_v1",
  "type": "axiom",
  "label": "Parallel Postulate: Through a point not on a line, there is exactly one line parallel to the given line.",
  "proof": null,
  "tags": {
    "sourcePath": "/path/to/paper.pdf",
    "citation": "Euclid, Elements, Book I",
    "trusted": "paper"
  }
}
```

---

### 规则 4: 从 Planner 输出提取

**代码位置**: `VibeDagBuilderAgent.cs` → `SystemPrompt` (第 44 行)

**提取内容**:
- 执行计划中的知识项
- 假设和未知项

**提取规则**:
1. **提取计划中提到的知识项**
2. **提取假设和未知项**（标记为 `hypothesis` 或 `assumption`）

---

### 规则 5: 从上传文件提取

**代码位置**: `UploadExtractionService.cs` → `CreateKnowledgeNodesAsync`

**提取内容**:
- 从上传的 PDF/文本文件中提取的知识点

**提取规则**:
1. **由 LLM 从文件内容中提取知识点**
2. **每个知识点创建一个节点**
3. **节点 ID 格式**: `upload_{timestamp}_{guid}`
4. **节点类型**: `KnowledgeNodeType.Reference`

**质量检查**:
- ✅ 标题和内容不能为空
- ✅ 内容长度 >= 20 字符
- ❌ 不能包含错误信息
- ❌ 标题不能包含 "Error" 或 "Failed"

---

## 🎯 节点创建的条件和约束

### 条件 1: 必须有 motivatedByPlanNodeId

**代码位置**: `VibeOrchestrator.Parsing.cs` (第 234-247 行)

**规则**:
- **每个 Knowledge 节点必须指定 `motivatedByPlanNodeId`**
- 链接到当前活动的 milestone（计划节点）
- 优先级链：
  1. LLM 提供的 `motivatedByPlanNodeId`（如果有效）
  2. 从 Neo4j 查询的活动 milestone
  3. 最佳匹配的 milestone
  4. 最近的 milestone（最高 round index）作为最后手段

**代码**:
```csharp
// CRITICAL: Knowledge nodes MUST ALWAYS have a motivated_by edge
// to the current Active milestone. This is a hard requirement.
var motivatedBy = Trimmed(n.MotivatedByPlanNodeId);

if (motivatedBy.Length == 0 || !existingMilestones.Contains(motivatedBy))
{
    // Find milestone using priority chain
    // ...
}
```

---

### 条件 2: 只能创建 Knowledge 节点

**代码位置**: `VibeOrchestrator.Parsing.cs` (第 209-212 行)

**规则**:
- **`dag_builder` 只能创建 Knowledge 节点**
- Plan 节点由 `VibeOrchestrator` 在计划生成时专门创建
- 忽略 LLM 输出中的 "kind" 字段，防止未授权的 Plan 节点创建

**代码**:
```csharp
// IMPORTANT: dag_builder can ONLY create Knowledge nodes.
// Plan nodes are created exclusively by VibeOrchestrator during plan generation.
// Ignore any "kind" field from LLM output to prevent unauthorized Plan node creation.
const SraDagNodeKind kind = SraDagNodeKind.Knowledge;
```

---

### 条件 3: 节点内容限制

**代码位置**: `VibeDagBuilderAgent.cs` → `SystemPrompt` (第 74 行)

**规则**:
- **Label**: <= 200 字符
- **Proof**: <= 1200 字符
- **Tags**: 每个值 <= 200 字符

**代码**:
```csharp
Label = BoundTrimmed(n.Label, 200),
Proof = BoundTrimmed(n.Proof, 1200),
node.Tags[key] = BoundTrimmed(kv.Value, 200);
```

---

### 条件 4: 节点 ID 规则

**代码位置**: `VibeDagBuilderAgent.cs` → `SystemPrompt` (第 71 行)

**规则**:
- **稳定且简短**（例如：`thm_pythagoras_v1`）
- **只使用小写字母、数字和下划线**
- **最大长度**: 64 字符（经过 `SanitizeId` 处理后）
- **应该能够从 ID 推断出节点的类型和内容**

---

### 条件 5: 空 mutation 处理

**代码位置**: `VibeDagBuilderAgent.cs` → `SystemPrompt` (第 45 行)

**规则**:
- **如果没有准备好任何内容，输出空的 mutation**:
  ```json
  {
    "mutationId": "...",
    "authorAgent": "dag_builder",
    "nodes": [],
    "edges": []
  }
  ```
- **不要创建无意义的节点**

---

## 🔄 节点创建流程

### 流程 1: Research Round 中的节点创建

```
用户输入
  ↓
research_assistant [BRIEF] → 创建 Milestone Plan Nodes
  ↓
research_assistant [PLAN] → 创建 Round Plan Node
  ↓
Worker Phase:
  ├─> planner → 生成执行计划
  ├─> reasoner → 生成推理过程
  ├─> verifier → 验证假设
  ├─> librarian → 提取可信公理（可选）
  └─> dag_builder → 提取知识 → 构建 DAG mutation candidate
      ↓
DAG Consensus:
  ├─> 解析 dag_builder 的输出
  ├─> 验证 candidate mutation
  └─> 应用 mutation → 创建 Knowledge Nodes
```

---

### 流程 2: dag_builder 的知识提取优先级

```
dag_builder 接收:
  ├─> verifier 输出（优先）→ 提取已验证的知识
  ├─> reasoner 输出 → 提取 axioms、theorems、definitions
  ├─> librarian 输出 → 提取 trusted axioms
  └─> planner 输出 → 提取知识项

提取规则:
  1. 优先提取 verifier 标记为 "VERIFIED" 的知识
  2. 交叉引用 reasoner 和 verifier 的输出
  3. 包含 librarian 的可信公理
  4. 提取 planner 中的知识项

输出:
  → JSON mutation candidate
  → 包含 nodes 和 edges
  → 每个 node 必须有 motivatedByPlanNodeId
```

---

## 📝 节点创建示例

### 示例 1: 从 Verifier 输出创建 Theorem 节点

**Verifier 输出**:
```
VERIFIED: Pythagorean Theorem
In a right triangle, the square of the hypotenuse equals the sum of squares of the other two sides.
Proof: Verified using geometric proof...
```

**创建的节点**:
```json
{
  "id": "thm_pythagoras_v1",
  "type": "theorem",
  "kind": "knowledge",
  "label": "Pythagorean Theorem: In a right triangle, the square of the hypotenuse equals the sum of squares of the other two sides.",
  "proof": "Verified using geometric proof...",
  "motivatedByPlanNodeId": "plan_abc123_ms_r1",
  "tags": {
    "verification_status": "verified",
    "verified_by": "verifier"
  }
}
```

---

### 示例 2: 从 Librarian 输出创建 Axiom 节点

**Librarian 输出**:
```
Trusted Axiom:
Parallel Postulate: Through a point not on a line, there is exactly one line parallel to the given line.
Source: Euclid, Elements, Book I
Path: /path/to/paper.pdf
```

**创建的节点**:
```json
{
  "id": "axiom_parallel_postulate_v1",
  "type": "axiom",
  "kind": "knowledge",
  "label": "Parallel Postulate: Through a point not on a line, there is exactly one line parallel to the given line.",
  "proof": null,
  "motivatedByPlanNodeId": "plan_abc123_ms_r1",
  "tags": {
    "sourcePath": "/path/to/paper.pdf",
    "citation": "Euclid, Elements, Book I",
    "trusted": "paper"
  }
}
```

---

### 示例 3: 从上传文件创建 Reference 节点

**上传文件提取的知识点**:
```json
{
  "title": "Key Finding: Protein Folding",
  "content": "Proteins fold into their native structure through a complex process involving hydrophobic interactions, hydrogen bonds, and van der Waals forces.",
  "keywords": ["protein", "folding", "structure"]
}
```

**创建的节点**:
```json
{
  "id": "upload_20250128103045_a1b2c3d4",
  "type": "reference",
  "kind": "knowledge",
  "label": "Key Finding: Protein Folding",
  "proof": "Proteins fold into their native structure through a complex process involving hydrophobic interactions, hydrogen bonds, and van der Waals forces.\n\n---\nSource: User Upload\nFile: paper.pdf\nKeywords: protein, folding, structure\nUploaded: 2025-01-28 10:30:45 UTC\nSession: session_123",
  "motivatedByPlanNodeId": null,
  "tags": {}
}
```

---

## 🚫 不会创建节点的情况

### 1. 空内容

**规则**: 如果 `dag_builder` 没有提取到任何知识，输出空的 mutation:
```json
{
  "nodes": [],
  "edges": []
}
```

---

### 2. 低质量内容

**规则**: 以下内容不会被创建为节点：
- ❌ 标题或内容为空
- ❌ 内容长度 < 20 字符
- ❌ 包含错误信息（`[PDF content could not be extracted...]`）
- ❌ 标题包含 "Error" 或 "Failed"

---

### 3. 无效的 motivatedByPlanNodeId

**规则**: 如果 `motivatedByPlanNodeId` 无效或不存在，系统会尝试找到替代的 milestone，但如果完全找不到，节点可能不会被创建或链接。

---

### 4. 非 Knowledge 类型的节点

**规则**: `dag_builder` 只能创建 Knowledge 节点，Plan 节点由系统专门创建。

---

## 💡 总结

### 节点创建来源

1. **Plan 节点**:
   - 由 `research_assistant` 创建
   - Milestone Plan Node（BRIEF 模式）
   - Round Plan Node（PLAN 模式）

2. **Knowledge 节点**:
   - 由 `dag_builder` 创建（通过 DAG Consensus）
   - 从 verifier 输出提取（优先）
   - 从 reasoner 输出提取
   - 从 librarian 输出提取
   - 从 planner 输出提取
   - 从上传文件提取

### 创建规则

1. **必须有 `motivatedByPlanNodeId`**（Knowledge 节点）
2. **只能创建 Knowledge 节点**（`dag_builder`）
3. **内容限制**: Label <= 200 字符，Proof <= 1200 字符
4. **节点 ID 规则**: 稳定、简短、可读
5. **质量检查**: 过滤低质量内容

### 优先级

1. **Verifier 输出**（已验证的知识）
2. **Reasoner 输出**（列出的知识项）
3. **Librarian 输出**（可信公理）
4. **Planner 输出**（计划中的知识项）
5. **上传文件**（用户提供的知识）

---

*最后更新: 2025-01-28*
