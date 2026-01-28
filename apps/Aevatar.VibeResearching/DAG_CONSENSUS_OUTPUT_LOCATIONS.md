# DAG Consensus 输出查看位置

## 📋 概述

DAG Consensus 的输出可以通过多种方式查看，包括文件、日志、前端 UI 和 API 端点。

---

## 📁 方式 1: Artifact 文件（最详细）

### 文件位置

**基础路径**:
```
workspace/sessions/{sessionId}/artifacts/dag/consensus/
```

**文件命名**:
- **verifier-quorum**: `quorum_{yyyyMMdd_HHmmss}_{guid}.json`
- **maker**: `{yyyyMMdd_HHmmss}_{guid}.json`

### 查找命令

```bash
# 查找特定 session 的所有 consensus artifacts
find workspace/sessions/{sessionId}/artifacts/dag/consensus -name "*.json"

# 查找最新的 consensus artifact
find workspace/sessions/{sessionId}/artifacts/dag/consensus -name "*.json" -type f -exec ls -lt {} + | head -1

# 查看文件内容
cat workspace/sessions/{sessionId}/artifacts/dag/consensus/{filename}.json | jq .
```

### 文件内容结构

#### verifier-quorum Artifact

```json
{
  "sessionId": "string",
  "runId": "string",
  "workflow": "verifier-quorum",
  "config": {
    "verifierCount": 3,
    "quorum": 2
  },
  "decision": "accepted" | "blocked" | "blocked_precheck",
  "error": "string" | "",
  "precheckFlags": ["string"],
  "approveCount": 2,
  "votes": [
    {
      "verifierKey": "dag_consensus_v1_structure",
      "focus": "structure" | "grounding" | "safety",
      "agentId": "string",
      "approve": true | false,
      "redFlags": ["string"],
      "notes": "string",
      "extractedJson": "string",
      "rawOutputExcerpt": "string (最多 4000 字符)",
      "error": "string" | ""
    }
  ]
}
```

**字段说明**:
- **`decision`**: 
  - `"accepted"`: 通过（approveCount >= quorum 且无 redFlags）
  - `"blocked"`: 被阻止（approveCount < quorum 或有 redFlags）
  - `"blocked_precheck"`: 预检查阶段被阻止（precheckFlags 不为空）
- **`error`**: 错误信息（如果有），可能为空字符串
- **`precheckFlags`**: 预检查阶段的 redFlags（如果 candidate 为空或解析失败）
- **`approveCount`**: 通过票数（approve=true 且 redFlags.Count == 0 的票数）
- **`votes`**: 每个 verifier 的投票详情
  - `verifierKey`: 格式为 `dag_consensus_v{index}_{focus}`
  - `focus`: `"structure"`（结构检查）、`"grounding"`（基础检查）、`"safety"`（安全检查）
  - `approve`: `true` 表示批准，`false` 表示拒绝
  - `redFlags`: 该 verifier 发现的 redFlags 列表
  - `notes`: verifier 的备注信息
  - `extractedJson`: 从 verifier 输出中提取的 JSON 字符串
  - `rawOutputExcerpt`: verifier 的原始输出摘要（最多 4000 字符）
  - `error`: verifier 执行错误（如果有），可能为空字符串

#### maker Artifact

```json
{
  "sessionId": "string",
  "runId": "string",
  "workflow": "maker",
  "status": "accepted" | "blocked",
  "error": "string" | null,
  "redFlags": ["string"],
  "stats": {
    "success": true | false,
    "durationMs": 12345,
    "llmCalls": 11,
    "promptTokens": 8799,
    "completionTokens": 8799,
    "totalTokens": 17598
  },
  "candidate": {
    "mutationId": "string",
    "authorAgent": "dag_builder",
    "nodeCount": 4,
    "edgeCount": 7
  },
  "extractedJson": "string" | null,
  "parsed": {
    "MutationId": "string",
    "Author": "string",
    "Nodes": [
      {
        "Id": "string",
        "Type": "axiom|theorem|assumption|hypothesis|unknown",
        "Label": "string (最多 200 字符)",
        "Proof": "string (最多 2000 字符)",
        "Tags": {"k": "v (每个值最多 200 字符)"}
      }
    ],
    "Edges": [
      {
        "From": "string",
        "To": "string",
        "Type": "depends_on" | "其他类型"
      }
    ],
    "RedFlags": ["string"]
  } | null,
  "rawOutput": "string (最多 30,000 字符)"
}
```

