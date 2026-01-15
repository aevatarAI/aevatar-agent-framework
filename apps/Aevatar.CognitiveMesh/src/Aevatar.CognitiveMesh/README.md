# Cognitive Mesh

> **认知网格** - DSL 驱动的认知架构构建器
> 
> **终极愿景**：一个 Chat Agent + DSL = 任意认知策略

---

## 🎯 核心愿景

```
传统方式                          Cognitive Mesh 愿景
─────────────────────────────────────────────────────────────────
Prompt Engineering               →  Cognitive Architecture DSL
   (手工调试)                          (声明式认知编排)

每种策略 = 一个硬编码 Agent      →  每种策略 = 一个 YAML 文件
   (MAKER Agent, UoT Agent...)         (可热加载、可修改、可共享)

学习曲线陡峭                     →  低代码/无代码定义认知流程
   (C# + 框架 + 设计模式)              (只需理解 DSL 语法)
```

---

## 🏗️ 架构演进路线

```
┌─────────────────────────────────────────────────────────────────┐
│                     终极架构 (Phase 4)                          │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │              CognitiveGAgentBase                           │ │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐        │ │
│  │  │ DSL Engine  │  │ Primitives  │  │ State Mgmt  │        │ │
│  │  │ (YAML解析)  │  │ (原语库)    │  │ (状态存储)  │        │ │
│  │  └─────────────┘  └─────────────┘  └─────────────┘        │ │
│  └───────────────────────────────────────────────────────────┘ │
│                            ↑                                    │
│         ┌──────────────────┼──────────────────┐                │
│         │                  │                  │                │
│    workflows/          workflows/         workflows/           │
│    direct.yaml         maker.yaml         uot.yaml             │
│    (1 LLM call)        (DSL 定义)         (DSL 定义)           │
└─────────────────────────────────────────────────────────────────┘

当前状态 (Phase 2.5)
┌─────────────────────────────────────────────────────────────────┐
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐            │
│  │ MakerStrategy│ │ UoTStrategy  │ │ DirectStrategy│            │
│  │ (C# 硬编码)  │ │ (C# 硬编码)  │ │ (C# 硬编码)  │            │
│  └──────────────┘ └──────────────┘ └──────────────┘            │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🧠 当前可用策略

| 策略 | Kind | 状态 | 描述 |
|------|------|------|------|
| **Direct** | `Direct` | ✅ 可用 | 单次 LLM 调用，最简策略 |
| **MAKER** | `Maker` | ✅ 可用 | 分解-共识-合成，多 Agent 投票 |
| **C-UoT** | `UotCombinational` | ✅ 可用 | 类比检索 + 思维合成 |
| **E-UoT** | `UotExploratory` | ✅ 可用 | 探索域外思想 |
| **T-UoT** | `UotTransformative` | ✅ 可用 | 挑战隐藏假设 |

### 策略复杂度光谱

```
简单 ←────────────────────────────────────────────────→ 复杂

Direct    CoT/ToT/GoT    MAKER           UoT (C/E/T)
 │           │             │                  │
 1 call    线性/树形     递归分解           类比+合成
           推理链       并发投票           规则变异
                        共识机制
```

---

## 🎯 DSL 终极目标

### 目标：用 YAML 定义 MAKER 策略

```yaml
# workflows/maker.yaml
name: MAKER
version: 1.0
description: 分解-共识-合成的可靠推理策略

inputs:
  - task: string
  - reliability: enum[low, medium, high]

steps:
  # ─────────────────────────────────────────────
  #  Step 1: 分解或直接求解
  # ─────────────────────────────────────────────
  - id: decompose_or_solve
    type: conditional
    condition: "{{is_atomic(task)}}"
    
    if_true:
      # 原子任务：直接求解 + 投票共识
      - id: solve_atomic
        type: vote_until_consensus
        k: "{{consensus_k(reliability)}}"
        max_rounds: 10
        generator:
          type: llm_call
          prompt: |
            Solve this atomic task:
            {{task}}
          output: text
        store: solution
    
    if_false:
      # 复杂任务：分解 + 递归
      - id: decompose
        type: vote_until_consensus
        k: "{{consensus_k(reliability)}}"
        generator:
          type: llm_call
          prompt: |
            Decompose this task into subtasks:
            {{task}}
          output: json_array<Subtask>
        store: subtasks
      
      - id: solve_subtasks
        type: fan_out
        for_each: subtasks
        step:
          type: recursive_call
          workflow: maker  # 递归调用自己
          params:
            task: "{{item}}"
            reliability: "{{reliability}}"
        reduce: collect
        store: subtask_results
      
      - id: compose
        type: llm_call
        prompt: |
          Compose the final answer from subtask results:
          {{subtask_results | json}}
        output: text
        store: solution

