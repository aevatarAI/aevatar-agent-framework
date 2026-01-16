# Design Document

## Overview

本设计在 `src/Aevatar.Agents.AI.LLMTornado` 内新增一个专用的 `IAevatarLLMProvider` 实现：**`ClaudeAgentSdkProvider`**，用于以 **进程外 headless runner** 的方式调用 **Claude Agent SDK**（Node/Python 运行形态），并将输出映射回 Aevatar 的 `AevatarLLMResponse` / `AevatarLLMToken`。

关键设计决策：
- **独立 Provider，不污染 `LLMTornadoProvider`**：Claude Agent SDK 属于“代理编排运行时”，与 `LlmTornado` 的“模型 API 适配”语义不同，必须隔离。
- **不叠加 tool-loop**：Claude Agent SDK 内部可使用其自身工具生态；Aevatar 侧不参与 function calling，不返回 `AevatarFunctionCall`，避免双循环。
- **runner 协议最小化**：MVP 只保证“输入 prompt → 输出文本”，支持 streaming best-effort；配置与权限通过 `ProviderSpecificSettings` 传入 runner。

## Steering Document Alignment

### Technical Standards (tech.md)

- **Safe by default**：Claude Agent SDK 的权限/可用工具默认收敛；只有在 provider 配置显式允许时才启用对应能力（文件写/命令执行/MCP 等）。
- **Secrets hygiene**：`ANTHROPIC_API_KEY` 等密钥只来自环境变量或 secrets 配置；不得写入仓库、不得打印到日志。
- **Port policy**：本集成默认不启用任何监听端口；若未来扩展为 sidecar server，示例/默认端口不得使用 `:5000`，优先 `:5678`。
- **Best-effort + bounded**：进程输出读取必须有界且持续 drain（避免 pipe 塞满导致子进程无法退出）；超时/取消必须能及时退出等待，避免卡死。

### Project Structure (structure.md)

- 新增代码位于 `src/Aevatar.Agents.AI.LLMTornado/` 下，遵守：
  - 单文件 ≤ 800 行；超过则拆分为 `*Runner.cs` / `*Config.cs` / `*Provider.cs`。
  - 单目录 ≤ 8 文件；如需示例 runner（Node/Python）则放在独立子目录，并附简短 README。

## Code Reuse Analysis

### Existing Components to Leverage

- **`AevatarLLMProviderBase`**（`src/Aevatar.Agents.AI.Abstractions/LLMProvider/AevatarLLMProviderBase.cs`）  
  复用其超时、重试、熔断器等韧性封装；`ClaudeAgentSdkProvider` 只实现纯粹的“调用 runner”。

- **`LLMTornadoProviderFactory` / `LLMProviderFactoryBase`**  
  复用 provider factory 的配置装配方式：由 `LLMProviderConfig.ProviderType` 决定创建哪一个 `IAevatarLLMProvider`。

- **`DotNetFileSkillRunner`**（`src/Aevatar.Agents.AI.Core/Tool/Tools/CustomTools/DotNetFileSkillTool.cs` 内部）  
  复用其关键经验：
  - `ProcessStartInfo` + `RedirectStandard{In,Out,Error}`
  - 超时/取消时 `Kill(entireProcessTree:true)`
  - stdout/stderr **持续 drain** 即使超过上限也不停止读取（避免死锁）
  - 输出中通过 marker 提取 JSON 的 best-effort 解析策略

### Integration Points

- **`LLMTornadoProviderFactory.CreateProvider`**：当 `ProviderType == "claude_agent_sdk"` 时，创建 `ClaudeAgentSdkProvider`；否则走原来的 `LlmTornado` 路径（OpenAI/Anthropic/Gemini/…）。
- **`LLMProviderConfig.ProviderSpecificSettings`**：承载 runner 相关参数（command/args/workingDir/projectRoot/plugins/permissions 等），避免新增全局 Options 类型。

## Architecture

### High-level flow

```mermaid
flowchart TD
  A[AIGAgentBase] -->|BuildLLMRequest| B[ILLMProviderFactory]
  B -->|ProviderType=claude_agent_sdk| C[ClaudeAgentSdkProvider]
  C --> D[ClaudeAgentSdkRunner (Process)]
  D -->|stdio JSON/stream| E[Claude Agent SDK (Node/Python)]
  E --> D --> C --> A
```

### Modular Design Principles

- **Single File Responsibility**：
  - `ClaudeAgentSdkProvider.cs`：实现 `GenerateCoreAsync` / `GenerateStreamCoreAsync`，只做请求映射与结果映射。
  - `ClaudeAgentSdkRunner.cs`：只负责进程启动、I/O、超时/取消、stdout/stderr 采集与解析。
  - `ClaudeAgentSdkConfig.cs`：只负责从 `ProviderSpecificSettings` 解析配置（含默认值与校验）。

- **No tool-loop coupling**：
  - 输入侧：忽略 `AevatarLLMRequest.Functions`（不把 Aevatar tools 传给 Claude Agent SDK）
  - 输出侧：永不返回 `AevatarFunctionCall`

## Components and Interfaces

### Component 1 — `ClaudeAgentSdkProvider` (C#)

- **Purpose:** 以 `IAevatarLLMProvider` 形态把 Claude Agent SDK 接入 Aevatar。
- **Interfaces:**
  - `GenerateCoreAsync(AevatarLLMRequest, CancellationToken) -> AevatarLLMResponse`
  - `GenerateStreamCoreAsync(AevatarLLMRequest, CancellationToken) -> IAsyncEnumerable<AevatarLLMToken>`
  - `GetModelInfoAsync(...)`：返回 `SupportsFunctions=false`（强调不走 tool-loop）