**字段说明**:
- **`status`**: `"accepted"` 表示通过，`"blocked"` 表示被阻止
- **`error`**: 错误信息（如果有），可能为 `null`
- **`stats`**: 
  - `success`: `true` 表示 maker workflow 执行成功，`false` 表示执行失败（如异常）
  - `durationMs`: 执行时长（毫秒）
  - `llmCalls`: LLM 调用次数
  - `promptTokens`, `completionTokens`, `totalTokens`: Token 统计
- **`candidate`**: 原始 candidate mutation 的摘要信息
- **`extractedJson`**: 从 maker 输出中提取的 JSON 字符串（可能为 `null`）
- **`parsed`**: 解析后的 mutation 对象（可能为 `null`，如果解析失败）
  - `Nodes[].Label`: 最多 200 字符
  - `Nodes[].Proof`: 最多 2000 字符
  - `Nodes[].Tags`: 每个 tag 值最多 200 字符
- **`rawOutput`**: maker workflow 的原始输出（最多 30,000 字符）

### 关键字段说明（通用）

- **`sessionId`**: 会话 ID
- **`runId`**: 运行 ID（格式：`{sessionId}:{runNumber}`）
- **`workflow`**: 使用的共识模式（`"maker"` 或 `"verifier-quorum"`）
- **`status`** / **`decision`**: 
  - `"accepted"`: 通过
  - `"blocked"`: 被阻止
  - `"blocked_precheck"`: 预检查阶段被阻止（仅 verifier-quorum）
- **`error`**: 错误信息（如果有）
- **`redFlags`**: 拒绝原因列表（如果不为空，说明被阻止）
- **`parsed`**: 解析后的 mutation（包含 nodes、edges、redFlags），可能为 `null`
- **`stats`**: LLM 调用统计（仅 maker 模式）
- **`votes`**: 投票详情（仅 verifier-quorum 模式）
- **`candidate`**: 原始 candidate mutation 的摘要信息（仅 maker 模式）
- **`extractedJson`**: 从输出中提取的 JSON 字符串（可能为 `null`）
- **`rawOutput`** / **`rawOutputExcerpt`**: 原始输出（maker 最多 30,000 字符，verifier-quorum 最多 4,000 字符）

---

## 💬 方式 2: 前端 UI（实时显示）

### SSE 事件流

**端点**: `GET /api/sessions/{sessionId}/events`

**事件类型**: `CUSTOM` → `aevatar.workflow.execution_event`

**关键事件**:

#### Step 事件

```json
{
  "type": "STEP_STARTED" | "STEP_FINISHED",
  "timestamp": 1730000000000,
  "stepName": "vibe.dag_consensus",
  "status": "running" | "completed" | "failed"
}
```

#### 进度事件（maker 模式）

```json
{
  "type": "CUSTOM",
  "timestamp": 1730000000000,
  "name": "aevatar.workflow.execution_event",
  "value": {
    "phase": "vote",
    "nodeId": "dag_consensus:decompose",
    "message": "Round 1: 2/3 votes",
    "status": "running",
    "fields": {
      "status": "running",
      "progress": 0.3,
      "execution_id": "run_123",
      "workflow_name": "maker",
      "step_type": "vote",
      "vote_round": 1,
      "vote_max_rounds": 10,
      "vote_k": 3,
      "vote_current_votes": 2,
      "tokens_used": 1200,
      "llm_calls": 5
    }
  }
}
```

#### DAG 更新事件

```json
{
  "type": "CUSTOM",
  "timestamp": 1730000000000,
  "name": "aevatar.vibe.dag_updated",
  "value": {
    "sessionId": "string",
    "dagId": "global",
    "runId": "string",
    "mutationId": "string",
    "nodes": 5,
    "edges": 3,
    "consensusWorkflow": "maker" | "verifier-quorum",
    "consensusArtifact": "artifacts/dag/consensus/20250116_143022_xxx.json",
    "updatedAt": "2025-01-16T14:30:22Z"
  }
}
```

