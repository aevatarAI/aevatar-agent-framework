# MAKER 可视化 - 后端变更指南

> 版本: 1.0  
> 日期: 2026-01-23  
> 预估工作量: 2-3 天

## 1. 变更概述

为了支持 MAKER "AI 辩论会"可视化，后端需要新增 **2 个事件类型**，修改 **3 个文件**。

### 变更目标

| 现状 | 目标 |
|-----|------|
| 只发送 Winner 信息 | 发送所有聚类的票数分布 |
| 不发送 Worker 提案内容 | 发送每个 Worker 的提案预览 |

### 文件变更清单

| 文件 | 变更类型 | 说明 |
|-----|---------|------|
| `src/Aevatar.Agents.Cognitive/cognitive_messages.proto` | **新增** | 添加 2 个新 message |
| `src/Aevatar.Agents.Maker/Voting/VoteEngine.cs` | **修改** | 新增 `GetClusterSnapshots()` 方法 |
| `src/Aevatar.Agents.Cognitive/Agents/CognitiveCoordinatorGAgent.Vote.cs` | **修改** | 在投票过程中发送新事件 |

---

## 2. Proto 定义变更

### 文件: `src/Aevatar.Agents.Cognitive/cognitive_messages.proto`

在文件末尾添加以下内容：

```protobuf
// ============================================================
//  MAKER 可视化事件 (2026-01-23 新增)
// ============================================================

// 投票快照事件 - 每次投票后发送，展示所有聚类的票数
message VoteSnapshotEvent {
  // 执行标识
  string execution_id = 1;
  string step_id = 2;
  
  // 投票进度
  int32 round = 3;
  int32 max_rounds = 4;
  int32 k = 5;
  bool consensus_reached = 6;
  
  // 所有聚类的快照
  repeated ClusterSnapshotProto clusters = 7;
  
  // 时间戳
  google.protobuf.Timestamp timestamp = 8;
}

// 聚类快照
message ClusterSnapshotProto {
  string cluster_id = 1;           // 聚类 ID (hash 前 8 位)
  string content_hash = 2;         // 完整 hash (16 位)
  int32 votes = 3;                 // 当前票数
  bool is_leader = 4;              // 是否领先
  string content_preview = 5;      // 内容预览 (≤200 字符)
  repeated string proposal_ids = 6; // 属于此聚类的提案 ID 列表
}

// 提案事件 - Worker 提交提案时发送
message ProposalSubmittedEvent {
  // 执行标识
  string execution_id = 1;
  string step_id = 2;
  
  // 提案信息
  string proposal_id = 3;          // 格式: {stepId}.gen[{index}]
  string worker_id = 4;            // Worker ID 或 "coordinator"
  string provider_name = 5;        // LLM 提供商: deepseek-chat, gpt-4, claude-3
  
  // 内容
  string content_preview = 6;      // 内容预览 (≤300 字符)
  string content_hash = 7;         // 内容 hash
  
  // 聚类归属 (投票后填充)
  string assigned_cluster_id = 8;  // 归属的聚类 ID
  float similarity_score = 9;      // 与聚类中心的相似度 (0.0-1.0)
  
  // 时间戳
  google.protobuf.Timestamp timestamp = 10;
}
```

---

## 3. VoteEngine 变更

### 文件: `src/Aevatar.Agents.Maker/Voting/VoteEngine.cs`

### 3.1 新增数据类型

在文件末尾（`SemanticCluster` 类之后）添加：

```csharp
/// <summary>
/// 聚类快照（用于可视化）
/// </summary>
public sealed record ClusterSnapshot
{
    /// <summary>聚类 ID (hash 前 8 位)</summary>
    public required string ClusterId { get; init; }
    
    /// <summary>完整 hash</summary>
    public required string ContentHash { get; init; }
    
    /// <summary>当前票数</summary>
    public int Votes { get; init; }
    
    /// <summary>是否领先</summary>
    public bool IsLeader { get; init; }
    
    /// <summary>内容预览 (≤200 字符)</summary>
    public string ContentPreview { get; init; } = "";
    
    /// <summary>属于此聚类的提案 ID 列表</summary>
    public IReadOnlyList<string> ProposalIds { get; init; } = [];
}
```

### 3.2 新增方法

在 `VoteEngine` 类中添加以下方法：

