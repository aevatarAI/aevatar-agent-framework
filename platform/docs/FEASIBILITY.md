# Aevatar Platform 可行性分析与技术规划

> 基于 Aevatar Agent Framework 实现对标 OpenCode 的 AI 编程助手

---

## 1. 执行摘要

### 1.1 结论：完全可行

Aevatar Agent Framework 提供了实现 Aevatar Platform 所需的全部核心能力：

| 核心需求 | Aevatar 能力 | 匹配度 | 现有组件 |
|----------|-------------|--------|----------|
| AI 对话 | LLMTornado / MEAI | ⭐⭐⭐⭐⭐ | `Aevatar.Agents.AI.*` |
| Tool Calling | Tool Calling + MCP | ⭐⭐⭐⭐⭐ | `Aevatar.Agents.AI.Core` |
| 多 Agent 协作 | Actor Model + Event Stream | ⭐⭐⭐⭐⭐ | `Aevatar.Agents.Core` |
| 工作流编排 | Cognitive Mesh DSL | ⭐⭐⭐⭐⭐ | `Aevatar.CognitiveMesh.Dsl` |
| 状态持久化 | Event Sourcing | ⭐⭐⭐⭐⭐ | `EventSourcedGAgentBase` |
| 配置管理 | 标准文件系统 | ⭐⭐⭐⭐⭐ | 新开发 (简单) |

### 1.2 主要工作量

| 组件 | 复杂度 | 预估工时 | 说明 |
|------|--------|----------|------|
| CLI 框架 | 中 | 3 天 | 交互式终端 UI |
| 配置加载 (`~/.aevatar/`) | 低 | 1 天 | YAML 解析 |
| 内置 Agent 实现 | 中 | 5 天 | Coder/Reviewer/Router 等 |
| 工具集成 | 中 | 4 天 | FileSystem/Bash/Git/LSP |
| DSL 运行时 | 低 | 2 天 | 已有 `CognitiveDslCompiler` |
| 会话管理 | 低 | 2 天 | 基于 Event Sourcing |

**总预估**: 4-6 周完成 MVP

---

## 2. Aevatar 现有组件复用分析

### 2.1 可直接复用

```
src/
├── Aevatar.Agents.Core/                    # Agent 基础设施
│   ├── GAgentBase                          # → 所有 Agent 基类
│   ├── EventSourcedGAgentBase              # → 会话持久化
│   └── Parent-Child / Event Stream         # → Agent 协作
│
├── Aevatar.Agents.AI.LLMTornado/           # AI 能力
│   ├── 多模型支持 (OpenAI/Claude/Ollama)   # → 模型切换
│   ├── Tool Calling                        # → 工具调用
│   └── 流式输出                            # → 实时响应
│
├── Aevatar.Agents.AI.Core/                 # AI 核心
│   ├── Conversation History                # → 对话历史
│   └── MCP Client                          # → MCP 工具支持
│
└── src/
    ├── Aevatar.CognitiveMesh.Dsl/          # DSL 编译器 ✅ 可直接使用
    │   ├── CognitiveDslCompiler            # → 解析 workflow JSON
    │   ├── MeshDefinition                  # → 工作流定义模型
    │   └── Validation Rules                # → Schema 验证
    │
    ├── Aevatar.Agents.Cognitive.Core/      # 策略抽象
    │   └── IReasoningStrategy              # → 可参考
    ├── Aevatar.Agents.Cognitive.Content/   # 内容加载/项目存储
    └── Aevatar.Agents.Cognitive.Streaming/ # 广播/流式工具
```

### 2.2 需要新开发

| 组件 | 说明 | 复用程度 |
|------|------|----------|
| CLI Framework | 终端交互 | 新开发 |
| ConfigLoader | `~/.aevatar/` 配置加载 | 新开发 (简单) |
| CoderAgent | 代码编写 Agent | 新开发，参考现有 AI Agent |
| ReviewerAgent | 代码审查 Agent | 新开发 |
| RouterAgent | 任务路由 | 新开发 |
| FileSystemTool | 文件操作工具 | 可参考现有示例 |
| BashTool | Shell 执行 | 可参考现有示例 |
| GitTool | Git 操作 | 新开发 |
| LSPTool | LSP 集成 | 新开发 |

