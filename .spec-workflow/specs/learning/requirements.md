# Requirements Document

## Introduction

本规格定义一个 **AI Native 学习系统**（拟落地系统目录：`learning/`），其核心价值依赖 AI：**若无 AI/LLM Provider，本系统的关键功能（问答/整理/百科/测验/学习卡片生成/skills 生成）即不成立**。

范围（MVP Skeleton）：
- 桌面端优先：前端采用 **Tauri + Web UI（React + Vite + TS）**，同时保留 Web Dev 模式便于开发调试。
- 复用既有系统：功能基线对齐 `notebook/`（上传资料、问答、整理报告等），并扩展：Notebook 专题目录、百科系统、学习卡片（SRS）、测验系统、进度统计、skills 生成。
- 通信与可观测：前后端通过 **AG‑UI SSE** 建立会话级实时事件流；后端可配置 LLMProviders；提供 `/health` 与 `/api/info`。

非目标（MVP 不做）：
- 多用户协作与权限体系（默认单机单用户）。
- 复杂的知识图谱推理与外部检索爬虫（先以本地资料为主，预留扩展点）。

## Alignment with Product Vision

本系统与 Aevatar Agent Framework 的产品方向一致：
- **Events are Truth**：交互过程以事件流（AG‑UI）驱动 UI 更新，便于可观测与回放。
- **Runtime Agnostic**：业务逻辑尽量保持运行时无关，运行时差异收敛在宿主与基础设施。
- **Protobuf‑First**：任何跨边界（state/event/config/stream/持久化事件）的类型必须由 `.proto` 生成，避免运行时序列化失败。

## Requirements

### Requirement 1 — Notebook（学习专题）管理：Notebook ⇄ 本地目录

**User Story:** 作为用户，我想创建/打开一个学习 Notebook（专题），并让每个 Notebook 对应一个真实目录，以便把资料、百科、卡片、测验与产出都归档到该专题下。

#### Acceptance Criteria

1. WHEN 用户创建 Notebook THEN 系统 SHALL 创建一个真实目录并返回 `notebookId`（目录名与显示名可不同）。
2. WHEN 用户打开某个 Notebook THEN 系统 SHALL 以该 Notebook 作为会话范围（后续问答/报告/百科/卡片/测验均归属它）。
3. IF Notebook 目录不存在或不可写 THEN 系统 SHALL 给出可理解错误并拒绝进入该 Notebook。

---

### Requirement 2 — 资料（Sources）导入与管理（对齐 `notebook/`）

**User Story:** 作为用户，我想把资料上传/导入到 Notebook，并管理它们，以便后续问答与生成内容可基于这些资料。

#### Acceptance Criteria

1. WHEN 用户导入资料（文本/文件）THEN 系统 SHALL 为每份资料生成稳定的 `sourceId`，并在列表中可见。
2. WHEN 用户查看某份资料 THEN 系统 SHALL 展示内容预览与元信息（标题、更新时间、大小、来源类型）。
3. IF 用户提交空内容或不支持的格式 THEN 系统 SHALL 拒绝并返回可理解错误信息。

---

### Requirement 3 — 问答（Q&A）：基于资料上下文的 AI 对话

**User Story:** 作为用户，我想在某个 Notebook 内提问并得到基于资料的回答，同时能追溯引用来源。

#### Acceptance Criteria

1. WHEN 用户发送问题 THEN 系统 SHALL 构建 Notebook context（包含可追踪的资料表示，如 `sourceId` + 摘要/片段）并注入 LLM 请求。
2. WHEN 系统返回回答 THEN 系统 SHALL 返回可追溯的引用信息（至少 `sourceId` 列表），用户可定位到来源资料。
3. IF LLMProviders 未配置或 provider 调用失败 THEN 系统 SHALL 明确提示配置入口/失败原因，并不破坏已有资料与历史。

---

### Requirement 4 — 整理/报告生成：基于资料的一键结构化产出

**User Story:** 作为用户，我想对某个 Notebook 的资料一键生成结构化报告（摘要、要点、引用、行动建议），并能保存多个版本。

#### Acceptance Criteria

1. WHEN 用户触发“生成报告” THEN 系统 SHALL 基于 Notebook context 生成结构化报告（至少：标题/摘要/要点/引用）。
2. WHEN 报告生成完成 THEN 系统 SHALL 将报告持久化到该 Notebook 目录中，并在 UI 中可查看历史版本。
3. IF 报告生成失败 THEN 系统 SHALL 返回可理解错误并不破坏已存在资料/对话。