```csharp
/// <summary>
/// 获取所有聚类的快照（用于可视化）
/// </summary>
public IReadOnlyList<ClusterSnapshot> GetClusterSnapshots()
{
    _rwLock.EnterReadLock();
    try
    {
        return _clusters
            .OrderByDescending(c => c.Votes)
            .Select((c, i) => new ClusterSnapshot
            {
                ClusterId = c.Hash.Length >= 8 ? c.Hash[..8] : c.Hash,
                ContentHash = c.Hash,
                Votes = c.Votes,
                IsLeader = i == 0,
                ContentPreview = TruncateContent(c.RepresentativeContent, 200),
                ProposalIds = Enumerable.Range(0, c.Contents.Count)
                    .Select(idx => $"gen[{idx}]")
                    .ToList()
            })
            .ToList();
    }
    finally
    {
        _rwLock.ExitReadLock();
    }
}

/// <summary>
/// 截断内容到指定长度
/// </summary>
private static string TruncateContent(string content, int maxLength)
{
    if (string.IsNullOrEmpty(content)) return "";
    if (content.Length <= maxLength) return content;
    return content[..(maxLength - 3)] + "...";
}
```

---

## 4. CognitiveCoordinatorGAgent.Vote.cs 变更

### 文件: `src/Aevatar.Agents.Cognitive/Agents/CognitiveCoordinatorGAgent.Vote.cs`

### 4.1 新增辅助方法

在文件中添加以下方法：

```csharp
// ============================================================
//  投票可视化事件发送 (2026-01-23 新增)
// ============================================================

/// <summary>
/// 发送提案提交事件
/// </summary>
private async Task EmitProposalSubmittedAsync(
    StepDefinition step,
    string content,
    int proposalIndex,
    string? workerId = null,
    string? providerName = null)
{
    var evt = new ProposalSubmittedEvent
    {
        ExecutionId = CustomState.ExecutionId ?? "",
        StepId = step.Id,
        ProposalId = $"{step.Id}.gen[{proposalIndex}]",
        WorkerId = workerId ?? "coordinator",
        ProviderName = providerName ?? "unknown",
        ContentPreview = TruncateContent(content, 300),
        ContentHash = ComputeContentHash(content),
        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
    };
    
    await PublishAsync(evt);
}

/// <summary>
/// 发送投票快照事件
/// </summary>
private async Task EmitVoteSnapshotAsync(
    StepDefinition step,
    VoteEngine engine,
    int round,
    int maxRounds,
    int k,
    bool consensusReached)
{
    var clusters = engine.GetClusterSnapshots();
    
    var evt = new VoteSnapshotEvent
    {
        ExecutionId = CustomState.ExecutionId ?? "",
        StepId = step.Id,
        Round = round,
        MaxRounds = maxRounds,
        K = k,
        ConsensusReached = consensusReached,
        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
    };
    
    foreach (var cluster in clusters)
    {
        var clusterProto = new ClusterSnapshotProto
        {
            ClusterId = cluster.ClusterId,
            ContentHash = cluster.ContentHash,
            Votes = cluster.Votes,
            IsLeader = cluster.IsLeader,
            ContentPreview = cluster.ContentPreview
        };
        clusterProto.ProposalIds.AddRange(cluster.ProposalIds);
        evt.Clusters.Add(clusterProto);
    }
    
    await PublishAsync(evt);
}

/// <summary>
/// 计算内容 hash (复用现有逻辑或简化版)
/// </summary>
private static string ComputeContentHash(string content)
{
    if (string.IsNullOrEmpty(content)) return "";
    var bytes = System.Text.Encoding.UTF8.GetBytes(content);
    var hashBytes = System.Security.Cryptography.SHA256.HashData(bytes);
    return Convert.ToHexString(hashBytes)[..16];
}

/// <summary>
/// 截断内容
/// </summary>
private static string TruncateContent(string content, int maxLength)
{
    if (string.IsNullOrEmpty(content)) return "";
    if (content.Length <= maxLength) return content;
    return content[..(maxLength - 3)] + "...";
}
```

### 4.2 修改投票流程

在现有的投票循环中，添加事件发送调用。

**查找位置**: 搜索 `SubmitVoteAsync` 的调用处

**修改前（示意）**:

