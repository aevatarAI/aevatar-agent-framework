# Learning ↔ Notebook 功能对齐（MVP 迁移/复用策略）

> 目标：`experimental/learning/` 必须“包含 notebook 的全部功能”（上传资料、问答、整理报告等）。  
> 本文把 **Notebook 系统的既有实现** 映射到 Learning 的目录模型（Notebook=真实目录），明确 **复用优先** 的落地路径，避免双实现与接口漂移。

## 现状对比（核心差异）

### Notebook（现有）

- 数据组织：以“session/thread”为中心，Sources/Reports 存在 MemoryStore（可选 Mongo/Supabase/File）。
- AI：`NotebookAgent : AIGAgentBase`，天然有 `State.History`，适配 AG‑UI `MESSAGES_SNAPSHOT`。
- Streaming：API 同时支持 legacy NDJSON 与标准 AG‑UI SSE。

### Learning（目标）

- 数据组织：以“Notebook（学习专题）= 真实目录”为中心；每个目录下有 `sources/ reports/ ...` 子目录。
- UI：桌面端优先（Tauri），本地文件系统权限与离线资产管理。
- Streaming：统一采用标准 AG‑UI（snapshot-first）。

## 模块映射表（复用优先）

> “复用”不等于直接复制文件；优先复用 **算法/管线**，同时把存储抽象替换为“目录存储”。

| 能力 | Notebook（参考实现） | Learning（落点位置） | 策略 |
|---|---|---|---|
| Sources 上传/创建/列表/读取 | `experimental/notebook/src/Aevatar.Notebook.Api/Sources/*` | `experimental/learning/src/Aevatar.Learning.Api/Sources/*` | 重写 API，但复用分块/索引算法 |
| 资料分块（chunking） | `Aevatar.Notebook/Sources/SourceChunker` | `Aevatar.Learning/Sources/SourceChunker` | 直接复用/轻改（输入改为目录文件） |
| 索引（metadata + optional vector） | `Aevatar.Notebook/Sources/SourceIndexer` | `Aevatar.Learning/Sources/SourceIndexer` | 复用流程，存储改为 per-notebook 目录 |
| Context 构建（coverage + Top‑K） | `Aevatar.Notebook/Context/NotebookContextBuilder` | `Aevatar.Learning/Context/LearningContextBuilder` | 复用策略与预算控制，替换 store 读取层 |
| Chat（问答） | `NotebookAgent.Chat*` + `NotebookRuntime` | `LearningSession -> run pipeline` | 保留 sessions API；把“占位回复”替换为真实 LLM streaming |
| Report（报告生成） | `Aevatar.Notebook/Reports/ReportPipeline` | `Aevatar.Learning/Reports/ReportPipeline` | 复用 prompt + 产物结构，落盘到 `reports/` |
| AG‑UI bootstrap（快照优先） | `NotebookAgUiBootstrap` | `LearningAgUiBootstrap` | snapshot 数据源改为 per-session transcript（或 agent state） |

## MVP 对齐范围（先跑通闭环）

MVP 先对齐三条主链路：

1. **Sources**：导入/列出/读取（目录落盘，产生 `source_id` 与元数据）
2. **Q&A**：基于 sources 构建 context → LLM 回答（AG‑UI 流式输出）
3. **Report**：基于 sources + Q&A 历史生成报告并写入 `reports/`

> 其他学习特性（百科/卡片/测验/skills）在上述三条链路之上扩展，不应抢占 MVP 主路径。

## 迁移实施顺序（推荐）

1. **目录存储完成**（已做）：`NotebookDirectoryStore` + `NotebookWorkspace`
2. **Sources 落盘与索引**：在 `sources/` 内存储原文与元数据；必要时生成 chunks（可选 embeddings）
3. **ContextBuilder**：把 sources/chunks 组装为可追溯的 prompt context（携带 sourceId/chunkId）
4. **Chat pipeline**：把 sessions `/input` 的占位逻辑替换为“构建 context → LLM streaming → AG‑UI 事件”
5. **Report pipeline**：报告生成写入 `reports/`，并返回 report meta + citations

## 关键约束（避免踩雷）

- **跨边界类型必须 Protobuf**：state/event/config/event-sourcing；不要为方便写 POCO。
- **快照优先（snapshot-first）**：重连依赖 `MESSAGES_SNAPSHOT`，不要依赖 replay token 事件。
- **不要硬编码 provider**：全部走 `LLMProviders:Default`，允许请求级 `providerName` 覆盖。
- **端口策略**：禁止 `:5000`；后端默认 `:5678`，前端 `:5173`。


