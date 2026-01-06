## ACP 调研结论（与 MCP 的关系）

### 先做一个“消歧”

业内/中文资料里 **ACP 这个缩写存在同名冲突**，至少常见两类含义：

- **Agentic Commerce Protocol（ACP, OpenAI × Stripe）**：面向“智能体购物/结账/支付委托”的商务协议。
- **Agent Communication Protocol（ACP, 泛指/论文）**：面向“智能体 ↔ 智能体”的通信协议（常被描述为 REST + 流式/多段消息等）。

因此 **ACP 不是 MCP 的进阶版本**，更像是“在不同层解决不同问题”的协议族：

- **MCP（Model Context Protocol）**：解决 *LLM 如何安全地发现并调用外部工具*（tools discovery + call）。
- **ACP（Commerce 或 Communication）**：解决 *智能体如何以标准方式完成某类业务协作*（商务流程）或 *跨系统互通*（agent-to-agent）。

### 对 Aevatar 来说，ACP 可能意味着什么

结合本仓库的定位（Actor Model + Event-Driven + Protobuf 跨边界），ACP 的价值通常在“跨系统边界”：

- **你想让 Aevatar Agent 调外部 ACP 生态**（作为客户端）：把 ACP 的远端能力“包装成 Tools”，让 LLM 用 tool-calling 驱动。
- **你想把 Aevatar Agent 暴露给外部 ACP 生态**（作为服务端）：做一个 HTTP Gateway，把 ACP 请求翻译成 Aevatar 内部事件（Protobuf），再把结果以 ACP 的响应/流式格式吐回去。

---

## 当前仓库已有的“接入点”（MCP/Tool 系统）

### MCP 已经是“一等公民”

仓库里 MCP 的集成路径是：

- `AIGAgentBase` 的 tool system：统一注册/缓存/执行 tools（支持 allowlist + dangerous gating）
- `src/Aevatar.Agents.AI.Core/WithTool/MCP/`：
  - `MCPClientWrapper`：对接官方 `ModelContextProtocol.Core` SDK（stdio/http/stream）
  - `MCPToolAdapter`：把 MCP tools 转成 Aevatar `ToolDefinition`
  - `MCPToolManagerExtensions`：`ToolManager.RegisterMCPServer...` 的便捷入口

示例可看：

- `examples/SkillsMCPUnifiedDemo/UnifiedAgent.cs`
- `examples/MCPToolDemo/MCPAgent.cs`

### 已有通用 HTTP 工具（可作为“最低成本 ACP 客户端”）

仓库内置了 `http_request` tool（`HttpRequestTool`），它可以对任意 HTTP API 发请求。

这意味着：**如果 ACP 的对接形态是 HTTP API**（无论是 commerce 还是 communication），你可以先用 `http_request` 做“最小可用集成”，再逐步演进到强类型/更安全的专用 ACP tools。

---

## 如何在本仓库集成 ACP（推荐路线）

### 路线 A（最快）：ACP ↔ MCP Bridge（如果你已有桥接服务）

如果你手上已有一个“把 ACP 能力暴露成 MCP tools”的 bridge（不管它内部怎么对 ACP 说话），那本仓库 **零新增协议代码**：

- 直接用 `MCPServerConfig.CreateHttpConfig(...)` 或 `CreateNpxConfig(...)`
- 然后 `ToolManager.RegisterMCPServerAsync(...)`

优点：复用现有 MCP 体系（discover tools + schema + call），最省心。  
缺点：依赖 bridge 的稳定性与安全性。

### 路线 B（可控）：新增 `WithTool/ACP`（把 ACP 做成一组一等 Tools）

当你确定 ACP 的“对象模型 + 端点/动作集合”后，推荐做一层专用适配，形态参考 `WithTool/MCP`：

- `ACPServerConfig`：baseUrl / auth / timeout / headers…
- `IACPClient`：对 ACP 的核心动作封装（例如：listAgents/sendMessage/stream… 或 productFeed/checkout…）
- `ACPToolAdapter`：把 ACP 动作映射为 Aevatar `ToolDefinition`（带参数 schema 与返回结构）
- `ACPToolManagerExtensions`：提供 `RegisterACPServerAsync(...)` 一键注册

优点：可控、可审计、可做更强的安全策略（例如对支付/下单类动作强制 `RequiresConfirmation`）。  
缺点：需要你选定“到底是哪一种 ACP”并落地协议细节。

### 路线 C（系统级）：ACP Gateway（把 Aevatar 暴露成 ACP 服务端）

当目标是“让外部 ACP 客户端/平台直接调用 Aevatar Agent 网络”，建议单独起一个 ASP.NET host：

- 入站：ACP HTTP 请求 / 流式连接
- 内部：翻译为 Aevatar 事件（Protobuf），通过 `PublishAsync` 或 runtime API 路由到目标 Agent
- 出站：把 Agent 的事件/输出再翻译回 ACP 的响应（必要时 SSE/stream）

优点：真正跨生态互通；边界清晰。  
缺点：工程量最大，需要明确 ACP 规范与安全模型（鉴权、幂等、审计、限流）。

---

## 下一步需要你确认的一句话

为了把“集成”落到具体代码/目录，我需要你确认你说的 ACP 是哪一个：

- **A)** Agentic Commerce Protocol（OpenAI × Stripe，电商/结账/支付委托）
- **B)** Agent Communication Protocol（agent-to-agent 通信；你希望接入/兼容外部 ACP 生态）

你回我 “A” 或 “B”，我就可以按对应路线把仓库里该加的模块/示例/配置与最小 PoC 代码直接补齐。


