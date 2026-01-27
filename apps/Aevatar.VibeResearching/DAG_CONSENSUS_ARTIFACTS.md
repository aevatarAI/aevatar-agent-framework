# DAG Consensus Artifacts 文件位置和内容

## 📁 文件保存位置

### 基础路径

**代码位置**: `DagConsensusRunner.cs` → `WriteArtifactAsync` / `WriteQuorumArtifactAsync`

```csharp
var dir = Path.Combine(ws.ArtifactsDir, "dag", "consensus");
Directory.CreateDirectory(dir);
```

**完整路径**:
```
workspace/sessions/{sessionId}/artifacts/dag/consensus/
```

**系统根目录解析**:
- 如果设置了 `VIBE_WORKSPACE_ROOT` 环境变量，使用该路径
- 否则，使用 `ContentRootPath/../..`（相对于应用目录）

---

## 📄 verifier-quorum Artifact 文件

### 文件命名

**代码位置**: `DagConsensusRunner.Quorum.cs` → `WriteQuorumArtifactAsync`

```csharp
var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
var name = $"quorum_{stamp}_{Guid.NewGuid():N}.json";
```

**格式**: `quorum_{yyyyMMdd_HHmmss}_{guid}.json`

**示例**: `quorum_20250116_143022_a1b2c3d4e5f6.json`

### 文件内容结构

```json
{
  "sessionId": "string",
  "runId": "string",
  "workflow": "verifier-quorum",
  "config": {
    "verifierCount": 3,
    "quorum": 2
  },
  "decision": "accepted" | "blocked",
  "error": "string" | "",
  "precheckFlags": ["string"],
  "approveCount": 2,
  "votes": [
    {
      "verifierKey": "dag_consensus_v1_structure",
      "focus": "structure",
      "agentId": "string",
      "approve": true,
      "redFlags": ["string"],
      "notes": "string",
      "extractedJson": "string",
      "rawOutputExcerpt": "string (最多 4000 字符)",
      "error": "string" | ""
    }
    // ... (最多 N 个 votes，N = VerifierCount)
  ]
}
```

### 包含的信息

✅ **包含**:
- Session ID 和 Run ID
- 共识配置（VerifierCount, Quorum）
- 决策结果（accepted/blocked）
- 预检查 flags
- 通过票数（approveCount）
- 每个 verifier 的投票详情：
  - `rawOutputExcerpt`: Verifier 的原始输出（最多 4000 字符）
  - `extractedJson`: 提取的 JSON（approve, redFlags, notes）
  - `approve`: 是否通过
  - `redFlags`: 拒绝原因列表
  - `notes`: 备注

❌ **不包含**:
- System Prompt（verifier 的 system prompt）
- User Prompt（发送给 verifier 的完整 user prompt）
- Materials Context（虽然包含在 user prompt 中，但不单独保存）

---

## 📄 maker Artifact 文件

### 文件命名

**代码位置**: `DagConsensusRunner.cs` → `WriteArtifactAsync`

```csharp
var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
var name = $"{stamp}_{Guid.NewGuid():N}.json";
```

**格式**: `{yyyyMMdd_HHmmss}_{guid}.json`

**示例**: `20250116_143022_a1b2c3d4e5f6.json`

### 文件内容结构

```json
{
  "sessionId": "string",
  "runId": "string",
  "workflow": "maker",
  "status": "accepted" | "blocked",
  "error": "string" | null,
  "redFlags": ["string"],
  "stats": {
    "success": true,
    "durationMs": 12345,
    "llmCalls": 10,
    "promptTokens": 5000,
    "completionTokens": 3000,
    "totalTokens": 8000
  },
  "candidate": {
    "mutationId": "string",
    "authorAgent": "dag_builder",
    "nodeCount": 5,
    "edgeCount": 3
  },
  "extractedJson": "string",
  "parsed": {
    "mutationId": "string",
    "author": "string",
    "nodes": [...],
    "edges": [...],
    "redFlags": [...]
  },
  "rawOutput": "string (最多 30,000 字符)"
}
```

### 包含的信息

✅ **包含**:
- Session ID 和 Run ID
- Workflow 名称（"maker"）
- 状态（accepted/blocked）
- 错误信息（如果有）
- RedFlags（如果有）
- 统计信息：
  - `durationMs`: 执行时长（毫秒）
  - `llmCalls`: LLM 调用次数
  - `promptTokens`: Prompt tokens
  - `completionTokens`: Completion tokens
  - `totalTokens`: 总 tokens