### 前端显示位置

**组件**: `sisyphus-frontend/src/pages/landing/landing-dag-viewer.tsx`

**显示内容**:
- DAG 图可视化
- 节点和边的详细信息
- 更新时间和统计信息

---

## 📝 方式 3: 助手消息（文本输出）

### 位置

**代码位置**: `VibeOrchestrator.DagConsensus.cs` → `RunDagApplyAsync`

**输出方式**: 通过 `emitAssistantDelta` 输出到前端

### 输出内容

#### 成功场景

```
### DAG Consensus (accepted)

**Applied** (mutationId: `maker_normalized_xxx`, workflow: `maker`)
```

**包含信息**:
- Consensus 状态：`accepted`
- Mutation ID：应用的 mutation 标识符
- Workflow：使用的共识模式（`maker` 或 `verifier-quorum`）

#### 被阻止场景

```
### DAG Consensus (blocked)

**Blocked** (workflow: `maker`) redFlags=[Edge type 'motivated_by' is not a standard dependency type, Node 'xxx' has type 'Unknown' which is not valid]
```

**包含信息**:
- Consensus 状态：`blocked`
- Workflow：使用的共识模式
- RedFlags：拒绝原因列表（逗号分隔）

#### 应用失败场景

```
### DAG Consensus (apply failed)

[dag apply error] {错误信息}
```

**包含信息**:
- Consensus 状态：`apply failed`
- 错误信息：应用 mutation 到 DAG 时发生的错误

### 查看位置

- **前端 UI**: 在助手消息流中显示
- **SSE 事件**: `TEXT_MESSAGE_CONTENT` 事件包含这些文本

---

## 📊 方式 4: 服务器日志

### 关键日志

**代码位置**: `DagConsensusRunner.cs` 和 `DagStore.cs`

#### Consensus 执行日志

```
[DagConsensus] dag_builder output length: {Length}, hasValue: {HasValue}
[DagConsensus] Parsed candidate: nodes={NodeCount}, edges={EdgeCount}
[DagConsensus] Maker workflow completed: success={Success}, durationMs={DurationMs}
```

#### Mutation 应用日志

```
[DagStore] ApplyMutationAsync starting: dagId={DagId}, writeSessionId={WriteSessionId}, nodeCount={NodeCount}, edgeCount={EdgeCount}
[DagStore] Upserting KnowledgeNode: nodeId={NodeId}, label={Label}, type={Type}
[DagStore] KnowledgeNode upserted successfully: nodeId={NodeId}
[DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=X, planNodes=Y, edges=Z
```

#### 错误日志

```
[DagConsensus] Failed to parse dag_builder output. Output length: {Length}
[DagStore] Failed to upsert dag node {NodeId}: {Error}
```

### 日志位置

- **控制台输出**: 如果使用 `dotnet run`
- **日志文件**: 根据日志配置（如果配置了文件输出）

---

## 🔌 方式 5: API 端点

### 获取 DAG 快照

**端点**: `GET /api/dag/global`

**位置**: `ResearchSessionsApi.Runtime.cs`

**返回格式**:
```json
{
  "ok": true,
  "dagId": "global",
  "dag": {
    "nodes": [
      {
        "id": "string",
        "type": "axiom|theorem|hypothesis|assumption|unknown",
        "label": "string",
        "proof": "string",
        "tags": {"k": "v"}
      }
    ],
    "edges": [
      {
        "fromId": "string",
        "toId": "string",
        "type": "depends_on"
      }
    ],
    "updatedAt": "2025-01-16T14:30:22Z"
  }
}
```

**用途**: 获取全局 DAG 快照，包含所有共识通过的节点和边

---

### 获取会话 DAG

**端点**: `GET /api/sessions/{sessionId}/dag`

**返回格式**: 类似全局 DAG，但只包含该会话的节点

---

### 获取 Artifact 文件（通过文件系统）

**注意**: 目前没有直接的 API 端点获取 artifact 文件，需要通过文件系统访问。

---

## 🎯 推荐查看方式

### 1. 快速查看结果

**推荐**: 前端 UI 助手消息

