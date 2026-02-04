# MAKER 可视化方案设计

> 版本: 1.0  
> 日期: 2026-01-23  
> 状态: 设计评审中

## 1. 目标

将 MAKER 的"多 AI 协作共识机制"从黑盒变成可观察的"AI 辩论会"，让用户能够：
- 看到 Workflow 执行流程（分解 → 并行 → 投票 → 合成）
- 看到每个 Worker 的观点/提案
- 看到观点如何被聚类
- 看到投票过程和共识达成

## 2. 架构总览

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           后端 (Backend)                                 │
│                                                                          │
│  ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐      │
│  │ CognitiveCoord- │    │   VoteEngine    │    │ CognitiveWorker │      │
│  │ inatorGAgent    │    │                 │    │ GAgent          │      │
│  │                 │    │ - 聚类管理       │    │                 │      │
│  │ - 步骤编排      │◄──►│ - 投票计数       │◄──►│ - LLM 调用      │      │
│  │ - 投票协调      │    │ - 共识判定       │    │ - 提案生成      │      │
│  └────────┬────────┘    └────────┬────────┘    └────────┬────────┘      │
│           │                      │                      │               │
│           └──────────────────────┼──────────────────────┘               │
│                                  │                                       │
│                                  ▼                                       │
│                     ┌────────────────────────┐                          │
│                     │  ExecutionTraceEvent   │                          │
│                     │  (Protobuf)            │                          │
│                     │                        │                          │
│                     │  + WorkflowStepEvent   │  ← 现有                  │
│                     │  + VoteSnapshotEvent   │  ← 新增                  │
│                     │  + ProposalEvent       │  ← 新增                  │
│                     └───────────┬────────────┘                          │
│                                 │                                        │
└─────────────────────────────────┼────────────────────────────────────────┘
                                  │ SSE (Server-Sent Events)
                                  ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                           前端 (Frontend)                                │
│                                                                          │
│  ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐      │
│  │ MakerWorkflow   │    │ MakerDebate     │    │ Consensus       │      │
│  │ Graph           │    │ Panel           │    │ Timeline        │      │
│  │                 │    │                 │    │                 │      │
│  │ - 流程图        │    │ - Worker 卡片   │    │ - 时间线        │      │
│  │ - 递归嵌套      │    │ - 聚类投票条    │    │ - 关键时刻      │      │
│  └─────────────────┘    └─────────────────┘    └─────────────────┘      │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. 数据契约

### 3.1 现有数据（✅ 已满足）

后端已经通过 `ExecutionTraceEvent` 提供以下字段：

```typescript
interface ExecutionTraceEvent {
  // 核心标识
  nodeId: string           // stepId
  phase: string            // stepType: llm_call | vote | fan_out | conditional | workflow_call
  message: string
  timestamp: number
  
  // fields 字典中的字段
  fields: {
    status: "pending" | "running" | "completed" | "failed"
    progress: number       // 0.0 - 1.0
    execution_id: string
    workflow_name: string
    step_type: string
    parent_step_id: string // ← 关键：支持嵌套
    depth: number
    
    // 投票字段
    vote_round: number
    vote_max_rounds: number
    vote_k: number
    vote_current_votes: number    // Leader 当前票数
    winner_proposal_id: string
    winner_hash: string
    winner_votes: number
    winner_runner_up_votes: number
    winner_cluster_count: number
    winner_semantic: boolean
    winner_is_consensus: boolean
    
    // 并行字段
    parallel_total: number
    parallel_completed: number
    parallel_failed: number
    
    // 统计字段
    duration_ms: number
    llm_calls: number
    tokens_used: number
    prompt_tokens: number
    completion_tokens: number
    
    // LLM 对话
    system_prompt: string
    user_prompt: string
    assistant_response: string
  }
}
```

### 3.2 新增数据（❌ 需要后端增强）

#### 3.2.1 投票快照事件 `VoteSnapshotEvent`

**用途**：每轮投票后发送，展示所有聚类的票数分布