output:
  result: "{{solution}}"
  trace: "{{execution_trace}}"
```

### 目标：用 YAML 定义 UoT 策略

```yaml
# workflows/uot-combinational.yaml
name: UoT Combinational
version: 1.0
description: 跨域类比 + 思维合成的创意推理

inputs:
  - problem: string
  - domain_hint: string?
  - max_analogies: int = 5

steps:
  # Step 1: 类比检索
  - id: retrieve_analogies
    type: llm_call
    prompt: |
      Find {{max_analogies}} analogous problems from different domains:
      Problem: {{problem}}
      Domain hint: {{domain_hint}}
    output: json_array<Analogy>
    store: analogies

  # Step 2: 思维分解 (并发)
  - id: decompose_thoughts
    type: fan_out
    for_each: analogies
    step:
      type: llm_call
      prompt: |
        Decompose this solution into atomic thoughts:
        {{item.solution}}
      output: json_array<Thought>
    reduce: flatten
    store: thoughts

  # Step 3: 宿主选择
  - id: select_host
    type: llm_call
    prompt: |
      Select the best host structure from analogies:
      {{analogies | json}}
    output: json<Host>
    store: host

  # Step 4: 供体选择
  - id: select_donors
    type: llm_call
    prompt: |
      Select far-then-analogical donors for host {{host}}
      From thoughts: {{thoughts | json}}
    output: json_array<Donor>
    store: donors

  # Step 5: 合成候选 (并发)
  - id: synthesize
    type: fan_out
    for_each: donors
    step:
      type: llm_call
      prompt: |
        Synthesize new solution:
        Host: {{host}}
        Donor: {{item}}
      output: json<Candidate>
    store: candidates

  # Step 6: 评估排序
  - id: evaluate
    type: llm_call
    prompt: |
      Evaluate candidates by Feasibility, Utility, Novelty:
      {{candidates | json}}
    output: json_array<ScoredCandidate>
    store: final_candidates

output:
  best: "{{final_candidates[0]}}"
  all: "{{final_candidates}}"
```

---

## 🔧 CognitiveGAgentBase 核心能力

为实现 DSL 驱动的策略，需要 CognitiveGAgentBase 提供以下原语：

### 1. 流程执行引擎
```csharp
// 执行 DSL 定义的工作流
await ExecuteWorkflowAsync(workflow, context, ct);
```

### 2. 并发原语
```csharp
// Fan-out: 并行执行多个任务
var results = await FanOutAsync(tasks, maxConcurrency: 5);

// Fan-in: 汇聚结果
var merged = FanIn(results, reducer: Flatten);
```

### 3. 投票/共识原语
```csharp
// 运行投票直到 K 票共识
var winner = await VoteUntilConsensusAsync(
    proposalGenerator: () => GenerateProposal(),
    k: 2,
    maxRounds: 10
);
```

### 4. 模板化 LLM 调用
```csharp
// 用模板生成响应
var result = await GenerateWithTemplateAsync<T>(
    promptTemplate: "Analyze {{input}}",
    variables: new { input = data },
    parser: JsonOutputParser<T>()
);
```

### 5. 状态存储
```csharp
// 存储中间结果
Store("analogies", analogyList);