**位置**: 研究会话的助手消息流

**内容**: 
- Consensus 是否通过
- Mutation ID
- Workflow 类型
- RedFlags（如果被阻止）

---

### 2. 详细分析

**推荐**: Artifact 文件

**位置**: `workspace/sessions/{sessionId}/artifacts/dag/consensus/*.json`

**内容**:
- 完整的 consensus 结果
- 所有投票详情（verifier-quorum）
- 完整的 mutation（nodes、edges）
- RedFlags 列表
- LLM 调用统计（maker）

---

### 3. 实时监控

**推荐**: SSE 事件流

**端点**: `GET /api/sessions/{sessionId}/events`

**内容**:
- 实时进度更新
- Step 状态变化
- DAG 更新事件
- 投票进度（maker 模式）

---

### 4. 调试问题

**推荐**: 服务器日志 + Artifact 文件

**内容**:
- 执行流程日志
- 错误信息
- 详细的 consensus 结果

---

## 📋 查看清单

### 检查 Consensus 是否通过

- [ ] 查看前端 UI 助手消息：显示 "**Applied**" 或 "**Blocked**"
- [ ] 查看 artifact 文件：`status` 或 `decision` 字段
- [ ] 查看日志：`[DagConsensus] Maker workflow completed: success=true`

### 查看 Consensus 结果详情

- [ ] 打开 artifact 文件：`workspace/sessions/{sessionId}/artifacts/dag/consensus/*.json`
- [ ] 检查 `parsed` 字段：包含完整的 mutation
- [ ] 检查 `redFlags` 字段：如果不为空，说明被阻止

### 查看投票详情（verifier-quorum）

- [ ] 打开 artifact 文件
- [ ] 查看 `votes` 数组：每个 verifier 的投票详情
- [ ] 查看 `approveCount`：通过票数
- [ ] 查看 `config`：共识配置

### 查看统计信息（maker）

- [ ] 打开 artifact 文件
- [ ] 查看 `stats` 字段：
  - `durationMs`: 执行时长
  - `llmCalls`: LLM 调用次数
  - `promptTokens`: Prompt tokens
  - `completionTokens`: Completion tokens
  - `totalTokens`: 总 tokens

### 查看 DAG 更新结果

- [ ] 查看前端 DAG 图：节点和边是否更新
- [ ] 调用 API：`GET /api/dag/global`
- [ ] 查看 Neo4j：直接查询数据库

---

## 🔍 实际示例

### 示例 1: 查看最新的 Consensus Artifact

```bash
# 查找最新的 artifact 文件
LATEST=$(find workspace/sessions/{sessionId}/artifacts/dag/consensus -name "*.json" -type f -exec ls -t {} + | head -1)

# 查看文件内容
cat "$LATEST" | jq .

# 只查看关键信息
cat "$LATEST" | jq '{workflow, status, decision, redFlags, parsed: {Nodes: .parsed.Nodes | length, Edges: .parsed.Edges | length}}'
```

### 示例 2: 查看所有被阻止的 Consensus

```bash
# 查找所有被阻止的 artifact
find workspace/sessions -path "*/consensus/*.json" -exec jq -e '.status == "blocked" or .decision == "blocked"' {} \; -print
```

### 示例 3: 查看特定 Session 的所有 Consensus 结果

```bash
SESSION_ID="25451e5f3d0b4f1c9770accf6601b9e8"

# 列出所有 consensus artifacts
ls -lt workspace/sessions/$SESSION_ID/artifacts/dag/consensus/*.json

# 查看每个文件的状态
for file in workspace/sessions/$SESSION_ID/artifacts/dag/consensus/*.json; do
  echo "=== $(basename $file) ==="
  jq '{workflow, status, decision, redFlags: .redFlags | length}' "$file"
done
```

---

## 💡 总结

### DAG Consensus 输出内容完整清单

#### 1. Artifact 文件（最完整）

**位置**: `workspace/sessions/{sessionId}/artifacts/dag/consensus/*.json`