```csharp
// 生成提案
var content = await GenerateProposalContentAsync(step, i);

// 提交投票
var voteResult = await voteEngine.SubmitVoteAsync(content);

if (voteResult?.Success == true)
{
    // 共识达成
    ...
}
```

**修改后（示意）**:

```csharp
// 生成提案
var content = await GenerateProposalContentAsync(step, i);

// 新增: 发送提案事件
await EmitProposalSubmittedAsync(
    step, 
    content, 
    proposalIndex: i,
    workerId: currentWorkerId,
    providerName: currentProviderName);

// 提交投票
var voteResult = await voteEngine.SubmitVoteAsync(content);

// 新增: 发送投票快照
await EmitVoteSnapshotAsync(
    step,
    voteEngine,
    round: currentRound,
    maxRounds: maxRounds,
    k: k,
    consensusReached: voteResult?.Success == true);

if (voteResult?.Success == true)
{
    // 共识达成
    ...
}
```

---

## 5. 事件发送时机总结

```
投票步骤开始
    │
    ├── Round 1
    │   ├── Worker 1 生成提案 → 发送 ProposalSubmittedEvent
    │   ├── 提交投票 → 发送 VoteSnapshotEvent (round=1, votes=[1])
    │   ├── Worker 2 生成提案 → 发送 ProposalSubmittedEvent
    │   ├── 提交投票 → 发送 VoteSnapshotEvent (round=1, votes=[1,1] or [2])
    │   ├── Worker 3 生成提案 → 发送 ProposalSubmittedEvent
    │   ├── 提交投票 → 发送 VoteSnapshotEvent (round=1, votes=[2,1])
    │   └── ... (继续直到 N=2K-1 个提案)
    │
    ├── 检查共识
    │   ├── 未达成 → 继续 Round 2
    │   └── 达成 → 发送最终 VoteSnapshotEvent (consensusReached=true)
    │
    └── 投票步骤结束
```

---

## 6. 测试要点

### 6.1 单元测试

```csharp
[Fact]
public void VoteEngine_GetClusterSnapshots_ReturnsOrderedClusters()
{
    var engine = new VoteEngine(k: 2);
    
    engine.SubmitVote("Answer A");
    engine.SubmitVote("Answer A");  // 相同，归入同一聚类
    engine.SubmitVote("Answer B");
    
    var snapshots = engine.GetClusterSnapshots();
    
    Assert.Equal(2, snapshots.Count);
    Assert.True(snapshots[0].IsLeader);
    Assert.Equal(2, snapshots[0].Votes);
    Assert.Equal(1, snapshots[1].Votes);
}
```

### 6.2 集成测试

- 验证 `VoteSnapshotEvent` 在每次投票后正确发送
- 验证 `ProposalSubmittedEvent` 包含正确的 `workerId` 和 `providerName`
- 验证 `consensusReached` 标志在共识达成时为 `true`

---

## 7. 注意事项

### 7.1 性能

- `GetClusterSnapshots()` 在 ReadLock 下执行，不影响投票性能
- `ContentPreview` 限制 200/300 字符，避免大 payload
- 事件发送使用 `PublishAsync` fire-and-forget，不阻塞主流程

### 7.2 兼容性

- 新增 Proto message 不影响现有客户端（可选字段）
- 前端需处理无新事件的旧版后端（graceful degradation）

### 7.3 安全性

- `ContentPreview` 可能包含用户输入，考虑是否需要脱敏
- 限制 `ProposalIds` 列表大小（理论上不超过 N=2K-1）

---

## 8. 完成检查清单

- [ ] `cognitive_messages.proto` 添加 `VoteSnapshotEvent` 和 `ProposalSubmittedEvent`
- [ ] `dotnet build` 生成新的 Proto 代码
- [ ] `VoteEngine.cs` 添加 `ClusterSnapshot` 类型
- [ ] `VoteEngine.cs` 添加 `GetClusterSnapshots()` 方法
- [ ] `CognitiveCoordinatorGAgent.Vote.cs` 添加 `EmitProposalSubmittedAsync()` 方法
- [ ] `CognitiveCoordinatorGAgent.Vote.cs` 添加 `EmitVoteSnapshotAsync()` 方法
- [ ] 在投票循环中调用新方法
- [ ] 单元测试通过
- [ ] 集成测试验证事件正确发送
