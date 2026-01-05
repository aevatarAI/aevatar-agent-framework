# Cognitive Mesh Workflow UI 设计

> **核心命题**：DSL 是认知过程的唯一真相源，Workflow UI 是这个真相的人类可读投影。用户不是在"画流程图"，而是在"观察 AI 如何思考"并适时干预。

---

## 0. 范式反转：从"用户画"到"AI 生成"

### 传统 Workflow 工具

```
用户拖拽方框 → 连线 → 配置参数 → 机器执行
```

**问题**：人类必须预先规划每一步，AI 只是执行者。

### Cognitive Mesh 的范式

```
用户描述意图 → AI 生成 DSL → 机器执行 → UI 渲染认知过程 → 用户观察/干预
```

**本质**：AI 是思考者，用户是观察者与仲裁者。

---

## 1. 架构关系

```
┌─────────────────────────────────────────────────────────────────┐
│                        用户提出问题                              │
│                            ↓                                    │
│   ┌───────────────────────────────────────────────────────────┐ │
│   │              Cognitive Compiler (LLM + 规则)               │ │
│   │                                                           │ │
│   │   自然语言意图  ──────→  DSL (MeshDefinition)              │ │
│   └───────────────────────────────────────────────────────────┘ │
│                            ↓                                    │
│            ┌───────────────┴───────────────┐                    │
│            ↓                               ↓                    │
│    Orleans 执行引擎                  Workflow UI                 │
│    (机器运行 DSL)                    (人类阅读 DSL)              │
│            │                               ↑                    │
│            └───── 运行时事件流 ─────────────┘                    │
│                                                                 │
│   ┌───────────────────────────────────────────────────────────┐ │
│   │                    用户干预指令                            │ │
│   │   (暂停/修正输出/调整约束/重新思考)                         │ │
│   └───────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### 数据流向

| 方向 | 数据 | 说明 |
|------|------|------|
| DSL → UI | `MeshDefinition` | 渲染拓扑结构（节点、边、约束） |
| Runtime → UI | `NodeExecution[]` | 实时更新每个节点的状态、输入、输出 |
| UI → Runtime | `InterventionCommand` | 用户干预指令（暂停、覆盖、重跑） |

---

## 2. UI 的三重职责

### 2.1 观察（Observe）— 让思维可见

**目标**：把 AI 的认知过程从"黑箱"变成"透明鱼缸"。

每个节点展示：

```
┌─────────────────────────────────────────────────────────────┐
│  🔵 explorer (DivergentAgent)                    [Running]  │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  📥 Input                                                   │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ "如何优化这段 React 代码的渲染性能？"                  │    │
│  │ context: { file: "App.tsx", lines: 42-87 }          │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                             │
│  ⚙️ Process                                                 │
│  发散思考中... 生成 5 个假设分支                             │
│  ████████░░░░░░░░ 53%                                       │
│                                                             │
│  📤 Output (Partial)                                        │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ [1] useMemo 缓存计算结果                             │    │
│  │ [2] React.memo 避免子组件重渲染                      │    │
│  │ [3] ...generating...                                │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                             │
│  📊 Metrics                                                 │
│  Tokens: 847 / 2000    Duration: 3.2s    Confidence: --     │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 理解（Understand）— 让决策可解释

**目标**：用户能回答"AI 为什么这样想"。

功能：

1. **策略可视化**：展示当前使用的思维策略（CoT / ToT / GoT / UoT）
2. **分支追踪**：ToT/GoT 模式下，展示被剪枝的分支及原因
3. **置信度热力图**：用颜色标识每个节点的置信度
4. **时间线回放**：可以"倒带"查看任意历史状态