- Candidate mutation 摘要
- 提取的 JSON（extractedJson）
- 解析后的 mutation（parsed）
- 原始输出（rawOutput，最多 30,000 字符）

❌ **不包含**:
- System Prompt（maker.yaml 中的 system prompt）
- User Prompt（BuildTaskPrompt 构建的 task prompt）
- Materials Context（虽然可能包含在 task prompt 中，但不单独保存）

---

## 🔍 如何查找 Artifact 文件

### 方法 1: 通过 Session ID 查找

```bash
# 查找特定 session 的 consensus artifacts
find workspace/sessions/{sessionId}/artifacts/dag/consensus -name "*.json"
```

### 方法 2: 查找所有 consensus artifacts

```bash
# 查找所有 consensus artifacts
find workspace/sessions -path "*/consensus/*.json" -type f
```

### 方法 3: 通过文件名模式查找

```bash
# 查找 verifier-quorum artifacts
find workspace/sessions -name "quorum_*.json"

# 查找 maker artifacts（排除 quorum_）
find workspace/sessions -path "*/consensus/*.json" ! -name "quorum_*"
```

---

## 📝 提示词保存情况

### 当前状态

**❌ 提示词未保存到 Artifact 文件**

从代码分析：

1. **verifier-quorum**:
   - Artifact 文件只包含 `rawOutputExcerpt`（verifier 的输出）
   - **不包含** system prompt 和 user prompt

2. **maker**:
   - Artifact 文件只包含 `rawOutput`（maker workflow 的输出）
   - **不包含** system prompt 和 user prompt

3. **SaveAgentPromptsToFileAsync**:
   - 只保存 `research_assistant`, `planner`, `reasoner`, `verifier`, `librarian`, `dag_builder` 的提示词
   - **不保存** verifier-quorum 和 maker 的提示词

### 提示词位置（代码中）

#### verifier-quorum

**System Prompt**:
- **文件**: `VibeVerifierAgent.cs` → `GetSystemPrompt()`
- **路径**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibeVerifierAgent.cs`

**User Prompt**:
- **文件**: `DagConsensusRunner.Quorum.cs` → `BuildVerifierPrompt()`
- **路径**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/Dag/DagConsensusRunner.Quorum.cs`

#### maker

