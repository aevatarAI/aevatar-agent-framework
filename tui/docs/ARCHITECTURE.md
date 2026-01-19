# TUI 架构说明

## 结构树
```
tui/
├── README.md
├── Directory.Packages.props
├── tui.slnx
├── docs/
│   └── ARCHITECTURE.md
├── src/
│   ├── Tui.Core/
│   │   ├── Input/
│   │   ├── Layout/
│   │   ├── Primitives/
│   │   ├── Render/
│   │   ├── Terminal/
│   │   └── Text/
│   ├── Tui.Widgets/
│   └── Tui.Declarative/
├── samples/
│   └── Tui.Samples/
└── test/
    ├── Tui.Core.Tests/
    ├── Tui.Widgets.Tests/
    └── Tui.Declarative.Tests/
```

## 模块职责
- `Tui.Core`：终端 I/O、输入解析、渲染内核、核心数据模型与测量抽象。
- `Tui.Widgets`：基础控件与交互行为，禁止直接输出 ANSI。
- `Tui.Declarative`：声明式组件树与 reconciler，将 UI 构建为 `ScreenBuffer` 操作。
- `Tui.Samples`：演示与性能基准，验证渲染与输入主循环。
- `Tui.Core.Tests`：diff、输入解析、布局边界等核心行为测试。
- `Tui.Widgets.Tests`：控件 snapshot/golden 测试，保证渲染稳定性。
- `Tui.Declarative.Tests`：Reconciler 复用与生命周期测试。

## 关键架构决策（不可回退）
1. **唯一真相源**：所有 UI 输出归一到 `ScreenBuffer(next)`。
2. **双缓冲 diff**：只输出差异，避免全量刷屏。
3. **输入状态机**：增量解析字节流，保证分片稳定性。
4. **文本测量抽象**：宽字符逻辑只在 `ITextMeasurer` 实现内演进。
5. **声明式 identity**：稳定 key + 明确生命周期，避免隐藏状态泄漏。

## 开发规范
- 禁止在控件层拼 ANSI 或直接写 stdout。
- 避免三层以上缩进，优先用模型消除特殊分支。
- 新增控件必须带 snapshot/golden 或 buffer 断言测试。

## 变更日志
- 2026-01-16：初始化独立 `tui/` 项目与核心护栏架构。
