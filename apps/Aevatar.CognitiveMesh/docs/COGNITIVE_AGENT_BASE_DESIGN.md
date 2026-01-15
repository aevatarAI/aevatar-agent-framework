# CognitiveGAgentBase 设计文档

> **目标**：创建 DSL 驱动的认知 Agent 基类，让任意认知策略可以用 YAML 定义
> 
> **核心理念**：一个 CognitiveGAgentBase + DSL = 任意认知策略

---

## 1. 设计背景

### 1.1 现状分析

当前 Cognitive Mesh 的策略实现方式：

```
每种策略 = 一个 C# 类 (硬编码)

MakerStrategy.cs     → 500+ 行 C# 代码
UoTStrategy.cs       → 300+ 行 C# 代码
EUoTStrategy.cs      → 250+ 行 C# 代码
TUoTStrategy.cs      → 280+ 行 C# 代码
```

**问题**：
- 修改策略需要：改代码 → 编译 → 部署
- 学习曲线陡峭：需要理解 C#、Actor 模型、设计模式
- 复用困难：投票、并发、模板等逻辑散落在各处
- 实验迭代慢：每次调整都需要编译周期

### 1.2 目标状态

```
每种策略 = 一个 YAML 文件 (DSL 定义)

workflows/maker.yaml     → 50 行 YAML
workflows/uot.yaml       → 80 行 YAML
workflows/custom.yaml    → 用户自定义
```

**优势**：
- 修改策略：改 YAML → 热重载 → 即时生效
- 学习曲线平缓：只需理解 YAML 语法
- 高度复用：原语封装所有复杂逻辑
- 快速迭代：无编译，即改即用

---

## 2. 架构设计

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────────┐
│                        用户层                                    │
│                                                                  │
│    workflows/maker.yaml    workflows/uot.yaml    custom.yaml    │
│              │                    │                   │         │
└──────────────┼────────────────────┼───────────────────┼─────────┘
               │                    │                   │
               ▼                    ▼                   ▼
┌─────────────────────────────────────────────────────────────────┐
│                      DSL Engine 层                               │
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │ YAML Parser  │  │  Validator   │  │  Scheduler   │          │
│  │ (解析)       │  │  (验证)      │  │  (调度)      │          │
│  └──────────────┘  └──────────────┘  └──────────────┘          │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────────────────────────────┐
│                  CognitiveGAgentBase 原语层                      │
│                                                                  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ LLM Call │ │ Fan-Out  │ │  Vote    │ │ Recurse  │           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐           │
│  │ Template │ │  Store   │ │Checkpoint│ │ Condition│           │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘           │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────────────────────────────┐
│                     AIGAgentBase 基础层                          │
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │ LLM Provider │  │  Embedding   │  │ Conversation │          │
│  │   Factory    │  │  Generator   │  │   History    │          │
│  └──────────────┘  └──────────────┘  └──────────────┘          │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 核心组件

| 组件 | 职责 | 位置 |
|------|------|------|
| `CognitiveGAgentBase` | 提供认知原语 | `Aevatar.Agents.Cognitive` |
| `WorkflowEngine` | DSL 解析与执行 | `Aevatar.Agents.Cognitive` |
| `WorkflowRegistry` | 工作流注册与热重载 | `Aevatar.CognitiveMesh` |
| `RunStateManager` | 运行状态持久化 | `Aevatar.CognitiveMesh` |

---

## 3. CognitiveGAgentBase 原语设计

### 3.1 原语清单

| 原语 | 类型 | 说明 | DSL 语法 |
|------|------|------|---------|
| `llm_call` | 基础 | LLM 调用 | `type: llm_call` |
| `fan_out` | 并发 | 并行执行 | `type: fan_out` |
| `fan_in` | 并发 | 汇聚结果 | `type: fan_in` |
| `vote` | 共识 | 投票共识 | `type: vote` |
| `conditional` | 控制流 | 条件分支 | `type: conditional` |
| `loop` | 控制流 | 循环 | `type: loop` |
| `workflow_call` | 递归 | 调用工作流 | `type: workflow_call` |
| `checkpoint` | 持久化 | 创建检查点 | `type: checkpoint` |
| `parallel` | 并发 | 并行步骤组 | `type: parallel` |

