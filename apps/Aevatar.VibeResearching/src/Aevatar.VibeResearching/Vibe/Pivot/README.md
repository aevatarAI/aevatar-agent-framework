# 研究方向转向（Pivot）功能

## 概述

研究方向转向功能允许用户在科研助手会话过程中动态改变研究方向。当用户表达想要转换研究主题的意图时，系统会：

1. **智能检测**用户的方向变更意图
2. **软删除**当前方向下的待执行计划节点
3. **保留**用户明确要求保留的研究成果
4. **标记**已完成的知识节点为"已取代"状态
5. **支持回滚**在指定时间窗口内撤销转向操作

## 架构设计

### 核心组件

```
┌─────────────────────────────────────────────────────────────────┐
│                      VibeOrchestrator                           │
│  (入口点：在每轮会话开始时检测方向变更意图)                        │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                  DirectionChangeDetector                         │
│  (LLM驱动的意图检测器，分析用户消息判断是否要转向)                  │
└──────────────────────────┬──────────────────────────────────────┘
                           │ DirectionChangeIntent
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                        PivotQueue                                │
│  (请求队列，确保同一会话的转向操作串行执行)                         │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                   AgentPivotCoordinator                          │
│  (协调器：编排DAG更新 + 子代理通知)                                │
└──────────────────────────┬──────────────────────────────────────┘
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
┌──────────────────────┐    ┌──────────────────────────┐
│   PivotOrchestrator  │    │   PivotEventPublisher    │
│  (DAG操作执行器)      │    │  (事件发布 + 确认追踪)     │
└──────────┬───────────┘    └──────────────────────────┘
           │
           ▼
┌──────────────────────┐
│  PivotSnapshotManager │
│  (快照管理 + 回滚)     │
└──────────────────────┘
```

### 数据流

```
用户消息 → 意图检测 → 置信度判断 → 队列入队 → DAG操作 → 代理通知 → 用户反馈
                         │
                         ├─ 高置信度 (≥0.7): 自动执行转向
                         ├─ 中置信度 (0.6-0.7): 请求用户确认
                         └─ 低置信度 (<0.6): 忽略，继续当前方向
```

## 核心概念

### 1. 方向变更意图 (DirectionChangeIntent)

用户意图的结构化表示，包含：

| 字段 | 类型 | 说明 |
|------|------|------|
| `SessionId` | string | 会话标识符 |
| `MessageId` | string | 触发检测的消息ID |
| `IsDirectionChange` | bool | 是否为方向变更请求 |
| `Confidence` | double | 置信度 (0.0 - 1.0) |
| `NewTopic` | string? | 新的研究方向/主题 |
| `PreserveAspects` | List<string> | 用户要求保留的方面 |
| `NeedsClarification` | bool | 是否需要用户澄清 |

### 2. 知识节点分类

转向时，现有节点会被分为三类：

| 分类 | 节点类型 | 处理方式 |
|------|----------|----------|
| **取消 (Cancelled)** | Plan节点 + Active状态 | 标记为 `Cancelled`，停止执行 |
| **保留 (Preserved)** | 匹配 `PreserveAspects` | 保持不变 |
| **取代 (Superseded)** | Knowledge节点 + Active状态 | 标记为 `Superseded`，仍可访问 |

### 3. 节点状态 (PivotNodeStatus)

```csharp
public enum PivotNodeStatus
{
    Active,      // 活跃状态（默认）
    Cancelled,   // 已取消（转向时软删除）
    Superseded,  // 已取代（旧方向的已完成知识）
    Preserved    // 已保留（用户明确要求保留）
}
```

### 4. 节点类型 (KnowledgeNodeKind)

```csharp
public enum KnowledgeNodeKind
{
    Knowledge,  // 已完成的知识/研究成果
    Plan        // 待执行的计划/任务
}
```

## 配置选项

通过 `appsettings.json` 配置：

```json
{
  "Pivot": {
    "ConfidenceThreshold": 0.7,
    "ClarificationThreshold": 0.6,
    "RollbackWindowMinutes": 30,
    "SubagentAckTimeoutSeconds": 3,
    "MaxQueueDepth": 5,
    "SemanticSimilarityThreshold": 0.7
  }
}
```

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `ConfidenceThreshold` | 0.7 | 自动触发转向的最低置信度 |
| `ClarificationThreshold` | 0.6 | 请求用户确认的置信度阈值 |
| `RollbackWindowMinutes` | 30 | 允许回滚的时间窗口（分钟） |
| `SubagentAckTimeoutSeconds` | 3 | 等待子代理确认的超时时间 |
| `MaxQueueDepth` | 5 | 每个会话的最大待处理转向请求数 |
| `SemanticSimilarityThreshold` | 0.7 | 语义相似度阈值（用于细化vs完全转向） |

## 工作流程详解

### 阶段1：意图检测

`DirectionChangeDetector` 使用 LLM 分析用户消息：