---

## 3. 配置系统设计

### 3.1 目录结构

```
~/.aevatar/
├── config.json              # 主配置
├── secrets.json             # 敏感信息 (加密)
├── agents/                  # ✅ Agent YAML（role 配置，跨应用复用）
│   ├── coder.yaml
│   ├── reviewer.yaml
│   └── ...
├── skills/                  # ✅ Agent Skills（可选，SKILL.md）
├── workflows/               # Cognitive Mesh DSL
│   ├── hermes.yaml          # 默认工作流
│   ├── code-review.json
│   └── ...
├── mcp/
│   └── servers.yaml         # MCP 服务器配置
├── logs/
│   └── aevatar.log
└── sessions/                # 会话历史 (可选)
    └── <session-id>/
```

### 3.2 配置加载实现

> 现状更新：Agent YAML 的加载与应用已经沉淀在框架层 `Aevatar.Agents.AI.Core`，Platform 只需要做 wiring（创建 agent 后调用 applier）。

```csharp
public class AevatarConfigLoader
{
    private readonly string _configDir;
    
    public AevatarConfigLoader()
    {
        _configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".aevatar"
        );
    }
    
    public AevatarConfig Load()
    {
        // 1. 加载主配置
        var configPath = Path.Combine(_configDir, "config.json");
        var config = LoadJson<AevatarConfig>(configPath);
        
        // 2. 合并 secrets
        var secretsPath = Path.Combine(_configDir, "secrets.json");
        if (File.Exists(secretsPath))
        {
            var secrets = LoadSecretsStore(secretsPath);
            MergeSecrets(config, secrets);
        }
        
        // 3. Agent YAML 已由框架层支持（GlobalAgentYamlRegistry + AgentYamlConfigApplier）
        //    OpenCode 运行时只需把 role -> yaml 应用到 agent 实例即可。
        
        // 4. 加载工作流
        config.Workflows = LoadWorkflows();
        
        return config;
    }
    
    private Dictionary<string, MeshDefinition> LoadWorkflows()
    {
        var compiler = new CognitiveDslCompiler();
        var workflows = new Dictionary<string, MeshDefinition>();
        
        var workflowDir = Path.Combine(_configDir, "workflows");
        foreach (var file in Directory.GetFiles(workflowDir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var json = File.ReadAllText(file);
            workflows[name] = compiler.Compile(json);
        }
        
        return workflows;
    }
}
```

### 3.3 初始化命令

```bash
# 创建默认配置
aevatar config init

# 会创建以下文件：
# ~/.aevatar/config.json      (默认模板)
# ~/.aevatar/secrets.json     (加密存储，由 aevatar-config 写入)
# ~/.aevatar/agents/          (预置 Agent 配置)
# ~/.aevatar/workflows/       (预置工作流)
```

---

## 4. Cognitive Mesh DSL 集成

### 4.1 现有 DSL 能力

```json
// 示例：code-review 工作流
{
  "dsl_version": "0.1",
  "goal": {
    "name": "code_review",
    "success_metric": "review_passed"
  },
  "strategy": "maker",
  "budget": {
    "max_steps": 50,
    "token_limit": 100000
  },
  "nodes": [
    {
      "id": "coder",
      "type": "CoderAgent",
      "params": { "model": "gpt-4" }
    },
    {
      "id": "reviewer",
      "type": "ReviewerAgent",
      "params": { "style": "constructive" }
    }
  ],
  "edges": [
    { "from": "coder", "to": "reviewer", "channel": "submit_code" },
    { "from": "reviewer", "to": "coder", "channel": "revision" }
  ],
  "constraints": [
    { "type": "max_iterations", "value": 3 }
  ]
}
```

### 4.2 DSL 执行引擎

