## 目录结构
```
Aevatar.Platform.Cli/
├── Commands/                      # 命令面与执行逻辑分层
├── Tui/                           # TUI 实现（GUI/REPL 双模式）
├── tui-opentui/                   # OpenTUI 前端（TypeScript + Zig）
├── Program.cs                     # CLI 入口
├── README.md                      # 使用说明与常见命令
└── docs/ARCHITECTURE.md           # 架构记录
```

### Tui 子目录
```
Tui/
├── TuiApp.cs                      # OpenTUI/REPL 入口（GUI 为外部 OpenTUI 进程）
├── OpenTuiBackendServer.cs        # OpenTUI 本地后端（/api/chat + /api/chat/stream）
├── TuiHandlers.cs                 # 命令/消息处理与输出抽象
├── InputParser.cs                 # 输入解析（/命令、!shell、@附件）
├── ShellRunner.cs                 # shell 执行（受策略约束）
├── AttachmentResolver.cs          # 路径解析与模糊匹配
├── SessionRuntime.cs              # TUI 会话运行态
└── TuiOptions.cs                  # TUI 启动参数

OpenTUI 前端：
```
tui-opentui/src/
├── index.ts                       # 启动入口（组装/生命周期）
├── api/chat.ts                    # 聊天 API（/api/chat/stream）
└── ui/
    ├── layout.ts                  # UI 布局
    └── input.ts                   # 输入/IME 兜底
```
```

## 架构决策
- **GUI/REPL 双模式**：默认 GUI（OpenTUI），非交互场景自动回退到 REPL，避免 pipeline 崩溃。
- **输出抽象**：`ITuiOutput` 统一输出通道，避免 GUI/Console 混杂写终端。
- **TUI 与 Core 解耦**：TUI 只负责输入与展示，业务执行依赖 `Platform.Core`。
- **本地后端**：OpenTUI 前端通过本地 HTTP（随机端口）与 .NET 后端交互。

## 开发规范
- 新增 TUI 行为优先在 `TuiApp` 侧聚合，业务逻辑放入 `TuiHandlers`。
- GUI 输出必须走 `ITuiOutput`，禁止直接 `Console/AnsiConsole`。
- 端口默认 `:5678`，禁止 `:5000`。

## 变更日志
- 2026-01-16：移除 Terminal.Gui（macOS 下输入/Tab 不稳定），GUI 改为 OpenTUI（TypeScript + Zig）外部前端进程；保留 REPL 自动回退。
