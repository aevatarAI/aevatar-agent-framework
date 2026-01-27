# Phase 4: 共识结果检查详解

## 📋 概述

Phase 4 负责检查 Phase 3（verifier-quorum 或 maker）返回的 `ConsensusResult`，并根据结果决定是否应用 mutation 到 DAG。

## 🔍 Phase 3 的输出：ConsensusResult

### 数据结构

**代码位置**: `DagConsensusRunner.cs` → `ConsensusResult`

```csharp
public sealed record ConsensusResult(
    bool Ok,                          // 是否通过
    bool Blocked,                     // 是否被阻止
    string Workflow,                  // 使用的 workflow ("verifier-quorum" / "maker")
    SraDagMutation? Mutation,        // 验证后的 mutation（如果通过）
    IReadOnlyList<string> RedFlags,   // 拒绝原因列表
    string? ArtifactPath,             // Artifact 文件路径
    string? Error);                   // 错误信息
```

### verifier-quorum 的输出构建

**代码位置**: `DagConsensusRunner.Quorum.cs` → `RunVerifierQuorumAsync`

#### Step 1: 计算投票结果

```csharp
// 计算通过票数（approve=true 且 redFlags.Count == 0）
var approveCount = votes.Count(v => v.Approve && v.RedFlags.Count == 0);

// 收集所有 redFlags（去重，最多 20 个）
var outFlags = votes
    .SelectMany(v => v.RedFlags)
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => x.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Take(20)
    .ToList();

// 如果票数不足且没有 redFlags，添加 "insufficient_quorum"
if (approveCount < cfg.Quorum && outFlags.Count == 0)
    outFlags.Add("insufficient_quorum");
```

#### Step 2: 判断是否通过

```csharp
// 通过条件：approveCount >= quorum 且 outFlags.Count == 0
var ok = approveCount >= cfg.Quorum && outFlags.Count == 0;
var decision = ok ? "accepted" : "blocked";
```

#### Step 3: 构建 Mutation（如果通过）

```csharp
SraDagMutation? mutation = null;
if (ok)
{
    // 如果通过，构建 accepted mutation
    mutation = BuildAcceptedQuorumMutation(ws.SessionId, input.Candidate, cfg, approveCount);
}
```

**关键点**: `BuildAcceptedQuorumMutation` 会：
- 克隆原始 candidate
- 添加共识元数据（`consensus_workflow`, `consensus_quorum`）
- 返回验证后的 mutation

#### Step 4: 返回 ConsensusResult

```csharp
return new ConsensusResult(
    ok,                    // Ok = true/false
    !ok,                   // Blocked = !ok
    "verifier-quorum",     // Workflow
    mutation,              // Mutation（如果通过）或 null（如果被阻止）
    outFlags,              // RedFlags
    artifactPath,          // ArtifactPath
    ok ? null : decision   // Error（如果被阻止）
);
```

**示例输出**:

**通过情况**:
```csharp
ConsensusResult(
    Ok: true,
    Blocked: false,
    Workflow: "verifier-quorum",
    Mutation: <SraDagMutation with nodes and edges>,
    RedFlags: [],
    ArtifactPath: "artifacts/dag/consensus/quorum_20250116_120000_abc123.json",
    Error: null
)
```

**被阻止情况**:
```csharp
ConsensusResult(
    Ok: false,
    Blocked: true,
    Workflow: "verifier-quorum",
    Mutation: null,
    RedFlags: ["insufficient_quorum", "self_edge"],
    ArtifactPath: "artifacts/dag/consensus/quorum_20250116_120000_abc123.json",
    Error: "blocked"
)
```

---

### maker 的输出构建

**代码位置**: `DagConsensusRunner.cs` → `RunAsync`

#### Step 1: 调用 Cognitive DSL Workflow

```csharp
rr = await _cognitive.ExecuteAsync(task, options, progress: input.Progress, ct: ct);
```

#### Step 2: 检查执行结果

```csharp
if (!rr.Success || string.IsNullOrWhiteSpace(rr.Content))
{
    return await FailMakerAsync(..., redFlags: [ErrorMakerFailed], ...);
}
```

#### Step 3: 提取和解析 JSON

```csharp
// 提取 JSON
if (!TryExtractJson(raw, out var json))
{
    return await FailMakerAsync(..., redFlags: [ErrorJsonParseFailed], ...);
}

// 反序列化
parsed = JsonSerializer.Deserialize<DagMutationJson>(json!, Json);
```

#### Step 4: 检查 RedFlags

```csharp
var redFlags = (parsed?.RedFlags ?? [])
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => x.Trim())
    .ToList();

if (redFlags.Count > 0)
{
    return await FailMakerAsync(..., redFlags, ...);
}
```

#### Step 5: 构建 Mutation

