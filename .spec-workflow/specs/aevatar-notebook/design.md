# Design Document

## Overview

`Aevatar.Notebook` 是一个 NotebookLM-lite 的三栏 Web 应用（项目路径 `notebook/`），核心能力：

- **Sources（左栏）**：资料上传/管理（MVP 以文本为主）
- **Q&A（中栏）**：基于资料上下文回答问题，并输出可追溯引用（sourceId）
- **Report（右栏）**：基于资料生成结构化报告（含引用）

设计原则：尽可能复用 Aevatar 的 **Memory 分层体系（Layer 0~4.2）**，并把“资料→上下文→回答/报告”的过程变成可追溯、可回放、可扩展的 pipeline（见 `docs/AI_MEMORY_GUIDE.md`）。

---

## Steering Document Alignment

### Technical Standards (tech.md)

仓库当前没有 `.spec-workflow/steering/tech.md`。本设计遵循仓库级规则 `AGENTS.md`：

- **跨 runtime / stream / state / event 的类型必须 Protobuf**（State/Event/Memory/Trace/Graph 皆为 proto）
- **可插拔持久化**：通过 DI + 配置切换，不在业务逻辑散落 if/else

### Project Structure (structure.md)

仓库当前没有 `.spec-workflow/steering/structure.md`。本设计遵循当前 repo 结构与已落地的 Notebook 工程：

```
notebook/                          # Aevatar.Notebook Web app (MVP)
├── Program.cs                     # Minimal API + Static UI hosting
├── Agents/NotebookAgent.cs         # Notebook agent (context-grounded + CQRS projection)
├── Cqrs/InMemoryCqrs.cs            # CQRS in-memory read model (MVP)
├── Persistence/NotebookPersistence.cs # Config-driven persistence switching
└── wwwroot/                        # NotebookLM-like 3-column UI (static)
```

---

## Code Reuse Analysis

### Existing Components to Leverage

- **`docs/AI_MEMORY_GUIDE.md`**：Memory Layer 0~4.2 的规范定义与使用策略
- **`examples/MemoryDemo/`**：最小 Web UI + APIs + config 驱动持久化切换的成熟范式
- **`AIGAgentBase`**（`src/Aevatar.Agents.AI.Core/`）：
  - Layer 1：`State.History`（滑窗）
  - Layer 2：`State.Context["history_summary"]`（滚动摘要 + 注入 system prompt）
- **内置工具 `search_memory`**（`AevatarMemorySearchTool`）：
  - 向量优先（Layer 4.1）→ 资源检索（Layer 4）→ CQRS（Layer 3）→ state scan（Layer 1/2）
- **Trace/Graph 投影链路**：
  - `IExecutionTraceStore` 默认用 `ProjectingExecutionTraceStore` 装饰：保存 trace 后 best-effort 产出 `MemoryGraph` + execution-scoped `MemoryEntry`
  - `ExecutionTraceMemoryProjector` + `IMemoryGraphStore`（file/neo4j）
- **持久化实现**：
  - `Aevatar.Agents.Persistence.MongoDB.Memory`
  - `Aevatar.Agents.Persistence.Supabase.Memory`
  - `Aevatar.Agents.Persistence.Neo4j.MemoryGraph`

### Integration Points

- **Agent Runtime**：MVP 使用 Local runtime（`UseLocalRuntime()`），后续可切 Orleans/ProtoActor（不改业务代码）
- **LLM Providers**：复用 MEAI 配置体系（`LLMProvidersConfig`）
- **Persistence**：复用“backend-first”布局与配置切换模式（与 MongoDB/Supabase 对齐）

---

## Architecture

### High-level data flow