**Maker Artifact 包含** (11 个字段):
- ✅ `sessionId`, `runId`, `workflow`
- ✅ `status` (accepted/blocked)
- ✅ `error`, `redFlags`
- ✅ `stats` (success, durationMs, llmCalls, promptTokens, completionTokens, totalTokens)
- ✅ `candidate` (mutationId, authorAgent, nodeCount, edgeCount)
- ✅ `extractedJson`, `parsed` (MutationId, Author, Nodes[], Edges[], RedFlags[]), `rawOutput`

**Verifier-Quorum Artifact 包含** (9 个字段):
- ✅ `sessionId`, `runId`, `workflow`
- ✅ `config` (verifierCount, quorum)
- ✅ `decision` (accepted/blocked/blocked_precheck)
- ✅ `error`, `precheckFlags`, `approveCount`
- ✅ `votes[]` (verifierKey, focus, agentId, approve, redFlags, notes, extractedJson, rawOutputExcerpt, error)

---

#### 2. 前端 UI 助手消息

**位置**: 研究会话的助手消息流

**包含内容**:
- ✅ Consensus 状态（accepted/blocked/apply failed）
- ✅ Mutation ID（如果通过）
- ✅ Workflow 类型
- ✅ RedFlags 列表（如果被阻止）
- ✅ 错误信息（如果失败）

---

#### 3. SSE 事件流

**端点**: `GET /api/sessions/{sessionId}/events`

**包含事件**:
- ✅ `STEP_STARTED` / `STEP_FINISHED`: 共识步骤状态（stepName: "vibe.dag_consensus"）
- ✅ `aevatar.workflow.execution_event`: maker 模式进度详情（vote_round, vote_k, vote_current_votes, tokens_used, llm_calls 等）
- ✅ `aevatar.vibe.dag_updated`: DAG 更新通知（sessionId, dagId, runId, mutationId, nodes, edges, consensusWorkflow, consensusArtifact, updatedAt）

---

#### 4. 服务器日志

**日志前缀**: `[DagConsensus]`, `[DagStore]`

**包含内容**:
- ✅ Candidate 解析信息（节点数、边数）
- ✅ Workflow 执行结果（成功/失败、时长）
- ✅ Mutation 应用过程（节点创建、边创建）
- ✅ 错误信息（解析失败、应用失败）

---

#### 5. API 端点

**端点**: `GET /api/dag/global`

**包含内容**:
- ✅ 完整的 DAG 快照（所有共识通过的节点和边）
- ✅ 节点详情（id, type, label, proof, tags）
- ✅ 边详情（fromId, toId, type）
- ✅ 更新时间戳

---

### 查看位置总结

| 位置 | 内容详细程度 | 实时性 | 推荐场景 |
|------|------------|--------|---------|
| **Artifact 文件** | ⭐⭐⭐⭐⭐ | ❌ | 详细分析、调试、审计 |
| **前端 UI 消息** | ⭐⭐⭐ | ✅ | 快速查看结果 |
| **SSE 事件流** | ⭐⭐⭐⭐ | ✅ | 实时监控进度 |
| **服务器日志** | ⭐⭐⭐ | ✅ | 调试问题、追踪执行流程 |
| **API 端点** | ⭐⭐⭐⭐ | ✅ | 程序化访问、前端显示 |

### 推荐工作流

1. **运行时**: 通过前端 UI 或 SSE 事件流实时查看
2. **完成后**: 查看 artifact 文件获取完整详情
3. **调试时**: 结合日志和 artifact 文件分析问题
4. **程序化访问**: 使用 API 端点获取 DAG 快照

### 输出完整性检查

✅ **所有输出位置已记录**:
- Artifact 文件结构完整（Maker 11 字段，Verifier-Quorum 9 字段）
- 前端 UI 消息格式完整（3 种场景）
- SSE 事件类型完整（3 种事件）
- 日志前缀和内容完整
- API 端点响应格式完整

✅ **所有字段已说明**:
- Maker artifact 的所有字段（包括 stats, candidate, parsed 的详细结构）
- Verifier-quorum artifact 的所有字段（包括 votes 的详细结构）
- Parsed mutation 的详细结构（Nodes[], Edges[] 的字段限制）
- 助手消息的所有场景（accepted/blocked/apply failed）
- SSE 事件的所有字段

---

*最后更新: 2025-01-16*
