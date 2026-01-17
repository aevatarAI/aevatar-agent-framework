# Aevatar Platform PRD

> 基于 Aevatar Agent Framework 的 AI 编程助手 —— OpenCode 的多 Agent 替代方案

---

## 1. 产品愿景

**一句话描述**: 一个对标 OpenCode 的 AI 编程助手，但底层采用多 Agent 协作架构，支持更复杂的推理和决策场景。

**核心理念**: 
- 对标 OpenCode 的**开发者体验**
- 底层采用 Aevatar 的**多 Agent 协作**
- 使用 Cognitive Mesh DSL **声明式编排** Agent 工作流
- 所有敏感配置存储在 `~/.aevatar/` 目录

---

## 2. 与 OpenCode 功能对标

### 2.1 功能对照表

| OpenCode 功能 | Aevatar Platform | 实现方式 |
|--------------|------------------|----------|
| 对话式编程 | ✅ 对标 | 单/多 Agent 对话 |
| Tool Calling | ✅ 对标 | Aevatar Tool + MCP |
| 文件操作 | ✅ 对标 | FileSystem Agent |
| Bash 执行 | ✅ 对标 | Shell Agent |
| Git 操作 | ✅ 对标 | Git Agent |
| LSP 集成 | ✅ 对标 | LSP Agent |
| 代码搜索 | ✅ 对标 | Search Agent (grep/ast-grep) |
| MCP 支持 | ✅ 对标 | MCP Bridge |
| 会话管理 | ✅ 对标 | Event Sourcing |
| 配置管理 | ✅ 对标 | `~/.aevatar/config.json` |
| **多 Agent 协作** | ✅ **增强** | Cognitive Mesh DSL |
| **可视化工作流** | ✅ **增强** | DSL + 可视化编辑器 |
| **Agent 市场** | ✅ **增强** | 预置/自定义 Agent |

### 2.2 核心差异化

1. **多 Agent 协作**: 代码编写、审查、测试由不同 Agent 协作完成
2. **DSL 编排**: 使用 Cognitive Mesh DSL 声明式定义工作流
3. **持久化记忆**: Event Sourcing 支持会话回溯和状态恢复
4. **可扩展架构**: 基于 Actor Model，易于扩展新 Agent

---

## 3. 配置管理

### 3.1 配置目录结构

```
~/.aevatar/
├── config.json              # 主配置文件
├── secrets.json             # 敏感信息 (API Keys, 加密)
├── agents/                  # ✅ Agent YAML（role 配置，跨应用复用）
│   ├── coder.yaml
│   ├── reviewer.yaml
│   ├── router.yaml
│   └── ...
├── skills/                  # ✅ Agent Skills（可选，SKILL.md 生态）
├── workflows/               # 自定义工作流 (Cognitive Mesh DSL)
│   ├── code-review.json
│   └── debug.json
├── mcp/                     # MCP 服务器配置
│   └── servers.yaml
└── sessions/                # 会话历史 (可选)
```

> 现状对齐：仓库里已经实现了 `~/.aevatar/agents/{role}.yaml` 的加载与应用（框架层），并支持用 YAML 控制 tools/skills/prompt/model（见“5.2 Agent 配置示例”和“实现状态”）。

### 3.2 主配置文件 (`config.json`)

```json
{
  "Aevatar": {
    "Models": {
      "DefaultProvider": "openai",
      "DefaultModel": "gpt-4",
      "Providers": {
        "openai": {},
        "claude": {},
        "ollama": {}
      }
    },
    "Agents": {
      "DefaultWorkflow": "standard",
      "ParallelLimit": 3
    },
    "Tools": {
      "Shell": {
        "AllowedCommands": ["git", "npm", "cargo", "dotnet"],
        "TimeoutSeconds": 120
      },
      "FileSystem": {
        "AllowedPaths": ["~/Code", "/tmp"]
      }
    },
    "Ui": {
      "Theme": "dark",
      "Editor": "vscode"
    },
    "Logging": {
      "Level": "info",
      "File": "~/.aevatar/logs/aevatar.log"
    }
  }
}
```

> 说明：沿用 Aevatar.Tools.Config，只需写 `secrets.json` 里已有的 provider 名称，不在这里配置 endpoint/model。

### 3.3 敏感配置 (`secrets.json`)

