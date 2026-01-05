# Aevatar.Agents.AI.Core — 架构说明

## 目标

- **单一入口**：`Aevatar.Agents.AI.Core` 是 AI Agent 能力的唯一工程入口。
- **工具能力内置**：`AIGAgentBase` 内置 Function Calling + Tool Call Loop；工具系统实现也随 `AI.Core` 一并发布。
- **兼容性优先**：工具相关类型 **保留命名空间** `Aevatar.Agents.AI.WithTool.*`，但它们 **由 `AI.Core` 程序集提供**（不再存在独立的 `Aevatar.Agents.AI.WithTool` 工程）。

## 目录结构（关键子集）

```
src/Aevatar.Agents.AI.Core/
├── AIGAgentBase.cs
├── AIGAgentBase.Chat.cs                  # Chat / Streaming（从 AIGAgentBase.cs 拆出，降低主文件体积）
├── AIGAgentBase.History.cs               # History/Compaction/Summary（Layer 1+2），对外行为不变，仅拆分职责
├── AIGAgentBase.Tools.cs                 # Tool system（Registration/Caches/Instruction block + LoggerAdapter）
├── AIGAgentBase.Tools.Loop.cs            # Tool call loop + tool messages + ToolExecutionRequestEvent handler
├── AIGAgentBase.Tools.Policy.cs          # Tool policy + allowlist guard（defense in depth）
├── AIGAgentBase.AgentSkills.cs           # SKILL.md 按需加载（skills_list/skills_load 工具 + 配置入口）
├── AIGAgentBase.AgentSkills.Discovery.cs # skills 发现/解析/YAML front matter/文件读取（best-effort + 调试可观测）
├── Hooks/                                # Hook/Harness：LLM/Tool 生命周期的横切治理（best-effort + 默认安全）
│   └── BuiltIn/                          # 内置 hooks（输出截断、上下文预算信号等）
├── Helpers/
├── Messages/
├── WithTool/                             # 工具系统实现（原 AI.WithTool）
│   ├── Abstractions/                     # ToolDefinition / IAevatarToolManager 等
│   ├── Tools/                            # AevatarToolManager + 内置/核心/自定义工具
│   ├── MCP/                              # Model Context Protocol 支持
│   └── tool_messages.proto               # Tool 相关事件/消息（Protobuf）
└── ai_messages.proto
```

## 依赖边界

- **对外**：业务工程只需要引用 `Aevatar.Agents.AI.Core`（即可获得工具/MCP 能力）。
- **对内**：工具系统代码位于 `WithTool/` 目录，但仍使用 `Aevatar.Agents.AI.WithTool.*` 命名空间以避免破坏上层代码。

## DI / 注入链路（节选）

AI Agent 的依赖注入由 `AIGAgentFactory` 统一负责：

- Store/Tooling 等依赖通过一组 Injector 注入到 Agent 实例上（best-effort）。
- Hook/Harness（可选）通过 **显式/类型安全** 注入（options + hooks），使横切能力不污染业务 Agent，且更易发现与调试。

### Memory 相关（扩展）

除 `IMemoryStore`、`IMemoryVectorIndex` 外，AI Agent 也可以（best-effort）获得：

- `IMemoryGraphStore`：当 Agent 声明可写属性 `MemoryGraphStore : IMemoryGraphStore` 时自动注入  
  用途：支持工具侧加载投影后的执行图（Layer 4.2 explainability），避免 host 代码耦合。

## 变更记录（合并 WithTool）

- **Removed**：独立工程 `src/Aevatar.Agents.AI.WithTool`（工程级别）。
- **Moved**：原 WithTool 源码迁移至 `src/Aevatar.Agents.AI.Core/WithTool/`，作为 `AI.Core` 的一部分编译。
- **Protobuf**：`tool_messages.proto` 由 `AI.Core` 统一生成代码（满足“跨边界类型必须 Protobuf”铁律）。

## 相关文档

- `docs/HOOKS_HARNESS.md`：Hook/Harness 机制（生命周期、默认 hooks、禁用/扩展方式）