```csharp
public class WorkflowEngine
{
    private readonly CognitiveDslCompiler _compiler;
    private readonly IGAgentActorFactory _actorFactory;
    private readonly GlobalAgentYamlRegistry _agentYamls; // from AI.Core
    
    public async Task<WorkflowResult> ExecuteAsync(
        string workflowName,
        string userInput,
        CancellationToken ct = default)
    {
        // 1. 加载并编译 DSL
        var definition = LoadWorkflow(workflowName);
        
        // 2. 创建 Agent 实例
        var agents = await CreateAgents(definition.Nodes);
        
        // 3. 建立 Agent 关系 (基于 edges)
        await EstablishRelationships(agents, definition.Edges);
        
        // 4. 启动工作流
        var orchestrator = agents["router"] ?? agents.Values.First();
        await orchestrator.PublishEventAsync(new UserInputEvent 
        { 
            Content = userInput 
        });
        
        // 5. 等待完成或超时
        return await WaitForCompletion(definition.Budget, ct);
    }
    
    private async Task<Dictionary<string, IGAgentActor>> CreateAgents(
        IReadOnlyList<NodeSpec> nodes)
    {
        var agents = new Dictionary<string, IGAgentActor>();
        
        foreach (var node in nodes)
        {
            // 约定：node.type == role（例如 "coder" / "reviewer"）
            var role = node.Type;

            // 典型做法：
            // - 用一个通用 RoleAgent（AIGAgentBase 派生）承载绝大多数 role
            // - 少数强结构/强约束角色可以用专用 Agent class
            var agentType = ResolveAgentTypeFromRole(role);
            var actor = await _actorFactory.CreateGAgentActorAsync(agentType, Guid.NewGuid());
            
            // 关键：应用全局 Agent YAML（role 配置）
            // - YAML 位于 ~/.aevatar/agents/{role}.yaml
            // - 由框架层 AgentYamlConfigApplier 应用到 AIGAgentBase：
            //   model/prompt/tools/skills 等
            if (actor.GetAgent() is AIGAgentBase ai)
            {
                var yaml = _agentYamls.TryLoad(role);
                await AgentYamlConfigApplier.ApplyAsync(ai, yaml, role, ct);
            }
            
            agents[node.Id] = actor;
        }
        
        return agents;
    }
}
```

> 这段 wiring 解释了你问的“agent 在哪创建”：**创建点在 app 的 composition root / workflow engine**（比如这里的 `CreateAgents`），框架层提供 registry/applier，让你无需在 app 里重复写“读 YAML + 赋值 prompt/tools/skills”的脏活。

### 4.3 支持的策略

| 策略 | DSL 值 | 执行模式 | 适用场景 |
|------|--------|----------|----------|
| 单步执行 | `cot` | 线性 | 简单任务 |
| 树状搜索 | `tot` | 分支回溯 | 复杂推理 |
| 图执行 | `got` | DAG 并行 | 多依赖任务 |
| MAKER | `maker` | 分解-投票-合成 | 需要共识 |
| UoT 组合 | `uot_comb` | 类比检索 | 创新方案 |
| UoT 探索 | `uot_expl` | 发散思维 | 发现新思路 |
| UoT 变换 | `uot_trans` | 突破假设 | 颠覆性创新 |

---

## 5. Agent 实现设计

### 5.1 基础 Agent 接口

```csharp
public interface IPlatformAgent : IGAgent
{
    /// <summary>
    /// Agent 配置
    /// </summary>
    AgentConfig Config { get; }
    
    /// <summary>
    /// 可用工具列表
    /// </summary>
    IReadOnlyList<ITool> Tools { get; }
    
    /// <summary>
    /// 处理用户输入
    /// </summary>
    Task<AgentResponse> ProcessAsync(string input, CancellationToken ct);
}

public interface IConfigurableAgent
{
    void Configure(IReadOnlyDictionary<string, JsonElement> @params);
}
```

### 5.2 CoderAgent 实现

