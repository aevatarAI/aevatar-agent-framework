# RedFlag: "Edge references unknown node" 详解

## 📋 含义

**RedFlag 文本**:
```
"Edge references unknown node 'plan_da9372a500d24d4f8226b986efc9808b_ms_r1'"
```

**含义**: 
在 DAG mutation candidate 中，有一条边（edge）引用了节点 `plan_da9372a500d24d4f8226b986efc9808b_ms_r1`，但这个节点**没有在当前 mutation 的 nodes 列表中定义**。

---

## 🔍 详细解释

### 1. 什么是 "Edge references unknown node"？

**场景**:
- Candidate mutation 包含 `edges` 数组，其中有一条边
- 这条边的 `from` 或 `to` 字段指向某个节点 ID
- 但这个节点 ID **没有出现在 candidate mutation 的 `nodes` 数组中**

**示例**:
```json
{
  "mutationId": "dag_h1_verification_v1",
  "nodes": [
    {"id": "thm_h1_multiplicativity_v1", "type": "theorem", ...},
    {"id": "def_z_homomorphism_v1", "type": "definition", ...}
  ],
  "edges": [
    {
      "from": "thm_h1_multiplicativity_v1",
      "to": "plan_da9372a500d24d4f8226b986efc9808b_ms_r1",  // ❌ 这个节点不在 nodes 中
      "type": "motivated_by"
    }
  ]
}
```

**问题**: 
- 边引用了 `plan_da9372a500d24d4f8226b986efc9808b_ms_r1`
- 但这个节点没有在 `nodes` 数组中定义
- 违反了**引用完整性（referential integrity）**

---

### 2. 为什么会出现这个问题？

#### 原因 1: dag_builder 创建了引用外部节点的边

**代码位置**: `VibeOrchestrator.Parsing.cs` → `AddMotivatedByEdges` (第 331-350 行)

**逻辑**:
- `dag_builder` 可能创建 `motivated_by` 边，指向 Plan 节点
- Plan 节点可能已经在 DAG 中存在（来自之前的 brief 生成）
- 但 `dag_builder` 的输出中**没有包含这个 Plan 节点的定义**

**示例**:
```csharp
// dag_builder 创建了这样的边
motivatedByEdges.Add((knowledgeNodeId, "plan_da9372a500d24d4f8226b986efc9808b_ms_r1"));

// 但 nodes 数组中只有 Knowledge 节点，没有 Plan 节点
```

#### 原因 2: Plan 节点的特殊性

**代码位置**: `DagStore.cs` → `ApplyMutationAsync` (第 230-236 行)

**逻辑**:
- 对于 Plan 节点（以 `plan_` 开头），系统**不会自动创建占位符**
- Plan 节点应该只在 brief 生成时创建
- 如果引用的 Plan 节点不存在，边会被"孤立"（orphaned）

**代码**:
```csharp
// For plan nodes: DO NOT create placeholders - plan nodes should only be created during brief generation.
if (id.StartsWith("plan_", StringComparison.OrdinalIgnoreCase))
{
    _logger.LogDebug("Skipping placeholder creation for missing plan node {NodeId}", id);
    continue;
}
```

---

### 3. 这个问题的严重性

#### 技术层面

**问题**:
- 违反了引用完整性：边引用了不存在的节点
- 可能导致 DAG 图不完整或无法正确查询

**影响**:
- 如果节点在 DAG 中存在：边可以正常创建，但 mutation 不完整
- 如果节点不存在：边无法创建，导致 mutation 失败

#### 业务层面

**Plan 节点的作用**:
- Plan 节点代表研究里程碑（milestone）
- Knowledge 节点通过 `motivated_by` 边连接到 Plan 节点
- 表示某个知识节点是由某个研究计划驱动的

**如果 Plan 节点缺失**:
- Knowledge 节点无法正确关联到研究计划
- 无法追踪知识的来源和目标

---

### 4. 为什么 Maker Workflow 会标记为 redFlag？

**验证逻辑** (修改前的提示词):
```
1. FIRST, perform technical validation:
   - Check for ID conflicts, cycles, invalid references
   - If technical issues found, set redFlags and proved=false
```