```csharp
var mutation = BuildMutation(ws.SessionId, input.Candidate, parsed);

// 检查空 mutation
if (mutation.UpsertNodes.Count == 0 && mutation.UpsertEdges.Count == 0)
{
    return await FailMakerAsync(..., redFlags: [ErrorEmptyMutation], ...);
}
```

#### Step 6: 返回 ConsensusResult

```csharp
return new ConsensusResult(
    true,                    // Ok = true（如果到达这里）
    false,                   // Blocked = false
    MakerWorkflow,           // Workflow = "maker"
    mutation,                // Mutation
    [],                      // RedFlags（空）
    okArtifact,              // ArtifactPath
    null                     // Error（无）
);
```

**示例输出**:

**通过情况**:
```csharp
ConsensusResult(
    Ok: true,
    Blocked: false,
    Workflow: "maker",
    Mutation: <SraDagMutation with normalized nodes and edges>,
    RedFlags: [],
    ArtifactPath: "artifacts/dag/consensus/20250116_120000_abc123.json",
    Error: null
)
```

**被阻止情况**（各种错误）:
```csharp
// JSON 解析失败
ConsensusResult(
    Ok: false,
    Blocked: true,
    Workflow: "maker",
    Mutation: null,
    RedFlags: ["json_parse_failed"],
    ArtifactPath: "...",
    Error: "failed to extract json from maker output"
)

// RedFlags 不为空
ConsensusResult(
    Ok: false,
    Blocked: true,
    Workflow: "maker",
    Mutation: null,
    RedFlags: ["incoherent_mutation", "missing_dependencies"],
    ArtifactPath: "...",
    Error: "blocked_by_red_flags"
)
```

---

## 🔍 Phase 4: 检查共识结果

**代码位置**: `VibeOrchestrator.DagConsensus.cs` → `RunDagApplyAsync`

### Step 1: 调用共识验证

```csharp
DagConsensusRunner.ConsensusResult consensus;
try
{
    var progress = BuildDagConsensusAgUiProgress(session, runId, workflowName: "maker");
    consensus = await _core.DagConsensus.RunAsync(new DagConsensusRunner.ConsensusInput(
        session.Id,
        runId,
        currentDag,
        candidate,
        MaterialsContext: materials?.RenderedContext,
        ProviderName: providerName ?? session.ProviderName,
        Progress: progress), ct);
}
catch (Exception ex)
{
    // 异常处理：返回 blocked 结果
    EmitSection(emit, "### DAG Consensus\n");
    emit($"[dag consensus error] {ex.Message}\n\n");
    return new DagRoundResult(false, true, candidate, null, null, ["consensus_exception"], null);
}
```

### Step 2: 检查共识结果（核心逻辑）

```csharp
if (!consensus.Ok || consensus.Mutation == null)
{
    // 被阻止的情况
    EmitSection(emit, "### DAG Consensus (blocked)\n");
    var flags = consensus.RedFlags.Count == 0 ? "unknown" : string.Join(", ", consensus.RedFlags);
    emit($"**Blocked** (workflow: `{consensus.Workflow}`) redFlags=[{flags}]\n\n");
    return new DagRoundResult(
        false,                    // Accepted = false
        true,                     // Blocked = true
        candidate,                // Candidate（原始 candidate）
        null,                     // AcceptedMutation = null
        consensus.ArtifactPath,   // StagedPath（artifact 路径）
        consensus.RedFlags,       // RedFlags
        consensus.ArtifactPath    // ArtifactPath
    );
}
```

**检查条件详解**:

1. **`!consensus.Ok`**: 
   - `Ok = false` 表示共识验证失败
   - 可能原因：
     - verifier-quorum: `approveCount < quorum` 或 `outFlags.Count > 0`
     - maker: JSON 解析失败、redFlags 不为空、空 mutation 等

2. **`consensus.Mutation == null`**:
   - `Mutation = null` 表示没有生成验证后的 mutation
   - 可能原因：
     - verifier-quorum: 未通过投票，`mutation` 保持为 `null`
     - maker: 各种错误导致 `FailMakerAsync` 返回 `null`

**关键点**: 这两个条件使用 **OR** 逻辑，只要有一个为真，就认为被阻止。

---

### Step 3: 应用通过的 Mutation

