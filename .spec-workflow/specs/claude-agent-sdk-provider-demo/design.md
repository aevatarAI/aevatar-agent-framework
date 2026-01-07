# Design Document

## Overview

本 Demo 的核心目标：用 **离线可复现** 的方式，让读者一眼看懂 `ProviderType = "claude_agent_sdk"` 的独特价值（`.claude` 项目级配置 / plugins / 权限收敛 / 进程隔离 / streaming marker 协议），并通过“对照组”展示其与常规 provider（直接 LLM API 调用模型）的边界差异。

关键设计原则：
- **不依赖真实 LLM / 不访问外网**：默认使用仓库内的 mock runner（进程外）。
- **差异可解释**：输出必须显式标注差异来自 `.claude`/plugins/allowedTools/streaming，而不是只给两段文本。
- **不叠加 tool-loop**：Demo 必须体现 `claude_agent_sdk` provider 不映射 Aevatar tools（忽略 `Functions`、不返回 `AevatarFunctionCall`）。

## Steering Document Alignment

> 注：当前 `.spec-workflow/steering/` 目录为空，本设计以仓库规则（`AGENTS.md`、Port Policy、安全默认策略）与现有 examples 风格为准。

### Technical Standards (tech.md)

- **Safe by default**：demo 的 mock runner 默认最小权限；只有 `allowedTools` 明确允许才会进行文件读写。
- **No secrets hygiene**：不提交 secrets；不在输出/日志中打印 `ANTHROPIC_API_KEY`。
- **Port policy**：demo 不启动任何监听服务；不出现 `:5000` 示例；如未来 sidecar（非本规格）默认建议 `:5678`。

### Project Structure (structure.md)

- 遵循 `examples/*Demo/` 的组织方式（参考 `examples/LlmTornadoDemo`）：
  - `Program.cs` + `README.md` + `appsettings*.json`
  - 需要运行态资源的文件用 `<CopyToOutputDirectory>` 输出到 bin 目录
- Demo 内部再按职责拆目录：`runner/`、`plugins/`、`demo_project/`（含 `.claude/`）分离，避免文件堆一层。

## Code Reuse Analysis

### Existing Components to Leverage

- **`ClaudeAgentSdkProvider`**（已实现）：作为 demo 的主角 provider，不重复造轮子。
- **`LLMProvidersConfig` + `AddAevatarLLMTornado()`**：复用现有 provider factory + DI 方式（参考 `examples/LlmTornadoDemo/Program.cs`）。
- **`AevatarLLMRequest/AevatarLLMResponse/AevatarLLMToken`**：统一请求/响应与 streaming token 的输出格式。
- **marker 协议解析**：复用 `ClaudeAgentSdkProtocol` 的约定（stream/output markers）。

### Integration Points

- **Demo Host/DI**：使用 `Host.CreateDefaultBuilder` + `services.Configure<LLMProvidersConfig>` + `services.AddAevatarLLMTornado()`。
- **Provider 选择**：从 `LLMProviders` 配置中获取两个 `claude_agent_sdk` provider 实例（minimal / full）。

## Architecture

### Directory layout (planned)

```
examples/ClaudeAgentSdkProviderDemo/
├── ClaudeAgentSdkProviderDemo.csproj
├── Program.cs
├── README.md
├── appsettings.json
├── runner/
│   ├── mock_claude_agent_sdk_runner.mjs
│   └── README.md
├── plugins/
│   ├── plugin_append_tag.mjs
│   └── plugin_uppercase.mjs
└── demo_project/
    ├── .claude/
    │   └── demo_settings.json
    ├── data/
    │   └── context.txt
    └── output/
        └── (runtime generated files)
```

### High-level flow

```mermaid
flowchart TD
    A[Program.cs] --> B[Load LLMProvidersConfig]
    B --> C1[Create claude_agent_sdk_minimal provider]
    B --> C2[Create claude_agent_sdk_full provider]
    A --> D[Baseline provider (in-process)]
    C1 --> E[ClaudeAgentSdkProvider -> Runner process]
    C2 --> E
    E --> F[stdout markers -> stream tokens / final output]
    A --> G[Compare + explain differences]
```

### Modular Design Principles

- **Single File Responsibility**：
  - Demo app 只做“调用与展示对比”
  - runner 只做“读取 `.claude`/plugins/权限判断/输出 marker”
  - 插件只做“对输出的纯变换”
