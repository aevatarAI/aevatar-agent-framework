# DAG 节点 Detailed Description 只有 ID 的问题分析

## 📋 问题描述

在 DAG 图中，有 2 个新增加的节点的 `detailed description` 只有它们的 ID，没有其他详细信息。

---

## 🔍 问题原因分析

### 1. Detailed Description 的构建逻辑

**代码位置**: `DagStore.cs` → `ApplyMutationAsync` (第 145-148 行)

**当前逻辑**:
```csharp
var label = (n.Label ?? string.Empty).Trim();
var proof = (n.Proof ?? string.Empty).Trim();
var baseDetail = string.IsNullOrWhiteSpace(label) ? proof : label;
var detail = AppendTags(baseDetail, n.Tags);
```

**问题**:
- 如果 `label` 为空，`baseDetail` 会使用 `proof`
- 但如果 `label` 和 `proof` 都为空，`baseDetail` 会是空字符串
- 如果 `baseDetail` 为空且没有 tags，`detail` 也会是空字符串
- 最终 `detailedDescription` 会是空字符串

**对于 KnowledgeNode** (第 192 行):
```csharp
detailedDescription: detail,  // 如果 detail 为空，detailedDescription 也是空
```

**对于 PlanNode** (第 167 行):
```csharp
detailedDescription: string.IsNullOrWhiteSpace(detail) ? id : detail,
```
- PlanNode 有回退逻辑：如果 `detail` 为空，使用 `id`
- 但 KnowledgeNode **没有**这个回退逻辑

---

### 2. 占位符节点的创建

**代码位置**: `DagStore.cs` → `ApplyMutationAsync` (第 211-230 行)

**场景**: 当边引用了不存在的节点时，会创建占位符节点

**当前逻辑**:
```csharp
foreach (var id in referenced)
{
    // ...
    await client.UpsertNodeAsync(
        nodeId: id,
        nodeType: KnowledgeNodeType.Unknown,
        owner: localOwner,
        coreDescription: id,  // 使用 ID 作为 coreDescription
        detailedDescription: id,  // 使用 ID 作为 detailedDescription
        // ...
    );
}
```

**问题**: 占位符节点只设置了 ID，没有其他信息

---

### 3. dag_builder 输出可能缺少 label 或 proof

**代码位置**: `VibeOrchestrator.Parsing.cs` → `ParseDagBuilderOutput` (第 219-220 行)

**当前逻辑**:
```csharp
Label = BoundTrimmed(n.Label, 200),
Proof = BoundTrimmed(n.Proof, 1200),
```

**问题**: 
- 如果 `dag_builder` 的 LLM 输出中，某个节点的 `label` 和 `proof` 都为空或只有 ID
- 解析后的节点会只有 ID，没有其他信息

---

### 4. DAG Consensus 可能清空了字段

**代码位置**: `DagConsensusRunner.cs` → `BuildMutation` (第 402-410 行)

**当前逻辑**:
```csharp
var label = Bound((n!.Label ?? string.Empty).Trim(), 200);
var proof = Bound((n.Proof ?? string.Empty).Trim(), 2000);

var node = new SraDagNode
{
    Id = nid,
    Type = ParseNodeType(n.Type),
    Label = label,
    Proof = proof,
    UpdatedAt = now
};
```

**问题**: 
- 如果 Maker workflow 的输出中，某个节点的 `label` 和 `proof` 都为空
- 构建的节点会只有 ID，没有其他信息

---

## 🔧 解决方案

### 方案 1: 为 KnowledgeNode 添加回退逻辑（推荐）

**修改位置**: `DagStore.cs` → `ApplyMutationAsync` (第 192 行)

**修改前**:
```csharp
detailedDescription: detail,
```

**修改后**:
```csharp
detailedDescription: string.IsNullOrWhiteSpace(detail) ? id : detail,
```

**说明**: 
- 与 PlanNode 保持一致
- 如果 `detail` 为空，使用 `id` 作为 `detailedDescription`
- 至少保证 `detailedDescription` 不为空

---

### 方案 2: 改进 baseDetail 的构建逻辑

**修改位置**: `DagStore.cs` → `ApplyMutationAsync` (第 147 行)

