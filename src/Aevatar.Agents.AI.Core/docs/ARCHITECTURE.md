# Aevatar.Agents.AI.Core — 架构说明

## 目标

- **单一入口**：`Aevatar.Agents.AI.Core` 是 AI Agent 能力的唯一工程入口。
- **工具能力内置**：`AIGAgentBase` 内置 Function Calling + Tool Call Loop；工具系统实现也随 `AI.Core` 一并发布。
- **清晰边界**：工具相关类型位于命名空间 `Aevatar.Agents.AI.Tool.*`，但它们 **由 `AI.Core` 程序集提供**（不再存在独立的 `Aevatar.Agents.AI.Tool` 工程）。

## 目录结构（关键子集）

```
src/Aevatar.Agents.AI.Core/
├── AIGAgentBase.cs
├── AIGAgentBase.Chat.cs                  # Chat / Streaming（从 AIGAgentBase.cs 拆出，降低主文件体积）
├── AIGAgentBase.History.cs               # History/Compaction/Summary（Layer 1+2），对外行为不变，仅拆分职责
├── History/HistoryRuntime.cs             # History runtime（锁/compaction/summary 细节从 AIGAgentBase 抽离）
├── Telemetry/LlmCallInstrumentationScope.cs # LLM call logs + telemetry + stopwatch（从 ChatAsync 收敛）
├── AIGAgentBase.Tools.cs                 # Tool system（Registration/Caches/Instruction block + LoggerAdapter）
├── AIGAgentBase.Tools.Loop.cs            # Tool call loop + tool messages + ToolExecutionRequestEvent handler
├── AIGAgentBase.Tools.Policy.cs          # Tool policy + allowlist guard（defense in depth）
├── RoleAIGAgent.cs                       # 通用 role 驱动 Agent（framework-level）
├── RoleAgentFactory.cs                   # role YAML 装配入口（GlobalAgentYamlRegistry + AgentYamlConfigApplier）
├── Tooling/ToolingRuntime.cs             # Tooling runtime（manager init + caches）
├── Tooling/ToolingRuntime.Loop.cs        # Tooling runtime（tool loop + allowlist enforcement）
├── Tooling/IToolingInitHost.cs           # Tooling init host interface（初始化专用）
├── Tooling/IToolingLoopHost.cs           # Tooling loop host interface（执行/发布专用）
├── AIGAgentBase.AgentSkills.cs           # SKILL.md 按需加载（skills_list/skills_load 工具 + 配置入口）
├── AIGAgentBase.AgentSkills.Discovery.cs # skills 发现/解析/YAML front matter/文件读取（best-effort + 调试可观测）
├── AgentSkills/AgentSkillsRuntime.cs     # AgentSkills runtime：roots/env + 扫描/解析/路径校验/IO 限幅（从 AIGAgentBase 抽离）
│   ├── AgentSkillsRuntime.SkillSearch.cs # find_helpful_skills/list_skills（embedding ranking + lexical fallback）
│   ├── AgentSkillsRuntime.ReadSkillDocument.cs # read_skill_document（glob + 资源目录限制）
│   ├── AgentSkillsRuntime.ResourceTools.cs # skills_files/skills_read_file/skills_run_python
│   ├── AgentSkillsRuntime.LoadTools.cs # skills_list/skills_load（含 dotnet-file tool import）
│   └── AgentSkillsRuntime.Serialization.cs # tool 输出 Struct 序列化 + 参数解析 helpers
├── Tool/Tools/BuiltIn/WebSearch/WebSearchProviderFactory.cs # WebSearch provider 组装（IConfiguration/Env → Provider），与 AIGAgentBase 解耦
├── AgenticRag/                           # Agentic RAG：有界循环基座（Plan/Retrieve/Synthesize/Critic）+ 证据/引用 + 可插拔检索
├── Hooks/                                # Hook/Harness：LLM/Tool 生命周期的横切治理（best-effort + 默认安全）
│   └── BuiltIn/                          # 内置 hooks（输出截断、上下文预算信号等）
├── Mcp/McpRuntime.cs                     # MCP auto-connect runtime（从 AIGAgentBase 抽离，best-effort）
├── Memory/MemoryStoreRuntime.cs          # MemoryStore/VectorIndex append runtime（从 AIGAgentBase 抽离，best-effort）
├── Llm/LlmRequestRuntime.cs              # LLM request 组装（prompt/history/allowlist/tools）
├── Llm/ILlmRequestHost.cs                # LLM request host interface（runtime context 适配）
├── Tooling/AIGAgentBase.ToolingRuntimeContext.cs # Tooling runtime context（单一职责）
├── Llm/AIGAgentBase.LlmRequestRuntimeContext.cs  # LLM request runtime context（单一职责）
├── Helpers/
├── Messages/
├── Tool/                             # 工具系统实现（原 WithTool）
│   ├── Abstractions/                     # ToolDefinition / IAevatarToolManager 等
│   ├── Tools/                            # AevatarToolManager + 内置/核心/自定义工具
│   ├── MCP/                              # Model Context Protocol 支持
│   └── tool_messages.proto               # Tool 相关事件/消息（Protobuf）
└── ai_messages.proto
```