```json
{
  "LLMProviders": {
    "Default": "openai",
    "Providers": {
      "openai": {
        "ProviderType": "OpenAI",
        "ApiKey": "sk-...",
        "Model": "gpt-4"
      },
      "claude": {
        "ProviderType": "Anthropic",
        "ApiKey": "sk-ant-...",
        "Model": "claude-3-5-sonnet"
      }
    }
  },
  "MCP": {
    "Servers": {
      "github": {
        "Token": "ghp_..."
      }
    }
  }
}
```

---

## 4. Cognitive Mesh DSL 工作流编排

### 4.1 DSL 格式 (JSON)

基于现有 Cognitive Mesh DSL 规范（并做一条关键约定：**node.type = role**）：

```json
{
  "dsl_version": "0.1",
  "goal": {
    "name": "code_review_workflow",
    "success_metric": "all_issues_addressed"
  },
  "strategy": "maker",
  "budget": {
    "max_steps": 50,
    "token_limit": 100000
  },
  "nodes": [
    {
      "id": "router",
      "type": "router",
      "params": {
        "routing_rules": ["code_task→coder", "review_task→reviewer"]
      }
    },
    {
      "id": "coder",
      "type": "coder",
      "params": {
        "note": "agent 的 model/tools/skills 等由 ~/.aevatar/agents/coder.yaml 定义"
      }
    },
    {
      "id": "reviewer",
      "type": "reviewer",
      "params": {
        "style": "constructive"
      }
    },
    {
      "id": "judge",
      "type": "judge",
      "params": {
        "voting_threshold": 0.7
      }
    }
  ],
  "edges": [
    { "from": "router", "to": "coder", "channel": "code_task" },
    { "from": "coder", "to": "reviewer", "channel": "submit_code" },
    { "from": "reviewer", "to": "judge", "channel": "review_result" },
    { "from": "judge", "to": "coder", "channel": "revision_needed" }
  ],
  "constraints": [
    { "type": "max_iterations", "value": 3 },
    { "type": "confidence_threshold", "value": 0.8 }
  ]
}
```

> 关键：**工作流 DSL 管“拓扑/数据流/约束”，role YAML 管“每个 role 怎么跑（provider/model/prompt/tools/skills）”。**

### 4.2 预置工作流

| 工作流 | 描述 | 节点 |
|--------|------|------|
| `standard` | 默认单 Agent 模式 | Coder |
| `code-review` | 代码+审查 | Coder → Reviewer |
| `tdd` | 测试驱动开发 | Coder ↔ Tester |
| `maker` | MAKER 多专家共识 | Router → [Experts] → Judge |
| `debug` | 调试工作流 | Analyzer → Debugger → Verifier |

### 4.3 策略类型

支持 Cognitive Mesh 定义的策略：

| 策略 | 说明 | 适用场景 |
|------|------|----------|
| `cot` | Chain of Thought | 简单线性任务 |
| `tot` | Tree of Thought | 需要回溯的问题 |
| `got` | Graph of Thought | 复杂依赖关系 |
| `maker` | 多专家共识 | 需要多视角验证 |
| `uot_comb` | 类比组合 | 创新解决方案 |
| `uot_expl` | 探索发散 | 发现新思路 |
| `uot_trans` | 变换突破 | 颠覆性创新 |

---

## 5. 核心 Agent

### 5.1 内置 Agent

| Agent | 职责 | 工具 |
|-------|------|------|
| `RouterAgent` | 任务路由与分发 | - |
| `CoderAgent` | 代码编写与修改 | file_*, bash, git |
| `ReviewerAgent` | 代码审查 | file_read, grep |
| `TesterAgent` | 测试编写与运行 | file_*, bash |
| `DebugAgent` | 调试与问题定位 | file_*, bash, lsp |
| `SearchAgent` | 代码搜索 | grep, ast-grep, glob |
| `DocsAgent` | 文档生成 | file_* |
| `JudgeAgent` | 决策与投票 | - |

### 5.2 Agent 配置示例

