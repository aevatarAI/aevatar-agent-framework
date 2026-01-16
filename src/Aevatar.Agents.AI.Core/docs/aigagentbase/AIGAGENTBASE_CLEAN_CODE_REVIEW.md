# AIGAgentBase — Clean Code Review 报告（2026-01-14）

> 范围：`src/Aevatar.Agents.AI.Core/AIGAgentBase*.cs` 及其相关 partial（Tools/Hooks/MCP/WebSearch/History/Memory/AgentSkills/泛型扩展）。
>
> 目标：识别坏味道（职责混杂、重复、深缩进、命名/注释不一致、隐式状态机）并给出可执行的整改路径；优先保持行为不变。

## 结论（TL;DR）

- **现状优点**：通过 `partial` 把“工具/外挂能力”分区到多个文件，整体已经比单文件 God-class 更可维护；并且大量逻辑明确标注 **best-effort**，避免把“辅助能力失败”升级为业务失败。
- **主要坏味道**：`AIGAgentBase` 仍然承担了过多“基础设施/平台能力”（Tool 管理、MCP 连接、WebSearch Provider 组装、AgentSkills 文件系统扫描与 YAML 解析、History summary 生成等），属于**“跨层聚合”**，导致：改动半径大、测试与边界更难清晰。
- **本轮已做的最小风险修复**：统一初始化守卫 + 初始化逻辑收敛（见“已落地修改”）。

## 已落地修改（行为保持）

### 1) 统一初始化守卫（消除散落 `_isInitialized`）

- **问题**：多个文件手写 `_isInitialized` 判断与异常文案，容易不一致、漏改。
- **处理**：在 `AIGAgentBase` 内引入 `EnsureInitialized()` + 统一错误信息常量，并替换使用点（`AIGAgentBase.Chat.cs` / `AIGAgentBase.cs` / `AgenticRagGAgent`）。

### 2) `InitializeAsync` 重复逻辑收敛 + 并发安全

- **问题**：两个 `InitializeAsync` 重载逻辑重复，且并发下可能重复初始化（多线程/并行请求/激活链路复杂时）。
- **处理**：引入 `_initializationSemaphore` + `InitializeAsyncCore(...)`，将 provider 创建差异通过委托注入，核心流程保持一致。

### 3) 注释与默认值一致性修正（无行为变化）

- **问题**：`EnableAgentSkills` 实际默认 `true`，但部分注释写“default false”，与 `Tools.cs` 的描述冲突，属于维护性“软 bug”。
- **处理**：修正 `AIGAgentBase.AgentSkills.cs` 注释为 **default true**。

### 4) AgentSkills 收敛：从 AIGAgentBase 抽到 `AgentSkillsRuntime`（已完成）

- **问题**：AgentSkills 一开始在 `AIGAgentBase.AgentSkills*.cs` 内同时承担：
  - roots/env 管理、目录扫描、YAML front matter 解析
  - 文档读取（glob）、资源列举、脚本执行（Python）
  - semantic ranking（embeddings + 索引）、lexical fallback、guidance snippet 提取
  - skills_load 的 dotnet-file tool import（危险能力）
  这使得 `AIGAgentBase` 变成“平台能力大杂烩”，改动半径过大。
- **处理**：新增 `internal sealed partial class AgentSkillsRuntime`，将上述重逻辑全部迁移；
  `AIGAgentBase` 仅保留 ToolDefinition 声明与委托路由（薄 façade）。
- **产物（关键文件）**：
  - `src/Aevatar.Agents.AI.Core/AgentSkills/AgentSkillsRuntime.cs`
  - `src/Aevatar.Agents.AI.Core/AgentSkills/AgentSkillsRuntime.LoadTools.cs`（skills_list/skills_load）
  - `src/Aevatar.Agents.AI.Core/AgentSkills/AgentSkillsRuntime.SkillSearch.cs`（find_helpful_skills/list_skills）
  - `src/Aevatar.Agents.AI.Core/AgentSkills/AgentSkillsRuntime.ReadSkillDocument.cs`
  - `src/Aevatar.Agents.AI.Core/AgentSkills/AgentSkillsRuntime.ResourceTools.cs`
  - `src/Aevatar.Agents.AI.Core/AgentSkills/AgentSkillsRuntime.Serialization.cs`
  - `src/Aevatar.Agents.AI.Core/AIGAgentBase.AgentSkills.Models.cs`（internal models）
  - 删除：`src/Aevatar.Agents.AI.Core/AIGAgentBase.AgentSkills.SemanticSearch.cs`

