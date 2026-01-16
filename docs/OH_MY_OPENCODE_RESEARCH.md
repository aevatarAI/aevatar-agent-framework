### 调研报告：oh-my-opencode（OpenCode 插件）与对 Aevatar 的启示

### 调研对象

- **Repo**: [oh-my-opencode](https://github.com/code-yeongyu/oh-my-opencode#oh-my-opencode)
- **定位**: “Battery included”的 OpenCode 增强插件：提供 async subagents、curated agents/models、工具链（LSP/AST）、curated MCPs、以及 Claude Code 兼容层。

### 一句话结论

oh-my-opencode 的价值不在“多几个工具”，而在于把 **长链路 Agent 工作流的工程化痛点**（稳定性、上下文、可插拔能力、默认安全与非交互环境）做成了 **可配置的 Hook/插件层**；Aevatar 已经具备 Tool Loop + MCP + Skills 的底座，只缺一个“**Harness/Hooks 中间层**”把这些工程化能力系统化落地。

---

### 1) oh-my-opencode：核心卖点与能力边界

- **核心卖点**: 把 OpenCode 变成一个“更像 Claude Code / AmpCode 的 agent harness”，并把常见踩坑写进默认行为里（而不是留给用户自己拼配置）。
- **能力边界**:
  - **上层**：通过 hooks 影响“会话生命周期 / 任务推进 / 上下文维护 / 工具输出整理”等行为。
  - **中层**：提供 curated MCP、LSP 等“外部能力”。
  - **底层**：通过非交互环境与稳定性机制，让长任务“更不容易死”。

---

### 2) Hooks：把“工程化习惯”做成可开关的系统

### 2.1 配置入口（来自 README 片段）

oh-my-opencode 允许通过配置禁用内置 hooks：

- **配置文件**：`~/.config/opencode/oh-my-opencode.json` 或 `.opencode/oh-my-opencode.json`
- **禁用 hooks**：`disabled_hooks`

示例（摘录）：

```json
{
  "disabled_hooks": ["comment-checker", "agent-usage-reminder"]
}
```

### 2.2 Hook 列表（来自 README 片段）与分类理解

README 中列出的 hooks（原文为平铺列表）可以按“问题域”分组理解：

- **任务推进/工作流约束**
  - `todo-continuation-enforcer`
  - `empty-task-response-detector`
  - `keyword-detector`
  - `agent-usage-reminder`
- **上下文与 token 管理**
  - `context-window-monitor`
  - `compaction-context-injector`
  - `preemptive-compaction`
  - `anthropic-context-window-limit-recovery`
  - `thinking-block-validator`
- **工具输出与信息密度控制**
  - `grep-output-truncator`
  - `tool-output-truncator`
- **目录/项目语境注入**
  - `directory-agents-injector`
  - `directory-readme-injector`
  - `rules-injector`
- **会话稳定性与 UX**
  - `session-recovery`
  - `session-notification`
  - `background-notification`
  - `auto-update-checker` / `startup-toast`
- **交互环境策略**
  - `non-interactive-env`
  - `interactive-bash-session`
- **兼容层 / 迁移层**
  - `claude-code-hooks`
  - `claude-code-compatible layer`（README 的定位描述）
- **其他**
  - `comment-checker`
  - `empty-message-sanitizer`
  - `ralph-loop`

这套设计本质是：把“人会不断手工纠偏的 meta 操作”收敛为 Hook 中间层，默认开启并提供显式禁用入口。

---

### 3) MCP：curated MCPs 的默认启用与可禁用策略

README 片段说明默认启用 MCP：

- `context7`
- `websearch_exa`（Exa）
- `grep_app`（grep.app）

并提供 `disabled_mcps` 作为禁用入口（示例同样来自 README 片段）：

```json
{
  "disabled_mcps": ["context7", "websearch_exa", "grep_app"]
}
```

关键点不是“接了多少 MCP”，而是 **默认带一套质量可控的能力组合**，并通过配置暴露安全/合规开关。

---

### 4) LSP：让 agent 具备“结构化代码理解/重构”工具链

README 片段强调：

- OpenCode 原生提供 LSP 工具用于分析；
- oh-my-opencode 增强了重构能力（rename / code actions），并通过 `lsp` 配置扩展更多语言服务器。

示例（摘录）：

```json
{
  "lsp": {
    "typescript-language-server": {
      "command": ["typescript-language-server", "--stdio"],
      "extensions": [".ts", ".tsx"],
      "priority": 10
    },
    "pylsp": {
      "disabled": true
    }
  }
}
```

这里的“关键设计味道”是：把 LSP 当作**可配置的工具后端池**，并在 agent runtime 中统一调度。

---

### 5) Experimental：把上下文压缩/输出截断做成可控实验开关

README 片段的 experimental 配置（摘录）：

```json
{
  "experimental": {
    "preemptive_compaction_threshold": 0.85,
    "truncate_all_tool_outputs": true,
    "aggressive_truncation": true,
    "auto_resume": true
  }
}
```

以及 `dcp_for_compaction`（Dynamic Context Pruning）的说明：先做重复/旧 tool 输出剪枝，再做 compaction。

从工程角度看，这是把“token 预算管理”从经验主义变成可测量、可回滚的配置项。

---

### 6) Aevatar 现状：已经拥有的“可对齐底座”（基于本仓库代码）

下面这些能力与 oh-my-opencode 的理念高度同构，意味着 Aevatar 借鉴不需要推倒重来：

- **Tool loop 已内置**（多轮工具调用 + 重复调用保护 + 最大轮次兜底）  
  - 见 `src/Aevatar.Agents.AI.Core/AIGAgentBase.Tools.cs`
- **默认安全策略**（Internal/Dangerous 工具开关 + defense-in-depth）  
  - `AllowInternalTools` / `AllowDangerousTools`（`AIGAgentBase.Tools.cs` + `AevatarToolManager.cs`）
- **AgentSkills：目录化技能按需加载**（避免把所有说明塞进 system prompt）  
  - `skills_list` / `skills_load` 工具与 `AEVATAR_AGENT_SKILLS_DIRS`（`AIGAgentBase.AgentSkills.cs`）
  - `allowed-tools` allowlist 会在 `skills_load` 后动态下发到请求上下文（`AIGAgentBase.Tools.cs`）
- **MCP 已有官方 SDK 集成**（npx/uvx/http + 工具发现/适配）  
  - 见 `src/Aevatar.Agents.AI.Core/Tool/MCP/*`（`MCPClientWrapper` / `MCPToolAdapter` / `MCPToolManagerExtensions` / `MCPServerConfig`）
- **兼容性修复“handler/hook”已存在样例**  
  - `DeepSeekThinkingModeFixHandler` 是一个典型的“在 HTTP 层补齐协议差异”的 best-effort hook（`src/Aevatar.Agents.AI.MEAI/Internal/DeepSeekThinkingModeFixHandler.cs`）
- **本仓库已明确 coding-agent roadmap**  
  - 见 `docs/coding-agent/TASKS.md`，其中已经提出 MaxOutputBytes、非交互命令、安全确认等工程化要求

---

### 7) Aevatar 借鉴 oh-my-opencode：从理念到落地的映射

### 7.1 借鉴的“本质层”

oh-my-opencode 的核心不是某个具体 hook，而是一个清晰的架构分层：

- **Agent（业务/推理）层**：解决“怎么做”
- **Tools/MCP/LSP（能力）层**：解决“能做什么”
- **Hooks/Harness（工程化治理）层**：解决“怎么稳定、怎么省 token、怎么不跑偏、怎么可控”

Aevatar 目前具备前两层（并且做得很干净），第三层还缺一个统一抽象。

### 7.2 可直接移植的 Hook 思路（建议优先级从高到低）

- **输出截断与结构化结果（高优先级）**
  - **对应 oh-my-opencode**：`tool-output-truncator` / `grep-output-truncator`
  - **对应 Aevatar 落点**：
    - 在 `ToolExecutionResult` 上引入 “truncated + totalBytes + keptBytes” 的结构化字段（跨边界 → 用 Protobuf）。
    - 对 `WorkspaceCodeSearchTool`（未来）或现有工具结果内容统一做“长度上限 + 可追溯截断”。
  - **现成对齐点**：`docs/coding-agent/TASKS.md` 已把 `MaxOutputBytes` 写进目标。

- **上下文窗口监控 + 预压缩（高优先级）**
  - **对应 oh-my-opencode**：`context-window-monitor` / `preemptive-compaction` / `compaction-context-injector`
  - **对应 Aevatar 落点**：
    - 结合已有 MemoryStore/VectorIndex：将长对话切片写入 memory，再在阈值触发时让模型基于“摘要 + 检索”继续（而不是硬塞历史）。
    - 把触发阈值做成配置（类似 `preemptive_compaction_threshold`）。

- **会话恢复与降级策略（中高优先级）**
  - **对应 oh-my-opencode**：`session-recovery` / `anthropic-context-window-limit-recovery` / `auto_resume`
  - **对应 Aevatar 落点**：
    - 在 `LLMProvider.GenerateAsync` 外围增加“可插拔重试策略”：对 413/上下文过长/特定模型错误做自动降级（缩短 prompt、移除工具 schema、切换更小模型等）。
    - 这类策略应归入“harness hooks”，而不是散落在各业务 Agent 里。

- **目录语境注入（中优先级）**
  - **对应 oh-my-opencode**：`directory-readme-injector` / `directory-agents-injector` / `rules-injector`
  - **对应 Aevatar 落点**：
    - Aevatar 已有 `AgentSkills` 的 SKILL.md 机制；可以增加一种“workspace profile/agent rules”的约定目录（例如 `.aevatar/`），并提供 tool 在需要时加载。
    - 通过 allowlist（已存在）约束技能导入后的可用工具集合，避免“技能=无限权限”。

- **非交互环境与命令执行治理（中优先级）**
  - **对应 oh-my-opencode**：`non-interactive-env` / `interactive-bash-session`
  - **对应 Aevatar 落点**：
    - 在未来 `SandboxCommandTool` 中强制 `--yes`/超时/白名单，并记录审计事件（同样已在 `docs/coding-agent/TASKS.md` 提出）。

### 7.3 推荐的最小落地方案（不大改架构）

- **Step A：在 AI.Core 中引入 Hook 管线（单一入口）**
  - 引入类似 `IAevatarAgentHook` 的接口（示例职责：`BeforeLLMRequest`/`AfterLLMResponse`/`BeforeToolExecute`/`AfterToolExecute`/`OnError`）。
  - 默认注册一套“安全/稳定/截断” hooks，允许业务工程通过 DI 替换/禁用。

- **Step B：把现有“零散修复”收敛进 Hook**
  - 例如把 `DeepSeekThinkingModeFixHandler` 这一类模型兼容修复纳入“Provider Hook”体系（按 model 启用）。

- **Step C：提供一份“官方 curated 配置”**
  - 类似 oh-my-opencode 的 “默认就能跑”的体验：
    - 默认 MCP 列表（按场景分组：code / docs / search）。
    - 默认 hooks（输出截断、上下文阈值、重试策略）。
    - 默认安全策略（危险工具默认关闭）。

---

### 8) 结语：借鉴的哲学层

oh-my-opencode 的美学在于：**把特殊情况消灭在默认策略里**——稳定性与可控性不是“用户自己小心”，而是系统默认提供的护栏。  
Aevatar 若补齐 Harness/Hooks 这一层，会天然把 “Agent Framework（底座）” 推向 “Agent Product（可直接交付）”。


