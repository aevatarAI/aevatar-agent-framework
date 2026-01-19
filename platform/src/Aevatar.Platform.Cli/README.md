# Aevatar CLI

`aevatar` 是 Aevatar Platform 的命令行入口，提供 TUI、session 管理与 OpenCode 风格命令面。

## 安装为 dotnet tool
```bash
dotnet pack platform/src/Aevatar.Platform.Cli -c Release
dotnet tool install --global --add-source platform/src/Aevatar.Platform.Cli/bin/Release aevatar
```

升级：
```bash
dotnet tool update --global --add-source platform/src/Aevatar.Platform.Cli/bin/Release aevatar
```

## 快速使用
```bash
# 启动 TUI
aevatar

# 单次运行
aevatar run "hello"

# 继续最近 session
aevatar --continue

# 启动本地 server（默认 :5678）
aevatar serve

# 远程 attach
aevatar attach http://127.0.0.1:5678
```

## TUI 模式
- 默认进入全屏 GUI（支持 Tab 切页）
- 强制 REPL：`AEVATAR_TUI_MODE=repl aevatar`
- 强制 GUI：`AEVATAR_TUI_MODE=gui aevatar`

## 主要命令
- `tui` 进入终端 UI
- `run` 单次执行
- `serve` / `web` 启动本地服务（供 attach/web）
- `attach` 连接远程 session
- `sessions` 会话管理（list/show/export/import）
- `config` 配置管理（show/edit/init）
- `agents` agent role 管理（list/show/create）
- `workflows` 工作流管理（list/validate/sync）
- `models` 模型/提供方查看
- `mcp` MCP 服务管理（list/add/auth/logout/debug）
- `auth` provider 认证（login/list/logout）
- `github` GitHub workflow（install/run）
- `stats` 会话统计
- `acp` ACP stdin/stdout 交互模式
- `upgrade` / `uninstall` 工具维护