**修改前**:
```csharp
var baseDetail = string.IsNullOrWhiteSpace(label) ? proof : label;
```

**修改后**:
```csharp
var baseDetail = string.IsNullOrWhiteSpace(label) 
    ? (string.IsNullOrWhiteSpace(proof) ? id : proof)
    : label;
```

**说明**: 
- 如果 `label` 为空，尝试使用 `proof`
- 如果 `label` 和 `proof` 都为空，使用 `id`
- 确保 `baseDetail` 始终有值

---

### 方案 3: 改进 dag_builder 的提示词

**修改位置**: `VibeDagBuilderAgent.cs` → `SystemPrompt`

**添加要求**:
```
CRITICAL - Node Description Requirements:
- Every node MUST have a non-empty "label" field (at least 10 characters).
- The "label" should be a clear, descriptive statement of the knowledge item.
- If a node only has an ID and no description, DO NOT include it in the mutation.
- Empty or ID-only nodes will be rejected.
```

**说明**: 
- 在源头防止空节点
- 要求 LLM 输出完整的节点信息

---

### 方案 4: 改进占位符节点的创建

**修改位置**: `DagStore.cs` → `ApplyMutationAsync` (第 211-230 行)

**修改前**:
```csharp
coreDescription: id,
detailedDescription: id,
```

**修改后**:
```csharp
coreDescription: $"Placeholder: {id}",
detailedDescription: $"This is a placeholder node referenced by edges but not yet defined. Node ID: {id}",
```

**说明**: 
- 明确标识占位符节点
- 提供更多上下文信息

---

## 📊 推荐方案

### 短期修复（快速）

**推荐**: 方案 1 + 方案 2

1. **为 KnowledgeNode 添加回退逻辑**:
   ```csharp
   detailedDescription: string.IsNullOrWhiteSpace(detail) ? id : detail,
   ```

2. **改进 baseDetail 的构建逻辑**:
   ```csharp
   var baseDetail = string.IsNullOrWhiteSpace(label) 
       ? (string.IsNullOrWhiteSpace(proof) ? id : proof)
       : label;
   ```

### 长期改进（完善）

**推荐**: 方案 3 + 方案 4

1. **改进 dag_builder 的提示词**，要求 LLM 输出完整的节点信息
2. **改进占位符节点的创建**，提供更多上下文信息

---

## 🔍 诊断步骤

### 1. 检查节点的创建来源

查看日志，查找节点的创建位置：
```bash
grep -i "Creating\|Upserting.*KnowledgeNode\|Placeholder" logs/*.log | grep -i "nodeId"
```

### 2. 检查 dag_builder 的输出

查看 `dag_builder` 的原始输出：
```bash
# 查找 prompt logs
find workspace/sessions -name "*dag_builder*.md" -type f | head -1 | xargs cat | grep -A 50 "dag_builder"
```

### 3. 检查 DAG Consensus 的输出

查看共识后的节点信息：
```bash
# 查找 consensus artifacts
find workspace/sessions -path "*/artifacts/dag/consensus/*.json" -type f | head -1 | xargs cat | jq '.parsed.Nodes[] | select(.Label == "" or .Label == .Id)'
```

### 4. 检查 Neo4j 中的节点

直接查询 Neo4j：
```cypher
MATCH (n:KnowledgeNode)
WHERE n.DetailedDescription = n.Id OR n.DetailedDescription IS NULL OR n.DetailedDescription = ""
RETURN n.Id, n.CoreDescription, n.DetailedDescription, n.Proof
LIMIT 10
```

---

## 💡 总结

**问题根源**:
1. **KnowledgeNode 缺少回退逻辑** - 如果 `label` 和 `proof` 都为空，`detailedDescription` 会是空字符串
2. **占位符节点只设置了 ID** - 没有其他描述信息
3. **dag_builder 可能输出空节点** - LLM 可能只输出 ID，没有其他信息

**解决方案**:
- **短期**: 添加回退逻辑，确保 `detailedDescription` 至少是 `id`
- **长期**: 改进提示词和占位符节点创建逻辑

---

*最后更新: 2025-01-28*