### 3.2 原语接口定义

```csharp
namespace Aevatar.Agents.Cognitive.Primitives;

/// <summary>
/// 原语执行上下文
/// </summary>
public class PrimitiveContext
{
    public Dictionary<string, object> Variables { get; } = new();
    public CancellationToken CancellationToken { get; init; }
    public IProgress<WorkflowProgress>? Progress { get; init; }
    public int CurrentDepth { get; init; }
    public int MaxDepth { get; init; } = 10;
    public string RunId { get; init; } = string.Empty;
}

/// <summary>
/// 原语执行结果
/// </summary>
public record PrimitiveResult
{
    public bool Success { get; init; }
    public object? Value { get; init; }
    public string? Error { get; init; }
    public int TokensUsed { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// 原语接口
/// </summary>
public interface IPrimitive
{
    string Type { get; }
    Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object> parameters);
}
```

### 3.3 各原语详细设计

#### 3.3.1 LLM Call 原语

```csharp
/// <summary>
/// LLM 调用原语
/// </summary>
public class LlmCallPrimitive : IPrimitive
{
    public string Type => "llm_call";
    
    private readonly IAevatarLLMProvider _llmProvider;
    private readonly TemplateEngine _templateEngine;
    private readonly OutputParserFactory _parserFactory;
    
    public async Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object> parameters)
    {
        // 1. 渲染 prompt 模板
        var promptTemplate = parameters.GetRequired<string>("prompt");
        var prompt = _templateEngine.Render(promptTemplate, context.Variables);
        
        // 2. 获取系统提示（可选）
        var systemPrompt = parameters.GetOptional<string>("system");
        
        // 3. 调用 LLM
        var response = await _llmProvider.GenerateAsync(
            prompt, 
            systemPrompt,
            context.CancellationToken);
        
        // 4. 解析输出
        var outputType = parameters.GetOptional<string>("output") ?? "text";
        var parser = _parserFactory.Create(outputType);
        var parsed = parser.Parse(response.Content);
        
        return new PrimitiveResult
        {
            Success = true,
            Value = parsed,
            TokensUsed = response.PromptTokens + response.CompletionTokens,
            Duration = response.Duration
        };
    }
}
```

**DSL 语法**：

```yaml
- id: analyze
  type: llm_call
  prompt: |
    Analyze the following text:
    {{content}}
  system: "You are an expert analyst."
  output: json<AnalysisResult>
  store: analysis
```

#### 3.3.2 Vote 原语

```csharp
/// <summary>
/// 投票共识原语
/// </summary>
public class VotePrimitive : IPrimitive
{
    public string Type => "vote";
    
    private readonly VoteEngineFactory _engineFactory;
    
    public async Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object> parameters)
    {
        // 1. 获取配置
        var k = parameters.GetOptional<int>("k") ?? 2;
        var maxRounds = parameters.GetOptional<int>("max_rounds") ?? 10;
        var similarity = parameters.GetOptional<float>("similarity") ?? 0.85f;
        var generator = parameters.GetRequired<StepDefinition>("generator");
        
        // 2. 创建投票引擎
        var engine = _engineFactory.Create(k, maxRounds, similarity);
        
        // 3. 执行投票循环
        while (!engine.HasConsensus && engine.RoundNumber < maxRounds)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            
            // 执行 generator 生成提案
            var proposalResult = await ExecuteStepAsync(generator, context);
            
            // 提交投票
            engine.SubmitVote(proposalResult.Value?.ToString() ?? "");
            
            // 报告进度
            context.Progress?.Report(new WorkflowProgress
            {
                Phase = "Voting",
                Message = $"Round {engine.RoundNumber}: {engine.VoteDistribution}"
            });
        }
        
        // 4. 返回共识结果
        var result = engine.GetResult();
        return new PrimitiveResult
        {
            Success = result.HasConsensus,
            Value = result.Winner,
            Error = result.HasConsensus ? null : "No consensus reached"
        };
    }
}
```

**DSL 语法**：