```
┌─────────────────────────────────────────────────────────────┐
│  📈 Strategy: Tree of Thoughts (ToT)                        │
│                                                             │
│  ┌─────┐                                                    │
│  │ 🌱  │ Root                                               │
│  └──┬──┘                                                    │
│     ├──────────┬──────────┐                                 │
│     ▼          ▼          ▼                                 │
│  ┌─────┐   ┌─────┐   ┌─────┐                                │
│  │ 🟢  │   │ 🟡  │   │ ❌  │ ← Pruned: confidence < 0.3     │
│  │ 0.89│   │ 0.67│   │ 0.21│                                │
│  └──┬──┘   └──┬──┘   └─────┘                                │
│     │         │                                             │
│     ▼         ▼                                             │
│  ┌─────┐   ┌─────┐                                          │
│  │ ✅  │   │ 🟡  │                                          │
│  │ 0.94│   │ 0.72│                                          │
│  └─────┘   └─────┘                                          │
│                                                             │
│  [Timeline] ◀ ●────────────────────○ ▶  Step 12/18         │
└─────────────────────────────────────────────────────────────┘
```

### 2.3 干预（Intervene）— 让人类可控

**目标**：用户不是旁观者，是最终仲裁者。

干预类型：

| 干预类型 | 操作 | 效果 |
|---------|------|------|
| **暂停** | 点击节点上的暂停按钮 | 冻结该节点及下游执行 |
| **修正输出** | 编辑节点的 Output | 用人工输出替代 AI 输出 |
| **重新思考** | 右键 → Retry | 重新执行该节点 |
| **调整约束** | 修改 Constraints 面板 | 动态调整置信度阈值等 |
| **强制剪枝** | 右键 → Prune | 手动标记分支为无效 |
| **注入上下文** | 添加 Context | 向节点注入额外信息 |

```
┌─────────────────────────────────────────────────────────────┐
│  🟢 judge (CriticAgent)                         [Completed] │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  📥 Input                                                   │
│  5 hypotheses from explorer                                 │
│                                                             │
│  📤 Output                                                  │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ Recommended: Hypothesis #2 (React.memo)             │    │
│  │ Confidence: 0.91                                    │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  [✏️ Edit Output]  [🔄 Retry]  [⏸️ Pause Downstream] │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. 核心数据模型

### 3.1 Workflow 视图模型

```csharp
// ============================================================
//  WORKFLOW VISUALIZATION
//  将 DSL + 运行时状态 组合为 UI 可渲染的结构
// ============================================================

public record WorkflowVisualization(
    string SessionId,                           // 会话唯一标识
    MeshDefinition Dsl,                         // 原始 DSL 定义
    IReadOnlyList<NodeView> Nodes,              // 节点视图列表
    IReadOnlyList<EdgeView> Edges,              // 边视图列表
    WorkflowStatus Status,                      // 整体状态
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    BudgetUsage BudgetUsage                     // 预算消耗情况
);

public record NodeView(
    string Id,
    string AgentType,                           // DivergentAgent, CriticAgent, etc.
    NodeExecutionState State,                   // Pending | Running | Completed | Failed | Paused
    JsonElement? Input,
    JsonElement? Output,
    double? Confidence,
    int TokensUsed,
    TimeSpan? Duration,
    IReadOnlyList<string> Logs,                 // 诊断日志
    bool IsInterventionAllowed                  // 是否允许干预
);

public record EdgeView(
    string From,
    string To,
    string Channel,
    int MessageCount,                           // 经过该边的消息数
    EdgeState State                             // Idle | Active | Completed
);

public record BudgetUsage(
    int StepsUsed,
    int MaxSteps,
    int TokensUsed,
    int TokenLimit
);

