# Claude Code SDK（已迁移为 Claude Agent SDK）调研报告（面向 Aevatar Agent Framework 集成）

> 结论先行：**如果目标是“在 Aevatar 内调用 Claude 模型”**，当前 repo 已经具备最短路径（`Aevatar.Agents.AI.LLMTornado` 支持 Anthropic/Claude）；**如果目标是“引入 Claude Code 那套子代理/插件生态做更强的工程化编排”**，更合理的方式是把 Claude Agent SDK 当作“外部编排层/开发运维助手”，通过 **MCP 或受控 API** 驱动 Aevatar，而不是把两套 Agent 框架硬塞到同一层。

---

## 1. 名称与定位澄清：Claude Code SDK → Claude Agent SDK

- **现象层（你搜到的关键词）**：Claude Code SDK
- **本质层（官方新名）**：Claude Agent SDK（官方文档给出了迁移/SDK overview、subagents、plugins 等内容）

参考（官方）：
- `https://docs.claude.com/zh-CN/docs/claude-code/sdk`
- `https://docs.claude.com/zh-CN/docs/agent-sdk/overview`

---

## 2. Claude Agent SDK 核心能力（按“能解决什么问题”来分）

### 2.1 子代理（Subagents）：把复杂任务拆成“职责明确的专门代理”

- **形式**：在工程目录内用 Markdown + YAML front matter 定义子代理（官方给出 `.claude/agents/` 约定），强调“描述字段清晰以便自动调用”。
- **价值**：
  - 独立上下文（降低主线程 token 压力）
  - 可并行（加速复杂流程）
  - 专门化（更稳定的工具选择与输出风格）

参考（官方）：
- `https://docs.claude.com/zh-CN/docs/claude-code/sdk/subagents`

### 2.2 插件（Plugins）：把扩展能力做成可共享的“插件包”

- 插件可以向会话注入：**slash commands / agents / skills / hooks / MCP servers** 等（官方描述）。
- 适合做团队级复用：统一编码规范、PR 审查流程、故障排查 runbook、统一的外部系统连接。

参考（官方）：
- `https://docs.claude.com/zh-CN/docs/agent-sdk/plugins`

### 2.3 MCP（Model Context Protocol）：把“外部工具/数据源”标准化接入

- Claude Agent SDK 把 MCP 当作一等公民来扩展工具生态。

参考（官方/协议）：
- Claude 侧 SDK overview：`https://docs.claude.com/zh-CN/docs/claude-code/sdk/sdk-overview`
- MCP 协议站点：`https://modelcontextprotocol.io/`

### 2.4 生产化能力：托管/沙箱建议、成本跟踪等

- 官方提到生产环境建议（例如沙箱容器隔离）与成本跟踪页面。

参考（官方）：
- Hosting：`https://docs.claude.com/zh-CN/api/agent-sdk/hosting`
- Cost tracking：`https://docs.claude.com/zh-CN/docs/agent-sdk/cost-tracking`

---

## 3. SDK 形态与安装方式（当前能确认的）

> 下面是基于官方迁移/overview 页面整理出来的“形态与包名线索”。由于当前调研未在本地拉取 npm/pypi 元数据，**许可证/精确版本号**建议在落地前再做一次最终核验。

### 3.1 形态（官方描述）

- **Headless / 无头模式**（用于服务端/CI/自动化流水线）
- **TypeScript SDK**
- **Python SDK**

参考（官方）：
- `https://docs.claude.com/zh-CN/docs/agent-sdk/overview`

### 3.2 迁移/安装线索（官方迁移文档中出现的包名）

- Node/TS：从旧包迁移到新包的指引（卸载旧包、安装新包、更新 import）
- Python：从旧导入路径迁移到新导入路径

参考（官方）：
- `https://docs.claude.com/zh-CN/docs/claude-code/sdk`

### 3.3 鉴权

- 典型方式：环境变量 `ANTHROPIC_API_KEY`

参考（官方）：
- `https://docs.anthropic.com/zh-CN/docs/get-started`

---

## 4. 结合当前 repo：Aevatar 已具备哪些“同构能力”

### 4.1 Aevatar 的 AI 分层与可插拔 Provider

