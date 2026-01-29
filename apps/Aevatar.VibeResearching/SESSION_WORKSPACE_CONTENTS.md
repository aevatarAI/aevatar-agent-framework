# Session Workspace 文件夹内容说明

## 📁 文件夹结构

会话工作空间位于：`workspace/sessions/{sessionId}/`

以会话 `fc284a36a5ac4950bba7a08bf96dc1d2` 为例，该文件夹保存了研究会话的完整工作痕迹和产出。

---

## 📂 目录结构详解

### 1. `decisions/` - 决策配置

**文件：**
- `mesh.yaml` - Cognitive Mesh DSL 配置

**内容：**
- 工作流节点定义（planner, reasoner, librarian, verifier, dag_builder）
- 节点之间的连接关系（edges）
- 预算配置（max_steps, token_limit）
- 策略配置（strategy: "cot"）

**用途：** 定义多智能体协作的工作流结构

---

### 2. `deliverables/` - 交付物

**文件：**
- `brief.json` - 研究摘要（Research Brief）
- `conclusions.json` - 结论卡片
- `delivery_snapshot.json` - 交付中心快照
- `evidence.json` - 证据表
- `tasks.json` - 下一步任务

**内容示例（brief.json）：**
```json
{
  "sessionId": "fc284a36a5ac4950bba7a08bf96dc1d2",
  "rewrittenQuestion": "从论文中系统性地识别并分类所有数学陈述...",
  "scope": "1. 从提供的论文文本（第1-63页）中提取所有数学陈述...",
  "successCriteria": "1. 完整识别论文中所有数学陈述...",
  "terms": [...],  // 术语定义
  "assumptions": [...],  // 假设
  "risks": [...],  // 风险
  "milestones": [...]  // 里程碑
}
```

**用途：** 存储研究任务的元数据、范围、成功标准等

---

### 3. `paper/` - 论文产出

**文件：**
- `outline.md` - 论文大纲
- `draft.md` - 论文草稿

**用途：** 存储研究过程中生成的论文内容

---

### 4. `artifacts/` - 工作痕迹

#### 4.1 `artifacts/trace/` - 追踪记录

**文件：**
- `trace.jsonl` - 逐行 JSON Lines 格式的轮次总结

**内容：**
每行是一个 `SraRoundSummary` 对象，包含：
- `sessionId`, `runId`, `roundIndex` - 会话/运行/轮次标识
- `triggerKind` - 触发类型（如 "user_message"）
- `perAgent` - 每个智能体的工作摘要：
  - `highlights` - 关键输出片段
  - `nextActions` - 下一步行动
  - `referencedPaths` - 引用的文件路径
- `dagChanges` - DAG 变更记录（节点ID、类型、变更状态）
- `openQuestions` - 开放问题
- `missingEvidence` - 缺失证据
- `metrics` - 指标（问题长度、智能体数量等）

**用途：** 记录每轮研究的完整工作痕迹，用于调试和回放

#### 4.2 `artifacts/dag/consensus/` - DAG 共识记录

**文件：**
- `{timestamp}_{guid}.json` - DAG 共识结果

**内容示例：**
```json
{
  "sessionId": "fc284a36a5ac4950bba7a08bf96dc1d2",
  "runId": "fc284a36a5ac4950bba7a08bf96dc1d2:1",
  "workflow": "maker",
  "status": "accepted",
  "stats": {
    "success": true,
    "durationMs": 134303,
    "llmCalls": 11,
    "totalTokens": 52624
  },
  "candidate": {
    "mutationId": "dag_builder_verification_round1",
    "authorAgent": "dag_builder",
    "nodeCount": 6,
    "edgeCount": 9
  },
  "extractedJson": "...",  // DAG 突变的完整 JSON
  "parsed": {
    "Nodes": [
      {
        "Id": "thm_multiplicative_homomorphism_v2",
        "Type": "theorem",
        "Label": "HPA嵌入的乘法同态性：Z(mn)=Z(m)Z(n)"
      }
    ],
    "Edges": [...]
  }
}
```

**用途：** 记录 DAG 共识流程的结果，包括：
- 共识状态（accepted/blocked）
- 节点和边的变更
- 验证统计信息
- 完整的 DAG 突变 JSON

---

### 5. `runs/` - 运行记录

**目录结构：**
```
runs/
  {runId}/
    summary.md          # 本轮总结（人读）
    ui_events.jsonl     # UI 工作痕迹
    {其他文件}          # 如 paper_patch_*.json
```

**文件说明：**

#### `summary.md`
- 人可读的研究总结报告
- 包含各智能体的工作亮点
- 验证结果摘要
- 关键发现和结论

**示例内容：**
```markdown
# 研究总结报告：Holographic Polar Arithmetic (HPA) 数学陈述验证

## TL;DR
- ✅ **成功验证**了第41-63页的所有核心数学陈述
- 📊 **数值验证**与理论推导一致
- 🔍 **逻辑验证**确认所有证明步骤正确
...
```

#### `ui_events.jsonl`
- UI 事件的 JSON Lines 记录
- 包含 RUN/STEP/TOOL/META/MESSAGE_END 等事件
- 用于浏览器刷新时恢复 UI 状态

#### `paper_patch_*.json`
- 论文补丁文件
- 记录对论文的修改

---

## 🔍 数据流向

```
用户输入
  ↓
ResearchSession (in-memory)
  ↓
├─→ artifacts/trace/trace.jsonl (工作痕迹)
├─→ artifacts/dag/consensus/*.json (DAG 共识)
├─→ deliverables/*.json (交付物)
├─→ paper/*.md (论文产出)
└─→ runs/{runId}/* (运行记录)
```

---

## 💾 持久化说明

### ✅ 会保留的内容

1. **所有文件系统内容**
   - `workspace/sessions/{sessionId}/` 下的所有文件
   - 包括 artifacts、deliverables、paper、runs 等

2. **DAG 数据**
   - 存储在 Neo4j 数据库中
   - 通过 `/api/dag/{dagId}` API 查询

3. **Session 元数据**
   - 存储在 `workspace/.data/vibe_sessions.json`
   - 包含 Session ID、Provider Name、DAG ID、创建时间

### ❌ 会丢失的内容

1. **Session 运行状态**
   - 当前正在执行的 run
   - Agent 的内存状态
   - 未完成的步骤
   - 流式输出连接（SSE）

**原因：** Session 是 in-memory 的，重启后需要重新创建

---

## 📊 文件大小和数量

以会话 `fc284a36a5ac4950bba7a08bf96dc1d2` 为例：

- **trace.jsonl**: 6 轮研究记录
- **consensus/**: 多个共识文件（按时间戳命名）
- **deliverables/**: 5 个 JSON 文件
- **runs/**: 1 个运行记录目录

---

## 🎯 用途

1. **调试和回放**
   - 通过 `trace.jsonl` 可以回放整个研究过程
   - 查看每个智能体在每轮中的输出

2. **数据恢复**
   - 浏览器刷新时从 `ui_snapshot.json` 恢复 UI 状态
   - 从共识文件恢复 DAG 状态

3. **审计和追溯**
   - 记录所有决策和变更
   - 追踪 DAG 节点的创建和修改历史

4. **产出管理**
   - 管理论文草稿和大纲
   - 存储研究摘要和结论

---

## 📝 相关文档

- `SESSION_PERSISTENCE.md` - Session 持久化说明
- `DAG_CONSENSUS_ARTIFACTS.md` - DAG 共识文件说明
- `docs/VIBE_RESEARCHING_PLATFORM.md` - 平台架构文档