```csharp
public class CoderAgent : GAgentBase<CoderState>, IPlatformAgent, IConfigurableAgent
{
    private readonly ILLMService _llm;
    private readonly IToolRegistry _tools;
    
    public AgentConfig Config { get; private set; }
    public IReadOnlyList<ITool> Tools => _tools.GetTools(Config.Tools);
    
    public override Task<string> GetDescriptionAsync() =>
        Task.FromResult("Coder Agent - 负责代码编写与修改");
    
    [EventHandler]
    public async Task HandleUserInput(UserInputEvent evt)
    {
        // 1. 构建 prompt
        var prompt = BuildPrompt(evt.Content);
        
        // 2. 调用 LLM (支持 Tool Calling)
        var response = await _llm.ChatWithToolsAsync(
            prompt, 
            Tools,
            streaming: true
        );
        
        // 3. 执行工具调用
        foreach (var toolCall in response.ToolCalls)
        {
            var result = await ExecuteTool(toolCall);
            // 追加到对话历史
        }
        
        // 4. 发布结果
        await PublishAsync(new AgentResponseEvent
        {
            AgentId = Id.ToString(),
            AgentName = Config.Name,
            Content = response.Content,
            ToolCalls = response.ToolCalls
        });
    }
    
    private string BuildPrompt(string userInput)
    {
        return $"""
            {Config.SystemPrompt}
            
            当前工作目录: {Environment.CurrentDirectory}
            
            用户请求:
            {userInput}
            
            请分析需求并完成任务。如果需要读取或修改文件，请使用相应的工具。
            """;
    }
}
```

### 5.3 ReviewerAgent 实现

```csharp
public class ReviewerAgent : GAgentBase<ReviewerState>, IPlatformAgent
{
    [EventHandler]
    public async Task HandleCodeSubmission(CodeSubmissionEvent evt)
    {
        var prompt = $"""
            你是一位代码审查专家。请审查以下代码变更：
            
            {evt.CodeDiff}
            
            请从以下方面进行审查：
            1. 代码正确性
            2. 代码风格
            3. 潜在 bug
            4. 安全问题
            5. 性能问题
            
            输出格式：
            - ✅ 优点: ...
            - ⚠️ 问题: ...
            - 💡 建议: ...
            """;
        
        var response = await _llm.ChatAsync(prompt);
        
        await PublishAsync(new ReviewResultEvent
        {
            AgentId = Id.ToString(),
            Review = response,
            Approved = !response.Contains("⚠️")
        });
    }
}
```

---

## 6. 工具系统设计

### 6.1 工具接口

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonElement Schema { get; }  // JSON Schema for parameters
    
    Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct);
}
```

### 6.2 内置工具

| 工具 | 功能 | 参数 |
|------|------|------|
| `file_read` | 读取文件 | `path`, `offset?`, `limit?` |
| `file_write` | 写入文件 | `path`, `content` |
| `file_edit` | 编辑文件 | `path`, `oldString`, `newString` |
| `glob` | 文件搜索 | `pattern`, `path?` |
| `grep` | 内容搜索 | `pattern`, `path?`, `include?` |
| `bash` | 执行命令 | `command`, `workdir?`, `timeout?` |
| `git_status` | Git 状态 | - |
| `git_diff` | Git diff | `staged?` |
| `git_commit` | Git 提交 | `message` |
| `lsp_hover` | LSP hover | `file`, `line`, `character` |
| `lsp_definition` | 跳转定义 | `file`, `line`, `character` |
| `lsp_references` | 查找引用 | `file`, `line`, `character` |

### 6.3 FileSystem 工具示例

```csharp
public class FileReadTool : ITool
{
    public string Name => "file_read";
    public string Description => "读取文件内容";
    
    public JsonElement Schema => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "文件路径" },
            offset = new { type = "integer", description = "起始行 (0-based)" },
            limit = new { type = "integer", description = "读取行数" }
        },
        required = new[] { "path" }
    });
    
    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var path = args.GetProperty("path").GetString()!;
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 2000;
        
        // 安全检查
        var fullPath = Path.GetFullPath(path);
        if (!IsPathAllowed(fullPath))
        {
            return ToolResult.Error($"Access denied: {path}");
        }
        
        var lines = await File.ReadAllLinesAsync(fullPath, ct);
        var content = string.Join("\n", lines.Skip(offset).Take(limit));
        
        return ToolResult.Success(content);
    }
}
```

---

## 7. CLI 设计

### 7.1 技术选型

| 组件 | 选择 | 理由 |
|------|------|------|
| CLI 框架 | `System.CommandLine` | .NET 官方，功能完善 |
| TUI 渲染 | `Spectre.Console` | 丰富的终端 UI 组件 |
| Markdown 渲染 | `Spectre.Console.Markup` | 代码高亮支持 |

### 7.2 主要命令

```csharp
var rootCommand = new RootCommand("Aevatar Platform - AI 编程助手");

