# Hooks

Agent Hooks 管线与扩展点实现目录，用于在执行过程中插入自定义逻辑。
内部分为内置 Hooks、外部 Hooks 与策略型 Hooks。

## 子目录
- `BuiltIn/`：内置 Hooks（预算、trace、输出截断等）。
- `External/`：外部进程 Hooks。
- `Policy/`：策略类 Hooks。

## 主要内容
- `AevatarAgentHookPipeline`：Hook 管线。
- `AevatarAgentHookContext`：Hook 上下文数据。
- `IAevatarAgentHook`：Hook 接口契约。