```
用户: "我不想研究量子计算了，改成研究机器学习吧，但是保留CNN相关的内容"
                    │
                    ▼
          ┌─────────────────┐
          │   LLM 分析器     │
          │ (低温度=0.1)    │
          └────────┬────────┘
                   │
                   ▼
{
  "isDirectionChange": true,
  "confidence": 0.92,
  "newTopic": "机器学习",
  "preserveAspects": ["CNN"],
  "needsClarification": false
}
```

### 阶段2：队列管理

`PivotQueue` 确保同一会话的转向请求串行执行：

```
Session-A: Request-1 → [执行中]
Session-A: Request-2 → [排队等待]
Session-A: Request-3 → [排队等待]
Session-B: Request-1 → [并行执行] ← 不同会话可并行
```

特性：
- 每会话独立队列
- 基于信号量的串行控制
- 队列满时拒绝新请求（返回 `QueueFull`）
- 异常后自动释放锁

### 阶段3：DAG 操作

`PivotOrchestrator` 执行知识图谱更新：

```
1. 创建回滚快照
   └─ PivotSnapshotManager.CreateSnapshotAsync()

2. 节点分类
   └─ ClassifyNodesForPivotAsync()
       ├─ 已取消节点 → Skip
       ├─ Plan + Active → Cancelled
       ├─ Knowledge + Active → Superseded
       └─ 匹配 PreserveAspects → Preserved

3. 更新节点状态
   └─ UpdateNodesForPivotAsync()
       ├─ UpsertNodeAsync(pivotStatus: Cancelled)
       └─ UpsertNodeAsync(pivotStatus: Superseded)
```

### 阶段4：代理协调

`AgentPivotCoordinator` 通知所有子代理：

```
                    PivotEvent
                        │
        ┌───────────────┼───────────────┐
        ▼               ▼               ▼
   ┌─────────┐    ┌─────────┐    ┌─────────┐
   │ Planner │    │Librarian│    │Reasoner │
   └────┬────┘    └────┬────┘    └────┬────┘
        │              │              │
        └──────────────┴──────────────┘
                       │
                       ▼
               Acknowledgments
               (3秒超时等待)
```

标准代理列表：
- planner
- librarian
- reasoner
- verifier
- dag_builder
- paper_editor

### 阶段5：回滚支持

在 `RollbackWindowMinutes` 时间窗口内，用户可以撤销转向：

```
POST /api/sessions/{sessionId}/pivot/rollback
{
  "pivotId": "pivot_xxxxx",        // 可选，默认回滚最近一次
  "preserveNewCompleted": true     // 是否保留转向后新完成的知识
}
```

回滚逻辑：
1. 恢复被取消/取代的节点状态
2. 可选：删除转向后创建的新节点
3. 可选：保留转向后完成的知识节点

## API 端点

### 执行回滚

```http
POST /api/sessions/{sessionId}/pivot/rollback
Content-Type: application/json

{
  "pivotId": "pivot_1704067200000",
  "preserveNewCompleted": true
}
```

响应：
```json
{
  "sessionId": "session-123",
  "pivotId": "pivot_1704067200000",
  "success": true,
  "restoredNodeCount": 5,
  "preservedNewNodeCount": 2
}
```

### 获取队列状态

```http
GET /api/pivot/queue/status
```

响应：
```json
[
  {
    "sessionId": "session-123",
    "isProcessing": true,
    "currentPivotId": "pivot_1704067200000",
    "queueDepth": 2,
    "maxDepth": 5
  }
]
```

## 用户界面反馈

### 中文消息

| 场景 | 消息 |
|------|------|
| 转向开始 | "正在更新研究方向..." |
| 转向完成 | "✓ 研究方向已更新完成 (取消了 X 个待执行计划，保留了 Y 个已完成成果)" |
| 队列等待 | "✓ 研究方向已更新完成 (队列等待后执行)" |
| 队列已满 | "⏳ 研究方向更新队列已满，请稍后重试..." |
| 转向失败 | "⚠ 研究方向更新失败，将继续使用当前方向" |
| 请求确认 | "您是否想要改变研究方向？请确认。" |

### 事件发布

系统通过 AG-UI 事件协议发布以下事件：

```javascript
// 转向开始
{
  "name": "aevatar.vibe.pivot_intent_detected",
  "value": {
    "sessionId": "...",
    "isDirectionChange": true,
    "confidence": 0.92,
    "newTopic": "机器学习"
  }
}

// 转向完成
{
  "name": "aevatar.vibe.pivot_completed",
  "value": {
    "sessionId": "...",
    "pivotId": "pivot_xxx",
    "status": "Completed",
    "cancelledCount": 3,
    "preservedCount": 1,
    "durationMs": 156,
    "wasQueued": false
  }
}
```

## 保留匹配逻辑

`ShouldPreserveNode` 方法按以下优先级匹配：