```yaml
- id: solve_with_consensus
  type: vote
  k: 2                    # K 票共识
  max_rounds: 10          # 最大轮次
  similarity: 0.85        # 语义相似度阈值
  generator:              # 每轮生成器
    type: llm_call
    prompt: "Solve: {{task}}"
    output: text
  store: solution
```

#### 3.3.3 Fan-Out 原语

```csharp
/// <summary>
/// 并行执行原语
/// </summary>
public class FanOutPrimitive : IPrimitive
{
    public string Type => "fan_out";
    
    public async Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object> parameters)
    {
        // 1. 获取迭代列表
        var forEachVar = parameters.GetRequired<string>("for_each");
        var items = context.Variables.GetRequired<IEnumerable<object>>(forEachVar);
        
        // 2. 获取并发配置
        var maxConcurrency = parameters.GetOptional<int>("max_concurrency") ?? 5;
        var step = parameters.GetRequired<StepDefinition>("step");
        var reducer = parameters.GetOptional<string>("reduce") ?? "collect";
        
        // 3. 并行执行
        var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = items.Select(async item =>
        {
            await semaphore.WaitAsync(context.CancellationToken);
            try
            {
                // 创建子上下文，注入 item
                var childContext = context.Clone();
                childContext.Variables["item"] = item;
                
                return await ExecuteStepAsync(step, childContext);
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        var results = await Task.WhenAll(tasks);
        
        // 4. 汇聚结果
        var reduced = reducer switch
        {
            "collect" => results.Select(r => r.Value).ToList(),
            "flatten" => results.SelectMany(r => (IEnumerable<object>)r.Value!).ToList(),
            "first" => results.First().Value,
            "last" => results.Last().Value,
            _ => throw new NotSupportedException($"Unknown reducer: {reducer}")
        };
        
        return new PrimitiveResult
        {
            Success = true,
            Value = reduced,
            TokensUsed = results.Sum(r => r.TokensUsed)
        };
    }
}
```

**DSL 语法**：

```yaml
- id: process_analogies
  type: fan_out
  for_each: analogies
  max_concurrency: 5
  step:
    type: llm_call
    prompt: "Extract thoughts from: {{item.solution}}"
    output: json_array<Thought>
  reduce: flatten          # collect | flatten | first | last
  store: all_thoughts
```

#### 3.3.4 Workflow Call 原语 (递归)

```csharp
/// <summary>
/// 工作流调用原语（支持递归）
/// </summary>
public class WorkflowCallPrimitive : IPrimitive
{
    public string Type => "workflow_call";
    
    private readonly WorkflowRegistry _registry;
    
    public async Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object> parameters)
    {
        // 1. 检查递归深度
        var maxDepth = parameters.GetOptional<int>("max_depth") ?? context.MaxDepth;
        if (context.CurrentDepth >= maxDepth)
        {
            return new PrimitiveResult
            {
                Success = false,
                Error = $"Max recursion depth {maxDepth} exceeded"
            };
        }
        
        // 2. 获取目标工作流
        var workflowName = parameters.GetRequired<string>("workflow");
        var workflow = _registry.Get(workflowName);
        
        // 3. 构建子上下文
        var childParams = parameters.GetOptional<Dictionary<string, object>>("params") 
            ?? new Dictionary<string, object>();
        
        var childContext = new PrimitiveContext
        {
            CancellationToken = context.CancellationToken,
            Progress = context.Progress,
            CurrentDepth = context.CurrentDepth + 1,
            MaxDepth = maxDepth,
            RunId = context.RunId
        };
        
        // 注入参数
        foreach (var (key, value) in childParams)
        {
            childContext.Variables[key] = _templateEngine.Resolve(value, context.Variables);
        }
        
        // 4. 执行子工作流
        var engine = new WorkflowEngine();
        return await engine.ExecuteAsync(workflow, childContext);
    }
}
```

**DSL 语法**：

```yaml
- id: solve_subtasks
  type: fan_out
  for_each: subtasks
  step:
    type: workflow_call
    workflow: maker              # 递归调用 maker
    params:
      task: "{{item}}"
      reliability: "{{reliability}}"
    max_depth: 10
  reduce: collect
  store: subtask_results
```

#### 3.3.5 Conditional 原语