---

### Requirement 5 — Notebook 百科系统（AI 构建）：结构化知识条目与查询

**User Story:** 作为用户，我想为每个 Notebook 构建一套“专题百科”，并能用自然语言输入得到结构化答案（以“人体经络”为例：症状 → 推荐穴位/解释/手法/理论）。

#### Acceptance Criteria

1. WHEN 用户在某 Notebook 中请求“构建/更新百科” THEN 系统 SHALL 使用 AI 基于资料生成可存储的结构化条目（可增量更新）。
2. WHEN 用户输入症状或体质描述 THEN 系统 SHALL 输出结构化结果，至少包含：
   - 推荐条目列表（例如穴位：位置、功效）
   - AI 解释“为什么有效”
   - 操作建议（例如按摩手法）
   - 相关理论讲解（可引用资料/百科条目）
3. IF 缺少足够资料 THEN 系统 SHALL 明确提示“需要补充资料/降低置信度”，并给出建议的资料类型。

---

### Requirement 6 — 学习卡片系统：SRS 间隔重复 + AI 记忆技巧 + 多模式练习

**User Story:** 作为用户，我想每天学习新内容并进行间隔重复复习；系统能基于资料生成卡片与记忆技巧，并提供多种练习模式与进度统计。

#### Acceptance Criteria

1. WHEN 用户在某 Notebook 中启用卡片学习 THEN 系统 SHALL 支持生成/导入卡片，并为每张卡片维护 SRS 计划（到期时间/间隔/熟练度）。
2. WHEN 用户开始每日学习 THEN 系统 SHALL 给出“新卡 + 到期复习卡”的队列，并支持至少三种模式：词汇卡/填空/选择题。
3. WHEN 卡片生成或记忆技巧生成涉及 AI THEN 系统 SHALL 允许选择/覆盖 providerName，且失败时提供降级（例如仅创建空卡片骨架）。

---

### Requirement 7 — 学习测验系统：基于资料自动出题（选择/判断/简答）

**User Story:** 作为用户，我想像 NotebookLM 一样让 AI 根据资料生成测验题，用于复习与巩固理解，并能记录成绩与错题。

#### Acceptance Criteria

1. WHEN 用户请求生成测验 THEN 系统 SHALL 基于 Notebook 资料生成题目集合，至少包含：选择题、判断题、简答题。
2. WHEN 用户提交作答 THEN 系统 SHALL 给出参考答案/解析（AI 或规则），并记录测验结果（分数、错题、时间）。
3. IF 资料不足以支撑出题 THEN 系统 SHALL 给出原因并建议补充资料范围（或允许仅基于已选 sources 出题）。

---

### Requirement 8 — Notebook 首页进度总览：学习统计与跟踪

**User Story:** 作为用户，我想在 Notebook 首页看到进度总览（学习卡片、测验、资料规模、近期学习活动），以便形成持续学习反馈回路。

#### Acceptance Criteria

1. WHEN 用户进入 Notebook 首页 THEN 系统 SHALL 展示核心统计：到期卡数量/新卡数量、测验历史与趋势、资料数量、学习连续天数（如有）。
2. WHEN 用户完成一次学习或测验 THEN 系统 SHALL 及时更新统计（允许通过 AG‑UI 事件流增量更新）。

---

### Requirement 9 — Skills 系统：按专题生成 Agent Skills（给 AI Agent 用）

**User Story:** 作为用户/开发者，我想根据 Notebook 的学习主题自动生成可复用的 agent skills（提示词/工具说明/知识摘要），以便给 AI agent 直接使用。

#### Acceptance Criteria

1. WHEN 用户请求生成 skills THEN 系统 SHALL 基于该 Notebook 的资料与产出生成一组 skills，并持久化到 Notebook 目录（可版本化）。
2. WHEN skills 生成完成 THEN 系统 SHALL 提供可复制/导出（Markdown/JSON）与后续迭代入口（增量更新、重新生成）。

---

### Requirement 10 — 系统脚手架与最小可运行后端（AG‑UI + Sessions）

**User Story:** 作为开发者，我希望系统具备最小可运行的后端与端到端链路（Session → AI Run → AG‑UI SSE），以便持续迭代上述功能。

#### Acceptance Criteria