```protobuf
// 新增到 cognitive_messages.proto
message VoteSnapshotEvent {
  string execution_id = 1;
  string step_id = 2;
  int32 round = 3;
  int32 max_rounds = 4;
  int32 k = 5;
  bool consensus_reached = 6;
  repeated ClusterSnapshot clusters = 7;
  google.protobuf.Timestamp timestamp = 8;
}

message ClusterSnapshot {
  string cluster_id = 1;           // 聚类 ID (hash 前 8 位)
  string content_hash = 2;         // 完整 hash
  int32 votes = 3;                 // 当前票数
  bool is_leader = 4;              // 是否领先
  string content_preview = 5;      // 内容预览 (≤200 字符)
  repeated string proposal_ids = 6; // 属于此聚类的提案 ID 列表
}
```

**对应 TypeScript 类型**：

```typescript
interface VoteSnapshotEvent {
  executionId: string
  stepId: string
  round: number
  maxRounds: number
  k: number
  consensusReached: boolean
  clusters: ClusterSnapshot[]
  timestamp: number
}

interface ClusterSnapshot {
  clusterId: string
  contentHash: string
  votes: number
  isLeader: boolean
  contentPreview: string
  proposalIds: string[]
}
```

#### 3.2.2 提案事件 `ProposalEvent`

**用途**：Worker 提交提案时发送，展示每个 Worker 的观点

```protobuf
// 新增到 cognitive_messages.proto
message ProposalEvent {
  string execution_id = 1;
  string step_id = 2;
  string proposal_id = 3;          // 提案 ID: {stepId}.gen[{index}]
  string worker_id = 4;            // Worker ID
  string provider_name = 5;        // LLM 提供商: deepseek-chat, gpt-4, claude-3
  string content_preview = 6;      // 内容预览 (≤300 字符)
  string content_hash = 7;         // 内容 hash
  string assigned_cluster_id = 8;  // 归属的聚类 ID (投票后填充)
  float similarity_score = 9;      // 与聚类中心的相似度 (0.0-1.0)
  google.protobuf.Timestamp timestamp = 10;
}
```

**对应 TypeScript 类型**：

```typescript
interface ProposalEvent {
  executionId: string
  stepId: string
  proposalId: string
  workerId: string
  providerName: string
  contentPreview: string
  contentHash: string
  assignedClusterId?: string
  similarityScore?: number
  timestamp: number
}
```

---

## 4. 后端改造点

### 4.1 文件清单

| 文件 | 改动类型 | 说明 |
|-----|---------|------|
| `src/Aevatar.Agents.Cognitive/cognitive_messages.proto` | 新增 | 添加 `VoteSnapshotEvent`, `ProposalEvent` |
| `src/Aevatar.Agents.Cognitive/Agents/CognitiveCoordinatorGAgent.Vote.cs` | 修改 | 在投票过程中发送新事件 |
| `src/Aevatar.Agents.Maker/Voting/VoteEngine.cs` | 修改 | 暴露 `GetAllClusters()` 方法 |
| `src/Aevatar.Agents.Cognitive/Agents/CognitiveWorkerGAgent.cs` | 修改 | 提案完成时发送 `ProposalEvent` |

### 4.2 VoteEngine 改造

```csharp
// VoteEngine.cs 新增方法

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
                ClusterId = c.Hash[..8],  // 短 ID
                ContentHash = c.Hash,
                Votes = c.Votes,
                IsLeader = i == 0,
                ContentPreview = Truncate(c.RepresentativeContent, 200),
                ProposalIds = c.Contents.Select((_, idx) => $"gen[{idx}]").ToList()
            })
            .ToList();
    }
    finally
    {
        _rwLock.ExitReadLock();
    }
}

public record ClusterSnapshot
{
    public required string ClusterId { get; init; }
    public required string ContentHash { get; init; }
    public int Votes { get; init; }
    public bool IsLeader { get; init; }
    public string ContentPreview { get; init; } = "";
    public List<string> ProposalIds { get; init; } = [];
}
```

### 4.3 CognitiveCoordinatorGAgent.Vote.cs 改造