// 主命令：启动交互式会话
rootCommand.SetHandler(async (context) =>
{
    var session = new InteractiveSession();
    await session.RunAsync(context.GetCancellationToken());
});

// -c: 单次执行
var executeOption = new Option<string>("-c", "执行单次任务");
rootCommand.AddOption(executeOption);

// --workflow: 指定工作流
var workflowOption = new Option<string>("--workflow", () => "hermes", "工作流名称");
rootCommand.AddOption(workflowOption);

// --model: 指定模型
var modelOption = new Option<string>("--model", "覆盖默认模型");
rootCommand.AddOption(modelOption);

// --resume: 继续会话
var resumeOption = new Option<bool>("--resume", "继续上次会话");
rootCommand.AddOption(resumeOption);

// 子命令
rootCommand.AddCommand(new ConfigCommand());
rootCommand.AddCommand(new SessionsCommand());
rootCommand.AddCommand(new AgentsCommand());
rootCommand.AddCommand(new WorkflowsCommand());
```

### 7.3 交互式会话

```csharp
public class InteractiveSession
{
    private readonly WorkflowEngine _engine;
    private readonly IAnsiConsole _console;
    
    public async Task RunAsync(CancellationToken ct)
    {
        // 显示欢迎信息
        _console.Write(new FigletText("Aevatar").Color(Color.Blue));
        _console.MarkupLine($"[grey]v0.1 | Workflow: {_workflow}[/]");
        _console.MarkupLine($"[grey]Working directory: {Environment.CurrentDirectory}[/]");
        _console.WriteLine();
        
        while (!ct.IsCancellationRequested)
        {
            // 读取用户输入
            var input = _console.Prompt(new TextPrompt<string>("[green]You:[/] "));
            
            if (string.IsNullOrWhiteSpace(input)) continue;
            if (input == "exit" || input == "quit") break;
            
            // 执行工作流
            await foreach (var evt in _engine.ExecuteStreamingAsync(input, ct))
            {
                RenderEvent(evt);
            }
        }
    }
    