1. WHEN 系统启动 THEN 后端 SHALL 提供 `GET /health` 返回 200 OK。
2. WHEN 调用 `GET /api/info` THEN 后端 SHALL 返回非敏感诊断信息（默认 provider、model、endpoint、timeout、runtimeType、版本等）。
3. WHEN 调用 `POST /api/sessions` THEN 后端 SHALL 创建 session（threadId=sessionId），并允许可选 `providerName` 覆盖。
4. WHEN 调用 `POST /api/sessions/{id}/input` THEN 后端 SHALL 触发一次 run 并产生 AG‑UI 事件。
5. WHEN 连接 `GET /api/sessions/{id}/agui/events` THEN 后端 SHALL 先发送 `MESSAGES_SNAPSHOT`（必要，快照优先），可选发送 `STATE_SNAPSHOT`，随后进入 live stream。

## Non-Functional Requirements

### Repository/Protocol Hard Requirements（必须写入实现）

- **Port Policy**: 仓库内任何服务/示例/文档/默认配置 SHALL NOT 使用 `:5000`；默认后端 `:5678`、前端（Vite dev）` :5173`，且端口必须可配置。
- **Protobuf 铁律**: Agent State / Event Messages / EventSourcing Events / Configuration Objects 等跨边界类型 SHALL 使用 `.proto` 定义并生成代码；禁止手写 C# POCO 作为跨边界类型。
- **AG‑UI**: 事件类型 SHALL 使用框架 `src/Aevatar.Agents.AGUI/AgUiEvents.cs`；后端 JSON 序列化 SHALL 使用 camelCase；前端 SHALL 使用 `@agui/sdk` 对接 SSE。
- **LLMProviders 可配置**: provider SHALL 通过 `LLMProviders` 配置选择；默认 provider 从 `LLMProviders:Default` 获取；请求级允许覆盖；secrets 通过 `appsettings.secrets.json`（提供 `.example`）管理。
- **Aspire AppHost**: 必须提供 `*.AppHost` 项目，使用 Aspire 同时编排后端与前端进程。
- **start.sh**: 必须提供一键启动脚本：可选 kill 端口、退出清理子进程、等待 `http://localhost:<backend>/health` OK 后启动前端、向前端注入 API base url env。
- **Docs must be real**: `learning/docs/ARCHITECTURE.md`、`CONFIGURATION.md`、`DEVELOPMENT.md` 必须包含可执行的运行与调试说明。

### Code Architecture and Modularity

- **Single Responsibility Principle**: Core/Api/Agents/Frontend/AppHost 分层清晰，避免跨层耦合。
- **Modular Design**: Notebook/Sources/Encyclopedia/Cards/Quiz/Skills 各自形成可替换模块（避免用大量 if/else 堆叠特例）。
- **Dependency Management**: 新增 NuGet 版本统一写入 `Directory.Packages.props`（Central Package Management）。

### Performance

- Notebook context 构建必须有界（token/片段数/大小限制），避免无上限拼接导致卡死。
- 大文件导入与索引构建应为异步任务（UI 不阻塞，事件流反馈进度）。

### Security

- API keys/密钥不得写入仓库；默认仅绑定 `127.0.0.1`；`/api/info` 不得泄露敏感值。
- 本地资料解析必须避免执行任意脚本/命令；对外导出需显式用户确认。

### Reliability

- AI 调用失败不得破坏既有数据；关键写入采用 best‑effort + 可观测日志；支持重试。

### Usability

- 桌面端交互应“先跑起来”：Notebook 列表清晰、会话可追溯、进度面板可理解；AI 功能的失败提示要可行动（给出配置入口与下一步）。

## Open Questions（需要你确认，避免走偏）

1. Notebook 对应目录：默认放在 `~/AevatarLearning/` 之类的固定根目录，还是首次创建时让用户选择根目录？
2. 资料类型 MVP 需要支持哪些：仅文本/Markdown/PDF，还是要包含网页抓取、图片 OCR、音频转写？
3. 是否要求离线可用（本地模型/本地 embeddings）？还是默认联网调用云端 Provider？
4. 学习卡片与测验的“正确答案/评分”偏好：更偏严格（可复现评分）还是允许 AI 评分（更灵活但不稳定）？
5. skills 的目标格式偏好：更像“提示词 + 工具说明”的 Markdown，还是要产出可直接被 Agent 代码消费的结构化（JSON/Proto）？