```csharp
/// <summary>
/// 条件分支原语
/// </summary>
public class ConditionalPrimitive : IPrimitive
{
    public string Type => "conditional";
    
    private readonly ExpressionEvaluator _evaluator;
    
    public async Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object> parameters)
    {
        // 1. 评估条件
        var condition = parameters.GetRequired<string>("condition");
        var result = _evaluator.Evaluate(condition, context.Variables);
        
        // 2. 选择分支
        var branch = result 
            ? parameters.GetRequired<List<StepDefinition>>("if_true")
            : parameters.GetOptional<List<StepDefinition>>("if_false");
        
        if (branch == null || branch.Count == 0)
        {
            return new PrimitiveResult { Success = true, Value = null };
        }
        
        // 3. 执行分支
        PrimitiveResult? lastResult = null;
        foreach (var step in branch)
        {
            lastResult = await ExecuteStepAsync(step, context);
            if (!lastResult.Success) break;
        }
        
        return lastResult ?? new PrimitiveResult { Success = true };
    }
}
```

**DSL 语法**：

```yaml
- id: decompose_or_solve
  type: conditional
  condition: "{{is_atomic(task)}}"
  
  if_true:
    - type: vote
      generator: { type: llm_call, prompt: "Solve: {{task}}" }
      store: solution
  
  if_false:
    - type: vote
      generator: { type: llm_call, prompt: "Decompose: {{task}}" }
      store: subtasks
    - type: fan_out
      for_each: subtasks
      step: { type: workflow_call, workflow: maker }
      store: subtask_results
```

#### 3.3.6 Checkpoint 原语

```csharp
/// <summary>
/// 检查点原语
/// </summary>
public class CheckpointPrimitive : IPrimitive
{
    public string Type => "checkpoint";
    
    private readonly IRunStateManager _stateManager;
    
    public async Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object> parameters)
    {
        var stepId = parameters.GetRequired<string>("step_id");
        var variableNames = parameters.GetOptional<List<string>>("variables");
        
        // 选择要持久化的变量
        var variables = variableNames != null
            ? context.Variables
                .Where(kv => variableNames.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value)
            : new Dictionary<string, object>(context.Variables);
        
        // 创建检查点
        var checkpoint = new Checkpoint
        {
            StepId = stepId,
            Timestamp = DateTime.UtcNow,
            Variables = variables
        };
        
        await _stateManager.SaveCheckpointAsync(context.RunId, checkpoint);
        
        return new PrimitiveResult { Success = true };
    }
}
```

**DSL 语法**：

```yaml
- id: save_progress
  type: checkpoint
  step_id: after_decompose
  variables:              # 可选：只保存指定变量
    - subtasks
    - current_depth
```

---

## 4. DSL 语法设计

### 4.1 工作流结构

```yaml
# 工作流元信息
name: string              # 必需：工作流名称
version: string           # 可选：版本号
description: string       # 可选：描述

# 输入参数定义
inputs:
  - name: string          # 参数名
    type: string          # 类型 (string, int, float, bool, array, object)
    required: bool        # 是否必需
    default: any          # 默认值

# 步骤列表
steps:
  - id: string            # 步骤 ID
    type: string          # 步骤类型 (原语类型)
    ...                   # 类型特定字段
    store: string         # 存储到变量名

# 输出定义
output:
  field: "{{expression}}" # 输出字段
```

### 4.2 模板语法

基于 Mustache/Handlebars 风格：

```yaml
# 变量引用
prompt: "Analyze: {{content}}"

# 嵌套属性
prompt: "Domain: {{item.domain}}"

# 管道过滤器
prompt: "Data: {{items | json}}"
prompt: "Summary: {{text | truncate:100}}"

# 条件渲染
prompt: |
  {{#if domain_hint}}
  Domain context: {{domain_hint}}
  {{/if}}

# 循环渲染
prompt: |
  Items:
  {{#each items}}
  - {{this.name}}: {{this.value}}
  {{/each}}
```

### 4.3 内置函数

```yaml
# 条件判断
condition: "{{is_atomic(task)}}"
condition: "{{length(items) > 5}}"
condition: "{{complexity > 0.7}}"

# 字符串操作
prompt: "{{task | uppercase}}"
prompt: "{{task | lowercase}}"
prompt: "{{task | truncate:100}}"

# 数组操作
reduce: "{{items | flatten}}"
reduce: "{{items | unique}}"
reduce: "{{items | sort_by:score}}"

# 类型转换
prompt: "{{data | json}}"
prompt: "Count: {{count | string}}"
```