    private void RenderEvent(WorkflowEvent evt)
    {
        switch (evt)
        {
            case AgentThinkingEvent thinking:
                _console.MarkupLine($"[blue][{thinking.AgentName}][/] [grey]思考中...[/]");
                break;
                
            case AgentResponseEvent response:
                _console.MarkupLine($"[blue][{response.AgentName}][/]");
                _console.Write(new Panel(Markup.Escape(response.Content)));
                break;
                
            case ToolCallEvent toolCall:
                _console.MarkupLine($"[yellow]🔧 {toolCall.ToolName}[/]");
                break;
        }
    }
}
```

---

## 8. 技术风险与对策

| 风险 | 影响 | 概率 | 对策 |
|------|------|------|------|
| DSL 表达能力不足 | 复杂工作流受限 | 低 | 现有 DSL 已支持主要模式 |
| 多 Agent 响应延迟 | 用户体验 | 中 | 流式输出 + 并行执行 |
| 工具执行安全 | 系统安全 | 中 | 路径白名单 + 命令过滤 |
| 上下文窗口溢出 | 记忆丢失 | 中 | 摘要压缩 + RAG |
| 配置文件格式变更 | 兼容性 | 低 | 版本号 + 迁移脚本 |

---

## 9. 开发计划

### Phase 1: 基础架构 (Week 1-2)

| 任务 | 天数 | 说明 |
|------|------|------|
| CLI 框架搭建 | 2 | System.CommandLine + Spectre.Console |
| 配置系统实现 | 1 | `~/.aevatar/` 加载逻辑 |
| 单 Agent 对话 | 2 | CoderAgent + LLM 集成 |
| 基础工具集 | 3 | file_read/write/edit, bash |
| 流式输出 | 1 | 终端实时渲染 |

**验收标准**: 能通过 CLI 与单个 CoderAgent 对话，执行文件操作

### Phase 2: Agent 协作 (Week 3-4)

| 任务 | 天数 | 说明 |
|------|------|------|
| ReviewerAgent | 2 | 代码审查 Agent |
| RouterAgent | 1 | 任务路由 |
| DSL 运行时 | 2 | 基于 CognitiveDslCompiler |
| 预置工作流 | 2 | hermes, code-review |
| Event Sourcing 会话 | 2 | 会话持久化与恢复 |

**验收标准**: code-review 工作流可用，会话可恢复

### Phase 3: 工具完善 (Week 5-6)

| 任务 | 天数 | 说明 |
|------|------|------|
| Git 工具集 | 2 | status/diff/commit |
| 搜索工具 | 2 | grep/glob/ast-grep |
| LSP 集成 | 3 | hover/definition/references |
| MCP Bridge | 3 | MCP 服务器支持 |

**验收标准**: 工具覆盖 OpenCode 80% 功能

### Phase 4: 打磨发布 (Week 7-8)

| 任务 | 天数 | 说明 |
|------|------|------|
| 更多 Agent | 3 | Tester, Debugger, Docs |
| 更多工作流 | 2 | tdd, debug, maker |
| 文档与示例 | 2 | README, 快速入门 |
| 测试与修复 | 3 | 集成测试 |

---

## 10. 目录结构

```
platform/
├── docs/
│   ├── PRD.md
│   └── FEASIBILITY.md
│
├── src/
│   ├── Aevatar.Platform.Cli/          # CLI 入口
│   │   ├── Program.cs
│   │   ├── Commands/
│   │   │   ├── ConfigCommand.cs
│   │   │   ├── SessionsCommand.cs
│   │   │   └── ...
│   │   └── Session/
│   │       └── InteractiveSession.cs
│   │
│   ├── Aevatar.Platform.Core/         # 核心逻辑
│   │   ├── Config/
│   │   │   ├── AevatarConfig.cs
│   │   │   ├── ConfigLoader.cs
│   │   │   └── SecretsConfig.cs
│   │   ├── Agents/
│   │   │   ├── CoderAgent.cs
│   │   │   ├── ReviewerAgent.cs
│   │   │   ├── RouterAgent.cs
│   │   │   └── ...
│   │   ├── Tools/
│   │   │   ├── FileSystemTools.cs
│   │   │   ├── BashTool.cs
│   │   │   ├── GitTools.cs
│   │   │   └── ...
│   │   └── Workflow/
│   │       └── WorkflowEngine.cs
│   │
│   └── Aevatar.Platform.Protos/       # Protobuf 定义
│       └── platform.proto
│
├── templates/                         # 默认配置模板
│   ├── config.json
│   ├── agents/
│   └── workflows/
│
└── README.md
```

---

## 11. 结论

### 11.1 可行性评估

| 维度 | 评分 | 说明 |
|------|------|------|
| 技术可行性 | ⭐⭐⭐⭐⭐ | 核心组件已存在 |
| 工作量可控 | ⭐⭐⭐⭐ | 4-6 周 MVP |
| 风险可控 | ⭐⭐⭐⭐⭐ | 主要是集成工作 |

### 11.2 关键成功因素

1. **复用 Cognitive Mesh DSL** - 工作流编排已有成熟实现
2. **复用 LLMTornado** - AI 能力已完善
3. **配置系统简洁** - `~/.aevatar/` 符合 Unix 惯例
4. **渐进式实现** - 先单 Agent，再多 Agent

### 11.3 建议

1. **先实现 CLI + 单 Agent** - 最小可用验证
2. **再集成 DSL 工作流** - 差异化功能
3. **最后完善工具链** - 对标 OpenCode

---

*文档版本: v0.3 | 创建日期: 2026-01-14*