```yaml
# ~/.aevatar/agents/coder.yaml
id: "coder"
name: "Coder Agent"
version: "1.0"

persona:
  role: "资深软件工程师"
  expertise: ["代码实现", "算法设计", "调试排错"]
  style: "专业、高效、注重代码质量"

provider: "openai"          # 可选：不写则走应用默认 provider 解析
model: "gpt-4.1-mini"       # 可选：不写则用应用/框架默认
temperature: 0.3            # 可选
max_tokens: 4096            # 可选（对应 AIGAgentBase.Config.MaxOutputTokens）

tools:
  - file_read
  - file_write
  - file_delete
  - bash
  - git_status
  - git_diff
  - git_commit

# 可选：skills 非空时会自动启用 ~/.aevatar/skills 并允许 skills_* / find_helpful_skills 等工具
skills:
  - "Claude Scientific Skills"

system_prompt: |
  你是一位资深软件工程师。你的职责是编写高质量、可维护的代码。
  
  工作原则：
  1. 先理解需求，再动手编码
  2. 遵循项目现有的代码风格
  3. 编写必要的注释
  4. 考虑边界情况和错误处理
  5. 不要过度设计
```

### 5.3 实现状态（已完成一部分设计）

这部分已经在框架层落地（可被 Platform/任意 Aevatar App 复用）：

- ✅ **Agent YAML Loader**：解析 `~/.aevatar/agents/{role}.yaml`
- ✅ **Global Agent YAML Registry**：扫描目录并发现可用 role
- ✅ **Agent YAML Applier**：将 YAML 应用到 `AIGAgentBase`（model/prompt/tools/skills）
- ✅ **Baseline Tool Allowlist**：每次 LLM 请求都可注入“固定 allowlist”，同时工具执行侧做 defense-in-depth

落地位置（框架层）：
- `src/Aevatar.Agents.AI.Core/Configuration/GlobalAgentYamlRegistry.cs`
- `src/Aevatar.Agents.AI.Core/Configuration/AgentYamlConfigApplier.cs`
- `src/Aevatar.Agents.AI.Core/Configuration/AgentYamlConfigLoader.cs`
- `src/Aevatar.Agents.AI.Core/AIGAgentBase.Chat.cs`（`SetFixedToolAllowlist`）

下一步（Platform 需要补的 wiring）：
- 在 Platform 的 workflow/engine 创建 agent actor 后，按 role 从 registry 取 YAML，调用 applier 应用配置

---

## 6. 用户交互

### 6.1 命令行界面

```bash
# 启动交互式会话
aevatar

# 指定工作流
aevatar --workflow code-review

# 指定模型
aevatar --model claude-3

# 执行单次任务
aevatar -c "实现一个 JWT 中间件"

# 继续上次会话
aevatar --resume

# 查看会话历史
aevatar sessions list
aevatar sessions show <session-id>
```

### 6.2 交互示例

```
$ aevatar --workflow code-review

🤖 Aevatar Platform v0.1
📁 Working directory: ~/Code/my-project
🔄 Workflow: code-review (Coder → Reviewer)

You: 帮我实现一个 JWT 认证中间件

[Coder Agent] 我来分析需求并实现...

正在读取项目结构...
发现这是一个 Express.js 项目。

我将创建以下文件：
- src/middleware/auth.ts
- src/utils/jwt.ts

[正在编写代码...]

✅ 代码已生成，提交给 Reviewer 审查...

[Reviewer Agent] 我来审查这段代码...

**审查报告**

✅ 优点：
- JWT 验证逻辑正确
- 错误处理完善

⚠️ 建议：
1. 建议添加 token 刷新机制
2. SECRET_KEY 应该从环境变量读取
3. 建议添加单元测试

[Coder Agent] 收到反馈，我来修改...

[修改完成]

You: 应用这些改动

✅ 已写入 3 个文件
```

---

## 7. 技术架构