**Maker workflow 的检查**:
1. 遍历所有 `edges`
2. 检查每条边的 `from` 和 `to` 是否都在 `nodes` 数组中
3. 如果不在，标记为 "Edge references unknown node"

**为什么严格**:
- 确保 mutation 的完整性
- 避免创建"孤立"的边
- 保证 DAG 图的一致性

---

### 5. 修改后的处理方式

**修改后的提示词** (第 262 行):
```
- Allow edges referencing nodes not in current mutation if they exist in the DAG (external references are OK)
```

**新的逻辑**:
- 允许引用外部节点（如果节点在 DAG 中存在）
- 不再将外部引用标记为 redFlag
- 只对真正的 CRITICAL 问题（循环、矛盾）设置 redFlag

**规范化规则** (第 283 行):
```
- Keep edges referencing external nodes (they may exist in the DAG)
```

---

### 6. 实际场景分析

#### 场景 1: Plan 节点已在 DAG 中存在

**情况**:
- Plan 节点 `plan_da9372a500d24d4f8226b986efc9808b_ms_r1` 已经在 DAG 中
- `dag_builder` 创建了指向它的边
- 但没有在 mutation 的 `nodes` 中包含它

**修改前**: ❌ redFlag（违反引用完整性）

**修改后**: ✅ 允许（外部引用是合理的）

**处理**:
- Maker workflow 会保留这条边
- `DagStore.ApplyMutationAsync` 会检查节点是否存在
- 如果存在，边会正常创建

#### 场景 2: Plan 节点不存在

**情况**:
- Plan 节点不存在于 DAG 中
- `dag_builder` 创建了指向它的边

**修改前**: ❌ redFlag

**修改后**: ⚠️ 仍然可能有问题（取决于验证逻辑）

**处理**:
- `DagStore` 不会为 Plan 节点创建占位符
- 边可能无法创建，但不会导致 mutation 失败（best-effort）

---

### 7. 如何避免这个问题？

#### 方法 1: dag_builder 包含所有引用的节点

**修改 `dag_builder` 的输出**:
- 确保所有被边引用的节点都在 `nodes` 数组中
- 包括 Plan 节点（如果它们需要被引用）

**示例**:
```json
{
  "nodes": [
    {"id": "thm_h1_multiplicativity_v1", ...},
    {"id": "plan_da9372a500d24d4f8226b986efc9808b_ms_r1", "type": "plan", ...}  // ✅ 包含 Plan 节点
  ],
  "edges": [
    {"from": "thm_h1_multiplicativity_v1", "to": "plan_da9372a500d24d4f8226b986efc9808b_ms_r1", ...}
  ]
}
```

#### 方法 2: 使用修改后的提示词（已实现）

**优势**:
- 允许外部引用
- 更灵活，适应实际场景
- 减少不必要的 redFlags

**注意**:
- 仍然需要确保引用的节点在 DAG 中存在
- 对于不存在的节点，边可能无法创建

---

## 📊 总结

### RedFlag 含义

**"Edge references unknown node 'plan_xxx'"** 表示：
1. ✅ **有一条边**引用了节点 `plan_xxx`
2. ❌ **这个节点**没有在 mutation 的 `nodes` 数组中定义
3. ⚠️ **违反了引用完整性**，可能导致问题

### 为什么会出现

1. `dag_builder` 创建了指向 Plan 节点的边
2. 但 Plan 节点没有包含在 mutation 的 `nodes` 中
3. Plan 节点可能已在 DAG 中存在（外部引用）

### 修改后的处理

- ✅ **允许外部引用**（如果节点在 DAG 中存在）
- ✅ **不再标记为 redFlag**（除非是 CRITICAL 问题）
- ✅ **保留边**（规范化规则）

### 最佳实践

1. **dag_builder**: 尽量包含所有引用的节点
2. **Maker workflow**: 允许外部引用，但验证节点是否存在
3. **DagStore**: 为外部节点创建占位符（Plan 节点除外）

---

*最后更新: 2025-01-28*