## 依赖边界

- **对外**：业务工程只需要引用 `Aevatar.Agents.AI.Core`（即可获得工具/MCP 能力）。
- **对内**：工具系统代码位于 `Tool/` 目录，对应命名空间 `Aevatar.Agents.AI.Tool.*`。

## 开发规范（节选）

- Agent 创建必须走 `IGAgentFactory` / `AIGAgentFactory`（保证注入一致）。
- YAML 统一通过 `GlobalAgentYamlRegistry` + `AgentYamlConfigApplier`。
- best-effort 不得阻断主链路（MCP/skills/memory/websearch）。

## DI / 注入链路（节选）

AI Agent 的依赖注入由 `AIGAgentFactory` 统一负责：

- Store/Tooling 等依赖通过一组 Injector 注入到 Agent 实例上（best-effort）。
- Hook/Harness（可选）通过 **显式/类型安全** 注入（options + hooks），使横切能力不污染业务 Agent，且更易发现与调试。

### Memory 相关（扩展）

除 `IMemoryStore`、`IMemoryVectorIndex` 外，AI Agent 也可以（best-effort）获得：

- `IMemoryGraphStore`：当 Agent 声明可写属性 `MemoryGraphStore : IMemoryGraphStore` 时自动注入  
  用途：支持工具侧加载投影后的执行图（Layer 4.2 explainability），避免 host 代码耦合。

## 变更记录（WithTool → Tool）

- **Removed**：独立工程 `src/Aevatar.Agents.AI.Tool`（工程级别）。
- **Moved**：原 `WithTool/` 源码迁移至 `src/Aevatar.Agents.AI.Core/Tool/`，作为 `AI.Core` 的一部分编译。
- **Protobuf**：`tool_messages.proto` 由 `AI.Core` 统一生成代码（满足“跨边界类型必须 Protobuf”铁律）。

## 相关文档

- `docs/aigagentbase/README.md`：AIGAgentBase 专区入口（全量指南 + YAML policy + Review）
- `docs/aigagentbase/YAML_TOOL_POLICY.md`：YAML 工具策略与审计 hook
- `docs/HOOKS_HARNESS.md`：Hook/Harness 机制（生命周期、默认 hooks、禁用/扩展方式）

## Global Agent YAML（跨应用：~/.aevatar/agents/*.yaml）

### 目标

让任意基于 Aevatar 的应用（SRA / CognitiveMesh / Maker / Notebook / …）都能用**同一套约定**：

- 用 **role（抽象能力）** 标识 agent
- 用 `~/.aevatar/agents/{role}.yaml` 描述该 role 的默认配置
- 运行时按需加载并应用（可 fallback 到应用内默认实现）

### 关键类型（Framework-level）

- `Configuration/GlobalAgentYamlRegistry.cs`
  - 扫描与加载全局 YAML（`~/.aevatar/agents/*.yaml`）
  - 只负责“发现/读取”，不负责“应用到 agent”
- `Configuration/AgentYamlConfigApplier.cs`
  - 将 `AgentYamlConfig` 应用到 `AIGAgentBase`
  - 覆盖范围（best-effort）：
    - 模型参数：model/temperature/max_tokens/top_p/penalties/stop_sequences
    - system prompt：system_prompt/persona（并可附加 pinned skills 提示）
    - tools：作为 baseline tool allowlist（限制 tool schema + 执行）
    - skills：当 `skills:` 非空时自动启用 skills roots（默认 `~/.aevatar/skills`）并加入 skills 工具到 allowlist
- `RoleAIGAgent.cs` / `RoleAgentFactory.cs`
  - 框架层通用 role Agent + 工厂
  - 以 role YAML 装配 system prompt / tools / skills
- `AIGAgentBase.Chat.cs`
  - 新增 `SetFixedToolAllowlist()`：框架级 baseline tool allowlist（每次 BuildLLMRequest 都注入）

### Fallback 语义（必须保持）

- **YAML 不存在/解析失败**：框架能力应当 no-op，不影响应用内默认行为
- **YAML 缺省字段**：只覆盖显式提供的字段，未提供的字段保持当前值

## 架构决策

- RoleAIGAgent/RoleAgentFactory 上移至 AI.Core，统一 role YAML 语义，避免跨应用重复实现。

## 变更日志

- 2026-01-15：新增 RoleAIGAgent/RoleAgentFactory 作为 role YAML 的框架层入口。



