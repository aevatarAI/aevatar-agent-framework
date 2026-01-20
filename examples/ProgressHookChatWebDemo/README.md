# ProgressHookChatWebDemo

一个最小的 **ChatGPT 风格网页**，用于演示 **ExecutionTraceProgressHook → AG‑UI** 的进度事件流。
前端通过 SSE 订阅 AG‑UI，右侧展示进度事件（RUN/TOOL/LLM/CUSTOM），左侧展示流式回复。

## ✅ 前置：配置 LLM API Key（推荐 Aevatar.Config）

1) 启动配置工具：

```bash
dotnet run --project apps/Aevatar.Config/Aevatar.Config.csproj
```

2) 打开：

- `http://localhost:6677`（推荐）

3) 在 UI 中配置默认 provider 与 API key（写入 `~/.aevatar/secrets.json`）。

> 该 demo 会通过 `AddAevatarUserConfig()` 自动读取该 secrets。

## 运行

```bash
dotnet run --project examples/ProgressHookChatWebDemo/ProgressHookChatWebDemo.csproj
```

默认地址：`http://127.0.0.1:5690`

## 关键点

- **Progress Hook**：`ExecutionTraceProgressHook` 在 session/llm/tool 边界产生 `ExecutionTraceEvent`。
- **投影到 AG‑UI**：后端订阅 agent stream → `AgUiTraceProjector.Map`。
- **流式回复**：`ChatStreamAsync` 的 token 被实时转成 `TEXT_MESSAGE_*`。

## 可选：本地 appsettings.secrets.json

如果不使用 Aevatar.Config，可复制：

```bash
cp examples/ProgressHookChatWebDemo/appsettings.secrets.json.example \
  examples/ProgressHookChatWebDemo/appsettings.secrets.json
```

并写入 API key（文件不要提交到 git）。
