## Hypothesis Promotion Loop（HPL）实施计划

本文件把 `hypothesis_promotion_loop.md` 的设计拆成可落地的实现阶段，并明确当前 DSL/引擎的真实能力边界。

- **设计文档**：`workflows/docs/hypothesis_promotion_loop.md`
- **目标产物（里程碑 A）**：新增可运行的 workflow：`workflows/hypothesis_promotion_loop.yaml`

---

## 约束与现实（先把坑写死）

- **DSL 字段必须“写了就生效”**：目前已补齐 `defaults / red_flag / max_length / strict_parse / timeout_seconds / idle_timeout_seconds / include_failures` 的解析与注入；未知字段仍会被 YAML 解析器忽略（但不再影响关键护栏）。
- **`fan_out` 失败条目**：默认仍只返回成功项；当 `include_failures=true` 时，会把失败/解析失败以结构化对象形式加入结果列表，便于 `transform` 统计与红旗记录。
- **Timeout/IdleTimeout 可配置**：`llm_call.timeout_seconds/idle_timeout_seconds`（Worker+Coordinator）与 `fan_out.timeout_seconds`（Coordinator）已支持 YAML 覆盖。

结论：
- 里程碑 A 先保证 **闭环能跑**（探索→验证→升级/换假设→递归），并使用现成的 `transform/retrieve_facts` 做 0-token 聚合与 RelevantFacts 裁剪。
- 里程碑 B 再把“必须护栏”做成 **可配置 + 可观测 + 不隐性失效**。

---

## 里程碑 A：只加 YAML，把 HPL 跑通（最小可运行闭环）

### A.1 新增 workflow 文件
- **新增**：`src/Aevatar.Agents.Cognitive/workflows/hypothesis_promotion_loop.yaml`
- 复用 `axiom_theorem_loop.yaml` 的递归骨架（`workflow_call` 自调用 + `assign` unwrap）。

### A.2 初始化状态（state）
- `init_state` 一次 LLM call 产出最小字段：
  - `axioms[] / theorems[] / current_hypothesis / iteration / max_iterations / seen_hypotheses[] / last_* / history[] / done / status`

### A.3 RelevantFacts（token 护栏）
- 每轮用 `retrieve_facts` 从 `state.theorems` 检索 top-k（默认 20）相关定理（lexical，0 token）。
- 之后所有 prompt **只喂**：`axioms + relevant_facts + hypothesis A`。

### A.4 两段式探索（先反证 scout，再全量 worker）
- `refute_scout`：固定 2 个 scout worker（反例猎手/缺失前提猎手），输出严格 JSON verdict。
- 若 `strong_refutation=true` 命中：直接进入 B pool 投票，跳过全量探索。
- 否则 `prove_or_refute_with_workers`：N=2K-1 workers 并行探索，输出 JSON verdict。

### A.5 分流：进入验证 vs 进入投票
- 用 `transform` 统计：
  - `accept_count`、`strong_refutation_count`、`b_candidates`（flatten + distinct）。
- 条件：`accept_count >= K && strong_refutation_count == 0` → 验证阶段；否则 → 投票阶段。

### A.6 验证阶段（MAKER + vote verify）
- `workflow_call` → `maker-v2`
- `vote` 严格验证，输出 `{ proved, final_proof, depends_on }`
- `proved=true` → Promote A to Theorem；否则 → 回退到 B pool 选下一条 hypothesis。

### A.7 投票阶段（从 B[] 里选下一条 A）
- B pool 空则 fallback 生成少量候选（LLM call）。
- `vote_next_hypothesis` 选 “最容易成功/依赖最少” 的 B 作为下一轮 A。

### A.8 终止条件
- `iteration >= max_depth` → `done=true, status="limit"`。

---

## 里程碑 B：把“必须护栏”做成真的（少量引擎改动）

- **B.1 DSL 配置不再隐性失效**：已补齐 `defaults` 注入 + 关键护栏字段映射（见 `docs/PRIMITIVES.md` 的“通用护栏字段”）。
- **B.2 fan_out 保留失败条目**：已支持 `include_failures=true`（失败/解析失败以结构化对象进入结果列表）。
- **B.3 timeout 可配置**：已支持 `timeout_seconds/idle_timeout_seconds`（llm_call）与 `timeout_seconds`（fan_out）。

---

## 里程碑 C：测试与回归

- **C.1 单测**：`TransformExecutor` / `RetrieveFactsExecutor`（去重、过滤、中文 bigram 回退）。
- **C.2 集成测试（mock LLM）**：覆盖 4 条控制流：
  - scout 强反例早停
  - accept→验证→升级
  - 验证失败→B 投票
  - B 为空→fallback→继续

---

## 里程碑 A 的 step 映射（YAML → 设计文档）

- **State init**：`ensure_state` / `init_state`
- **RelevantFacts**：`retrieve_relevant_facts`
- **Scout**：`build_scouts` / `refute_scout` / `aggregate_scout`
- **Explore**：`build_provers` / `prove_or_refute_with_workers` / `aggregate_verdicts`
- **Verify**：`maker_argumentation` / `verify_maker_solution` / `promote_theorem`
- **Vote next**：`ensure_b_pool_*` / `vote_next_hypothesis_*` / `set_next_hypothesis_*`
- **Recurse**：`stop_or_continue` / `recurse` / `unwrap_state`


