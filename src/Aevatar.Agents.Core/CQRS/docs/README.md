# CQRS Core（框架层实现）

本目录提供 CQRS 的框架级默认实现，供 Apps/Tools 复用。

## 文件职责

- `StateQueryService.cs`: `IStateQueryService` 默认实现（基于 `IStateIndexService`），为 HTTP API 和 AI Tools 提供统一查询入口。

## 设计要点

- **投影源头**：`GAgentBase<TState>.OnStateChangedAsync` → `IStateProjector` → `IStateIndexService`（ES）。
- **AgentType 规范**：投影时使用 `GetType().FullName`（索引名会做 `.`→`-` 清洗），查询侧必须保持一致。