```csharp
// Apply accepted mutation
try
{
    var dagId = session.EffectiveDagId;
    var accepted = consensus.Mutation;  // 使用验证后的 mutation

    // 添加共识元数据到 mutation labels
    if (!string.IsNullOrWhiteSpace(consensus.Workflow))
        accepted.Labels["consensus_workflow"] = consensus.Workflow;
    if (!string.IsNullOrWhiteSpace(consensus.ArtifactPath))
        accepted.Labels["consensus_artifact"] = consensus.ArtifactPath;

    // 应用到 DAG
    var applied = await _core.Dag.ApplyMutationAsync(dagId, accepted, ct);

    // 发布事件
    session.Events.Publish(new CustomEvent
    {
        Timestamp = NowMs(),
        Name = "aevatar.vibe.dag_updated",
        Value = new
        {
            sessionId = session.Id,
            dagId,
            runId,
            mutationId = accepted.MutationId,
            nodes = accepted.UpsertNodes.Count,
            edges = accepted.UpsertEdges.Count,
            consensusWorkflow = consensus.Workflow,
            consensusArtifact = consensus.ArtifactPath ?? string.Empty,
            updatedAt = applied.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
        }
    });

    // 输出成功消息
    EmitSection(emit, "### DAG Consensus (accepted)\n");
    emit($"**Applied** (mutationId: `{accepted.MutationId}`, workflow: `{consensus.Workflow}`)\n\n");

    // 返回成功结果
    return new DagRoundResult(
        true,                    // Accepted = true
        false,                   // Blocked = false
        candidate,               // Candidate（原始 candidate）
        accepted,                // AcceptedMutation（验证后的 mutation）
        null,                    // StagedPath = null
        [],                      // RedFlags（空）
        consensus.ArtifactPath   // ArtifactPath
    );
}
catch (Exception ex)
{
    // 应用失败的情况
    EmitSection(emit, "### DAG Consensus (apply failed)\n");
    emit($"[dag apply error] {ex.Message}\n\n");
    return new DagRoundResult(
        false,                   // Accepted = false
        true,                    // Blocked = true
        candidate,               // Candidate
        null,                    // AcceptedMutation = null
        null,                    // StagedPath = null
        ["apply_exception"],     // RedFlags
        consensus.ArtifactPath   // ArtifactPath
    );
}
```

---

## 📊 决策流程图

```
Phase 3 返回 ConsensusResult
    │
    ├─> consensus.Ok == false?
    │   └─> YES → 被阻止
    │       ├─> 输出: "### DAG Consensus (blocked)"
    │       ├─> 显示: redFlags
    │       └─> 返回: DagRoundResult(Accepted=false, Blocked=true, ...)
    │
    ├─> consensus.Mutation == null?
    │   └─> YES → 被阻止
    │       ├─> 输出: "### DAG Consensus (blocked)"
    │       ├─> 显示: redFlags
    │       └─> 返回: DagRoundResult(Accepted=false, Blocked=true, ...)
    │
    └─> NO (Ok=true 且 Mutation != null) → 通过
        ├─> 添加共识元数据到 mutation labels
        │   ├─> consensus_workflow
        │   └─> consensus_artifact
        │
        ├─> 应用 mutation → DAG
        │   └─> DagStore.ApplyMutationAsync(dagId, accepted, ct)
        │
        ├─> 发布事件
        │   └─> aevatar.vibe.dag_updated
        │
        ├─> 输出: "### DAG Consensus (accepted)"
        │
        └─> 返回: DagRoundResult(Accepted=true, Blocked=false, ...)
```

---

## 🔍 详细检查逻辑

### verifier-quorum 的检查逻辑

**Phase 3 输出**:
```csharp
ConsensusResult(
    Ok: approveCount >= quorum && outFlags.Count == 0,
    Blocked: !(approveCount >= quorum && outFlags.Count == 0),
    Workflow: "verifier-quorum",
    Mutation: ok ? BuildAcceptedQuorumMutation(...) : null,
    RedFlags: outFlags,
    ArtifactPath: artifactPath,
    Error: ok ? null : decision
)
```

**Phase 4 检查**:
```csharp
if (!consensus.Ok || consensus.Mutation == null)
{
    // 被阻止
    // consensus.Ok == false 当:
    //   - approveCount < quorum（票数不足）
    //   - outFlags.Count > 0（有 redFlags）
    // consensus.Mutation == null 当:
    //   - ok == false（未通过投票）
}
```

**示例场景**:

**场景 1: 票数不足**
```csharp
// Phase 3 输出
ConsensusResult(
    Ok: false,  // approveCount=1 < quorum=2
    Blocked: true,
    Mutation: null,
    RedFlags: ["insufficient_quorum"]
)

// Phase 4 检查
if (!consensus.Ok || consensus.Mutation == null)  // true || true = true
{
    // 被阻止
}
```

**场景 2: 有 redFlags**
```csharp
// Phase 3 输出
ConsensusResult(
    Ok: false,  // outFlags.Count > 0
    Blocked: true,
    Mutation: null,
    RedFlags: ["self_edge", "duplicate_node_id"]
)

// Phase 4 检查
if (!consensus.Ok || consensus.Mutation == null)  // true || true = true
{
    // 被阻止
}
```

