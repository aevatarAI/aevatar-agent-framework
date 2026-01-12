# Aevatar.Agents.Core — Architecture Notes

本目录是 `src/Aevatar.Agents.Core/` 的架构镜像：当核心生命周期/注入链路新增文件或职责变化时，必须同步更新。

## 关键模块速览（与 Memory/Trace 强相关）

- **`Extensions/ServiceCollectionExtensions.cs`**：核心 DI 注册入口（State/Config/Event/Trace/Memory/Graph 的默认实现与可替换点）
- **`Secrets/AevatarUserSecrets.cs`**：用户级加密 secrets（默认 `~/.aevatar/secrets.json`），提供 `IConfiguration` merge 入口（`AddAevatarUserSecrets()`）与运行时写入 store
- **`docs/UserSecrets.md`**：User Secrets 使用方式、优先级与安全说明（给应用/示例统一接入用）
- **`src/Aevatar.Agents.SecretsCli/`**：最小 secrets CLI（写入/读取/列出），用于填充 user secrets（避免手工编辑加密文件）
- **`Tracing/ProjectingExecutionTraceStore.cs`**：装饰 `IExecutionTraceStore`，在保存 trace 后 best‑effort 投影到 `MemoryGraph + MemoryEntry`
- **`MemoryGraph/ExecutionTraceMemoryProjector.cs`**：把 `ExecutionTrace` 树转换为可导航的 `MemoryGraph`（Layer 4.2）
- **`Helpers/*Injector.cs`**：反射注入点（把 DI 中的 store/tooling 注入到 Agent 对象上）

## 注入链路（新增）

为支持 Notebook 侧 `get_execution_graph` 等工具，需要把 `IMemoryGraphStore` 注入到 Agent：

- **`Helpers/MemoryGraphStoreInjector.cs`**：当 Agent 有可写属性 `MemoryGraphStore : IMemoryGraphStore` 时自动注入（best‑effort）

> 设计原则：注入永远 best‑effort，不因为缺少 store 让 Agent creation 失败；工具侧自行降级（返回结构化错误而非抛异常）。


