## 目录结构
```
Commands/
├── DirectorySync.cs                # 目录同步工具（校验 + 备份 + copy）
├── FileBackup.cs                   # 文件备份工具（_legacy_时间命名）
├── RepoPathResolver.cs             # 仓库路径解析（默认 workflows 目录）
├── RootCommands.cs                  # 根命令入口与全局选项
├── RootCommands.Commands.Runtime.cs # tui/run/serve/web/attach 命令构建
├── RootCommands.Commands.Sessions.cs# sessions/stats/export/import 命令构建
├── RootCommands.Commands.Config.cs  # config/agents/workflows/models 命令构建
├── RootCommands.Commands.Integration.cs # mcp/auth/github/acp/upgrade/uninstall 命令构建
├── RootCommands.Handlers.Core.cs    # 本地运行与 session 执行主逻辑
├── RootCommands.Handlers.Support.cs # 配置/MCP/统计/维护性逻辑
├── RootCommands.Handlers.Remote.cs  # 远程 attach/run 的 HTTP 交互
└── docs/ARCHITECTURE.md             # 架构与约束记录
```

## 架构决策
- **命令构建与执行分离**：`Commands.*` 只负责命令面，`Handlers.*` 负责执行，降低认知负担并利于测试。
- **核心/远程拆分**：`Handlers.Core` 处理本地逻辑，`Handlers.Remote` 处理 HTTP/SSE，隔离依赖与复杂度。
- **partial 拆分**：遵循“单文件 < 800 行”与“职责单一”，避免 RootCommands 膨胀。

## 开发规范
- 新增命令：在 `Commands.*` 添加构建方法，并在 `RootCommands.BuildRootCommand()` 注册。
- 新增执行逻辑：优先落在 `Handlers.Core`，涉及远程交互放入 `Handlers.Remote`。
- 任何新增/移动文件必须同步更新本 `docs`。
- 端口禁用 `:5000`，默认使用 `:5678`。
- secrets 禁止进入 session 事件或日志输出。

## 变更日志
- 2026-01-15：拆分 RootCommands，消除巨型文件坏味道；本地/远程逻辑分层。
- 2026-01-15：新增 `FileBackup`，集中处理备份命名与冲突规避。
- 2026-01-15：新增 `DirectorySync`，抽离目录同步的校验与复制逻辑。
- 2026-01-15：新增 `RepoPathResolver`，统一 repo root 与默认路径推导。