```csharp
// 在投票循环中，每收到一个提案后发送快照

private async Task<PrimitiveResult> ExecuteVoteStepAsync(StepDefinition step)
{
    // ... 现有逻辑 ...
    
    for (int i = 0; i < samplesPerRound; i++)
    {
        var proposal = await GenerateProposalAsync(step, i);
        
        // 新增：发送提案事件
        await PublishProposalEventAsync(step, proposal, i);
        
        var voteResult = await voteEngine.SubmitVoteAsync(proposal.Content);
        
        // 新增：发送投票快照
        await PublishVoteSnapshotAsync(step, voteEngine, round);
        
        if (voteResult?.Success == true)
        {
            // 共识达成
            break;
        }
    }
    
    // ... 现有逻辑 ...
}

private async Task PublishVoteSnapshotAsync(
    StepDefinition step, 
    VoteEngine engine, 
    int round)
{
    var clusters = engine.GetClusterSnapshots();
    
    var evt = new VoteSnapshotEvent
    {
        ExecutionId = CustomState.ExecutionId,
        StepId = step.Id,
        Round = round,
        MaxRounds = engine.MaxRounds,
        K = engine.K,
        ConsensusReached = false,  // 在外层判断
        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
    };
    
    foreach (var c in clusters)
    {
        evt.Clusters.Add(new ClusterSnapshotProto
        {
            ClusterId = c.ClusterId,
            ContentHash = c.ContentHash,
            Votes = c.Votes,
            IsLeader = c.IsLeader,
            ContentPreview = c.ContentPreview
        });
        evt.Clusters[^1].ProposalIds.AddRange(c.ProposalIds);
    }
    
    await PublishAsync(evt);
}

private async Task PublishProposalEventAsync(
    StepDefinition step,
    ProposalResult proposal,
    int index)
{
    var evt = new ProposalEvent
    {
        ExecutionId = CustomState.ExecutionId,
        StepId = step.Id,
        ProposalId = $"{step.Id}.gen[{index}]",
        WorkerId = proposal.WorkerId ?? "coordinator",
        ProviderName = proposal.ProviderName ?? "unknown",
        ContentPreview = Truncate(proposal.Content, 300),
        ContentHash = ComputeHash(proposal.Content),
        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
    };
    
    await PublishAsync(evt);
}
```

---

## 5. 前端改造点

### 5.1 组件结构

```
sisyphus-frontend/src/components/sisyphus/maker/
├── MakerVisualization.tsx       # 主容器
├── MakerWorkflowGraph.tsx       # Workflow 流程图
├── MakerDebatePanel.tsx         # Worker 争论面板
├── MakerVotingBars.tsx          # 聚类投票条
├── MakerConsensusTimeline.tsx   # 共识时间线
├── MakerWorkerCard.tsx          # Worker 卡片
├── hooks/
│   ├── useMakerStream.ts        # SSE 事件流 Hook
│   └── useMakerState.ts         # 状态管理 Hook
├── types.ts                     # 类型定义
└── index.ts                     # 导出
```

### 5.2 状态管理

```typescript
// useMakerState.ts

interface MakerState {
  // Workflow 结构
  steps: Map<string, StepState>
  rootStepId: string | null
  
  // 投票状态
  activeVoteStepId: string | null
  voteSnapshots: Map<string, VoteSnapshotEvent[]>  // stepId -> snapshots
  
  // 提案状态
  proposals: Map<string, ProposalEvent[]>  // stepId -> proposals
  
  // 统计
  totalLlmCalls: number
  totalTokensUsed: number
}

interface StepState {
  stepId: string
  stepType: string
  status: "pending" | "running" | "completed" | "failed"
  parentStepId: string | null
  depth: number
  children: string[]
  
  // 投票信息
  voteRound?: number
  voteK?: number
  consensusReached?: boolean
  
  // 并行信息
  parallelTotal?: number
  parallelCompleted?: number
}
```

### 5.3 数据流

