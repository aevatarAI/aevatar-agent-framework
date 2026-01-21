# TUI (独立项目)

这是一个完全独立的 .NET TUI 框架工程，目标对标 OpenTUI 的核心思路：以 `ScreenBuffer` 为唯一真相源，双缓冲 diff 渲染，输入事件增量解析，后续叠加声明式层。

## 核心原则
- 控件/布局只写 `ScreenBuffer`，禁止输出 ANSI。
- 输入解析必须是增量状态机（可分片喂入字节流）。
- 文本测量抽象为 `ITextMeasurer`，避免宽字符问题污染核心。

## 目录结构
- `src/Tui.Core/`：终端后端、输入、渲染、布局与核心模型
- `src/Tui.Widgets/`：基础控件
- `src/Tui.Declarative/`：声明式组件树与 reconciler
- `samples/Tui.Samples/`：演示与基准
- `test/Tui.Core.Tests/`：核心单元测试
- `docs/ARCHITECTURE.md`：架构说明与决策

## 快速开始
```bash
cd tui
dotnet build
dotnet test
dotnet run --project samples/Tui.Samples
```

说明：解决方案文件为 `tui.slnx`（不再提供 `.sln`）。

## MVP 约束
- 初期只保证 ASCII + 常见 BMP 的宽度正确性。
- POSIX 优先（macOS/Linux），Windows 兼容后置。
