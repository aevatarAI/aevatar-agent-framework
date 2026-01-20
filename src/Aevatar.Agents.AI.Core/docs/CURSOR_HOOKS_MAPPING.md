# Cursor Hooks ↔ Aevatar Hook/Harness 映射

> 目标：把 Cursor hooks 的生命周期语义映射到 Aevatar 的 Hook/Harness 管线，并明确缺口与替代路径。
> 参考：https://cursor.com/cn/docs/agent/hooks

## 结论

- **不是“加一个 tool 就行”**：Cursor hooks 覆盖的是 *Agent 生命周期*（会话、prompt、shell、文件、MCP 等），Aevatar 已有的 Hook/Harness 位于 **LLM/Tool 链路**，需要扩展到 session 层，或在宿主/Runner 中补齐。
- **Aevatar 可覆盖大多数 hook**：`BeforeLLMRequest / AfterLLMResponse / BeforeToolExecute / AfterToolExecute / OnError` 能直接承接 prompt、tool、MCP 等阶段。
- **缺口集中在 session/stop**：需要新增 sessionStart/sessionEnd/stop 类阶段（已在本次对齐补齐）。

## 映射表（Cursor → Aevatar）

| Cursor Hook | Aevatar Hook/Harness | 说明 |
|---|---|---|
| `sessionStart` | `OnSessionStartAsync` | 会话级；在 `ChatAsync/ChatStreamAsync` 入口触发 |
| `sessionEnd` | `OnSessionEndAsync` | 会话级；在 `ChatAsync/ChatStreamAsync` 结束触发 |
| `stop` | `OnStopAsync` | 会话级；对应一次 agent loop 的终止状态 |
| `beforeSubmitPrompt` | `BeforeLLMRequestAsync` | LLM 请求发送前 |
| `afterAgentResponse` | `AfterLLMResponseAsync` | 非流式 response 完成后触发；流式不会触发 |
| `afterAgentThought` | *缺口* | Aevatar 暂无显式“thought”阶段（可通过 provider metadata/日志实现） |
| `beforeShellExecution` | `BeforeToolExecuteAsync` | 针对 `bash` / `run_terminal_cmd` / `lsp` / `read_lints` |
| `afterShellExecution` | `AfterToolExecuteAsync` | 同上 |
| `beforeReadFile` | `BeforeToolExecuteAsync` | 针对 `file_read` / `file_stat` / `path_exists` / `dir_list` |
| `afterFileEdit` | `AfterToolExecuteAsync` | 针对 `file_write` / `file_delete` / `path_copy` / `path_move` / `path_remove_tree` / `path_mkdir` |
| `beforeMCPExecution` | `BeforeToolExecuteAsync` | MCP 工具名通常以 `mcp__` 前缀命名 |
| `afterMCPExecution` | `AfterToolExecuteAsync` | 同上 |
| `preCompact` | *部分覆盖* | 可用 `ContextBudgetMonitorHook` 做预算告警；无显式 compaction hook |

## 工具名与分类（用于 hook 过滤）

- **Shell/Process**：`bash` / `run_terminal_cmd` / `lsp` / `read_lints`
- **File/Path**：`file_read` / `file_write` / `file_delete` / `path_copy` / `path_move` / `path_remove_tree` / `path_mkdir` / `file_stat` / `path_exists` / `dir_list`
- **MCP**：`mcp__{server}__{tool}`（由 MCP runtime namespaceTools 生成）

## 已知差异与约束

- Hook **只能收敛**，不能扩大 `AllowInternalTools / AllowDangerousTools` 权限；LLM 请求在 hook 后会再次执行 `AttachToolsToRequest`。
- Streaming 路径不会触发 `AfterLLMResponseAsync`（token 流没有完整 response envelope）。
- `preCompact` 尚无直接阶段；可通过 `ContextBudgetMonitorHook` 观测预算并提示。

