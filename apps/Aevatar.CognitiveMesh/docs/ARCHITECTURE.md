# 架构参考: Cognitive Mesh

> **技术基础**: Aevatar Agent Framework (Orleans + GAgents)

## 1. 系统概览

Cognitive Mesh 建立在 **Aevatar GAgent** (通用 Agent) 模型之上。它将 "思维" 视为 **状态 (State)**，将 "推理" 视为 **状态转换 (State Transitions)**。

```mermaid
graph TD
    User[用户 / 架构师] -->|自然语言| Compiler[认知编译器]
    Compiler -->|网格蓝图 (JSON)| Orchestrator[网格编排器]
    
    subgraph "网格 (Orleans 集群)"
        Orchestrator -->|生成| RootNode[根目标 Agent]
        RootNode -->|分解| BranchA[策略 Agent A]
        RootNode -->|分解| BranchB[策略 Agent B]
        
        BranchA -->|生成| Worker1[工人: 搜索]
        BranchA -->|生成| Worker2[工人: 起草]
        
        BranchB -->|生成| Critic[工人: 批评]
        
        Worker1 -->|事件| EventStore[(事件存储)]
        Worker2 -->|事件| EventStore
        
        Critic -.->|反馈| BranchA
    end
```

## 2. 核心组件

### 2.1 认知编译器 (规划者)
*   **角色**: 将用户意图转化为 `MeshDefinition`。
*   **模型**: 高智商 LLM (如 GPT-4o/Claude 3.5)。
*   **输出**:
    ```json
    {
      "goal": "解决 P vs NP 问题",
      "strategy": "UoT_Exploratory",
      "budget": 1000,
      "nodes": [
        { "id": "explorer", "type": "DivergentAgent", "n_branches": 5 },
        { "id": "judge", "type": "ConvergentAgent", "criteria": "mathematical_proof" }
      ],
      "edges": [
        { "from": "explorer", "to": "judge", "type": "submit_hypothesis" }
      ]
    }
    ```

### 2.2 网格 Agent (神经元)
所有 Agent 均继承自 `Aevatar.Agents.Core.GAgentBase`。

#### A. `StrategyAgent` (经理)
*   **职责**: 管理特定思考策略的生命周期 (如 ToT, UoT)。
*   **行为**: 
    *   维护子问题的 "全局视图"。
    *   实现特定算法 (如思维树的 BFS/DFS)。
    *   决定何时修剪分支。

#### B. `WorkerAgent` (执行者)
*   **职责**: 原子执行 (LLM 调用, 工具使用)。
*   **行为**: 
    *   无状态执行逻辑，有状态结果存储。
    *   针对 API 错误实现 "退避重试"。

#### C. `CriticAgent` (验证者)
*   **职责**: "零错误" 守门人。
*   **行为**: 
    *   将输出与约束进行比较。
    *   返回 `Approved` (批准) 或 `Rejected(Reason)` (拒绝+理由)。

### 2.3 状态管理 (记忆)
*   **事件溯源**: 每个想法都是一个事件 (`ThoughtGenerated`, `ThoughtCritiqued`, `ThoughtPruned`)。
*   **Orleans State**: 为思维流的当前 "头部" 提供即时一致性。
*   **向量存储**: 为 "组合式" UoT 策略提供长期检索。

## 3. 研究概念的实现

### 3.1 UoT: 组合式 (Combinational)
*   **机制**: Agent 查询向量数据库 (以前想法的嵌入 + 外部知识)。
*   **代码路径**: `Aevatar.Agents.AI.Tool` 扩展，允许 Agent 查询 *其他* Agent 的 `EventStore`。

### 3.2 UoT: 探索式 (Exploratory)
*   **机制**: `StrategyAgent` 生成 $N$ 个并行的 `WorkerAgent`。
*   **Orleans 特性**: 动态 Grain 放置确保这些 Agent 在集群中并行运行。

### 3.3 UoT: 变革式 (Transformative)
*   **机制**: 一个专门的 `MetaAgent` 观察 `StrategyAgent` 的失败率。如果进展停滞，它会暂停树，重写工人的 `SystemPrompt`（改变规则），并重新启动一个分支。

### 3.4 "零错误" 循环
*   **模式**: `生成 -> 验证 -> 提交`。
*   **持久化**: `提交` 阶段写入持久化状态。如果进程在 `提交` 之前死亡，它会从上一个检查点恢复。

## 4. 数据结构

### 思维单元 (Thought Unit)
```csharp
public record ThoughtUnit(
    Guid Id,
    Guid ParentId,
    string Content,
    double ConfidenceScore,
    List<string> Tags,
    ThoughtStatus Status // Pending, Validated, Rejected, Pruned
);
```

### 网格定义 (Mesh Definition)
```csharp
public class MeshDefinition {
    public string Name { get; set; }
    public List<NodeDefinition> Nodes { get; set; }
    public List<EdgeDefinition> Edges { get; set; }
    public Dictionary<string, object> GlobalContext { get; set; }
}
```

## 5. 安全与限制
*   **沙盒**: 代码执行（如果有）必须在隔离容器 (Docker/WASM) 中运行。
*   **循环检测**: `StrategyAgent` 跟踪递归深度。最大深度硬限制可防止无限成本。

## 6. 托管与运行布局
- **CognitiveMesh.App**：基于 `Microsoft.Extensions.Hosting` 的后台服务，装配 `CognitiveDslCompiler`、Mesh Orchestrator、Orleans 客户端 / Silo。它是把 DSL 蓝图变成具体 Aevatar Agents + Orleans Grain 的执行层。
- **CognitiveMesh.AppHost**：基于 .NET Aspire 的部署编排项目。通过 `DistributedApplicationBuilder` 将 CognitiveMesh.App、Orleans Silo、存储、监控等资源声明式组合，可在本地或云端一键启动。
- **运行时契约**：
  1. AppHost 负责注入环境变量（如 `COGNITIVE_MESH_RUNTIME=orleans`、策略集合）以及参数化的 DSL Schema 版本。
  2. App 监听来自 DSL Registry 的蓝图事件，生成对应的 Actor 拓扑，并通过 Orleans 的 Event Sourcing 与外部观测系统对齐。
  3. 两者之间通过 Aspire 的资源建模共享配置、密钥与可观测性数据，保证交付的 Agent 运行在受控的 Actor Runtime 上。

