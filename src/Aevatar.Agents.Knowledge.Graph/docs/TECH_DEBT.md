# 技术债清单（Knowledge.Graph）

## P2

### 过时 API 仍在使用（RelationshipType / KnowledgeSnapshot）

- **现状**：多个文件仍使用 `RelationshipType` 与 `KnowledgeSnapshot`（已标注过时）。
- **影响**：编译期告警堆积，后续版本移除时会引发破坏性变更。
- **建议**：统一切换到 `EdgeType` 与 `GraphSnapshot`，并清理所有旧 API 调用路径。
- **触发条件**：Graph 模块下次迭代或依赖升级时优先处理。

### GraphClientBackedStore 的 nullability 与返回类型不一致

- **现状**：`GraphClientBackedStore` 存在可能的 null 赋值与返回类型 nullability 不匹配（CS8601/CS8619）。
- **影响**：潜在 NRE 与类型语义不一致，影响调用方契约理解。
- **建议**：补充显式兜底（默认值/空集合），并统一类型注解与实际行为。