### 4.4 输出解析器

```yaml
# 纯文本
output: text

# JSON 对象
output: json<TypeName>

# JSON 数组
output: json_array<TypeName>

# 正则提取
output: regex("\\d+")

# 首行提取
output: first_line

# 代码块提取
output: code_block

# 多模式匹配
output:
  type: fallback
  parsers:
    - json<Result>
    - regex("Answer: (.*)")
    - text
```

---

## 5. 持久化设计

### 5.1 状态层次

```
┌─────────────────────────────────────────────────────────────────┐
│                     三层状态架构                                 │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Layer 1: Workflow Run State (运行状态)                         │
│  ─────────────────────────────────────────────────────────────  │
│  位置: runs/{run_id}/run.yaml                                   │
│  生命周期: 工作流运行期间                                        │
│  用途: 追踪执行进度，支持断点续传                                │
│                                                                  │
│  Layer 2: Agent State (Agent 状态)                              │
│  ─────────────────────────────────────────────────────────────  │
│  位置: Agent 内部 Protobuf State                                │
│  生命周期: Agent 生命周期                                        │
│  用途: 对话历史、业务状态、Token 消耗                            │
│                                                                  │
│  Layer 3: Step Results (步骤结果)                               │
│  ─────────────────────────────────────────────────────────────  │
│  位置: runs/{run_id}/steps/{step_id}.json                       │
│  生命周期: 永久（用于审计和分析）                                │
│  用途: 详细的执行记录，便于调试和优化                            │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 5.2 Run State 结构

```yaml
# runs/{run_id}/run.yaml

# 基本信息
id: "run-20251204-001"
workflow: "maker"
project_id: "paper-review"
created_at: "2025-12-04T10:00:00Z"
updated_at: "2025-12-04T10:15:00Z"

# 执行状态
status: "paused"              # running | paused | completed | failed | cancelled
current_step: "solve_subtask_2"
total_steps: 5
completed_steps: 2

# 输入参数
inputs:
  task: "Review this academic paper..."
  reliability: "high"

# 参与的 Agents
agents:
  - id: "a1b2c3d4-..."
    type: "CognitiveCoordinatorAgent"
    status: "active"
  - id: "e5f6g7h8-..."
    type: "CognitiveWorkerAgent"
    status: "idle"

# 检查点列表
checkpoints:
  - step_id: "decompose"
    timestamp: "2025-12-04T10:05:00Z"
    variables:
      subtasks: ["task1", "task2", "task3"]
      
  - step_id: "solve_subtask_1"
    timestamp: "2025-12-04T10:10:00Z"
    variables:
      subtask_results:
        task1: "Result for task1..."

# 资源消耗
metrics:
  total_llm_calls: 15
  prompt_tokens: 8000
  completion_tokens: 4345
  total_tokens: 12345
  duration_ms: 300000

# 错误信息（如果有）
error:
  step_id: "solve_subtask_2"
  message: "LLM rate limit exceeded"
  timestamp: "2025-12-04T10:15:00Z"
  retries: 3