- **Component Isolation**：
  - `.claude` 读/写逻辑仅存在于 runner（模拟 Claude Agent SDK 的 project setting sources）
  - baseline provider 不读取 `.claude`、不加载 plugins，保证对照干净

## Components and Interfaces

### Demo App (`examples/ClaudeAgentSdkProviderDemo/Program.cs`)

- **Purpose:** 运行 3 组对比并打印解释：
  1) baseline provider（对照组）
  2) `claude_agent_sdk_minimal`（allowedTools 空，展示拒绝）
  3) `claude_agent_sdk_full`（允许 read/write，展示能力开启）
- **Interfaces:** N/A（console app）
- **Dependencies:** `Aevatar.Agents.AI.Abstractions`, `Aevatar.Agents.AI.LLMTornado`, `Microsoft.Extensions.Hosting`
- **Reuses:** `ILLMProviderFactory`（LLMTornado）、`ClaudeAgentSdkProvider`、`AevatarLLMRequest`

### Mock Runner (`runner/mock_claude_agent_sdk_runner.mjs`)

- **Purpose:** 离线模拟 Claude Agent SDK runner 的工程化行为：
  - 读取 stdin JSON
  - 读取 `projectRoot/.claude/demo_settings.json`
  - 加载 `plugins/*` 并对输出做变换
  - 根据 `allowedTools` 决定是否允许读/写 demo_project 文件
  - 输出 streaming marker 与 final output marker
- **Dependencies:** Node builtin modules only（`fs`, `path`），不依赖 npm 安装

### Plugins (`plugins/*.mjs`)

- **Purpose:** 用最小实现展示 “插件能改变输出”：
  - `plugin_uppercase`: 将输出 upper-case（可观察的 deterministic 变换）
  - `plugin_append_tag`: append 一个固定 tag（展示可组合性）
- **Contract:** runner 以约定方式加载（例如导出 `transform(text, ctx)`）

### Baseline Provider (in-process)

- **Purpose:** 作为“普通 provider”的对照：
  - 只使用 request 的 system/messages/user prompt
  - 不读取 `.claude`、不加载 plugins、无 runner 权限系统
- **Implementation note:** Demo 内实现一个极简 `IAevatarLLMProvider`（返回 deterministic 文本），避免外网/真实 key。

## Data Models

### Runner stdin request (JSON)

最小字段集（与当前 `ClaudeAgentSdkProvider` payload 对齐即可）：

- `stream`: bool
- `systemPrompt`: string
- `messages`: array of `{ role, content }`
- `projectRoot`: string
- `plugins`: string[]
- `allowedTools`: string[]
- `metadata`: object（用于 demo 标注）

### Runner stdout markers

- `AEVATAR_AGENT_SDK_STREAM:{text}`（可多行）
- `AEVATAR_AGENT_SDK_OUTPUT:{ "content": "..." }`

## Error Handling

### Error Scenarios

1. **runner 不存在 / node 不可执行**
   - **Handling:** Demo 捕获异常并输出 “缺少 node / runner 路径错误” 的修复提示
   - **User Impact:** baseline 仍可跑；claude_agent_sdk 路径给出清晰失败原因

2. **plugin 加载失败**
   - **Handling:** runner 直接失败并写 stderr（demo 输出 stderr tail）
   - **User Impact:** 明确哪一个 plugin 路径/导出不符合约定

3. **权限拒绝（allowedTools 缺失）**
   - **Handling:** runner 输出可读的拒绝信息（不崩溃）
   - **User Impact:** 看到 “默认最小权限” 的效果，以及如何开启

4. **streaming 不可用**
   - **Handling:** runner 不输出 stream marker 时，provider 自动降级为单 chunk
   - **User Impact:** Demo 输出说明“发生了 streaming 降级”

## Testing Strategy

### Unit Testing

- Demo runner/插件本身不强制写单测（示例工程），但需保证输出 deterministic、无外网依赖。

### Integration Testing

- `dotnet run` 可执行并打印 3 组对照输出（baseline / minimal / full）。
- 验证 `claude_agent_sdk` provider 的 streaming 路径能逐 chunk 输出。

### End-to-End Testing

- 开发者按 README 步骤运行，能在 1 分钟内看懂差异点并复现输出。


