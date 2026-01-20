# Aevatar.Agents.AI.Tools 工具盘点

> 目的：汇总当前工具集、默认约束与风险分级，便于选择与收敛。

## 总览

- 总数：38
- 分组：File/Path(11) / Hash(3) / Text+JSON+URL(9) / Search(4) / Process(4) / Git(3) / System(3) / Patch(1)

## 统一约束

### FileToolOptions

- ReadRoots / WriteRoots 为唯一信任边界；为空会返回 `path_allowlist_empty`
- 相对路径会基于 `WorkingDirectory` 归一化
- `MaxReadChars` 默认 12000（`file_read` 可显式覆盖）
- 写入扩展默认白名单：`.yaml` / `.yml` / `.json`
- `AllowOverwrite` 默认 false（`file_write`/`path_copy`/`path_move` 也可通过参数显式覆盖）

### CommandToolOptions

- `AllowedCommands` 为空时视为全允许
- `TimeoutSeconds` 默认 120 秒
- `MaxOutputChars` 默认 20000，超出裁剪
- `WorkingDirectory` 用于解析 cwd，缺省时回退到当前进程目录

### 访问控制与风险标记

- RequiresInternalAccess：File/Path/Hash/Search 相关工具 + `env_get` + `apply_patch`
- IsDangerous：`file_delete` / `path_copy` / `path_move` / `path_remove_tree` / `bash` / `lsp` / `run_terminal_cmd` /
  `read_lints` / `git_commit` / `apply_patch`

## 工具清单

### File / Path (11)

- `file_read` (FileReadTool): 读文本；参数 `path`, `max_chars`；返回 `content`, `truncated`
- `file_write` (FileWriteTool): 写文本；参数 `path`, `content`, `overwrite`；扩展名限制
- `dir_list` (DirListTool): 列目录；`recursive`, `include_files`, `include_dirs`, `max_results`
- `file_stat` (FileStatTool): 文件/目录元信息；返回 size 与时间戳
- `path_exists` (PathExistsTool): 判断存在性与类型
- `file_delete` (FileDeleteTool): 删除文件；不支持目录；危险
- `path_copy` (FileCopyTool): 复制文件；`overwrite` 可选；危险
- `path_move` (FileMoveTool): 移动文件；`overwrite` 可选；危险
- `path_mkdir` (PathMkdirTool): 创建目录；默认递归
- `path_remove_tree` (PathRemoveTreeTool): 删除目录树；危险
- `path_tempfile` (PathTempFileTool): 创建临时文件；`dir`, `prefix`, `suffix`, `content`

### Hash (3)

- `hash_sha256` (HashSha256Tool): 文本/文件 SHA-256；`max_bytes` 默认 5_000_000
- `hash_md5` (HashMd5Tool): 文本/文件 MD5；`max_bytes` 默认 5_000_000
- `hash_sha1` (HashSha1Tool): 文本/文件 SHA-1；`max_bytes` 默认 5_000_000

### Text / JSON / URL (9)

- `base64_encode` (Base64EncodeTool): 文本编码为 base64
- `base64_decode` (Base64DecodeTool): base64 解码为 UTF-8；无效输入返回 `invalid_base64`
- `json_format` (JsonFormatTool): JSON 格式化；`minify` 控制是否压缩
- `json_validate` (JsonValidateTool): JSON 语法校验；返回 `valid` 布尔值
- `url_encode` (UrlEncodeTool): URL 编码
- `url_decode` (UrlDecodeTool): URL 解码
- `url_parse` (UrlParseTool): URL 解析；相对路径返回 `is_absolute=false`
- `text_replace` (TextReplaceTool): 文本替换；`ignore_case`, `replace_all`
- `text_diff` (TextDiffTool): 行级 diff；`max_lines` 默认 400

### Search (4)

- `glob` (GlobTool): glob 匹配文件；`root`, `max_results`
- `grep` (GrepTool): 文本检索；`pattern`, `path`, `glob`, `ignore_case`, `literal`, `max_results`, `max_file_kb`
- `codebase_search` (CodebaseSearchTool): best-effort 关键词评分；`root`, `glob`, `max_results`, `max_files`
- `ast-grep` (AstGrepTool): 运行 `ast-grep`/`sg`；`pattern`, `language`, `json`, `path`

### Process / Exec (4)

- `bash` (BashTool): shell 命令；`cwd`, `timeout_sec`；危险
- `lsp` (LspTool): 外部命令 + stdin；`args`, `stdin`, `cwd`, `timeout_sec`；危险
- `run_terminal_cmd` (RunTerminalCmdTool): shell 命令；支持 `is_background`；危险
- `read_lints` (ReadLintsTool): 默认 `dotnet build`；可自定义 `command`；危险

### Git (3)

- `git_status` (GitStatusTool): `git status --porcelain=v1`
- `git_diff` (GitDiffTool): `staged` 与 `path` 过滤
- `git_commit` (GitCommitTool): 需要 `message`；支持 `all`, `amend`；危险

### System (3)

- `time_now` (TimeNowTool): `format` 默认 "O"，返回 UTC + Local
- `env_get` (EnvGetTool): 单个 `name` 或 `names` 列表
- `uuid` (UuidTool): `count`(1-50) 与 `format`(D/N/B/P)

### Patch (1)

- `apply_patch` (ApplyPatchTool): `git apply`，失败回退 `patch`；支持 `strip`；危险
