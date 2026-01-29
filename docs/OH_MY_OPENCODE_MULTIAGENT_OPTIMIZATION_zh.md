## oh-my-opencode 的优势在哪里？Aevatar 多智能体系统如何用 Hook/Harness 继续进化

本页把 `docs/OH_MY_OPENCODE_RESEARCH.md` 的调研结论进一步落到 **Aevatar 现有 multi-agent（MAKER / Cognitive）** 的真实代码上，回答两个问题：

- **oh-my-opencode 的优势到底是什么**（不是“工具更多”）
- **Aevatar 有了 Hook/Harness 之后，系统里哪些地方可以借用拦截器做优化**（以及短中长期路线）

---

### 1) 一句话结论（给老板）

oh-my-opencode 的核心优势是：把“长链路 Agent 工程化必踩坑”（上下文爆炸、工具输出失控、非交互环境、会话恢复、可观测与可回滚）做成 **默认开启且可禁用** 的 Harness 层；Aevatar 已经具备 Tool Loop + MCP + Skills + Actor 多智能体底座，现在补齐 Hook/Harness 后，可以把 multi-agent 的稳定性与可控性统一收敛到框架层，而不是散落在每个 Coordinator/Worker 里。

---

### 2) Aevatar 现状（multi-agent 的真实入口）

#### 已有的多智能体骨架

- **MAKER（Agent 模式）**：`MakerCoordinatorGAgent` 协调 N 个 `MakerWorkerGAgent` 并行生成 + 投票早停
  - Coordinator：`src/Aevatar.Agents.Maker/Agents/MakerCoordinatorGAgent.cs`（+多个 partial）
  - Worker：`src/Aevatar.Agents.Maker/Agents/MakerWorkerGAgent.cs`
- **Cognitive（DSL Workflow）**：`CognitiveCoordinatorGAgent` 执行 workflow step；`fan_out/parallel` 分发到 `RoleAIGAgent`（由 `CognitiveStepExecutionHandler` 处理）
  - Coordinator：`src/Aevatar.Agents.Cognitive/Agents/CognitiveCoordinatorGAgent.cs`（+ Parallel/Llm 等 partial）
  - Worker：`src/Aevatar.Agents.Cognitive/Execution/CognitiveStepExecutionHandler.cs`
- **底层事件传播/父子层级**：`EventRouter`（Up/Down/Both、防循环、hop 计数）
  - `src/Aevatar.Agents.Core/EventRouting/EventRouter.cs`

#### 已补齐的 Harness 层（AI 侧）

- Hook 接口/上下文/管线：`src/Aevatar.Agents.AI.Core/Hooks/*`
- 与 LLM/Tool 的集成点：`src/Aevatar.Agents.AI.Core/AIGAgentBase.Hooks.cs`
- **关键提升（已落地）**：
  - `ChatStreamAsync` 的 streaming 路径也会跑 `BeforeLLMRequest` / `OnError` hooks（避免“同步 vs 流式”分叉）
  - `MakerWorkerGAgent` 的 streaming / non-streaming LLM 调用不再绕开 hooks（让 multi-agent 最关键的 worker 路径也享受同一套治理）

---

### 3) oh-my-opencode 的优势拆解 → Aevatar 的 Hook/Harness 映射

下面按 “oh-my-opencode 的 hook 分类” 映射到 Aevatar，并标注 **可立即用现有 Hook/Harness 做的优化** 与 **需要扩展 hook stage 才能更优雅的点**。

#### 3.1 上下文与 token 管理（context-window-monitor / preemptive-compaction / dcp）

**oh-my-opencode 优势**：把 token 预算从“经验主义”变成可观测、可回滚的配置项；在爆窗前做预处理（DCP + compaction）。

**Aevatar 现状**：
- 已有：`ContextBudgetMonitorHook`（warn-only，信号化）
- 已有：`ToolOutputTruncationHook`（防 tool 输出撑爆下一轮）
- 仍缺：DCP（剪枝旧工具输出/重复内容）与自动 compaction（摘要/检索/分层记忆）

**用现有 Hook/Harness 立刻能做**（短期）：
- **DCP（最小版本）**：在 `BeforeLLMRequestAsync` 中对 `ctx.LlmRequest.Messages` 做“去重/裁剪”：
  - 优先裁剪旧的 tool 消息、重复的 tool 结果、重复的系统块
  - 只做 bounded 操作（O(n) + 限制扫描字符数）
- **preemptive compaction（最小版本）**：当 metadata 被 `ContextBudgetMonitorHook` 标记后：
  - 在 `BeforeLLMRequestAsync` 注入一条 system 消息，要求模型优先检索/优先复用已有 tool 结果
  - 或者主动 **收敛工具可见集合**（只允许最少工具）——纯收敛、默认安全

**需要扩展的 hook stage**（中期）：
- **AfterStreamComplete / OnStreamToken**：让 streaming 场景也能做更精细的上下文治理（例如 token 级预算、idle watchdog）

#### 3.2 会话恢复与降级（session-recovery / auto_resume / context-window-limit-recovery）

**oh-my-opencode 优势**：长任务不怕中断，能恢复；遇到上下文过长/模型差异能自动降级重试。

**Aevatar 现状**：
- MAKER 有 `MakerStateRecovery`（任务级状态持久化）
- Cognitive 有稳定 worker id、step event 记录、ExecutionTraceStore（best-effort）
- AI.Core hook 已有 `OnErrorAsync` 入口，但当前 **不能在 hook 内直接“吞错并重试”**（Generate 会 rethrow）