在本 repo 中：
- `Aevatar.Agents.AI.Core`：AI Agent 基类、工具循环、Hook/Harness、技能系统、MCP Client 等统一入口。
- Provider 以“适配器模块”形式存在（隔离外部依赖）：
  - `Aevatar.Agents.AI.MEAI`：基于 `Microsoft.Extensions.AI` 的 OpenAI-compatible/DeepSeek 等适配。
  - `Aevatar.Agents.AI.LLMTornado`：基于 LlmTornado 的多厂商适配，**包含 Anthropic/Claude**。

### 4.2 Aevatar 已经内置 MCP Client（关键契合点）

你们已经在 `AI.Core` 内集成了 **官方 MCP C# SDK（ModelContextProtocol.Core）**，能：
- 通过 `npx/uvx` 启动本地 MCP server（stdio）
- 连接远程 MCP server（HTTP/SSE）
- 把 MCP tool 转成 Aevatar 的 `ToolDefinition`，融入统一 tool-loop

这意味着：**不引入 Claude Agent SDK 也能复用 MCP server 生态**（GitHub、filesystem、Context7 等）。

参考（repo 内文档）：
- `src/Aevatar.Agents.AI.Core/WithTool/MCP/README.md`

### 4.3 Aevatar 的 Agent Skills 与 Claude Subagents：形式几乎同构

Aevatar 已有：
- `SKILL.md`（YAML front matter + 正文 SOP）
- `skills_list/skills_load` 按需加载
- `allowed-tools` 作为**硬约束**（不仅是提示词字段）

这和 Claude 的“子代理/技能以 Markdown + YAML 管理”的思路高度一致。差异在于：
- Claude 是“子代理定义”，偏“代理实例/角色”
- Aevatar 是“技能定义”，偏“流程/规范（指导模型）”

参考（repo 内文档）：
- `docs/AGENT_SKILLS_GUIDE.md`

---

## 5. 集成目标拆解（先问清楚你要什么）

把“集成 Claude Code SDK”拆成两类完全不同的问题：

1) **我要在 Aevatar 里用 Claude 模型**（模型接入问题）
2) **我要把 Claude Code/Agent SDK 的工程化代理体系引入工作流**（编排与工具生态问题）

如果不先分清，容易把两套系统的职责搅成一团（重复的 tool-loop、重复的权限系统、重复的 skill/subagent 组织方式）。

---

## 6. 集成方案选型（推荐从简单到复杂）

### 方案 A：只接入 Claude 模型（推荐作为第一步）

**做法**：使用现有 `Aevatar.Agents.AI.LLMTornado` provider，配置 `providerType = anthropic/claude` 与模型名即可。

- **优点**：纯 .NET、改动小、贴合现有 `AIGAgentBase` 工具系统与事件架构。
- **缺点**：拿不到 Claude Agent SDK 的“插件/子代理工程化运行时”；但 Aevatar 本身已覆盖大量能力（skills + MCP + hooks）。

适用：你要的是“Claude 更强的推理/写作/代码能力”，并让它在 Aevatar 的 Event/Tool 体系中干活。

### 方案 B：Aevatar 继续做主框架，复用 MCP server 生态（推荐作为第二步）

**做法**：保持 Aevatar 的 tool-loop；通过 `ToolManager.RegisterMCPServerViaNpxAsync/…` 接入 MCP servers。

- **优点**：与 Claude Agent SDK 的 MCP 生态“同协议”，但不会引入另一个 agent runtime。
- **缺点**：如果你强依赖 Claude Agent SDK 的“插件体系/子代理调度策略”，这条路只能“自己实现类似策略”。

适用：你主要想要的是“工具生态与外部系统连接”，而不是 Claude Agent SDK 的编排框架。

### 方案 C：Claude Agent SDK 作为“外部编排层/开发运维助手”（推荐作为第三步）

**做法**：
- Claude Agent SDK（Node/Python/Headless）跑在 sidecar / CI / 运维容器里
- 通过 MCP 或受控 API（HTTP/gRPC）调用 Aevatar
- Aevatar 仍然用 Protobuf event/state + actor runtime 保持一致性

建议职责划分：
- Claude Agent SDK：做“计划/拆解/调用工具/生成变更”
- Aevatar：做“领域执行/状态机/事件溯源/长生命周期 agent”

