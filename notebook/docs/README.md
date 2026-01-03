# Aevatar.Notebook — Architecture Notes

本目录是 `notebook/` 的架构镜像：每次新增/移动/拆分模块，都应同步更新此文档。

## 目录结构（当前）

```
notebook/
├── Aevatar.Notebook.csproj              # Web 项目（net10.0）
├── Program.cs                           # Minimal API + 静态 UI 托管
├── NotebookRuntime.cs                   # 运行期：单例 NotebookAgent 生命周期管理
├── NotebookStreaming.cs                 # Chat streaming：NDJSON 协议 + Tool 进度事件（MVP）
├── Agents/
│   └── NotebookAgent.cs                 # Agent：注入 notebook_context + Layer1/2 + CQRS 投影
├── Cqrs/
│   └── InMemoryCqrs.cs                  # Layer3：进程内 CQRS（MVP）
├── Persistence/
│   └── NotebookPersistence.cs           # 持久化切换（file/mongodb/supabase/neo4j）
├── Context/
│   ├── NotebookContextBudget.cs         # 上下文预算（bounded）
│   └── NotebookContextBuilder.cs        # Coverage + Top‑k 上下文构建
├── Protos/
│   └── notebook_messages.proto          # Notebook 领域契约（Sources/Chunks/Reports/Context）
├── Tracing/
│   └── NotebookTraceBuilder.cs          # ExecutionTrace 构建（→ MemoryGraph 投影）
├── Reports/
│   ├── ReportPipeline.cs                # 报告多步生成 + 版本化落 MemoryStore
│   ├── ReportApi.cs                     # 报告查询 API（list/get）
│   └── ReportStreamApi.cs               # 报告生成 Streaming API（/api/report/stream）
├── Tools/
│   └── NotebookTools.cs                 # Notebook 专用工具集（可被模型调用）
├── Sources/
│   ├── SourceChunker.cs                 # Source 分块（offset + bounded）
│   ├── SourceIndexer.cs                 # 写入 MemoryStore + 可选 VectorIndex
│   └── SourceApi.cs                     # Sources Minimal API（text/file/list/get）
└── wwwroot/
    ├── index.html                       # 三栏 UI（Sources / Chat / Report）
    ├── report.html                      # 报告查看页（Markdown 渲染 / Copy / Download）
    ├── report.js
    ├── styles.css
    └── app.js
```

## 设计要点（MVP）

- **Memory Layer 复用**：对话记忆（Layer 1/2）、CQRS（Layer 3）、MemoryStore（Layer 4）、VectorIndex（Layer 4.1）、MemoryGraph（Layer 4.2）
- **可插拔持久化**：通过配置切换，不把 backend 选择写进业务逻辑分支
- **跨边界契约**：Notebook 领域元信息用 Protobuf 定义（见 `Protos/notebook_messages.proto`）

## Chat Streaming（/api/chat/stream）

Notebook 的 Chat 默认走流式接口 `POST /api/chat/stream`（NDJSON：每行一个 JSON 对象）。

事件类型（按出现顺序）：

- `meta`：首包，包含 `agentId/requestId/executionId/context/citations`
- `delta`：增量 token（`content`）
- `tool_start`：模型触发工具调用时发出（`toolCallId/toolName`），前端显示“工具名 + 转圈”
- `tool_end`：工具执行结束（`toolCallId/toolName/success/durationMs`，失败时附 `error` 的截断预览）
- `done`：流结束
- `error`：best-effort 错误事件（随后仍会发 `done`）

## Report Streaming（/api/report/stream）

右侧 Studio 的 “Generate report” 默认走 `POST /api/report/stream`，以 **多阶段** 方式流式输出生成过程（outline → draft → refine）。

事件类型（按阶段穿插出现）：

- `meta`：首包，包含 `reportId/executionId/topic/context/citations/stages`
- `stage_start`：阶段开始（`stage` ∈ `outline|draft|refine`）
- `stage_delta`：阶段增量 token（`stage/content`）
- `stage_end`：阶段结束（`stage/durationMs/chars`）
- `tool_start` / `tool_end`：与 Chat 相同（工具进度）
- `saved`：落盘成功（`reportId/version/viewerUrl`）
- `done` / `error`：流结束 / best-effort 错误


