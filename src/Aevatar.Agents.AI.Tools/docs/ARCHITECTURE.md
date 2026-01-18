# Aevatar.Agents.AI.Tools — 工具层（框架）

> 目标：提供通用的 AI 工具实现，供 Platform 与其它应用复用。

## 目录结构

```
src/Aevatar.Agents.AI.Tools/
├── FileToolOptions.cs   # 文件工具访问策略（根目录/扩展名/覆盖）
├── FileToolHelpers.cs   # 路径规范化与读写辅助
├── FileReadTool.cs      # file_read 工具
├── FileWriteTool.cs     # file_write 工具
├── FileTools/
│   └── FileDeleteTool.cs  # file_delete 工具
│   ├── PathExistsTool.cs  # path_exists 工具
│   ├── DirListTool.cs     # dir_list 工具
│   ├── FileStatTool.cs    # file_stat 工具
│   └── HashSha256Tool.cs  # hash_sha256 工具
├── SystemTools/
│   ├── TimeNowTool.cs     # time_now 工具
│   ├── EnvGetTool.cs      # env_get 工具
│   └── UuidTool.cs        # uuid 工具
├── TextTools/
│   ├── Base64EncodeTool.cs # base64_encode 工具
│   ├── Base64DecodeTool.cs # base64_decode 工具
│   ├── JsonFormatTool.cs   # json_format 工具
│   └── JsonValidateTool.cs # json_validate 工具
├── ProcessTools/
│   ├── CommandToolOptions.cs  # 命令执行选项
│   ├── CommandToolHelpers.cs  # 命令执行辅助
│   ├── BashTool.cs            # bash 工具
│   └── LspTool.cs             # lsp 工具（外部命令）
├── GitTools/
│   ├── GitStatusTool.cs       # git_status 工具
│   ├── GitDiffTool.cs         # git_diff 工具
│   └── GitCommitTool.cs       # git_commit 工具
└── SearchTools/
    ├── SearchToolHelpers.cs   # glob/grep 辅助
    ├── GlobTool.cs            # glob 工具
    ├── GrepTool.cs            # grep 工具
    └── AstGrepTool.cs         # ast-grep 工具
```

## 模块职责（一句话）

- `FileReadTool`: 在白名单路径内读取文本文件。
- `FileWriteTool`: 在白名单路径内写入文本文件。
- `FileToolOptions`: 提供工具运行时的权限/范围约束。
- `FileDeleteTool`: 受限删除文件（危险操作）。
- `PathExistsTool` / `DirListTool` / `FileStatTool` / `HashSha256Tool`: 文件与路径辅助。
- `TimeNowTool` / `EnvGetTool` / `UuidTool`: 日常系统工具。
- `Base64*` / `Json*`: 文本与 JSON 日常工具。
- `BashTool` / `LspTool`: 外部命令执行（可选白名单 + 超时）。
- `Git*` / `Grep` / `Glob` / `AstGrep`: 代码与仓库常用工具集。

## 设计要点

- 工具不感知业务语义，仅执行受限读写。
- 路径白名单是唯一信任边界，避免任意文件访问。
- 默认扩展名限制为 `.yaml/.yml/.json`，由上层按需放宽。

## 变更日志

- 2026-01-17：新增文件读写工具（供 Hermes 与平台复用）。
- 2026-01-18：`~/.aevatar` 目录 helper 拆分到 `Aevatar.Agents.Configuration`。
- 2026-01-18：新增 coding agent 常用工具集（git/grep/glob/ast-grep/lsp/time/env/uuid）。