## 坏味道清单（按严重程度）

### P0（高风险/容易出错）

#### A) `AIGAgentBase<TCustomState, TCustomConfig>.OnActivateAsync` base 调用顺序可疑

- **现象**：`OnActivateAsync` 里先做 `Config.CustomConfig = Any.Pack(CustomConfig);`，再 `await base.OnActivateAsync(ct);`
- **风险**：
  - 框架通用约定通常是 **base first**（仓库规范里也强调这一点），否则 base 可能加载/覆盖 `Config`，导致自定义配置被覆盖或状态不一致。
  - 这类问题很难靠肉眼确认，需要对 `GAgentBase` / store 注入链路有强约束。
- **建议**：
  - 明确“为什么必须在 base 之前写入”并用注释固定下来；或者调整为 base first + 再 pack（更符合约定）。
  - 增加单测：激活后 `Config.CustomConfig` 是否保持预期值（覆盖两种情形：ConfigStore 有/无、已有值/空值）。

- **已修复（2026-01-14）**：
  - `OnActivateAsync` 改为 **base-first**，并在初始化 scope 内确保 `Config.CustomConfig` 始终是可 `Unpack<TCustomConfig>()` 的 typed Any；
  - `ConfigAI` 改为 *safe unpack → ConfigCustom → Any.Pack 回写*，避免 `Unpack` 失败与“改了不落盘”的隐形 bug。

### P1（结构性坏味道 / 维护成本高）

#### B) `AIGAgentBase` 仍是“跨层平台聚合点”

- **现象**：一个 base 同时做：
  - LLM 调用（Chat/Stream）
  - Tool 管理（注册、缓存、指令块、loop、防循环、事件）
  - Hook/Harness（横切治理）
  - MCP（连接、重连、生命周期清理）
  - WebSearch provider 组装（从 `IConfiguration` 与环境变量拼出 provider）
  - MemoryStore / VectorIndex 追加写
  - AgentSkills（目录扫描、YAML front matter 解析、脚本运行、资源读取、embedding index）
- **影响**：任何“平台能力”改动都可能触达基座，回归范围大；派生 agent 难以只启用/替换某一块能力。
- **建议（分阶段）**：
  - **Phase 1（不动 public API）**：把每块能力的“内部状态 + 细节函数”迁移到独立 `internal sealed class`（如 `McpRuntime`, `WebSearchFactory`, `AgentSkillsRuntime`），`AIGAgentBase` 只保留薄 façade。
  - **Phase 2（允许更强的 DI）**：把“可替换能力”抽成接口并 DI（例如 `IAgentSkillsService`, `IMcpConnector`, `IWebSearchProviderFactory`），AIGAgentBase 只 orchestration。

- **进度（2026-01-14）**：
  - `McpRuntime` 已抽离：`MCP` 的连接/重连/Dispose/节流状态迁移到 runtime，`AIGAgentBase.MCP.cs` 变为薄 façade。
  - `MemoryStoreRuntime` 已抽离：`MemoryStore/VectorIndex` 的 append/embedding 细节迁移到 runtime，`AIGAgentBase.MemoryStore.cs` 变为薄 façade（保留 scope/memoryId 的 override 点）。
  - `HistoryRuntime` 已抽离：History 的锁/compaction/summary 细节迁移到 runtime，`AIGAgentBase.History.cs` 变为薄 façade；`BuildLLMRequest` 使用 history snapshot 避免直接 lock State.History。

#### C) best-effort 模式大量重复（try/catch + LogDebug）

