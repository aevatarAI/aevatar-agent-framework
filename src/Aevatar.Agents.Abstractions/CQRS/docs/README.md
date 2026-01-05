# CQRS Abstractions（契约层）

本目录只定义 CQRS 的“边界契约”，供 Core/Plugins/Apps/Tools 统一依赖。

## 文件职责

- `IStateProjector.cs`: 状态投影接口（`OnStateChangedAsync` 触发，投影到 read-model）。
- `IStateIndexService.cs`: 索引读写接口（ES 实现位于 Plugins）。
- `IStateQueryService.cs`: 查询门面（Facade）接口，供 HTTP/Tool 统一使用，避免直接依赖索引实现细节。

## 关键约束

- **跨边界数据必须 Protobuf**：投影输入是 `StateWrapper`（Proto），State 本身必须是 Proto 生成类型。


