# AI.Core Hooks / Harness

本页描述 `Aevatar.Agents.AI.Core` 的 Hook/Harness 机制：用于把“稳定性/上下文治理/工具输出治理/兼容性修复”等横切逻辑从业务 Agent 中抽离出来，并以 **可插拔 + 可禁用 + best-effort** 的方式运行。

## 设计原则

- **best-effort**：Hook 抛异常只记录日志，不阻塞主链路（LLM 调用 / Tool 执行继续）。
- **默认安全**：Hook 不能扩权：
  - 不能绕过 `AllowInternalTools` / `AllowDangerousTools`
  - LLM 请求在 `BeforeLLMRequest` hooks 之后会重新执行一次 `AttachToolsToRequest`，确保工具可见性仍受策略约束
- **确定性**：按 `Priority`（小的先）+ `Name` 排序执行。
- **可治理**：支持通过 `DisabledHooks` 禁用指定 hooks（类似 oh-my-opencode 的 `disabled_hooks`）。

## Hook 生命周期（阶段）

接口：`src/Aevatar.Agents.AI.Core/Hooks/IAevatarAgentHook.cs`

- `OnSessionStartAsync`（会话开始）
- `OnSessionEndAsync`（会话结束）
- `OnStopAsync`（agent loop 停止）
- `BeforeLLMRequestAsync`
- `AfterLLMResponseAsync`
- `BeforeToolExecuteAsync`
- `AfterToolExecuteAsync`
- `OnErrorAsync`

上下文：`src/Aevatar.Agents.AI.Core/Hooks/AevatarAgentHookContext.cs`

## Streaming（ChatStreamAsync）与 Hooks 的关系

为了避免 “同步 vs 流式” 两套行为分叉，`ChatStreamAsync` 目前做了 **最小对齐**：

- **会执行**
  - `OnSessionStartAsync` / `OnStopAsync` / `OnSessionEndAsync`
  - `BeforeLLMRequestAsync`：在打开流之前执行（可用于预算监控、给 request 打标、收敛工具可见性）
  - `OnErrorAsync`：当 streaming 过程中发生异常时执行（best-effort）
- **不会执行**
  - `AfterLLMResponseAsync`：streaming 是 token 流，不存在一次性完整 response envelope（当前实现不触发）

> 备注：即使在 `BeforeLLMRequestAsync` hooks 之后，也会再次执行 `AttachToolsToRequest`，防止 hook 试图“扩权”。

## 内置 Hooks（默认启用）

内置 hooks 由 `AIGAgentBase.CreateBuiltInHooks()` 提供（不需要 DI 注册）。

- **ToolOutputTruncationHook**（`AfterToolExecute`）
  - 作用：截断过长的 `ToolExecutionResult.Content`，避免 tool 输出把上下文撑爆
- **ContextBudgetMonitorHook**（`BeforeLLMRequest`，warn-only）
  - 作用：按字符/消息数做预算告警，只发信号/打标/日志，不做自动压缩

### Tool Evolution Hooks（需显式开启）

当 `ToolEvolutionOptions.Enabled=true` 时，内置 hooks 额外启用：

- **ToolExecutionHistoryHook**（`AfterToolExecute`）
  - 作用：记录二值反馈（success/fail、耗时、错误码）并可发布 `ToolExecutionFeedback`
- **ToolMetricsHook**（`AfterToolExecute`）
  - 作用：聚合工具指标并按阈值发布 `ToolMetricsSnapshot`

## 如何禁用某个 Hook（DisabledHooks）

配置对象：`AevatarAgentHookOptions`（`src/Aevatar.Agents.AI.Core/Hooks/AevatarAgentHookOptions.cs`）

你可以通过 DI 绑定 options，然后用 `DisabledHooks` 禁用：

```csharp
// 示例：宿主项目
services.Configure<AevatarAgentHookOptions>(configuration.GetSection("Aevatar:AI:Hooks"));
```

```json
{
  "Aevatar": {
    "AI": {
      "Hooks": {
        "DisabledHooks": ["ToolOutputTruncationHook"]
      }
    }
  }
}
```

> 匹配规则：按 hook 的 `Name`（默认是类型名）大小写不敏感。

## 如何添加自定义 Hook（DI 注入）

实现接口并注册到 DI：

```csharp
public sealed class MyHook : IAevatarAgentHook
{
    public int Priority => -100; // 越小越早

    public Task BeforeToolExecuteAsync(AevatarAgentHookContext ctx, CancellationToken ct)
    {
        // 例：拒绝执行某个工具（纯收敛）
        if (string.Equals(ctx.ToolName, "publish_event", StringComparison.OrdinalIgnoreCase))
        {
            ctx.DenyTool("publish_event disabled by policy");
        }

        return Task.CompletedTask;
    }
}

services.AddSingleton<IAevatarAgentHook, MyHook>();
```

## 可选 Hook：ToolDenyListHook（按工具名拒绝）

```csharp
services.Configure<ToolDenyListHookOptions>(configuration.GetSection("Aevatar:AI:ToolDenyList"));
services.AddSingleton<IAevatarAgentHook, ToolDenyListHook>();
```

```json
{
  "Aevatar": {
    "AI": {
      "ToolDenyList": {
        "DeniedTools": ["bash", "run_terminal_cmd"],
        "DenyReason": "Disabled by host policy."
      }
    }
  }
}
```

## 外部脚本 Hook（stdio JSON runner）

当你需要复用 Cursor hooks 风格的脚本时，可以注册外部脚本 runner：

```csharp
services.Configure<AevatarExternalHookOptions>(configuration.GetSection("Aevatar:AI:ExternalHooks"));
services.AddSingleton<IAevatarAgentHook, ExternalProcessHook>();
```

```json
{
  "Aevatar": {
    "AI": {
      "ExternalHooks": {
        "IncludeChatMessageContent": false,
        "Commands": [
          { "Stage": "BeforeToolExecute", "Command": "./hooks/audit.sh" },
          { "Stage": "OnStop", "Command": "./hooks/session-end.sh", "TimeoutMs": 5000 }
        ]
      }
    }
  }
}
```

脚本输出可选字段（stdin/out 均为 JSON）：

- `deny_tool`: true/false（仅在 tool 阶段有效）
- `deny_reason`: string
- `metadata`: object（合并到 `context.Metadata`）

注入机制（best-effort）：
- `AIGAgentFactory` 会在创建 agent 时以 **显式/类型安全** 的方式注入：
  - `IOptions<AevatarAgentHookOptions>`（或直接注入 `AevatarAgentHookOptions`）
  - `IEnumerable<IAevatarAgentHook>`（从 DI 解析，未注册时为空集合）
- 注入时会使 hook pipeline 缓存失效，确保注入立即生效（下一次 LLM/tool 调用重建 pipeline）。

## 常见注意事项

- **不要把 secrets 写进 hook metadata / 日志**（metadata 主要用于调试标注）
- **hook 只做横切治理**：业务行为仍应留在 Agent 中，避免把业务写进 harness