**System Prompt**:
- **文件**: `workflows/maker.yaml` → `defaults.llm_call.system`
- **路径**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/workflows/maker.yaml`

**User Prompt (Task Prompt)**:
- **文件**: `DagConsensusRunner.cs` → `BuildTaskPrompt()`
- **路径**: `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/Dag/DagConsensusRunner.cs`

---

## 💡 如何查看提示词

### 方法 1: 查看源代码

**verifier-quorum System Prompt**:
```bash
cat apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching/Vibe/VibeVerifierAgent.cs
```

**verifier-quorum User Prompt**:
```bash
# 查看 BuildVerifierPrompt 方法
grep -A 50 "BuildVerifierPrompt" apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/Dag/DagConsensusRunner.Quorum.cs
```

**maker System Prompt**:
```bash
cat apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/workflows/maker.yaml | grep -A 30 "system:"
```

**maker User Prompt**:
```bash
# 查看 BuildTaskPrompt 方法
grep -A 50 "BuildTaskPrompt" apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Vibe/Dag/DagConsensusRunner.cs
```

### 方法 2: 查看 Artifact 文件中的输出

虽然 artifact 文件不包含提示词，但包含 LLM 的输出：

**verifier-quorum**:
```bash
# 查看某个 artifact 文件
cat workspace/sessions/{sessionId}/artifacts/dag/consensus/quorum_*.json | jq '.votes[].rawOutputExcerpt'
```

**maker**:
```bash
# 查看某个 artifact 文件
cat workspace/sessions/{sessionId}/artifacts/dag/consensus/*.json | jq '.rawOutput'
```

---

## 🔧 如果需要保存提示词

### 当前限制

Artifact 文件**不包含**提示词，如果需要保存，需要修改代码：

1. **修改 `WriteQuorumArtifactAsync`**:
   - 添加 `systemPrompt` 和 `userPrompt` 字段
   - 从 `BuildVerifierPrompt` 获取 user prompt
   - 从 `VibeVerifierAgent.GetSystemPrompt()` 获取 system prompt

2. **修改 `WriteArtifactAsync`**:
   - 添加 `systemPrompt` 和 `userPrompt` 字段
   - 从 `BuildTaskPrompt` 获取 user prompt
   - 从 `maker.yaml` 读取 system prompt

3. **或者修改 `SaveAgentPromptsToFileAsync`**:
   - 添加对 verifier-quorum 和 maker 的支持
   - 在共识执行时收集提示词记录

---

## 📊 Artifact 文件示例

### verifier-quorum 示例

```json
{
  "sessionId": "abc123",
  "runId": "run_xyz",
  "workflow": "verifier-quorum",
  "config": {
    "verifierCount": 3,
    "quorum": 2
  },
  "decision": "accepted",
  "error": "",
  "precheckFlags": [],
  "approveCount": 2,
  "votes": [
    {
      "verifierKey": "dag_consensus_v1_structure",
      "focus": "structure",
      "agentId": "verifier_abc",
      "approve": true,
      "redFlags": [],
      "notes": "Structure is sound, no cycles detected",
      "extractedJson": "{\"approve\":true,\"redFlags\":[],\"notes\":\"...\"}",
      "rawOutputExcerpt": "{\"approve\":true,\"redFlags\":[],\"notes\":\"Structure is sound...\"}",
      "error": ""
    },
    {
      "verifierKey": "dag_consensus_v2_grounding",
      "focus": "grounding",
      "agentId": "verifier_def",
      "approve": true,
      "redFlags": [],
      "notes": "Mutations align with DAG facts",
      "extractedJson": "{\"approve\":true,\"redFlags\":[],\"notes\":\"...\"}",
      "rawOutputExcerpt": "{\"approve\":true,\"redFlags\":[],\"notes\":\"Mutations align...\"}",
      "error": ""
    },
    {
      "verifierKey": "dag_consensus_v3_safety",
      "focus": "safety",
      "agentId": "verifier_ghi",
      "approve": false,
      "redFlags": [],
      "notes": "Minor concerns but acceptable",
      "extractedJson": "{\"approve\":false,\"redFlags\":[],\"notes\":\"...\"}",
      "rawOutputExcerpt": "{\"approve\":false,\"redFlags\":[],\"notes\":\"Minor concerns...\"}",
      "error": ""
    }
  ]
}
```

### maker 示例

```json
{
  "sessionId": "abc123",
  "runId": "run_xyz",
  "workflow": "maker",
  "status": "accepted",
  "error": null,
  "redFlags": [],
  "stats": {
    "success": true,
    "durationMs": 12345,
    "llmCalls": 10,
    "promptTokens": 5000,
    "completionTokens": 3000,
    "totalTokens": 8000
  },
  "candidate": {
    "mutationId": "dag_builder_abc",
    "authorAgent": "dag_builder",
    "nodeCount": 5,
    "edgeCount": 3
  },
  "extractedJson": "{\"mutationId\":\"...\",\"nodes\":[...],\"edges\":[...]}",
  "parsed": {
    "mutationId": "maker_normalized_abc",
    "author": "maker",
    "nodes": [
      {
        "id": "thm_1",
        "type": "theorem",
        "label": "...",
        "proof": "..."
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
  },
  "rawOutput": "You are validating and synthesizing...\n\n{\"mutationId\":\"...\",...}"
}
```

---

## 🎯 总结

### Artifact 文件位置

```
workspace/sessions/{sessionId}/artifacts/dag/consensus/
├── quorum_{timestamp}_{guid}.json  (verifier-quorum)
└── {timestamp}_{guid}.json         (maker)
```

### 包含的信息

| 信息类型 | verifier-quorum | maker |
|---------|----------------|-------|
| **输出** | ✅ (rawOutputExcerpt) | ✅ (rawOutput) |
| **解析结果** | ✅ (extractedJson) | ✅ (extractedJson, parsed) |
| **投票详情** | ✅ (votes) | ❌ |
| **统计信息** | ❌ | ✅ (stats) |
| **System Prompt** | ❌ | ❌ |
| **User Prompt** | ❌ | ❌ |

### 提示词位置（代码中）

- **verifier-quorum System Prompt**: `VibeVerifierAgent.cs`
- **verifier-quorum User Prompt**: `DagConsensusRunner.Quorum.cs` → `BuildVerifierPrompt()`
- **maker System Prompt**: `workflows/maker.yaml`
- **maker User Prompt**: `DagConsensusRunner.cs` → `BuildTaskPrompt()`

---

## 💡 建议

如果需要保存提示词到 artifact 文件，可以：

1. **修改 artifact 文件结构**，添加 `systemPrompt` 和 `userPrompt` 字段
2. **或者**修改 `SaveAgentPromptsToFileAsync`，添加对共识过程的支持
3. **或者**创建单独的提示词日志文件，专门保存共识相关的提示词