```

### 5.3 Step Result 结构

```json
// runs/{run_id}/steps/decompose.json
{
  "step_id": "decompose",
  "type": "vote",
  "status": "completed",
  
  "started_at": "2025-12-04T10:02:00Z",
  "completed_at": "2025-12-04T10:05:00Z",
  "duration_ms": 180000,
  
  "input": {
    "task": "Review this academic paper...",
    "k": 2,
    "max_rounds": 10
  },
  
  "output": {
    "subtasks": [
      { "id": "task1", "description": "Check methodology" },
      { "id": "task2", "description": "Verify citations" },
      { "id": "task3", "description": "Improve clarity" }
    ]
  },
  
  "voting_trace": {
    "rounds": 3,
    "votes": [
      { "round": 1, "content": "[task1, task2, task3]", "hash": "abc123" },
      { "round": 2, "content": "[task1, task2, task3]", "hash": "abc123" },
      { "round": 3, "content": "[task1, task2]", "hash": "def456" }
    ],
    "winner": "[task1, task2, task3]",
    "consensus_at_round": 2
  },
  
  "metrics": {
    "llm_calls": 3,
    "prompt_tokens": 1500,
    "completion_tokens": 800,
    "total_tokens": 2300
  }
}
```

### 5.4 断点续传流程

```
┌─────────────────────────────────────────────────────────────────┐
│                    断点续传流程                                  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. 正常执行                                                    │
│  ───────────────────────────────────────────────────────────    │
│  Start → Step1 → [checkpoint] → Step2 → [checkpoint] → Step3    │
│                                              │                   │
│                                              ▼                   │
│                                          系统中断                │
│                                     (崩溃/超时/手动暂停)         │
│                                                                  │
│  2. 恢复执行                                                    │
│  ───────────────────────────────────────────────────────────    │
│  Resume(run_id)                                                  │
│      │                                                           │
│      ▼                                                           │
│  读取 run.yaml                                                   │
│      │                                                           │
│      ▼                                                           │
│  找到最后一个 checkpoint (Step2)                                │
│      │                                                           │
│      ▼                                                           │
│  恢复 Agent IDs，重新激活 Agents                                │
│      │                                                           │
│      ▼                                                           │
│  加载 checkpoint 的 variables                                   │
│      │                                                           │
│      ▼                                                           │
│  从 Step3 继续执行                                              │
│      │                                                           │
│      ▼                                                           │
│  Complete                                                        │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 5.5 RunStateManager 接口

```csharp
public interface IRunStateManager
{
    // 创建新运行
    Task<WorkflowRunState> CreateRunAsync(
        string workflowName,
        string projectId,
        Dictionary<string, object> inputs);
    
    // 更新状态
    Task UpdateStatusAsync(string runId, RunStatus status);
    Task UpdateCurrentStepAsync(string runId, string stepId);
    
    // 检查点管理
    Task SaveCheckpointAsync(string runId, Checkpoint checkpoint);
    Task<Checkpoint?> GetLastCheckpointAsync(string runId);
    
    // Agent 管理
    Task RegisterAgentAsync(string runId, Guid agentId, string agentType);
    Task UpdateAgentStatusAsync(string runId, Guid agentId, AgentStatus status);
    
    // 步骤结果
    Task SaveStepResultAsync(string runId, StepResult result);
    Task<StepResult?> GetStepResultAsync(string runId, string stepId);
    
    // 恢复
    Task<WorkflowRunState?> LoadRunAsync(string runId);
    Task<bool> CanResumeAsync(string runId);
}
```

---

## 6. 示例工作流

### 6.1 MAKER 策略 (DSL 版)

```yaml
# workflows/maker.yaml
name: maker
version: "1.0"
description: 分解-共识-合成的可靠推理策略

inputs:
  - name: task
    type: string
    required: true
  - name: reliability
    type: string
    default: "medium"

steps:
  # 判断是否原子任务
  - id: check_atomic
    type: llm_call
    prompt: |
      Is this task atomic (can be solved directly without decomposition)?
      Task: {{task}}
      
      Answer with ONLY "yes" or "no".
    output: text
    store: is_atomic_answer

  # 条件分支
  - id: process
    type: conditional
    condition: "{{is_atomic_answer == 'yes'}}"
    
    if_true:
      # 原子任务：直接求解
      - id: solve_atomic
        type: vote
        k: "{{consensus_k(reliability)}}"
        max_rounds: 10
        generator:
          type: llm_call
          prompt: |
            Solve this task:
            {{task}}
          output: text
        store: solution
    
    if_false:
      # 复杂任务：分解
      - id: decompose
        type: vote
        k: "{{consensus_k(reliability)}}"
        max_rounds: 10
        generator:
          type: llm_call
          prompt: |
            Decompose this task into 2-5 subtasks:
            {{task}}
            
            Output as JSON array: [{"id": "1", "description": "..."}]
          output: json_array<Subtask>
        store: subtasks
      
      # 检查点
      - id: checkpoint_after_decompose
        type: checkpoint
        variables: [subtasks]
      
      # 递归处理子任务
      - id: solve_subtasks
        type: fan_out
        for_each: subtasks
        max_concurrency: 3
        step:
          type: workflow_call
          workflow: maker
          params:
            task: "{{item.description}}"
            reliability: "{{reliability}}"
          max_depth: 5
        reduce: collect
        store: subtask_results
      
      # 合成最终结果
      - id: compose
        type: llm_call
        prompt: |
          Compose a final answer from these subtask results:
          
          Original task: {{task}}
          
          Subtask results:
          {{subtask_results | json}}
        output: text
        store: solution

output:
  result: "{{solution}}"
  trace:
    is_atomic: "{{is_atomic_answer}}"
    subtasks: "{{subtasks}}"
```

