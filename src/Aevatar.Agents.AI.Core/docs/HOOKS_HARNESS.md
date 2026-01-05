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

- `BeforeLLMRequestAsync`
- `AfterLLMResponseAsync`
- `BeforeToolExecuteAsync`
- `AfterToolExecuteAsync`
- `OnErrorAsync`

上下文：`src/Aevatar.Agents.AI.Core/Hooks/AevatarAgentHookContext.cs`

## Streaming（ChatStreamAsync）与 Hooks 的关系

为了避免 “同步 vs 流式” 两套行为分叉，`ChatStreamAsync` 目前做了 **最小对齐**：

- **会执行**
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
            ctx.Metadata["deny_tool"] = true;
            ctx.Metadata["deny_reason"] = "publish_event disabled by policy";
        }

        return Task.CompletedTask;
    }
}

services.AddSingleton<IAevatarAgentHook, MyHook>();
```

注入机制（best-effort）：
- `AIGAgentFactory` 会通过反射把 `IOptions<AevatarAgentHookOptions>` 与 `IEnumerable<IAevatarAgentHook>` 注入到 `AIGAgentBase`。
- 见：`src/Aevatar.Agents.AI.Core/Helpers/AIAgentHookInjector.cs`

## 常见注意事项

- **不要把 secrets 写进 hook metadata / 日志**（metadata 主要用于调试标注）
- **hook 只做横切治理**：业务行为仍应留在 Agent 中，避免把业务写进 harness


