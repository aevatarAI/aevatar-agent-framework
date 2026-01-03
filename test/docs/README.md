# test/ — Unit Test Architecture Notes

本目录用于记录 `test/` 下各测试项目的组织方式与边界，作为架构镜像。

## 组织原则

- **按模块拆分**：每个核心模块对应一个 `*.Tests` 项目，避免“巨型测试工程”
- **契约优先**：跨边界类型使用 Protobuf；测试尽量复用同一契约避免漂移
- **不连真实外部依赖**：默认使用 fake/mock（除非是明确的集成测试项目）

## 重要测试项目（节选）

- **`test/Aevatar.Notebook.Tests/`**：Notebook 产品侧的单元测试（source 分块/索引、context builder、report pipeline 等逐步补齐）