**用现有 Hook/Harness 立刻能做**（短期）：
- `OnErrorAsync` 里做 **错误分类 + 可观测标注**：
  - 识别“上下文过长 / 413 / provider 特定错误码”
  - 写入 `ctx.Metadata`（不含 secrets）并打日志/事件，供 Coordinator 决策（例如换 provider、切短 prompt）

**需要扩展的 hook stage**（中期）：
- **Retry policy hook**：允许 hook 建议一次“安全重试”：
  - e.g. `ctx.Metadata["retry"]=true` + `ctx.Metadata["retry_mode"]="detach_tools"`，由 core 执行有限次重试（有上限）

#### 3.3 工具输出治理（tool-output-truncator / grep-output-truncator）

**oh-my-opencode 优势**：工具输出默认截断、结构化标注，避免把上下文炸穿。

**Aevatar 现状**：已实现 `ToolOutputTruncationHook`，并且预算集中在 `AevatarAgentHookOptions.MaxToolOutputChars`。

**进一步优化（中期）**：
- 把“截断标注”做成 **Protobuf 结构化字段**（跨边界契约，满足 `AGENTS.md` 铁律），而不是仅 metadata

#### 3.4 目录/规则语境注入（directory-readme-injector / rules-injector）

**oh-my-opencode 优势**：把“项目语境”默认注入（但允许禁用），减少人肉贴规则。

**Aevatar 可落点（短期）**：
- 新增一个内置 hook（`BeforeLLMRequestAsync`）：
  - best-effort 读取 `.aevatar/rules.md` / `README.md`（有最大字符限制）
  - 注入到 `SystemPrompt` 或作为系统消息附加
  - 与 `AgentSkills` 形成互补：skills 解决“按需加载过程性技能”，rules 注入解决“全局约束与项目语境”

#### 3.5 非交互环境（non-interactive-env / interactive-bash-session）

**oh-my-opencode 优势**：默认假设“非交互”，工具执行自动加 `--yes`/超时，避免卡死。

**Aevatar 现状**：`DotNetFileSkillTool` 已在读 stdout/stderr 时做了“持续 drain + 只截断存储”避免 deadlock；但更普遍的命令工具还在 roadmap。

**Aevatar 可落点（中期）**：
- 当未来引入 command/sandbox tool 时，用 `BeforeToolExecuteAsync` 自动注入非交互参数（仍遵循 AllowDangerousTools）

---

### 4) “拦截器还能用在哪”：从 AI Hook 扩展到 Multi-Agent 基础设施 Hook

AI hook 解决的是 “LLM + Tool”。multi-agent 系统还缺一层对 **事件传播/父子关系/可观测** 的拦截器。

#### 4.1 EventRouter 拦截器（建议新增：IAevatarEventHook）

`EventRouter.CreateEventEnvelope` 目前 `CorrelationId = Guid.NewGuid()`，并且代码里有 TODO（“Get from context”）。

**价值**：把 Coordinator → Worker → Coordinator 的整条链路串起来（Trace/Log/ExecutionTrace/审计都更容易）。

**建议**：
- 增加 `IAevatarEventHook`：
  - `BeforeCreateEnvelope` / `AfterCreateEnvelope`
  - `BeforeRoute` / `AfterRoute` / `OnRouteError`
- 默认内置一个 `CorrelationPropagationHook`：
  - 读取 `Activity.Current.TraceId` 或从 event payload（若包含 request_id/execution_id）推导
  - 统一写入 `EventEnvelope.CorrelationId`

#### 4.2 Actor 生命周期拦截器（建议新增：IAevatarActorLifecycleHook）

用于统一做：
- parent-child link 的观测/审计
- worker pool 创建/回收策略
- crash 后恢复（auto_resume）

---

### 5) 可执行的下一步任务清单（按收益/风险排序）

#### P0（已完成）
- [x] streaming 路径也能跑 hooks（Before + OnError）
- [x] MAKER Worker 不再绕开 hooks（stream + non-stream）

#### P1（低风险、收益高）
- [ ] 新增内置 hook：`DirectoryRulesInjectorHook`（可禁用、限长、best-effort）
- [ ] 新增内置 hook：`DcpToolOutputPrunerHook`（BeforeLLMRequest，对 Messages 做 bounded 剪枝）

#### P2（需要少量框架改造）
- [ ] 扩展 hook stage：`AfterStreamComplete`（让 streaming 也能跑 AfterLLMResponse 类能力）
- [ ] 扩展 hook stage：`OnStreamToken`（实现 idle watchdog、token 预算/节流）

#### P3（跨模块拦截器：事件与生命周期）
- [ ] `IAevatarEventHook` + 默认 correlation 传播（补 EventRouter 的 TODO）
- [ ] `IAevatarActorLifecycleHook`：worker pool 生命周期治理与 auto_resume

---

### 6) 备注：为什么这条路线“味道对”

- **让特殊情况消失**：把每个 Coordinator/Worker 自己写的“工程护栏”消掉，收敛到 Harness 层。
- **默认安全**：hook 只能收敛权限（Allow* 不可被扩大），并且关键点有 defense-in-depth。
- **可治理**：默认开启，但有显式禁用入口（disabled_hooks 同构）。


