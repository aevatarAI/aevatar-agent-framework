# 技术债记录

## 当前债务
- 依赖模块 `Aevatar.Agents.Knowledge.Graph` 存在大量过时 API 与空引用告警（`RelationshipType`/`KnowledgeSnapshot` 过时，`GraphClientBackedStore` 可能 null 赋值，返回类型 nullability 不匹配）。
- 依赖模块 `Aevatar.Agents.AI.Abstractions` 存在非空属性未初始化告警（`LLMProviderConfig.Default`）。
- 依赖模块 `Aevatar.Agents.AI.LLMTornado` 存在可能的 null 赋值告警（`LLMTornadoProvider`）。
- `ResearchRuntime` 体量过大（>1400 行）且初始化逻辑重复，维护与测试定位成本高。

## 修复策略
- Graph 模块：统一切换到 `EdgeType` 与 `GraphSnapshot`，并移除所有 `RelationshipType/KnowledgeSnapshot` 相关调用；修正 `GraphClientBackedStore` 中的 nullability 与返回类型注解一致性。
- AI.Abstractions：为 `LLMProviderConfig.Default` 增加 `required` 或设为可空并在读取点处理默认值。
- AI.LLMTornado：对可空来源做显式兜底（默认值/guard）或调整类型注解，避免隐式 null 赋值。
- `ResearchRuntime`：按角色/职责拆分为子组件或 `partial`，抽取通用初始化路径以消除重复。

## 清理节奏
- 中优先级：依赖升级与 API 迁移集中处理，避免碎片化修复（建议纳入近期一次性清理）。