适用：你要的是一套“工程化编码/运维代理”来驱动 Aevatar 系统（例如自动生成 agent skill、自动跑测试、自动分析 trace、自动做 PR）。

> 端口提醒：repo 规则禁止使用 `:5000`；如需要 sidecar，`5678` 仅作为推荐示例端口（也可以用任意未占用端口）。

### 方案 D：把 Claude Agent SDK 当作 Aevatar 的 LLMProvider（不推荐，除非强需求）

**做法**：写一个新的 `IAevatarLLMProvider`，把 Aevatar 的 `AevatarLLMRequest/FunctionCall` 映射到 Claude Agent SDK 的请求/工具体系。

- **缺点**：
  - 两套 tool-loop 叠加，复杂度指数增长
  - 语义映射困难（函数调用 schema、tool call id、streaming、reasoning_content 等）
  - 运维更重（多语言运行时 + 可靠性边界）

仅当你“必须”使用 Claude Agent SDK 的内部调度能力，并且愿意承担长期维护成本，才考虑。

---

## 7. 推荐落地路线图（结合本 repo 的最小闭环）

### Phase 0（1 天）：确认“Claude 模型可用”与最小 Demo

- 用 `LLMTornado` provider 配好 Anthropic/Claude（仅配置，不改架构）
- 跑一个现有 `AIGAgentBase` demo（或在现有应用里开一个 endpoint）验证：
  - 普通对话
  - tool-loop（内置 tool + MCP tool）

### Phase 1（2~3 天）：把“工程知识”用 Agent Skills 组织起来（你们已经有）

- 把关键 SOP 写成 `SKILL.md`（优先：故障排查/代码规范/发布流程）
- 用 `allowed-tools` 把每个 SOP 的工具面缩到最小（执行层硬约束）

### Phase 2（可选，3~5 天）：如果确实需要 Claude Agent SDK 的插件/子代理生态

- 让 Claude Agent SDK 跑 headless（CI/sidecar）
- 为 Aevatar 暴露一个“受控入口”（二选一）：
  - **MCP server**：把 Aevatar 的关键动作做成 MCP tools（publish event、query state、start workflow…）
  - **gRPC/HTTP API**：用 Protobuf 定义跨边界请求/响应（更贴合本 repo“跨边界 Protobuf”的铁律）
- Claude 侧用插件/子代理把这些入口封装成稳定工作流

---

## 8. 风险与注意事项（要提前说清）

- **重复造轮子风险**：Aevatar 已有 tool-loop/skills/MCP；引入 Claude Agent SDK 时要避免两套系统同层竞争。
- **权限与安全**：
  - Aevatar 默认对危险工具是关闭的（`AllowDangerousTools=false`），保持这个默认是正确的。
  - Claude Agent SDK 官方也强调权限控制与沙箱运行；两边策略要对齐，不要互相“放水”。
- **多语言运行时运维成本**：Node/Python sidecar 会引入部署复杂度（镜像、依赖、升级、观测）。
- **协议边界**：事件/状态仍要坚持 Protobuf；外部工具协议（MCP/HTTP）要有明确的输入校验与审计日志。

---

## 9. 参考链接

- Claude Code SDK / 迁移到 Claude Agent SDK：`https://docs.claude.com/zh-CN/docs/claude-code/sdk`
- Claude Agent SDK Overview：`https://docs.claude.com/zh-CN/docs/agent-sdk/overview`
- Subagents：`https://docs.claude.com/zh-CN/docs/claude-code/sdk/subagents`
- Plugins：`https://docs.claude.com/zh-CN/docs/agent-sdk/plugins`
- SDK overview：`https://docs.claude.com/zh-CN/docs/claude-code/sdk/sdk-overview`
- Hosting：`https://docs.claude.com/zh-CN/api/agent-sdk/hosting`
- Cost tracking：`https://docs.claude.com/zh-CN/docs/agent-sdk/cost-tracking`
- MCP 协议：`https://modelcontextprotocol.io/`
- 本 repo MCP 集成说明：`src/Aevatar.Agents.AI.Core/WithTool/MCP/README.md`
- 本 repo Agent Skills 指南：`docs/AGENT_SKILLS_GUIDE.md`


