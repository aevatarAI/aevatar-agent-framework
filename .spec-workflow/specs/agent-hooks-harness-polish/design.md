# Design Document

## Overview

本设计对现有 `AI.Core Hooks/Harness` 做两处打磨，目标是 **消灭隐式协议** 与 **提升注入可发现性**，并保持 best-effort 与 backward compatible：

1. **Typed Deny API**：在 `AevatarAgentHookContext` 上提供 `DenyTool(reason)`（以及可选的读取 API），把 “拒绝工具执行” 从字符串 metadata 约定收敛为强类型方法。
2. **Explicit Injection**：在 `AIGAgentFactory` 中以类型化方式注入 HookOptions + AdditionalHooks，避免 `AIAgentHookInjector` 通过反射写属性；同时确保 pipeline 缓存不阻碍注入生效。

## Steering Document Alignment

### Technical Standards (tech.md)

仓库当前没有 `.spec-workflow/steering/tech.md`；本设计遵循仓库既有标准：

- **跨边界类型必须 Protobuf**（本次不新增跨边界契约）。
- **best-effort**：hook/injection 失败不得阻塞主链路。
- **默认安全**：hook 不能扩权，deny 仅做“收敛型”能力。

### Project Structure (structure.md)

仓库当前没有 `.spec-workflow/steering/structure.md`；本设计遵循现有 AI.Core 组织：

- Hook API：`src/Aevatar.Agents.AI.Core/Hooks/AevatarAgentHookContext.cs`
- Hook 调用点：`src/Aevatar.Agents.AI.Core/AIGAgentBase.Hooks.cs`
- 注入入口：`src/Aevatar.Agents.AI.Core/AIGAgentFactory.cs`
- 文档：`src/Aevatar.Agents.AI.Core/docs/HOOKS_HARNESS.md`（同步更新）

## Code Reuse Analysis

### Existing Components to Leverage

- **Deny metadata keys**：`src/Aevatar.Agents.AI.Core/Utils/AIGAgentKeys.cs` 已集中定义 `HookDenyTool/HookDenyReason`
- **Tool deny 执行点**：`AIGAgentBase.ExecuteAllowedToolWithHooksAsync` 已基于上述 keys 实现“拒绝工具执行”
- **当前注入链**：`AIGAgentFactory` + `Helpers/AIAgentHookInjector`（本次将前者改成显式注入，后者退场/仅保兼容）

### Integration Points

- **Hook API**：`IAevatarAgentHook.BeforeToolExecuteAsync` → `context.DenyTool(reason)`
- **Tool 执行链**：`ExecuteAllowedToolWithHooksAsync` 读取 deny 决策（优先强类型 API，兼容旧 metadata）
- **DI 注入链**：`AIGAgentFactory.CreateGAgent` 负责把 options/hooks 注入到 `AIGAgentBase` 实例

## Architecture

### Modular Design Principles

- **Single Responsibility**：DenyTool API 只负责表达意图；执行拒绝仍由 AIGAgentBase 的 tool wrapper 完成
- **Backward Compatible**：旧 metadata 写法仍然生效；新 API 只是更优雅的入口
- **Cache Safety**：任何“注入 hooks/options”行为都必须让 pipeline 缓存失效（避免注入不生效）

## Components and Interfaces

### Component 1 — Typed DenyTool API on `AevatarAgentHookContext`

- **Purpose:** 用强类型方法表达“拒绝工具执行”，消灭字符串 key 协议的隐式约定
- **Interfaces:**
  - `void DenyTool(string? reason = null)`：写入 deny 标记与原因（使用 `AIGAgentKeys` 常量）
  - （可选）`bool IsToolDenied(out string? reason)`：读取 deny 决策（同时兼容旧 metadata）
- **Dependencies:** `AIGAgentKeys`（集中 keys，避免硬编码散落）
- **Reuses:** 复用现有 metadata 字典作为观测数据载体

### Component 2 — Explicit Hook Injection in `AIGAgentFactory`

- **Purpose:** 去反射，提高可发现性与可调试性
- **Interfaces:**
  - `AIGAgentBase` 提供 `internal` 注入方法（例如 `InjectHookOptions(...)` / `InjectAdditionalHooks(...)`），内部会清空 `_hookPipeline` 缓存
  - `AIGAgentFactory` 在创建 agent 后：
    - 解析 `IOptions<AevatarAgentHookOptions>`（优先）或直接解析 `AevatarAgentHookOptions`
    - 解析 `IEnumerable<IAevatarAgentHook>`（DI 默认可解析空集合）
    - 调用 `AIGAgentBase` 的 internal 注入方法
- **Dependencies:** `Microsoft.Extensions.Options`、现有 DI container
- **Reuses:** 复用 AIGAgentFactory 的统一注入时机（已有 ToolManager/MemoryStore 等注入模式）

### Component 3 — Docs refresh

- **Purpose:** 确保 repo 文档反映真实实现（不再描述反射注入）
- **Touch:**
  - `src/Aevatar.Agents.AI.Core/docs/HOOKS_HARNESS.md`：更新 deny 示例为 `ctx.DenyTool(reason)`；更新注入机制描述为 AIGAgentFactory 的显式注入

## Data Models

本规格不新增跨边界模型；仍使用既有：

- `AevatarAgentHookOptions`
- `AevatarAgentHookContext.Metadata`（用于 observability，不存 secrets）

## Error Handling

### Error Scenarios

1. **Scenario 1: Hook 调用 DenyTool 但 reason 为空**
   - **Handling:** 仍拒绝执行；返回默认 reason（例如 “Denied”），并保证不抛异常
   - **User Impact:** 工具调用失败但可理解

2. **Scenario 2: 注入失败（DI 不提供 options/hooks 或发生异常）**
   - **Handling:** best-effort：回退默认 options + 内置 hooks；agent creation 不失败
   - **User Impact:** 仅表现为缺少自定义 hooks 或未应用自定义预算

3. **Scenario 3: 注入发生在 pipeline 已构建之后**
   - **Handling:** internal 注入方法必须清空 pipeline 缓存，确保后续请求重建 pipeline
   - **User Impact:** 注入即时生效（下一次 LLM/tool 调用开始）

## Testing Strategy

### Unit Testing

- **DenyTool API**
  - hook 调用 `ctx.DenyTool("x")` 后，工具执行返回失败 `ToolExecutionResult` 且包含原因
  - 旧 metadata 写法仍兼容（确保不破坏现有 hook）
- **Injection**
  - DI 注册 `IAevatarAgentHook` 后，创建 `AIGAgentBase` 能触发 hook（覆盖 ChatAsync/ChatStreamAsync/Tool wrapper 任一入口）

### Integration Testing

- 运行 `test/Aevatar.Agents.AI.Core.Tests/` 全量测试，确保无回归

### End-to-End Testing

- 本规格不新增 E2E；保持单元/集成测试覆盖即可