### 6.2 UoT Combinational 策略 (DSL 版)

```yaml
# workflows/uot-combinational.yaml
name: uot-combinational
version: "1.0"
description: 跨域类比 + 思维合成的创意推理

inputs:
  - name: problem
    type: string
    required: true
  - name: domain_hint
    type: string
    default: ""
  - name: max_analogies
    type: int
    default: 5

steps:
  # Step 1: 类比检索
  - id: retrieve_analogies
    type: llm_call
    prompt: |
      Find {{max_analogies}} analogous problems from different domains.
      
      Original Problem: {{problem}}
      {{#if domain_hint}}
      Domain hint: {{domain_hint}}
      {{/if}}
      
      Output as JSON array with fields: id, description, domain, solution
    output: json_array<Analogy>
    store: analogies

  - id: checkpoint_analogies
    type: checkpoint
    variables: [analogies]

  # Step 2: 思维分解 (并发)
  - id: decompose_thoughts
    type: fan_out
    for_each: analogies
    max_concurrency: 5
    step:
      type: llm_call
      prompt: |
        Decompose this solution into atomic thoughts/mechanisms:
        
        Domain: {{item.domain}}
        Solution: {{item.solution}}
        
        Output as JSON array with fields: id, content, mechanism
      output: json_array<Thought>
    reduce: flatten
    store: thoughts

  # Step 3: 宿主选择
  - id: select_host
    type: llm_call
    prompt: |
      Select the best host structure for creative transfer.
      
      Original problem: {{problem}}
      Available analogies:
      {{analogies | json}}
      
      Output the selected analogy ID and explain why.
    output: json<HostSelection>
    store: host

  # Step 4: 供体选择
  - id: select_donors
    type: llm_call
    prompt: |
      Select donor thoughts that are far-yet-analogical to the host.
      
      Host: {{host}}
      Available thoughts:
      {{thoughts | json}}
      
      Select 3-5 thoughts that could create novel combinations.
    output: json_array<Donor>
    store: donors

  # Step 5: 合成候选 (并发)
  - id: synthesize
    type: fan_out
    for_each: donors
    max_concurrency: 5
    step:
      type: llm_call
      prompt: |
        Synthesize a new solution by combining:
        
        Host structure: {{host}}
        Donor thought: {{item}}
        Original problem: {{problem}}
        
        Create a novel solution that leverages both.
      output: json<Candidate>
    reduce: collect
    store: candidates

  - id: checkpoint_candidates
    type: checkpoint
    variables: [candidates]

  # Step 6: 评估排序
  - id: evaluate
    type: llm_call
    prompt: |
      Evaluate and rank these candidate solutions.
      
      Original problem: {{problem}}
      Candidates:
      {{candidates | json}}
      
      Score each on: Feasibility (0-1), Utility (0-1), Novelty (0-1)
      Output sorted by total score descending.
    output: json_array<ScoredCandidate>
    store: final_candidates

output:
  best_solution: "{{final_candidates[0]}}"
  all_candidates: "{{final_candidates}}"
  analogies_used: "{{analogies}}"
  thoughts_generated: "{{thoughts | length}}"
```

### 6.3 Direct 策略 (DSL 版)

```yaml
# workflows/direct.yaml
name: direct
version: "1.0"
description: 单次 LLM 调用，最简策略

inputs:
  - name: task
    type: string
    required: true
  - name: system_prompt
    type: string
    default: "You are a helpful AI assistant."

steps:
  - id: respond
    type: llm_call
    system: "{{system_prompt}}"
    prompt: "{{task}}"
    output: text
    store: response

output:
  result: "{{response}}"
```