```
                     SSE Events
                         │
                         ▼
              ┌─────────────────────┐
              │   useMakerStream    │
              │   (事件解析)         │
              └──────────┬──────────┘
                         │
         ┌───────────────┼───────────────┐
         │               │               │
         ▼               ▼               ▼
┌─────────────┐  ┌─────────────┐  ┌─────────────┐
│ StepEvent   │  │ VoteSnapshot│  │ Proposal    │
│ (现有)      │  │ (新增)      │  │ (新增)      │
└──────┬──────┘  └──────┬──────┘  └──────┬──────┘
       │                │                │
       └────────────────┼────────────────┘
                        │
                        ▼
              ┌─────────────────────┐
              │   useMakerState     │
              │   (状态聚合)         │
              └──────────┬──────────┘
                         │
         ┌───────────────┼───────────────┐
         │               │               │
         ▼               ▼               ▼
┌─────────────┐  ┌─────────────┐  ┌─────────────┐
│ Workflow    │  │ Debate      │  │ Timeline    │
│ Graph       │  │ Panel       │  │             │
└─────────────┘  └─────────────┘  └─────────────┘
```

### 5.4 关键组件实现

#### MakerVotingBars.tsx

```tsx
interface MakerVotingBarsProps {
  clusters: ClusterSnapshot[]
  k: number
  round: number
  maxRounds: number
}

export function MakerVotingBars({ clusters, k, round, maxRounds }: Props) {
  const maxVotes = Math.max(...clusters.map(c => c.votes), 1)
  
  return (
    <div className="voting-bars">
      <div className="voting-header">
        <span>🗳️ VOTE</span>
        <span>Round {round}/{maxRounds}</span>
        <span>K={k}</span>
      </div>
      
      <div className="clusters">
        {clusters.map((cluster, i) => (
          <div 
            key={cluster.clusterId}
            className={cn(
              "cluster-row",
              cluster.isLeader && "leader",
              cluster.votes >= k && "consensus"
            )}
          >
            <div className="cluster-color" style={{ 
              backgroundColor: CLUSTER_COLORS[i % CLUSTER_COLORS.length] 
            }} />
            
            <div className="cluster-bar-container">
              <div 
                className="cluster-bar"
                style={{ width: `${(cluster.votes / maxVotes) * 100}%` }}
              >
                <span className="votes">{cluster.votes}</span>
              </div>
            </div>
            
            <div className="cluster-preview">
              {cluster.contentPreview}
            </div>
          </div>
        ))}
      </div>
      
      <div className="consensus-status">
        {clusters[0]?.votes - (clusters[1]?.votes || 0) >= k ? (
          <span className="consensus-reached">✅ 共识达成！</span>
        ) : (
          <span className="waiting">
            差距: {clusters[0]?.votes - (clusters[1]?.votes || 0)} / 需要: {k}
          </span>
        )}
      </div>
    </div>
  )
}
```

#### MakerWorkerCard.tsx

```tsx
interface MakerWorkerCardProps {
  proposal: ProposalEvent
  cluster?: ClusterSnapshot
  isWinner: boolean
}

export function MakerWorkerCard({ proposal, cluster, isWinner }: Props) {
  return (
    <div className={cn(
      "worker-card",
      isWinner && "winner"
    )}>
      <div className="worker-header">
        <span className="worker-icon">🤖</span>
        <span className="worker-id">{proposal.workerId}</span>
        <span className="provider">{proposal.providerName}</span>
      </div>
      
      {cluster && (
        <div 
          className="cluster-badge"
          style={{ backgroundColor: getClusterColor(cluster.clusterId) }}
        >
          Cluster {cluster.clusterId}
        </div>
      )}
      
      <div className="proposal-content">
        {proposal.contentPreview}
      </div>
      
      {proposal.similarityScore && (
        <div className="similarity">
          相似度: {(proposal.similarityScore * 100).toFixed(1)}%
        </div>
      )}
    </div>
  )
}
```

---

## 6. SSE 事件类型汇总

### 6.1 现有事件（保持不变）

| 事件名 | 用途 |
|-------|------|
| `aevatar.workflow.execution_event` | 步骤状态更新 |
| `STEP_STARTED` / `STEP_FINISHED` | AG-UI 步骤生命周期 |
| `TEXT_MESSAGE_*` | LLM 流式输出 |

### 6.2 新增事件