- **Dependencies:**
  - `ClaudeAgentSdkRunner`
  - `ILogger`
  - 解析后的 `ClaudeAgentSdkProviderConfig`
- **Reuses:** `AevatarLLMProviderBase` 的韧性封装

### Component 2 — `ClaudeAgentSdkRunner` (C#)

- **Purpose:** 统一封装子进程执行、协议 I/O、资源清理。
- **Interfaces:**
  - `ExecuteAsync(requestJson, CancellationToken) -> (stdout, stderr, exitCode, timedOut)`
  - `ExecuteStreamingAsync(requestJson, CancellationToken) -> IAsyncEnumerable<string>`（best-effort）
- **Dependencies:**
  - `System.Diagnostics.Process`
  - stdout/stderr 读取与限制（持续 drain）

### Component 3 — Runner Script（Node/Python，示例）

> 注意：该脚本属于 **部署时依赖**；本仓库不负责自动安装依赖。

- **Purpose:** 在 runner 进程内调用 Claude Agent SDK，读取 stdin 的请求，输出 stdout 的响应（支持 streaming）。
- **Protocol (MVP):**
  - stdin：单个 JSON（UTF-8）
  - stdout：支持两种模式
    - 非 streaming：输出一段文本，并在末尾追加 `AEVATAR_AGENT_SDK_OUTPUT:{json}` marker
    - streaming：逐行输出 `AEVATAR_AGENT_SDK_STREAM:{text}`，最后输出 `AEVATAR_AGENT_SDK_OUTPUT:{json}`

## Data Models

### Model 1 — `ClaudeAgentSdkProviderConfig`（从 ProviderSpecificSettings 派生）

```
- runnerCommand: string            // e.g. "node" or "python"
- runnerArgs: string[]             // e.g. ["path/to/runner.mjs"]
- workingDirectory: string?        // process cwd (default: projectRoot or Environment.CurrentDirectory)
- projectRoot: string?             // where to load .claude/* (default: workingDirectory)
- plugins: string[]?               // optional plugin dirs
- settingSources: string[]?        // e.g. ["project","user"] (best-effort pass-through)
- allowedTools: string[]?          // Claude Agent SDK permission model (best-effort pass-through)
- permissionMode: string?          // e.g. "default" / "ask" / ... (best-effort pass-through)
- env: map<string,string>?         // extra env vars (do NOT allow secrets in plain text by default)
- maxOutputChars: int              // cap collected output; still drain pipe
- timeoutMs: int                   // per-call hard timeout (align with LLMProviderConfig.TimeoutMilliseconds)
```

### Model 2 — Runner 请求/响应（JSON）

```
request:
- prompt: string
- model: string?
- temperature: number?
- maxTokens: number?
- projectRoot: string?
- plugins: string[]?
- settingSources: string[]?
- allowedTools: string[]?
- permissionMode: string?
- metadata: { requestId?: string, provider?: string, ... }?

response:
- success: bool
- content: string
- stderr?: string
- usage?: { promptTokens?: number, completionTokens?: number, totalTokens?: number }
```

> 注：如后续发现需要更强 schema 约束，可在实现阶段升级为 Protobuf 协议；MVP 先用 JSON 保持最小复杂度。

## Error Handling

### Error Scenarios

1. **Runner 不存在/无法启动**
   - **Handling:** `CreateProvider` 或首次调用时直接抛出 `InvalidOperationException`（含缺失项与建议：安装 node/python、检查脚本路径）
   - **User Impact:** 立刻失败，错误信息可自助修复；不允许挂起

2. **Runner 退出码非 0 / 输出不可解析**
   - **Handling:** 返回 `AevatarLLMResponse.Content` 为可读错误摘要（best-effort），并在日志记录 stderr（截断+脱敏）
   - **User Impact:** 请求失败可定位；不会触发 Aevatar tool-loop

3. **超时/取消**
   - **Handling:** 终止等待并 `Kill(entireProcessTree:true)`（best-effort）；取消向上抛出 `OperationCanceledException`
   - **User Impact:** 不会卡住 actor；上层可重试/降级

4. **Streaming 不可用**
   - **Handling:** 自动降级为一次性输出（把完整内容作为单个 chunk yield）
   - **User Impact:** 仍可用，但体验略差；日志标注降级原因

## Testing Strategy

### Unit Testing

- **配置解析**：`ProviderSpecificSettings` -> `ClaudeAgentSdkProviderConfig`（含默认值、缺失项报错）
- **协议解析**：stdout marker 提取 JSON、stream line 提取文本（含噪声日志场景）
- **取消/超时**：模拟子进程长时间不退出，验证能 kill 并返回/抛出正确异常（best-effort）

### Integration Testing

- **Fake runner**：在测试项目内提供一个可执行脚本/小程序（不依赖网络）：
  - 非 streaming：回显固定 JSON
  - streaming：按行输出若干 token，再输出最终 marker
- 断言：
  - `AevatarFunctionCall` 永远为 null
  - `GenerateStreamAsync` 能逐步 yield（或正确降级）

### End-to-End Testing

- 在示例工程中给出一份 `appsettings.secrets.json` 的 provider 配置，说明如何启用 `claude_agent_sdk`，并演示 `.claude` 目录下的 subagents/plugins 生效。