public enum WorkflowStatus
{
    Initializing,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum NodeExecutionState
{
    Pending,
    Running,
    Completed,
    Failed,
    Paused,
    Skipped
}
```

### 3.2 干预命令模型

```csharp
// ============================================================
//  INTERVENTION COMMANDS
//  用户干预的结构化指令
// ============================================================

public abstract record InterventionCommand(
    string SessionId,
    string NodeId,
    DateTimeOffset Timestamp,
    string UserId
);

public record PauseNodeCommand(
    string SessionId,
    string NodeId,
    DateTimeOffset Timestamp,
    string UserId,
    bool PauseDownstream = true
) : InterventionCommand(SessionId, NodeId, Timestamp, UserId);

public record ResumeNodeCommand(
    string SessionId,
    string NodeId,
    DateTimeOffset Timestamp,
    string UserId
) : InterventionCommand(SessionId, NodeId, Timestamp, UserId);

public record OverrideOutputCommand(
    string SessionId,
    string NodeId,
    DateTimeOffset Timestamp,
    string UserId,
    JsonElement NewOutput,
    string Reason                               // 干预原因（审计用）
) : InterventionCommand(SessionId, NodeId, Timestamp, UserId);

public record RetryNodeCommand(
    string SessionId,
    string NodeId,
    DateTimeOffset Timestamp,
    string UserId,
    JsonElement? ModifiedInput = null           // 可选：修改后的输入
) : InterventionCommand(SessionId, NodeId, Timestamp, UserId);

public record PruneBranchCommand(
    string SessionId,
    string NodeId,
    DateTimeOffset Timestamp,
    string UserId,
    string Reason
) : InterventionCommand(SessionId, NodeId, Timestamp, UserId);

public record UpdateConstraintCommand(
    string SessionId,
    string NodeId,
    DateTimeOffset Timestamp,
    string UserId,
    string ConstraintType,                      // confidence_threshold, max_iterations, etc.
    JsonElement NewValue
) : InterventionCommand(SessionId, NodeId, Timestamp, UserId);
```

### 3.3 实时事件流

```csharp
// ============================================================
//  REAL-TIME EVENT STREAM
//  从 Orleans Runtime 推送到 UI 的事件
// ============================================================

public abstract record WorkflowEvent(
    string SessionId,
    DateTimeOffset Timestamp
);

public record NodeStateChangedEvent(
    string SessionId,
    DateTimeOffset Timestamp,
    string NodeId,
    NodeExecutionState OldState,
    NodeExecutionState NewState
) : WorkflowEvent(SessionId, Timestamp);

public record NodeOutputUpdatedEvent(
    string SessionId,
    DateTimeOffset Timestamp,
    string NodeId,
    JsonElement Output,
    bool IsPartial                              // 是否为流式输出
) : WorkflowEvent(SessionId, Timestamp);

public record EdgeMessageEvent(
    string SessionId,
    DateTimeOffset Timestamp,
    string From,
    string To,
    string Channel,
    JsonElement Payload
) : WorkflowEvent(SessionId, Timestamp);

public record BudgetUpdatedEvent(
    string SessionId,
    DateTimeOffset Timestamp,
    BudgetUsage Usage
) : WorkflowEvent(SessionId, Timestamp);

public record InterventionAppliedEvent(
    string SessionId,
    DateTimeOffset Timestamp,
    InterventionCommand Command,
    bool Success,
    string? ErrorMessage
) : WorkflowEvent(SessionId, Timestamp);
```

---

## 4. 交互流程

### 4.1 启动新会话

```
用户 ─────────────────────────────────────────────────────────────→
     │
     │  "分析这段代码的性能问题并给出优化方案"
     │
     ▼
┌─────────────────────────────────────────────────────────────────┐
│  Cognitive Compiler                                             │
│                                                                 │
│  1. 解析用户意图                                                 │
│  2. 选择策略 (ToT - 因为需要多角度分析)                          │
│  3. 生成 DSL                                                    │
│  4. Schema + 语义校验                                            │
│  5. 返回 MeshDefinition                                         │
└─────────────────────────────────────────────────────────────────┘
     │
     ▼
┌─────────────────────────────────────────────────────────────────┐
│  Workflow UI                                                    │
│                                                                 │
│  1. 渲染初始拓扑图（所有节点 Pending）                           │
│  2. 展示策略类型和预算限制                                       │
│  3. 建立 WebSocket 连接                                         │
│  4. 等待运行时事件                                               │
└─────────────────────────────────────────────────────────────────┘
```

### 4.2 运行时实时更新

```
Orleans Runtime                              Workflow UI
      │                                           │
      │ ──── NodeStateChangedEvent ─────────────→ │
      │      (explorer: Pending → Running)        │ 更新节点状态
      │                                           │
      │ ──── NodeOutputUpdatedEvent ────────────→ │
      │      (explorer: partial output)           │ 流式展示输出
      │                                           │
      │ ──── EdgeMessageEvent ──────────────────→ │
      │      (explorer → judge)                   │ 高亮边动画
      │                                           │
      │ ──── BudgetUpdatedEvent ────────────────→ │
      │      (steps: 5/100, tokens: 1200/50000)   │ 更新进度条
      │                                           │
```

### 4.3 用户干预

```
Workflow UI                                  Orleans Runtime
      │                                           │
      │ ──── OverrideOutputCommand ─────────────→ │
      │      (judge: 强制选择 Hypothesis #3)       │
      │                                           │
      │                                           │ 1. 验证权限
      │                                           │ 2. 应用覆盖
      │                                           │ 3. 记录审计日志
      │                                           │ 4. 继续执行
      │                                           │
      │ ←─── InterventionAppliedEvent ────────── │
      │      (success: true)                      │ 显示成功提示
      │                                           │
```

---

## 5. UI 组件设计

### 5.1 页面布局

```
┌─────────────────────────────────────────────────────────────────┐
│  🧠 Cognitive Mesh                              [Session: #42]  │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────────────────┬───────────────────────────┐│
│  │                                 │                           ││
│  │        📊 拓扑画布               │      📋 节点详情           ││
│  │                                 │                           ││
│  │    ┌─────┐                      │  Node: explorer           ││
│  │    │  A  │                      │  Type: DivergentAgent     ││
│  │    └──┬──┘                      │  Status: Running          ││
│  │       │                         │                           ││
│  │    ┌──┴──┐                      │  ┌───────────────────┐    ││
│  │    ▼     ▼                      │  │ Input:            │    ││
│  │ ┌─────┐┌─────┐                  │  │ "分析性能问题..."  │    ││
│  │ │  B  ││  C  │                  │  └───────────────────┘    ││
│  │ └─────┘└─────┘                  │                           ││
│  │                                 │  ┌───────────────────┐    ││
│  │                                 │  │ Output:           │    ││
│  │                                 │  │ [1] useMemo...    │    ││
│  │                                 │  │ [2] React.memo... │    ││
│  │                                 │  └───────────────────┘    ││
│  │                                 │                           ││
│  │                                 │  [✏️ Edit] [🔄 Retry]     ││
│  │                                 │                           ││
│  └─────────────────────────────────┴───────────────────────────┘│
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │  📈 进度: ████████░░░░ 65%    💰 Tokens: 12,450 / 50,000    ││
│  │  🕐 Duration: 2m 34s          📊 Strategy: Tree of Thoughts ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │  [⏸️ Pause All]  [▶️ Resume]  [🛑 Cancel]  [📥 Export DSL]  ││
│  └─────────────────────────────────────────────────────────────┘│
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 5.2 核心组件

| 组件 | 职责 | 交互 |
|------|------|------|
| **TopologyCanvas** | 渲染节点和边的图形 | 点击选中、拖拽平移、滚轮缩放 |
| **NodeCard** | 展示单个节点的详细信息 | 点击展开/收起、编辑输出 |
| **EdgeLine** | 渲染节点间的连接 | 悬停显示消息统计、点击查看历史 |
| **ProgressBar** | 展示整体进度和预算消耗 | 点击查看详细统计 |
| **ControlPanel** | 全局操作按钮 | 暂停/恢复/取消/导出 |
| **TimelineSlider** | 时间线回放控制 | 拖动回溯历史状态 |
| **ConstraintsEditor** | 编辑运行时约束 | 实时调整参数 |

### 5.3 状态颜色编码

| 状态 | 颜色 | 说明 |
|------|------|------|
| Pending | `#9CA3AF` (灰) | 等待执行 |
| Running | `#3B82F6` (蓝) | 正在执行，带脉冲动画 |
| Completed | `#10B981` (绿) | 成功完成 |
| Failed | `#EF4444` (红) | 执行失败 |
| Paused | `#F59E0B` (黄) | 用户暂停 |
| Skipped | `#6B7280` (深灰) | 被跳过（如剪枝） |

---

## 6. 技术实现要点

### 6.1 实时通信

```csharp
// SignalR Hub for real-time workflow updates
public class WorkflowHub : Hub
{
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }

    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
    }
}

// Server-side event broadcasting
public class WorkflowEventBroadcaster : IWorkflowEventHandler
{
    private readonly IHubContext<WorkflowHub> _hubContext;

    public async Task HandleAsync(WorkflowEvent evt)
    {
        await _hubContext.Clients
            .Group(evt.SessionId)
            .SendAsync("WorkflowEvent", evt);
    }
}
```

### 6.2 干预权限控制

```csharp
public interface IInterventionAuthorizer
{
    Task<AuthorizationResult> AuthorizeAsync(
        InterventionCommand command,
        ClaimsPrincipal user
    );
}

public class InterventionAuthorizer : IInterventionAuthorizer
{
    public async Task<AuthorizationResult> AuthorizeAsync(
        InterventionCommand command,
        ClaimsPrincipal user)
    {
        // 1. 验证用户是否是会话所有者
        // 2. 验证节点是否允许干预
        // 3. 验证干预类型是否在用户权限范围内
        // 4. 记录审计日志
        return AuthorizationResult.Success();
    }
}
```

### 6.3 状态快照与回放

```csharp
public interface IWorkflowSnapshotStore
{
    Task SaveSnapshotAsync(string sessionId, int step, WorkflowVisualization snapshot);
    Task<WorkflowVisualization?> GetSnapshotAsync(string sessionId, int step);
    Task<IReadOnlyList<int>> GetAvailableStepsAsync(string sessionId);
}
```

---

## 7. 与 DSL 的协同

### 7.1 DSL 是唯一真相源

```
                    ┌────────────────────┐
                    │   MeshDefinition   │
                    │   (DSL 对象)        │
                    └─────────┬──────────┘
                              │
          ┌───────────────────┼───────────────────┐
          ▼                   ▼                   ▼
   ┌────────────┐      ┌────────────┐      ┌────────────┐
   │ Orleans    │      │ Workflow   │      │ Audit      │
   │ Runtime    │      │ UI         │      │ Log        │
   └────────────┘      └────────────┘      └────────────┘
```

**规则**：
- UI 不存储独立的"workflow 配置"
- 所有结构信息来自 DSL
- 运行时状态来自事件流
- 干预操作生成新事件，不修改 DSL

### 7.2 干预产生新 DSL（可选）

当用户的干预导致结构性变更时（如添加新节点），系统可以生成新版 DSL：

```csharp
public record DslMutation(
    string OriginalDslVersion,
    string NewDslVersion,
    string MutationType,            // AddNode | RemoveEdge | UpdateConstraint
    JsonElement Diff,
    string Reason
);
```

---

## 8. 设计原则总结

| 原则 | 说明 |
|------|------|
| **Single Source of Truth** | DSL 是认知拓扑的唯一定义，UI 只是渲染器 |
| **Real-time Transparency** | 用户应该能实时看到 AI 的思维过程 |
| **Controlled Intervention** | 用户可以干预，但每次干预都必须记录和可审计 |
| **Graceful Degradation** | 即使 WebSocket 断开，用户也能通过轮询获取状态 |
| **Replay Capability** | 任何会话都可以回放，用于调试和学习 |

---

## 9. 未来演进

### v0.2 计划
- [ ] 支持多用户协同观察同一会话
- [ ] 添加"分支对比"视图（并排查看不同思维路径）
- [ ] 集成 LLM 解释器，用自然语言解释每个决策

### v0.3 计划
- [ ] 支持"模板保存"——将成功的 DSL 保存为可复用模板
- [ ] 添加"what-if"模式——在不影响主流程的情况下测试干预效果
- [ ] 集成成本预测——在执行前估算 token 消耗

---

> **结论**：Workflow UI 不是让用户"画流程图"的工具，而是让用户"看见 AI 如何思考"并在必要时介入的观察与控制面板。DSL 定义了认知拓扑的结构，运行时事件流记录了执行过程，UI 则是这两者的人类可读投影。三者共同构成了 Cognitive Mesh 的"可观测、可追溯、可干预"的核心承诺。