```mermaid
flowchart LR
  UI[Notebook UI\n(3 columns)] --> API[Notebook APIs\nMinimal API]

  API -->|write| Store[IMemoryStore\nLayer 4]
  API -->|optional upsert| Vector[IMemoryVectorIndex\nLayer 4.1]

  API -->|build context| Ctx[NotebookContextBuilder\n(budgeted)]
  Ctx --> Store
  Ctx --> Vector

  API --> Agent[NotebookAgent\nAIGAgentBase]
  Ctx -->|notebook_context| Agent
  Agent --> LLM[IAevatarLLMProvider]
  LLM --> Agent --> API --> UI

  Agent -->|state changes| CQRS[IStateProjector -> IStateIndexService\nLayer 3]

  API --> Trace[IExecutionTraceStore\n(trace bundles)]
  Trace --> Graph[IMemoryGraphStore\nLayer 4.2]
  Trace --> Store
```

### Memory Layer mapping (0~4.2)

- **Layer 0（Stateless Prompt）**：
  - 报告生成 pipeline 的每一步（outline/draft/refine）可使用“无 history 的独立提示 + Notebook context”，以提升可复现与可控性
- **Layer 1（State.History）**：
  - Q&A 的短期对话窗口（UI 回放 + 连贯性）
- **Layer 2（history_summary）**：
  - 压缩被裁剪历史，低成本注入 system prompt（AIGAgentBase 内置）
- **Layer 3（CQRS Read Model）**：
  - 投影出可检索字段（例如：historyText/historySummary/sourceIndex/reportIndex…），供 `search_memory` 优先使用
- **Layer 4（IMemoryStore）**：
  - Sources（`source::<sourceId>`）
  - Source chunks（建议：同 memoryId 多 entry + tags：chunk_index/range…）
  - Reports（`report::<reportId>`）
  - Execution-scoped memory entries（`execution::<executionId>`，由 trace projector 产出）
- **Layer 4.1（IMemoryVectorIndex）**：
  - 为 source chunks 与关键条目建立 embedding 索引（top‑k 召回）
- **Layer 4.2（IMemoryGraphStore）**：
  - 每次问答/报告的 ExecutionTrace 投影为 MemoryGraph（可 file/neo4j）

---

## Components and Interfaces

### Component 1: Notebook APIs (`notebook/Program.cs`)

- **Purpose**：
  - 承载 UI 静态资源与 Minimal API
  - 负责 Sources 管理、Q&A、Report 生成的入口与数据编排
- **Interfaces (MVP 已有)**：
  - `POST /api/sources/text`
  - `GET /api/sources`
  - `GET /api/sources/{sourceId}`
  - `POST /api/chat`
  - `POST /api/report`
  - `GET /api/info`, `POST /api/reset`
- **Planned extensions**：
  - `POST /api/sources/file`（上传 txt/pdf 等，MVP 先做 txt）
  - `POST /api/sources/{sourceId}/reindex`（重建 chunk/vector/summary）
  - `GET /api/executions/{executionId}`（trace/graph 调试视图，默认关闭）

### Component 2: NotebookAgent (`notebook/Agents/NotebookAgent.cs`)

- **Purpose**：
  - 统一与 LLM 交互（Q&A/Report）
  - 将 `notebook_context` 注入 system prompt，保证“问答时上下文喂给 LLM”
  - 复用 AIGAgentBase 的 Layer 1/2 记忆与工具体系
- **Key behaviors**：
  - `EnableChatHistoryInState = true` + compaction summary
  - `EnableMemoryStoreAppend/EnableMemoryVectorIndexAppend` 默认开启（best-effort）
  - 每次 `ChatAsync` 后调用 `ProjectStateAsync` 触发 Layer 3 投影

### Component 3: NotebookContextBuilder（待实现）

- **Purpose**：为每次 Q&A/Report 构建有界的 Notebook context
- **Inputs**：
  - 用户 query
  - 选中的 sources（或默认 all）
  - token/char budget
- **Strategy**：
  - 每源至少提供：`sourceId + summary/preview`
  - 在预算允许时，追加与 query 相关的 top‑k chunks（优先 Layer 4.1）
  - 预算不足时：全局 summary + 每源更短 summary + top‑k

