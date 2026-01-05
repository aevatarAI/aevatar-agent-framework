### 0. 产品名（暂定）

NovelOS（Aevatar Novel Studio）

---

### 1. 背景与问题

对标 FeelFish（参考：[FeelFish Documentation](https://www.feelfish.com/en/docs)），但我们要解决的核心痛点是：

- **作者掌控力不足**：工具让 AI 写得多，但作者无法一眼看清全局结构、设定债务、时间线风险。
- **缓存不同步**：软件内部缓存与文件真实内容漂移，导致作者不敢用外部编辑器（Cursor）改稿。
- **提示词工程脆弱**：靠堆 prompt 维持一致性，缺乏可回放、可审计、可回归的工程体系。

---

### 2. 产品愿景与理念（不可妥协）

- **目标**：让作者提高对自己故事的掌控力——看得见、改得动、改得起、改了不崩。
- **AI 永远是初稿**：可发布，但只是一层可用版本；作者通过写作与输出完成自我。
- **文件是唯一真相源（SSOT）**：
  - 正文 `.txt`（纯文本，不输出 Markdown 语法）
  - 其他产物 `.md`
  - SQLite 仅索引/加速读，可删可重建，任何冲突“文件赢”

---

### 3. 目标用户与使用场景

- **核心用户**：长篇网络小说作者（百万字级），重度使用 Cursor/本地编辑器。
- **关键场景**：
  - 全自动流水线写到某个 `Story` 结束
  - 作者随时插手改稿（外部编辑器也行）→ 立刻看到后续牵连与可选路线图
  - 导入 TXT 参考文本：学习风格、抽取/融合设定

---

### 4. 核心对象模型

- Book/Novel → Volume → Story（剧情大纲）→ Chapters

---

### 5. 功能需求（按“掌控力”组织）

#### 5.1 基础写作闭环

- 章节生成（初稿）→ 自动润色 → 审校 → 保存 revision
- 支持暂停点与自动继续（跑到 `Story` 结束）

#### 5.2 偏离驱动联动改写（作者意志最高信号）

- 对比“AI 初稿 vs 作者改稿”生成：
  - 偏离结构化条目（剧情/人设/语气/信息密度/设定/时间线/节奏…）
  - 一键提示词/指令（Author Intent）
  - 后续影响分析 + 备用方案（路线图）

#### 5.3 故事单元测试（Narrative Tests）

- 作者以规则/约束形式定义“不可违背的故事逻辑”
- 每次生成/改稿后自动跑
- 必须输出：失败定位（哪里拽歪）+ 原因 + 修复建议（路线图）

#### 5.4 写作日志（按会话聚合）

- 自动聚合一次写作会话内的关键事件：
  - 偏离、闸门决策、单元测试、影响分析、采纳方案、分支合并决策
- 输出：
  - 会话日志（md）
  - 可复用的“作者意志提示词”（md）

#### 5.5 Story Control Panel（故事仪表盘）

- 进度：Volume/Story/Chapter
- 风险：时间线冲突、设定引用违规、测试失败
- 债务：未回收伏笔清单（Setup/Payoff Ledger）
- 关系：人物出场/关系网
- 热度：力量体系/规则引用热度

#### 5.6 伏笔台账（Setup/Payoff Ledger）

- 结构化记录承诺/债务/回收
- 自动提示：该回收了/被改稿破坏了/可升级爆点

#### 5.7 设定治理（Canon Governance）

- 设定变更必须显式记录：原因/影响范围/追溯策略
- 变更触发联动扫描与修复路线图（可分支处理）

#### 5.8 分支与合并（Rewrite Branch / Merge）

- 多方案并行写作、对比、择优合并
- 合并策略：整枝择优 / 章节择优 / 片段择优（需强 diff 纪律）

#### 5.9 读者画像模拟（Reader Personas）

- 多画像对章节输出：爽点/困惑/信息负荷 等评分与解释
- 输出 2-3 条可选优化路线（保守/激进/爽点强化/信息降噪）

---

### 6. 技术与架构要求（高层）

- 技术栈：Tauri + React + .NET sidecar（Aevatar Agent Framework）
- Multi-agent + Cognitive Mesh DSL 负责编排
- Agent Skills（`SKILL.md`）负责 SOP/模板/rubric 按需加载（见 `novel/agent_skills/`）

---

### 7. 非功能需求

- **可靠性**：任何时候不允许“缓存不同步”；外部编辑器改稿必须被实时感知与索引刷新
- **可回放/可审计**：关键产出可追溯到输入、偏离、决策、测试结果与路线图
- **性能**：百万字/500万字规模下可用（按需加载、增量索引、避免全书塞上下文）
- **安全**：API Key 仅引用（不入事件/日志），支持未来密钥管理

---

### 8. 里程碑（与 PLAN 对齐）

- Phase 1：写作闭环 + 偏离联动 + Narrative Tests + 会话日志 + Dashboard v1
- Phase 2：设定抽取/一致性 + 伏笔台账 v1 + 设定治理 v1
- Phase 3：剧情讨论/大纲系统 + Branch/Merge v1 + Reader Personas v1

---

### 9. 核心用户旅程（Phase 1 必须跑通）

#### 9.1 旅程 A：全自动跑到 Story 结束（可暂停/可一键继续）

- 作者创建 Project → 新建 Volume/Story →（可选）写 Story 大纲
- 点击“Run Story”
  - 系统按配置的暂停点运行：大纲/段落计划/章节初稿/润色/审校/收敛
  - 若命中暂停点：进入协商（作者可改/可拒绝/可重写/可分支）
  - 若允许自动继续：一路跑到 Story 结束

#### 9.2 旅程 B：作者改稿（Cursor/外部编辑器）→ 立刻看到“后面哪里拽歪”+ 路线图

- 作者直接改 `.txt` 正文（外部编辑器）
- Sidecar 监听到文件变更并触发：
  - 偏离结构化条目（分类）+ 一键作者意志提示词
  - 影响分析：后续大纲/后续章节/设定/伏笔台账/时间线是否需要联动
  - Narrative Tests 自动运行：失败定位 + 修复路线图
  - 写作会话日志实时更新
- 作者在 UI 中一键选择路线：
  - 最小修补 / 大纲联动更新 / 分支重写 / 合并策略
- 点击“Continue”继续流水线（或切换到分支）

#### 9.3 旅程 C：导入 TXT → 学风格 / 抽设定 / 融合世界观

- 导入 TXT（v1 只支持 txt）
- 选择：
  - 学习风格（StyleProfile）
  - 抽取/融合设定（SettingProfile，可带提示词约束）
- 产物落盘为 `.md`，作者确认后纳入 Canon（受治理）

---

### 10. 页面与信息架构（IA）

#### 10.1 全局

- **Project Hub**：项目列表、最近活动、导入/新建
- **Project Settings**：API Key 引用、预算策略、暂停点/自动继续、skills root 配置

#### 10.2 Story 工作区（核心）

- **Story Workspace**
  - 左侧：Volume/Story/Chapter 树 + 分支切换
  - 中间：正文编辑器（`.txt`）+ revision/diff
  - 右侧：AI 面板（运行工作流/暂停点/建议/备用方案）

#### 10.3 掌控力面板（并列入口）

- **Story Control Panel（仪表盘）**：进度、硬错误、软警报、热度、债务与入口
- **Narrative Tests**：测试套件编辑（`.md`）+ 最新报告 + 失败定位跳转
- **Writing Session Logs**：会话列表 → 会话日志（`.md`）+ 作者意志提示词
- **Setup/Payoff Ledger**：台账与回收路线图（`.md`）+ 破坏检测
- **Canon Governance**：设定变更请求/审批/影响分析（`.md`）
- **Branches**：分支对比（测试/台账/画像）+ 合并策略
- **Reader Personas**：多画像评分与解释（`.md`）+ 改写路线

---

### 11. 关键交互与状态机（产品必须稳定的“骨架”）

#### 11.1 Story 运行状态机（最小）

- **状态**：Idle → Running → Paused(Gate) → Running → Succeeded/Failed/Cancelled
- **阶段**：StoryOutline → ParagraphPlan → ChapterDraft/Edit/Review（循环）→ StoryFinalize
- **闸门（Gate）**：
  - Opened（等待作者）→ Decision（Approve/RequestChanges/RejectAndRewrite）→ Continue

#### 11.2 写作会话状态机（按会话聚合）

- Started（自动/手动）→ Updated（偏离/测试/影响分析持续写入）→ Ended

#### 11.3 设定治理状态机（Canon Governance）

- Draft(Change Request) → Impact Scanned → Author Approved/Rejected → Propagation Plan Executed（可分支）

#### 11.4 分支/合并状态机（Rewrite Branch/Merge）

- Branch Created → Diverged Writing → Compare (tests/ledger/personas) → Merge Plan → Merge Applied

---

### 12. 验收标准（按 Phase，可回归）

#### 12.1 Phase 1 验收（必须）

- **SSOT 与同步**：
  - 作者用外部编辑器修改正文 `.txt` 后，UI 必须在合理时间内反映变化（并触发索引刷新），不允许“缓存不同步”。
- **偏离 → 路线图**：
  - 任意章节改稿后，必须自动产出：偏离结构化条目 + 一键作者意志提示词 + 影响分析/备用方案（`.md`）。
- **故事单元测试**：
  - 作者可编辑测试定义（`.md`），每次生成/改稿后自动运行并产出报告（`.md`），报告包含失败定位 + 修复路线图。
- **会话写作日志**：
  - 系统能把一次写作会话聚合成日志（`.md`），并生成可复用的作者意志提示词（`.md`）。
- **Dashboard v1**：
  - 至少展示：进度 + 硬错误（测试失败/时间线冲突）+ 关键入口（偏离/测试/会话日志）。

#### 12.2 Phase 2 验收（必须）

- **Setup/Payoff Ledger v1**：
  - 台账结构化落盘（`.md`），能标记 open/paid/broken，能生成回收路线图。
- **Canon Governance v1**：
  - 设定变更必须记录原因/影响范围/追溯策略，并触发影响扫描与修复路线图。

#### 12.3 Phase 3 验收（必须）

- **Branch/Merge v1**：
  - 支持创建分支并行写作，对比分支必须包含 tests/ledger/personas 的差异摘要，并能按策略合并。
- **Reader Personas v1**：
  - 对指定章节输出多画像评分与解释，并提供 2-3 条可选优化路线（落盘 `.md`）。

---

### 13. 开放问题（暂不阻塞 Phase 1）

- EPUB/PDF 导入（v1 只做 TXT）
- 云同步/多人协作
- 更强的测试 DSL（当前先用自然语言规则 + 报告定位）

---

### 14. 实现规范（必须遵守，否则必然失控）

这一节把 PRD 从“想要什么”落到“怎么做才不会烂”。

#### 14.1 SSOT 规则（实现必须对齐）

- **正文唯一真相源**：章节正文是 `.txt` 文件内容（UTF-8 纯文本）。
  - 系统生成/润色必须避免输出 Markdown 语法（标题/列表/粗体/代码块等）；作者写入的字符不做“格式纠错”。
- **其他资产唯一真相源**：大纲/台账/测试/日志/报告/治理文档一律 `.md`。
- **SQLite 永远不是 SSOT**：SQLite 只能做索引/加速读模型，可删可重建；冲突时**文件赢**。

#### 14.2 项目落盘目录布局（v1 建议标准）

作者创建项目时选择一个本地目录作为 Project Root（后文用 `PROJECT_ROOT/` 表示）。

```text
PROJECT_ROOT/
  project.md                      # 项目说明与设置（不含密钥）
  volumes/
    01-<volume_slug>/
      volume.md
      stories/
        001-<story_slug>/
          story_outline.md
          chapters/
            001-<chapter_slug>.txt
            002-<chapter_slug>.txt
          artifacts/
            deviation/
              <session_or_run_id>_deviation_report.md
              <session_or_run_id>_deviation_prompt.md
              <session_or_run_id>_change_impact.md
              <session_or_run_id>_backup_options.md
            tests/
              narrative_tests.md
              <run_id>_test_report.md
            ledger/
              setup_payoff_ledger.md
              payoff_roadmap.md
            canon/
              canon_change_request.md
              canon_change_impact.md
            personas/
              <run_id>_reader_persona_report.md
            branches/
              <branch_name>/
                chapters/         # 分支覆写（只放变更文件；未变更文件回退到主线）
          sessions/
            <session_id>/
              writing_session_log.md
              author_intent_prompt.md
  .index/
    novel.sqlite                  # 索引数据库（可删可重建）
```

- **命名规范**：
  - `volume_slug/story_slug/chapter_slug`：建议 ASCII + `-`（避免跨平台文件名坑）
  - 顺序前缀用于稳定排序：`01-`、`001-`
- **分支目录（v1）**：
  - 分支不复制整棵树，只放“变更文件”（overlay）；读取时按“分支→主线”回退查找。

#### 14.3 文件写入与一致性（避免半写入/自触发）

- **原子写入**：写文件必须采用“写临时文件 → rename 覆盖”策略，避免 watcher 读取到半写入内容。
- **自触发抑制**：Sidecar 自己写入文件时，必须带“写入会话标识”，并在 watcher 侧做短窗口去抖/合并，避免同一改动触发多次流水线。
- **冲突策略**：
  - 当流水线运行中检测到作者外部编辑：自动进入 Gate（暂停点），要求作者选择“继续/回滚/分支”。

---

### 15. 文件监听与增量索引（Phase 1 的工程底座）

#### 15.1 监听范围与过滤

- 监听 `PROJECT_ROOT/` 下所有 `.txt` 与 `.md` 文件变更
- 忽略：
  - `.index/`（SQLite）
  - `bin/obj/` 等构建产物
  - 临时文件（例如 `*.tmp`、编辑器 swap）

#### 15.2 去抖与合并

- 将短时间内的多次变更合并为一次“会话事件”
- 以“最终落盘内容（hash）”驱动索引更新与后续分析，禁止用“文件系统事件次数”当真实信号

#### 15.3 索引更新策略（SQLite 可重建）

- **文件注册表**：path → hash → last_modified → asset_type
- **全文索引（FTS）**：对 `.md` 与 `.txt` 做全文检索（按需：正文可只存摘要/分段索引）
- **实体/引用索引**：
  - 角色/地点/势力/设定术语出现位置
  - 设定引用热度、时间线冲突点、伏笔债务统计

---

### 16. Sidecar API（v1 建议，面向 Tauri/React）

原则：**API 的数据结构必须对应 Protobuf 契约**（见 `novel/protos/*.proto`），即使前端先用 JSON 映射，也不能出现手写 DTO 漂移。

#### 16.1 同步接口（HTTP/gRPC 二选一）

- **推荐路线**：HTTP + Protobuf JSON 映射（前端易接入），内部仍使用 Protobuf 作为唯一模型
- **可选路线**：gRPC（更强契约，但前端接入成本更高）

#### 16.2 必要接口（Phase 1）

- **Project**
  - 创建/打开项目（绑定 `PROJECT_ROOT/`）
  - 读取项目树（Volume/Story/Chapter）
- **Run Control**
  - 启动 StoryRun（对应 `StoryRunRequestedEvent`）
  - Pause/Resume/Cancel（对应 `StoryRunPauseRequestedEvent` 等）
  - Gate Decision（对应 `StoryGateDecisionSubmittedEvent`）
- **Deviation/Impact**
  - 触发偏离影响分析（对应 `StoryDeviationImpactAnalysisRequestedEvent`）
  - 获取产物引用（`ArtifactRef` → 文件路径）
- **Narrative Tests**
  - 触发测试运行（对应 `StoryUnitTestsRunRequestedEvent`）
  - 获取测试报告（`.md`）
- **Writing Session**
  - 手动开始/结束会话（对应 `WritingSessionStartedEvent/EndedEvent`）
  - 获取会话日志与作者意志提示词（`.md`）

#### 16.3 事件流（Phase 1 必须）

UI 必须订阅 Sidecar 事件流以实现“实时同步”，最小事件包含：

- StoryRun 状态变化：`StoryRunStatusChangedEvent`
- 产物生成：`StoryArtifactProducedEvent`
- 测试完成：`StoryUnitTestsRunCompletedEvent`
- 会话日志更新：`WritingSessionLogUpdatedEvent`

实现可选：WebSocket / SSE / 本地 IPC；但必须保证“外部编辑器改稿”也能驱动 UI 更新。

---

### 17. Agent / Skills / Tools / DSL 的实现划分（可直接照着写）

#### 17.1 什么时候用 Agent（长期职责）

当能力需要维护长期状态、需要事件审计回放、需要与其他能力并行协作时，用 Agent：

- StoryDashboard、SetupPayoffLedger、CanonGovernance、BranchManager、Timeline、LoreKeeper、NarrativeTest、WritingJournal、ReaderPersona

#### 17.2 什么时候用 Skill（流程/模板/rubric）

当能力本质是 SOP/模板/评分 rubric，需要按需加载并可独立迭代时，用 Skill（`novel/agent_skills/*/SKILL.md`）：

- 伏笔台账 SOP、设定治理 SOP、合并策略、读者画像评分 rubric、单元测试模板、偏离分析流程

> Aevatar 支持 `skills_list/skills_load` 按需加载，并可用 `allowed-tools` 做“硬约束”（见仓库 `docs/AGENT_SKILLS_GUIDE.md`）。

#### 17.3 什么时候用 Tool（执行层）

所有“真实动作”必须落到 Tools：

- 文件读写（SSOT）
- diff/merge 算法
- SQLite 索引与查询
- 图谱计算（关系网/引用热度）

#### 17.4 什么时候用 DSL（编排）

所有 feature 都应被编排为可回放工作流（A/B/C/D/E/F/G/H/I/J）。DSL 负责：

- 串联多个 Agent/Tool
- Gate 暂停/继续
- 产物落盘与 trace 回放

---

### 18. Phase 1 技术实现拆解（工程可执行）

#### 18.1 必须先做的“地基”

- Project Root 目录布局与解析（Volume/Story/Chapter 映射）
- 文件监听 + 去抖 + 增量索引（SQLite 可删可重建）
- 事件流推送到 UI（实时）

#### 18.2 Phase 1 功能实现顺序（建议）

1) Story 运行控制（Run/Gate/Pause/Resume）+ 基础写作闭环  
2) 偏离检测（diff）→ 结构化偏离条目 → 影响分析/备用方案（落盘 `.md`）  
3) Narrative Tests 定义/运行/报告（落盘 `.md`）  
4) 会话日志聚合（落盘 `.md` + author_intent_prompt）  
5) Dashboard v1（进度 + 硬错误 + 关键入口）  

---

### 19. 附录：文档/契约引用

- Protobuf 契约目录：`novel/protos/`
  - 资产与配置：`novel/protos/novel_assets.proto`
  - 流水线/事件：`novel/protos/novel_pipeline.proto`
- Skills 目录：`novel/agent_skills/`