**场景 3: 通过**
```csharp
// Phase 3 输出
ConsensusResult(
    Ok: true,  // approveCount=2 >= quorum=2 && outFlags.Count == 0
    Blocked: false,
    Mutation: <SraDagMutation>,  // 不为 null
    RedFlags: []
)

// Phase 4 检查
if (!consensus.Ok || consensus.Mutation == null)  // false || false = false
{
    // 不进入，继续应用 mutation
}
```

---

### maker 的检查逻辑

**Phase 3 输出**:
```csharp
ConsensusResult(
    Ok: true/false,  // 取决于是否成功
    Blocked: !Ok,
    Workflow: "maker",
    Mutation: ok ? BuildMutation(...) : null,
    RedFlags: redFlags,
    ArtifactPath: artifactPath,
    Error: ok ? null : error
)
```

**Phase 4 检查**:
```csharp
if (!consensus.Ok || consensus.Mutation == null)
{
    // 被阻止
    // consensus.Ok == false 当:
    //   - JSON 解析失败
    //   - redFlags.Count > 0
    //   - 空 mutation
    //   - Cognitive DSL 执行失败
    // consensus.Mutation == null 当:
    //   - 任何失败情况
}
```

**示例场景**:

**场景 1: JSON 解析失败**
```csharp
// Phase 3 输出
ConsensusResult(
    Ok: false,
    Blocked: true,
    Mutation: null,
    RedFlags: ["json_parse_failed"],
    Error: "failed to extract json from maker output"
)

// Phase 4 检查
if (!consensus.Ok || consensus.Mutation == null)  // true || true = true
{
    // 被阻止
}
```

**场景 2: RedFlags 不为空**
```csharp
// Phase 3 输出
ConsensusResult(
    Ok: false,
    Blocked: true,
    Mutation: null,
    RedFlags: ["incoherent_mutation", "missing_dependencies"],
    Error: "blocked_by_red_flags"
)

// Phase 4 检查
if (!consensus.Ok || consensus.Mutation == null)  // true || true = true
{
    // 被阻止
}
```

**场景 3: 通过**
```csharp
// Phase 3 输出
ConsensusResult(
    Ok: true,
    Blocked: false,
    Mutation: <SraDagMutation>,  // 规范化后的 mutation
    RedFlags: []
)

// Phase 4 检查
if (!consensus.Ok || consensus.Mutation == null)  // false || false = false
{
    // 不进入，继续应用 mutation
}
```

---

## 🎯 关键设计决策

### 1. 为什么使用双重检查（`Ok` 和 `Mutation`）？

- **防御性编程**: 即使 `Ok=true`，如果 `Mutation=null`，也无法应用
- **明确性**: `Ok` 表示逻辑判断，`Mutation` 表示实际数据
- **容错性**: 处理边界情况（如空 mutation）

### 2. 为什么使用 OR 逻辑（`!Ok || Mutation == null`）？

- **严格性**: 只要有一个条件不满足，就认为被阻止
- **安全性**: 避免在数据不完整时应用 mutation
- **一致性**: 确保 `Ok=true` 和 `Mutation != null` 同时满足

### 3. 为什么保存 ArtifactPath？

- **可追溯性**: 记录共识过程的详细信息
- **调试支持**: 帮助排查问题
- **审计支持**: 支持研究过程的审计

---

## 📝 返回结果对比

### 被阻止的情况

```csharp
DagRoundResult(
    Accepted: false,
    Blocked: true,
    Candidate: candidate,              // 原始 candidate
    AcceptedMutation: null,           // 无接受的 mutation
    StagedPath: consensus.ArtifactPath, // Artifact 路径
    RedFlags: consensus.RedFlags,      // 拒绝原因
    ArtifactPath: consensus.ArtifactPath
)
```

### 通过的情况

```csharp
DagRoundResult(
    Accepted: true,
    Blocked: false,
    Candidate: candidate,              // 原始 candidate
    AcceptedMutation: accepted,       // 验证后的 mutation
    StagedPath: null,                  // 无 staged path
    RedFlags: [],                      // 无 redFlags
    ArtifactPath: consensus.ArtifactPath
)
```

---

## 🔧 错误处理

### 异常情况

```csharp
catch (Exception ex)
{
    // 共识执行异常
    return new DagRoundResult(
        false, true, candidate, null, null, 
        ["consensus_exception"], null
    );
}
```

### 应用失败

```csharp
catch (Exception ex)
{
    // Mutation 应用失败
    return new DagRoundResult(
        false, true, candidate, null, null,
        ["apply_exception"], consensus.ArtifactPath
    );
}
```

---

## 💡 最佳实践

1. **检查 ArtifactPath**: 如果共识失败，查看 artifact 文件了解详细信息
2. **检查 RedFlags**: 了解具体的拒绝原因
3. **对比 Candidate 和 AcceptedMutation**: 了解共识过程对 mutation 的修改
4. **监控共识成功率**: 如果频繁失败，可能需要调整配置或优化 `dag_builder`