1. **DirectionContext** - 节点的研究方向上下文
2. **CoreDescription** - 节点核心描述
3. **DetailedDescription** - 节点详细描述

匹配规则：
- 大小写不敏感
- 子字符串包含匹配
- 任一 `PreserveAspect` 匹配即保留

```csharp
// 示例
preserveAspects: ["CNN", "图像"]
node.CoreDescription: "CNN架构分析与优化"
→ 匹配 "CNN" → 保留
```

## 性能考虑

### 快照优化

为避免重复调用 `GetKnowledgeSnapshotAsync()`，快照在创建时会被缓存：

```csharp
// PivotSnapshotManager.CreateSnapshotAsync() 中创建快照
var snapshot = await client.GetKnowledgeSnapshotAsync();
var metadata = PivotSnapshotMetadata.Create(..., snapshot, ...);

// PivotOrchestrator.ExecutePivotAsync() 中复用
var snapshot = snapshotMetadata.Snapshot;  // 直接使用，不再调用API
```

### 并发控制

- 不同会话的转向操作可并行执行
- 同一会话的转向操作严格串行
- 使用 `SemaphoreSlim` 实现无锁等待

## 监控指标

`PivotMetrics` 提供以下 OpenTelemetry 指标：

| 指标名称 | 类型 | 说明 |
|----------|------|------|
| `pivot.operations.total` | Counter | 转向操作总数（按成功/失败分组） |
| `pivot.duration.ms` | Histogram | 转向操作耗时分布 |
| `pivot.detection.confidence` | Histogram | 检测置信度分布 |
| `pivot.queue.depth` | ObservableGauge | 当前队列深度 |
| `pivot.rollback.total` | Counter | 回滚操作总数 |

Meter 名称：`VibeResearching.Pivot`

## 测试覆盖

功能包含 113 个单元测试，覆盖：

- 意图检测（含中英文消息）
- 节点分类逻辑
- 队列串行化
- 并发安全
- 回滚操作
- 代理协调
- 用户反馈
- 边界条件

运行测试：
```bash
dotnet test --filter "FullyQualifiedName~Pivot"
```

## 文件结构

```
Vibe/Pivot/
├── Models/
│   ├── DirectionChangeIntent.cs    # 方向变更意图
│   ├── PivotOperation.cs           # 转向操作结果
│   └── PivotSnapshotMetadata.cs    # 快照元数据
├── Messages/
│   ├── PivotEvent.cs               # 广播事件
│   ├── PivotAcknowledgment.cs      # 代理确认
│   ├── RollbackRequest.cs          # 回滚请求
│   └── RollbackResponse.cs         # 回滚响应
├── DirectionChangeDetector.cs      # LLM意图检测器
├── IDirectionChangeDetector.cs     # 检测器接口
├── PivotOrchestrator.cs            # DAG操作编排器
├── IPivotOrchestrator.cs           # 编排器接口
├── PivotQueue.cs                   # 请求队列
├── AgentPivotCoordinator.cs        # 代理协调器
├── PivotEventPublisher.cs          # 事件发布器
├── PivotSnapshotManager.cs         # 快照管理器
├── PivotFeedbackEmitter.cs         # 用户反馈发射器
├── PivotMetrics.cs                 # 监控指标
├── PivotAwareAgentBase.cs          # 感知转向的代理基类
├── PivotOptions.cs                 # 配置选项
├── PivotStatus.cs                  # 状态枚举
├── PivotMessages.cs                # 本地化消息
└── README.md                       # 本文档
```

## 依赖注入配置

在 `Program.cs` 中注册服务：

```csharp
services.Configure<PivotOptions>(configuration.GetSection(PivotOptions.SectionName));

services.AddSingleton<IDirectionChangeDetector, DirectionChangeDetector>();
services.AddSingleton<IPivotOrchestrator, PivotOrchestrator>();
services.AddSingleton<IPivotQueue, PivotQueue>();
services.AddSingleton<IPivotSnapshotManager, PivotSnapshotManager>();
services.AddSingleton<IPivotEventPublisher, PivotEventPublisher>();
services.AddSingleton<IAgentPivotCoordinator, AgentPivotCoordinator>();
services.AddSingleton<PivotFeedbackEmitter>();
services.AddSingleton<PivotMetrics>();
```

## 设计原则

1. **非阻塞检测** - 意图检测失败不影响正常会话流程
2. **软删除** - 节点状态变更而非物理删除，支持回滚
3. **明确保留** - 用户可显式指定要保留的研究方面
4. **串行保护** - 同一会话内的转向操作严格串行，防止竞态
5. **优雅降级** - 子代理通知超时不阻塞主流程
6. **可观测性** - 完整的指标和日志支持

## 版本历史

- **v1.0** - 初始实现
  - LLM驱动的意图检测
  - DAG节点分类与软删除
  - 子代理协调
  - 30分钟回滚窗口
  - 队列化串行执行
  - 完整的监控指标