// 获取中间结果
var analogies = Get<List<Analogy>>("analogies");
```

### 6. 递归调用
```csharp
// 递归调用工作流
await RecursiveCallAsync("maker", childParams);
```

---

## 📊 DSL vs 硬编码对比

| 维度 | 硬编码 (当前) | DSL (目标) |
|------|--------------|-----------|
| **修改成本** | 改 C# → 编译 → 部署 | 改 YAML → 热重载 |
| **学习曲线** | C# + Actor + 设计模式 | YAML 语法 |
| **可共享性** | 分享整个项目 | 分享单个 YAML 文件 |
| **版本管理** | Git diff 看代码 | Git diff 看配置 |
| **实验迭代** | 慢（编译周期） | 快（即时生效） |
| **调试** | 断点 + 日志 | 执行追踪 + 可视化 |
| **复杂逻辑** | 无限制 | 受限于原语库 |

### DSL 的局限性

```
DSL 能优雅表达的：
✅ Prompt 模板
✅ 线性流程 (step1 → step2 → step3)
✅ 简单并发 (fan-out → reduce)
✅ 条件分支 (if/else)
✅ 循环 (for_each, while)

需要内置原语支持的：
⚠️ 投票共识 (复杂算法)
⚠️ 递归分解 (动态深度)
⚠️ Streaming Race (实时竞争)
⚠️ 语义聚类 (embedding)
```

---

## 🗺️ 演进路线 (Roadmap)

| 阶段 | 目标 | 状态 |
|------|------|------|
| **Phase 1** | 策略抽象层 (IReasoningStrategy) | ✅ 完成 |
| **Phase 2** | 统一执行引擎 (CognitiveMeshService) | ✅ 完成 |
| **Phase 2.5** | UoT 三重奏 (C/E/T-UoT) | ✅ 完成 |
| **Phase 3** | 内容加载 + 任务模板 | ✅ 完成 |
| **Phase 3.5** | **CognitiveGAgentBase** | 📋 设计中 |
| **Phase 4** | **DSL 引擎 + 工作流热加载** | 📋 计划中 |
| **Phase 5** | 可视化增强 | 📋 计划中 |
| **Phase 6** | 高级功能 (断点续跑、多策略编排) | 📋 计划中 |

### Phase 3.5: CognitiveGAgentBase (下一步)

核心目标：创建一个功能完备的认知 Agent 基类，提供 DSL 执行所需的全部原语。

```csharp
public abstract class CognitiveGAgentBase<TState> : AIGAgentBase<TState>
{
    // 原语 1: 工作流执行
    protected Task<WorkflowResult> ExecuteWorkflowAsync(...);
    
    // 原语 2: 并发控制
    protected Task<T[]> FanOutAsync<T>(...);
    protected T FanIn<T>(...);
    
    // 原语 3: 投票共识
    protected Task<VoteResult> VoteUntilConsensusAsync(...);
    
    // 原语 4: 模板化 LLM
    protected Task<T> GenerateWithTemplateAsync<T>(...);
    
    // 原语 5: 状态存储
    protected void Store(string key, object value);
    protected T Get<T>(string key);
    
    // 原语 6: 递归调用
    protected Task RecursiveCallAsync(...);
}
```

### Phase 4: DSL 引擎

- YAML 解析器
- 步骤类型注册
- 输出解析器库
- 热重载支持
- 执行追踪

---

## 🚀 快速开始

### 1. 配置 API Key

```bash
# 创建 appsettings.secrets.json
cat > appsettings.secrets.json << 'EOF'
{
  "LLMProviders": {
    "Providers": {
      "deepseek": { "ApiKey": "sk-your-key" }
    }
  }
}
EOF
```

### 2. 运行

```bash
cd apps/Aevatar.CognitiveMesh/src/Aevatar.CognitiveMesh
dotnet run
```

### 3. 打开浏览器

访问 `http://localhost:5678`（如端口冲突可自行改）

---

## 📁 项目结构

```
apps/Aevatar.CognitiveMesh/
├── src/Aevatar.CognitiveMesh/        # 主服务实现
├── CognitiveMesh.AppHost/            # Aspire AppHost
└── docs/                             # 设计文档

src/
├── Aevatar.Agents.Cognitive.Core/
├── Aevatar.Agents.Cognitive.Content/
├── Aevatar.Agents.Cognitive.Streaming/
└── Aevatar.CognitiveMesh.Dsl/
```

---

## 🔗 相关资源

- [MAKER Paper](https://arxiv.org/abs/2411.00332) - 分解-共识-合成理论
- [Universe of Thoughts](https://arxiv.org/html/2511.20471v2) - 创意推理框架
- [详细路线图](../docs/ROADMAP.md) - 完整演进计划

---

*Last Updated: 2025-12-04*