| 事件名 | 触发时机 | 用途 |
|-------|---------|------|
| `aevatar.maker.vote_snapshot` | 每次投票计数后 | 展示所有聚类票数 |
| `aevatar.maker.proposal_submitted` | Worker 提交提案后 | 展示 Worker 观点 |
| `aevatar.maker.consensus_reached` | 共识达成时 | 高亮获胜聚类 |

### 6.3 事件时序示例

```
t=0     STEP_STARTED: {stepId: "decompose", stepType: "vote"}
t=100   aevatar.maker.proposal_submitted: {proposalId: "decompose.gen[0]", workerId: "w1"}
t=150   aevatar.maker.vote_snapshot: {round: 1, clusters: [{votes: 1}]}
t=200   aevatar.maker.proposal_submitted: {proposalId: "decompose.gen[1]", workerId: "w2"}
t=250   aevatar.maker.vote_snapshot: {round: 1, clusters: [{votes: 1}, {votes: 1}]}
t=300   aevatar.maker.proposal_submitted: {proposalId: "decompose.gen[2]", workerId: "w3"}
t=350   aevatar.maker.vote_snapshot: {round: 1, clusters: [{votes: 2}, {votes: 1}]}
t=400   aevatar.maker.proposal_submitted: {proposalId: "decompose.gen[3]", workerId: "w4"}
t=450   aevatar.maker.vote_snapshot: {round: 1, clusters: [{votes: 3}, {votes: 1}]}
t=500   aevatar.maker.consensus_reached: {winnerId: "cluster_a", votes: 3}
t=550   STEP_FINISHED: {stepId: "decompose", status: "completed"}
```

---

## 7. 实现计划

### Phase 1: 基础 Workflow 可视化（无需后端改动）

**工作量**: 前端 3-5 天

- [ ] 实现 `MakerWorkflowGraph` 组件
- [ ] 实现 `useMakerStream` Hook（解析现有事件）
- [ ] 实现基础投票状态展示（Leader vs RunnerUp）
- [ ] 实现并行子任务进度条

### Phase 2: 后端数据增强

**工作量**: 后端 2-3 天

- [ ] 新增 `VoteSnapshotEvent` 到 proto
- [ ] 新增 `ProposalEvent` 到 proto
- [ ] 修改 `VoteEngine.cs` 暴露聚类信息
- [ ] 修改 `CognitiveCoordinatorGAgent.Vote.cs` 发送新事件

### Phase 3: 完整"AI 辩论会"可视化

**工作量**: 前端 3-5 天

- [ ] 实现 `MakerDebatePanel` 组件
- [ ] 实现 `MakerVotingBars` 组件
- [ ] 实现 `MakerWorkerCard` 组件
- [ ] 实现 `MakerConsensusTimeline` 组件
- [ ] 添加动画效果

---

## 8. 风险与注意事项

### 8.1 性能考虑

- **事件频率**: 投票快照事件可能频繁（每秒多次），考虑节流
- **内容预览**: 限制 `contentPreview` 长度（≤300 字符）避免带宽浪费
- **前端聚合**: 前端需要高效的状态管理，避免重复渲染

### 8.2 兼容性

- **新事件向后兼容**: 前端需处理旧版后端（无新事件）的情况
- **Proto 版本**: 新增字段使用 optional，避免破坏旧客户端

### 8.3 安全性

- **内容脱敏**: `contentPreview` 可能包含敏感信息，考虑脱敏
- **频率限制**: 避免前端被大量事件淹没

---

## 9. 附录

### 9.1 聚类颜色方案

```typescript
const CLUSTER_COLORS = [
  '#3B82F6', // blue
  '#10B981', // green
  '#F59E0B', // amber
  '#EF4444', // red
  '#8B5CF6', // purple
  '#EC4899', // pink
  '#06B6D4', // cyan
  '#F97316', // orange
]
```

### 9.2 参考链接

- MAKER 论文: https://arxiv.org/html/2511.09030v1
- 现有代码: `src/Aevatar.Agents.Cognitive/`
- AG-UI 协议: `src/Aevatar.Agents.AGUI/`