### Component 4: CQRS In-memory read model (`notebook/Cqrs/InMemoryCqrs.cs`)

- **Purpose**：MVP 用于 Layer 3，保证 `search_memory` 优先走 CQRS
- **Planned**：后续替换为 ES/其它后端实现（不影响业务代码）

### Component 5: Persistence switching (`notebook/Persistence/NotebookPersistence.cs`)

- **Purpose**：
  - 通过 `Aevatar:Persistence:*` 配置切换 file/mongodb/supabase/neo4j
  - 启动期 fail-fast 校验（缺配置直接报错）

### Component 6: Tooling（待实现）

除内置 `search_memory` 外，Notebook 需要一组“可被模型调用”的工具来完成可解释的检索/引用：

- `list_sources`：列出当前 notebook 的 sources（含 sourceId/title/size/updatedAt）
- `get_source`：按 sourceId 获取摘要/片段（有界）
- `retrieve_chunks`：基于 query + source filter 返回 top‑k chunks（向量优先、资源 fallback）
- `generate_report`：触发报告 pipeline（返回 reportId）
- `get_report`：按 reportId 获取报告版本
- `get_execution_graph`：按 executionId 获取 MemoryGraph（调试/开发模式）

---

## Data Models

### Storage contract (MVP)

MVP 尽量复用已有 Protobuf 契约：

- **Sources / Chunks / Reports**：统一落 `MemoryEntry`（`IMemoryStore`）
  - `memoryId` 约定：
    - `source::<sourceId>`：原文/摘要/分块均可作为 entries 存在同一资源下
    - `report::<reportId>`：报告版本
  - tags 约定：
    - `title`、`source_id`、`chunk_index`、`offset_start`、`offset_end`、`topic`、`citations` 等
- **Vectors**：`MemoryVectorRecord`（`IMemoryVectorIndex`）
- **Trace**：`ExecutionTrace`（`IExecutionTraceStore`）
- **Graph**：`MemoryGraph`（`IMemoryGraphStore`）

后续如需要更强契约，可新增 `notebook_messages.proto` 定义 `NotebookSourceMeta/NotebookReportMeta/NotebookContextSlice` 等（仍通过 MemoryEntry/Any 关联）。

---

## Error Handling

### Error Scenarios

1. **数据库配置缺失**
   - **Handling**：启动时 fail-fast（提示缺少哪个 connection string/credential）
   - **User Impact**：服务启动失败，日志给出明确配置路径

2. **向量/MemoryStore 写入失败**
   - **Handling**：best-effort，吞掉异常 + 记录 trace/log
   - **User Impact**：问答/报告仍返回，但可能检索质量下降

3. **上下文过大**
   - **Handling**：NotebookContextBuilder 进行预算压缩（分层 summary + top‑k）
   - **User Impact**：回答仍可用，并明确提示“上下文被压缩/引用来源”

4. **LLM 调用失败**
   - **Handling**：返回可理解错误 + requestId/executionId（方便追踪）
   - **User Impact**：UI 展示失败原因，可重试

---

## Testing Strategy

### Unit Testing

- NotebookContextBuilder 的预算策略（覆盖：all sources、selected sources、超预算压缩、top‑k 召回）
- Source chunker（字符边界、token 近似、tags 正确性）
- Tool contracts（参数校验、best-effort fallback 路径）
- CQRS 投影：`StateWrapper -> StateIndexDocument` 扁平字段正确

### Integration Testing

- 持久化切换（file/mongodb/supabase/neo4j）按配置生效（可复用 `examples/MemoryDemo/VALIDATION.md` 的思路）
- Trace -> Graph 投影链路：保存 trace 后能加载 graph（file/neo4j）

### End-to-End Testing

- 三栏 UI 主流程：
  - 添加 sources → 问答 → 生成报告 → 查看引用/来源定位
  -（开发模式）查看 execution trace/graph