- **现象**：`AgentSkills` / `WebSearch` / `MCP` / `MemoryStore` 等处多次出现“吞异常 + best-effort”。
- **建议**：
  - 抽一个统一 helper：`BestEffort.TryAsync(...)` / `BestEffort.Try(...)`，规范记录字段（feature、operation、key、异常类型）。
  - 这样既减少重复，也能让可观测性更一致（现在多处是自由文本日志，难 grep 也难做 metrics）。

- **进度（2026-01-14）**：
  - 已新增 `Utils/BestEffort.cs`，并在 `MemoryStore` + `McpRuntime.Dispose` 路径落地（语义保持 best-effort）。

#### F) ChatAsync 的 Telemetry/Logging 横切逻辑散落

- **现象**：`ChatAsync` 里同时维护 stopwatch、activity、log scope、completed/failed 记录与 cancel 分支。
- **影响**：未来改 telemetry 字段/日志格式时容易漏改一处；而且业务流程被“仪表盘代码”淹没。
- **进度（2026-01-14）**：
  - 已新增 `Telemetry/LlmCallInstrumentationScope.cs`，把 **telemetry + logs + stopwatch** 收敛为一个 internal helper（保持现有行为不变）。

#### G) ChatStreamAsync 的“streaming + tool-call”策略是写死的

- **现象**：遇到 function call mid-stream 时，当前实现直接切换到 non-streaming tool loop 并一次性吐出最终答案。
- **风险**：策略和实现耦合在 while 循环里，未来要支持“工具调用后继续 streaming / 分段输出最终答案”会改动核心循环，容易引入回归。
- **进度（2026-01-14）**：
  - 引入 `StreamingToolCallMode` + `StreamingToolCalls`（protected virtual）作为策略点，默认保持当前行为不变。

#### H) `ChatStreamAsync` token 读取返回值存在 “default!” 哨兵风险

- **现象**：早期实现用 `default!` 作为 “no next token” 的占位，靠调用方先判断 `hasNext` 来避免访问。
- **风险**：这是对未来维护者的隐形约束（踩空就是 NRE），属于典型“用约定而不是类型表达语义”的坏味道。
- **进度（2026-01-14）**：
  - `TryReadNextStreamingTokenAsync` 改为返回 `AevatarLLMToken?`，用 `null` 表示结束，消除 `default!` 哨兵。

#### I) AgentYamlConfigApplier 对 Config 的修改缺少 modifiable scope

- **现象**：`AgentYamlConfigApplier` 直接改 `Config`，在非初始化/事件上下文下可能触发 StateProtection 异常。
- **进度（2026-01-14）**：
  - 统一使用 `StateProtectionContext.BeginInitializationScope()`（仅在必要时开启）包裹 Apply 路径，避免非法修改抛错。

#### J) YAML 工具策略缺少可扩展的 policy hook

- **现象**：Dangerous tools 的启用逻辑写死在 `AgentYamlConfigApplier`（只靠 allowlist 包含判断）。
- **风险**：未来若要更严格/更宽松的策略，需要修改多个地方，难以局部 override。
- **进度（2026-01-14）**：
  - 新增 `AIGAgentBase.ShouldEnableDangerousToolsFromYaml(...)`（protected virtual），`AgentYamlConfigApplier` 调用该 hook，默认行为不变。

#### K) YAML allowlist 拼接规则写死在 applier

- **现象**：`AgentYamlConfigApplier` 直接拼 allowlist（yaml.tools + yaml.skills），策略散落。
- **风险**：未来如果需要“按角色/环境增删工具”，必须改 applier 本体。
- **进度（2026-01-14）**：
  - 新增 `AIGAgentBase.BuildToolAllowlistFromYaml(...)`（protected virtual）并提供 internal wrapper，applier 只调用 hook，默认行为不变。

#### L) skills 工具自动注入规则缺少细粒度 hook

- **现象**：skills 工具是否自动加入 allowlist 仍然是整体策略，缺少“只允许部分 skills 工具”的扩展点。
- **进度（2026-01-14）**：
  - 新增 `AIGAgentBase.GetSkillToolsAutoIncludedFromYaml(...)`（protected virtual），默认仍然全量加入；需要细化策略时可 override。