---

## 7. 实现计划

### 7.1 Phase 3.5: CognitiveGAgentBase (2 周)

| 周次 | 任务 | 交付物 |
|------|------|--------|
| Week 1 | 基础原语 | `LlmCallPrimitive`, `ConditionalPrimitive`, `FanOutPrimitive` |
| Week 1 | 模板引擎 | `TemplateEngine`, `OutputParserFactory` |
| Week 2 | 高级原语 | `VotePrimitive`, `WorkflowCallPrimitive`, `CheckpointPrimitive` |
| Week 2 | 基类实现 | `CognitiveGAgentBase<TState>` |

### 7.2 Phase 4: DSL Engine (3 周)

| 周次 | 任务 | 交付物 |
|------|------|--------|
| Week 1 | YAML 解析 | `WorkflowParser`, `WorkflowDefinition` |
| Week 1 | 语义验证 | `WorkflowValidator`, `ValidationRule[]` |
| Week 2 | 执行引擎 | `WorkflowEngine`, `StepExecutor` |
| Week 2 | 状态管理 | `RunStateManager`, 文件格式 |
| Week 3 | 热重载 | `WorkflowRegistry`, `FileWatcher` |
| Week 3 | 迁移现有策略 | `maker.yaml`, `uot.yaml`, `direct.yaml` |

### 7.3 里程碑

| 里程碑 | 内容 | 验收标准 |
|--------|------|---------|
| M3.5.1 | 基础原语可用 | `direct.yaml` 能正常执行 |
| M3.5.2 | 完整原语可用 | `maker.yaml` 能正常执行 |
| M4.1 | DSL 引擎可用 | 热重载生效，无需重启 |
| M4.2 | 断点续传可用 | 中断后能恢复执行 |

---

## 8. 风险与缓解

| 风险 | 可能性 | 影响 | 缓解措施 |
|------|--------|------|---------|
| DSL 表达力不足 | 中 | 高 | 保留 C# 扩展点，允许自定义原语 |
| 递归深度失控 | 低 | 高 | 强制 max_depth 限制，默认 10 |
| 状态文件损坏 | 低 | 中 | 原子写入 + 备份机制 |
| 热重载冲突 | 中 | 低 | 版本锁 + 优雅降级 |
| 性能瓶颈 | 中 | 中 | 懒加载 + 缓存 + 并发限制 |

---

## 9. 附录

### 9.1 与 GitHub Actions 对比

| 维度 | GitHub Actions | Cognitive Mesh DSL |
|------|---------------|-------------------|
| 设计目标 | CI/CD 自动化 | 认知推理编排 |
| 递归支持 | ❌ 不支持 | ✅ 支持 (有深度限制) |
| 状态持久化 | artifacts/cache | 三层状态架构 |
| 投票/共识 | ❌ 无 | ✅ 内置原语 |
| Agent 状态 | ❌ 无 | ✅ 支持 |
| 热重载 | ❌ 需要新 commit | ✅ 文件修改即生效 |
| 并发模型 | jobs + needs | fan_out + fan_in |

### 9.2 类型系统

```yaml
# 基础类型
string, int, float, bool

# 复合类型
array<T>, object, any

# 自定义类型 (JSON Schema)
types:
  Analogy:
    properties:
      id: string
      description: string
      domain: string
      solution: string
    required: [id, description, domain]
  
  Subtask:
    properties:
      id: string
      description: string
    required: [id, description]
```

### 9.3 错误处理

```yaml
# 步骤级错误处理
- id: risky_step
  type: llm_call
  prompt: "..."
  on_error:
    retry: 3                    # 重试次数
    backoff: exponential        # 退避策略
    fallback:                   # 降级步骤
      type: llm_call
      prompt: "Simplified version: {{task}}"

# 工作流级错误处理
error_handler:
  on_timeout:
    action: checkpoint_and_pause
  on_rate_limit:
    action: backoff_and_retry
    max_retries: 5
  on_unknown:
    action: fail_fast
```

---

*Last Updated: 2025-12-04*

