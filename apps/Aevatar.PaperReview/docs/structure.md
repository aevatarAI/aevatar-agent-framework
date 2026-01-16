# Project Structure

## Directory Tree

```
apps/Aevatar.PaperReview/
├── PaperReview.AppHost/                 # Aspire 编排入口
├── src/
│   └── Aevatar.PaperReview/             # 业务与 API
├── test/
│   └── Aevatar.PaperReview.Tests/       # 单元测试
└── docs/
    ├── structure.md                     # 结构说明（本文）
    └── TECH_DEBT.md                     # 技术债与风险
```

## 模块边界

- **AppHost**：仅负责编排与启动，不承载业务逻辑。
- **src/Aevatar.PaperReview**：业务主体、事件流、SSE 与项目配置。
- **test**：围绕 PaperReview 行为与 DSL 运行的单测集合。

## 开发规范

- 单文件 ≤ 800 行，单目录 ≤ 8 文件（超出即拆分）。
- 跨边界类型必须使用 Protobuf 定义。
- 结构变更必须同步更新 `docs/structure.md`。

## 变更记录

- 2026-01-15：`Aevatar.PaperReview.Tests` 移入 `apps/Aevatar.PaperReview/test/`。