#### M) Skill 工具名单散落在 applier 中

- **现象**：skills 工具列表硬编码在 `AgentYamlConfigApplier`，与策略 hook 分离。
- **风险**：未来若新增/移除 skills 工具，需要跨文件同步，容易遗漏。
- **进度（2026-01-14）**：
  - 新增 `AIGAgentBase.GetYamlSkillToolNames(...)`（protected virtual）并提供 internal wrapper，applier 从 base 获取名单，默认行为不变。

#### N) Dangerous tool 名单写死在 applier 中

- **现象**：危险工具名单硬编码在 `AgentYamlConfigApplier`，策略分散。
- **风险**：新增/移除危险工具时易遗漏；无法按 agent/环境覆盖。
- **进度（2026-01-14）**：
  - 新增 `AIGAgentBase.GetYamlDangerousToolNames(...)`（protected virtual）并提供 internal wrapper，applier 从 base 获取名单，默认行为不变。

#### O) YAML 工具策略仍散落在 applier

- **现象**：allowlist + dangerous enablement 的组合逻辑仍在 `AgentYamlConfigApplier`，可读性与复用性不足。
- **进度（2026-01-14）**：
  - 新增 `AIGAgentBase.BuildYamlToolPolicy(...)`（protected virtual）并提供 internal wrapper，applier 只接收 policy 并做 orchestration，默认行为不变。

#### P) YAML 策略决策缺少统一的审计日志

- **现象**：当前 YAML policy 的最终 allowlist / dangerous 决策在多个点形成，缺少统一、结构化的审计日志。
- **进度（2026-01-14）**：
  - 新增 `AIGAgentBase.LogYamlToolPolicyDecision(...)`（protected virtual），默认 Debug 日志，applier 在应用前调用，便于追踪策略变更。

#### Q) YAML skills roots 写死在 applier

- **现象**：skills roots 固定为 `~/.aevatar/skills`，无法按 agent / 环境定制。
- **进度（2026-01-14）**：
  - 新增 `AIGAgentBase.GetYamlDefaultSkillRoots(...)`（protected virtual），applier 从 base 获取 roots，默认行为不变。

### P2（低风险但影响可读性/一致性）

#### D) 命名/注释一致性问题

- **已修**：`EnableAgentSkills` 默认值注释不一致（default true vs false）。
- **仍建议**：
  - 对外暴露的开关（`EnableMcpServers`、`EnableMemoryStoreAppend`、`AllowDangerousTools` 等）统一以同一模版注释：**默认值、风险、依赖项（DI/Config）、失败语义（best-effort or fail-fast）**。

#### E) 工具系统中的缓存与线程安全语义需写死

- **现象**：`_registeredToolsCache` / `_functionDefinitionsCache` 在多个 async 路径刷新；当前代码依赖“赋值为新引用”的原子性保证一致性。
- **建议**：在 docs 里明确：缓存是“最终一致”，在刷新期间可能短暂看到旧版本；若要更强一致性，需引入锁/版本戳（但要小心性能）。

## 对现有实现的正面评价（保留理由）

- **Hook/Harness 的设计**：`CreatePolicySnapshot` + “defense in depth” 重新附加 tools 的做法是正确的（避免 hooks 扩权）。
- **Tool loop guard**：对“重复调用同一工具”强制停止工具循环，是实战友好的保护。
- **AgentSkills 的 bounded discovery**：BFS + depth cap + skip 常见目录，避免“扫爆磁盘”。

## 建议的下一步（你一句话我就继续做）

1) **修正/验证泛型基类 OnActivateAsync 顺序（P0）**：要么补注释说明“必须 base 前写入”的理由，要么改为 base-first 并加测试固化（最偏正确性）。
2) **继续抽离 MCP runtime（P1）**：将连接/重连/Dispose、节流与状态缓存抽为 `McpRuntime`，AIGAgentBase 仅路由调用。
3) **MemoryStore best-effort 写入统一化（P1/P2）**：抽 `BestEffort.TryAsync`，统一吞异常策略与日志结构，降低重复与 observability 噪音。