```
┌─────────────────────────────────────────────────────────────────┐
│                      Aevatar Platform                            │
├─────────────────────────────────────────────────────────────────┤
│  CLI / TUI Interface                                             │
│  - 命令解析                                                       │
│  - 会话管理                                                       │
│  - 输出渲染                                                       │
├─────────────────────────────────────────────────────────────────┤
│  Cognitive Mesh Engine                                           │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  DSL Compiler                                             │   │
│  │  - 解析 workflow JSON                                     │   │
│  │  - 验证 Schema                                            │   │
│  │  - 生成执行计划                                           │   │
│  └──────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Agent Orchestrator                                       │   │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐         │   │
│  │  │ Router  │ │ Coder   │ │Reviewer │ │ Judge   │ ...     │   │
│  │  └────┬────┘ └────┬────┘ └────┬────┘ └────┬────┘         │   │
│  │       └───────────┴───────────┴───────────┘               │   │
│  │                    Event Bus                               │   │
│  └──────────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────────┤
│  Tool Layer                        │  AI Layer                   │
│  ├── FileSystem                    │  ├── LLMTornado            │
│  ├── Bash/Process                  │  ├── Ollama                │
│  ├── Git                           │  └── OpenAI/Claude         │
│  ├── LSP Client                    │                             │
│  ├── Search (grep/ast-grep)        │                             │
│  └── MCP Bridge                    │                             │
├─────────────────────────────────────────────────────────────────┤
│  Config Layer                                                    │
│  ~/.aevatar/                                                     │
│  ├── config.json    (主配置)                                     │
│  ├── secrets.json   (API Keys, 加密)                              │
│  ├── agents/        (Agent 配置)                                 │
│  └── workflows/     (DSL 工作流)                                 │
├─────────────────────────────────────────────────────────────────┤
│  Storage Layer                                                   │
│  ├── Event Store    (会话历史 / Agent 状态)                      │
│  └── Vector DB      (长期记忆 - 可选)                            │
└─────────────────────────────────────────────────────────────────┘
```

---

## 8. MVP 范围

### Phase 1: 基础功能 (Week 1-3)

| 功能 | 优先级 | 对标 OpenCode |
|------|--------|---------------|
| CLI 框架 | P0 | ✅ |
| `~/.aevatar/` 配置加载 | P0 | ✅ |
| 单 Agent 对话 | P0 | ✅ |
| 文件操作工具 | P0 | ✅ |
| Bash 执行 | P0 | ✅ |
| 流式输出 | P0 | ✅ |

### Phase 2: Agent 协作 (Week 4-5)

| 功能 | 优先级 | 增强功能 |
|------|--------|----------|
| Cognitive Mesh DSL 解析 | P0 | ✅ |
| 多 Agent 协作 | P0 | ✅ |
| 预置工作流 (code-review) | P0 | ✅ |
| Event Sourcing 会话持久化 | P1 | ✅ |

### Phase 3: 生态完善 (Week 6-8)

| 功能 | 优先级 |
|------|--------|
| MCP 支持 | P1 |
| LSP 集成 | P1 |
| Git 深度集成 | P1 |
| 自定义 Agent | P2 |
| 可视化工作流编辑器 (Tauri) | P2 |

---

## 9. 非功能需求

| 指标 | 要求 |
|------|------|
| 启动时间 | < 1s (CLI) |
| AI 响应延迟 | < 3s (首字节，流式) |
| 内存占用 | < 200MB (基础运行) |
| 离线可用 | 支持本地 Ollama |
| 跨平台 | macOS / Windows / Linux |
| 数据安全 | 全部本地存储，secrets 单独文件 |

---

## 10. 成功指标

| 指标 | MVP 目标 |
|------|----------|
| OpenCode 功能覆盖率 | > 80% |
| 多 Agent 协作有效性 | 代码审查采纳率 > 70% |
| 用户上手时间 | < 5 分钟 |

---

## 附录 A: 术语表

| 术语 | 定义 |
|------|------|
| Agent | 具有独立职责的 AI 实体，基于 Aevatar GAgentBase |
| Workflow | 由 Cognitive Mesh DSL 定义的 Agent 协作流程 |
| DSL | Domain Specific Language，声明式定义工作流 |
| MCP | Model Context Protocol，工具扩展协议 |
| Event Sourcing | 通过事件序列持久化状态的模式 |

## 附录 B: 命令参考

```bash
# 基础命令
aevatar                     # 启动交互式会话
aevatar -c "任务"           # 单次任务执行
aevatar --workflow <name>   # 指定工作流
aevatar --model <model>     # 指定模型

# 会话管理
aevatar sessions list       # 列出会话
aevatar sessions show <id>  # 查看会话
aevatar --resume            # 继续上次会话

# 配置管理
aevatar config show         # 显示配置
aevatar config edit         # 编辑配置
aevatar config init         # 初始化配置

# Agent 管理
aevatar agents list         # 列出 Agent
aevatar agents show <id>    # 查看 Agent 配置

# 工作流管理
aevatar workflows list      # 列出工作流
aevatar workflows validate <file>  # 验证 DSL
```

---

*文档版本: v0.3 | 创建日期: 2026-01-14*
