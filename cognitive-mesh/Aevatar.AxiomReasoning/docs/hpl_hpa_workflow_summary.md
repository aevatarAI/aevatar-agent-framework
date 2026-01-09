# Hypothesis Promotion Loop (HPL) + HPA 工作流程总结

## 🎯 工作流目标

通过迭代循环的方式，从用户提供的假设池中选择假设，进行并行评估、数学验证（HPA），最终将验证通过的假设提升为定理。

---

## 📋 完整执行流程

### 🔵 阶段 0：初始化（首次运行）

#### `ensure_state` → `init_state`
**作用**：解析输入，初始化状态

**LLM 任务**：
1. 从 `raw_task` 提取：
   - **公理** (Axioms)：以 "O1:"、"A1:" 等开头的行
   - **目标** (Focus)：`Focus (optional):` 下的文本
   - **种子假设** (SeedHypothesis)：`SeedHypothesis (optional):` 下的文本
   - **假设池** (ExistingHypothesis)：`ExistingHypothesis (optional):` 下的文本 ⭐
2. 创建结构性假设 S1（量子结构设置）
3. 从 `existing_hypothesis` 中选择初始假设作为 `current_hypothesis`

**输出**：完整的 `state` 对象（iteration=0, theorems=[], seen_hypotheses=[]）

---

### 🔵 阶段 1：HPA 准备（每轮开始）

#### `hpa_prepare`
**作用**：计算 HPA 确定性信号

**操作**：
1. **scan_target**：计算扫描目标 Θ
   - 公式：`Θ = (iteration × α + seed_phase) mod 2π`
   - 其中 `α = φ⁻¹ ≈ 0.618`（黄金比例倒数）
   - 作用：提供确定性探索方向，避免共振
2. **embed_list**：将已有定理嵌入复平面/八元数空间
   - 为后续的相似度计算和关联子计算做准备

**输出**：
- `scan`：扫描目标 Θ
- `theorem_index`：定理的 HPA 嵌入

---

### 🔵 阶段 2：选择候选假设

#### `ensure_candidate` → `propose_hypothesis`（如果需要）

**逻辑**：
- 如果 `state.current_hypothesis` 存在 → 直接使用（从 B 池选择的）
- 否则 → LLM 从 `state.existing_hypothesis` 中选择一个

**LLM 任务**：
- 从假设池中选择一个假设
- 必须包含 `depends_on`（依赖的公理/定理 ID）
- 必须包含 `factor_sequence`（有序推理路径）
- 不能是 `seen_hypotheses` 中的

**输出**：假设对象 `candidate`

---

### 🔵 阶段 3：嵌入和检索

#### `embed_candidate`
**作用**：将候选假设嵌入 HPA 空间
- 计算复平面坐标
- 计算八元数方向向量
- 附加到 `candidate.hpa` 字段

#### `retrieve_relevant_facts`
**作用**：基于候选假设的 `statement` 检索相关的定理/事实

**输出**：`relevant_facts`（用于后续验证）

---

### 🔵 阶段 4：侦察阶段（并行评估）

#### `build_scouts`
**作用**：创建 N 个 Worker（通常 N = 2K-1 = 5，K=3）

#### `refute_scout`
**作用**：每个 Worker 并行评估假设 A

**每个 Worker 输出**：
```json
{
  "accept": bool,                    // 是否接受假设
  "strong_refutation": bool,        // 是否有强反驳（致命反例）
  "proof": string,                  // 证明草图（如果接受）
  "gap_or_counterexample": string,  // 间隙或反例（如果拒绝）
  "depends_on": [string],           // 实际使用的依赖
  "factor_sequence": [string],       // 推理路径
  "proposed_b": [...]               // 1-3 个备选假设（如果拒绝）
}
```

#### `aggregate_scout`
**作用**：聚合所有 Worker 的判决

**统计**：
- `accept_count`：接受假设的 Worker 数量
- `strong_refutation_count`：强反驳的数量
- `scout_verdicts`：所有 Worker 的详细判决
- `scout_b_candidates`：所有 Worker 提出的 B 候选（去重）

---

### 🔵 阶段 5：HPA 证据计算

#### `hpa_evidence` (compute_hpa_evidence)
**作用**：计算 HPA 证据指标

**计算内容**：
- `coherence`：一致性指标（越高越好）
- `gap_norm`：间隙范数（越小越好）
- 基于候选假设的嵌入和已有定理的嵌入

#### `hpa_associator` (compute_associator)
**作用**：计算关联子

**计算内容**：
- `associator_mean`：关联子均值（非结合性指标）
- 基于 `factor_sequence` 的顺序依赖
- 值越大，说明推理路径越脆弱

---

### 🔵 阶段 6：侦察门控（第一个决策点）

#### `scout_gate`
**作用**：决定是否进入验证阶段

**决策逻辑**：

**情况 A：强反驳**
```yaml
如果 strong_refutation_count > 0:
  → 直接进入 B 池选择（跳过验证）
```

**情况 B：通过门控（进入验证）**
```yaml
条件（满足其一）：
  1. 强接受：accept_count >= k 且 strong_refutation_count == 0
  2. 软接受 + HPA 稳定：
     - accept_count >= k-1
     - coherence >= min_coherence
     - gap_norm <= max_gap_norm
     - associator_mean <= max_associator_mean
```

**情况 C：不通过门控（不验证）**
```yaml
如果上述条件都不满足:
  → 进入 B 池选择（不验证）
```

---

### 🔵 阶段 7A：验证路径（如果通过门控）

#### `decide_verify`
**作用**：最终决定是否验证

**双重门控**：
- 如果 `accept_count >= k` → 直接验证
- 否则需要 `accept_count >= k-1` + HPA 指标通过

#### `maker_argumentation`
**作用**：调用 MAKER 工作流生成结构化论证

**MAKER 任务**：
- 生成结构化、依赖引用的论证
- 每个步骤必须引用依赖（如 [O1, T2]）
- 遵循 HPA 协议：无隐藏假设，明确 gap δ

**输出**：`maker_solution`（结构化论证文本）

#### `verify_maker_solution`
**作用**：验证 MAKER 解决方案

**投票机制**：多个 Worker 判断是否 `proved`

**每个 Worker 输出**：
```json
{
  "proved": true/false,
  "reason": "判断理由",
  "depends_on": [...],
  "factor_sequence": [...]
}
```

**聚合结果**：`judgement`（最终判决）

#### `decide_promote`
**作用**：决定是否提升为定理

```yaml
如果 judgement.proved == true:
  → 提升为定理
否则:
  → 进入 B 池选择
```

---

### 🔵 阶段 7B：B 池选择（如果失败）

#### `ensure_b_pool_*` → `propose_fallback_b_*`（如果需要）

**三种情况**：

1. **侦察阶段失败** (`ensure_b_pool_scout`)
   - 如果 Workers 已经提出了 B 候选 → 使用它们
   - 否则 → LLM 从 `existing_hypothesis` 中选择 3-5 个备选

2. **验证失败** (`ensure_b_pool_verify_failed`)
   - 如果已有 B 池 → 使用
   - 否则 → LLM 从 `existing_hypothesis` 中选择 3-5 个备选

3. **不进入验证** (`ensure_b_pool_no_verify`)
   - 如果已有 B 池 → 使用
   - 否则 → LLM 从 `existing_hypothesis` 中选择 3-5 个备选

**LLM 选择规则**：
- 必须从 `existing_hypothesis` 中选择（不能生成）
- 不能是 `seen_hypotheses` 中的
- 应该解决当前的 refutation/gap 问题
- 优先选择最小改动的假设

#### `vote_next_hypothesis_*`
**作用**：从 B 池中选择最佳候选

**投票机制**：多个 Worker 投票选择最佳 B 候选

**输出**：`next_hypothesis`（下一个要尝试的假设）

---

### 🔵 阶段 8：提升为定理（如果验证成功）

#### `promote_to_theorem`
**作用**：将假设提升为定理

**操作**：
1. 创建新定理对象
2. 添加到 `state.theorems`
3. `state.iteration++`
4. 记录到 `state.history`
5. 清空 `state.current_hypothesis`
6. 添加到 `state.seen_hypotheses`

**输出**：更新后的 `state`

---

### 🔵 阶段 9：状态更新和递归

#### `update_state`
**作用**：更新状态

**更新内容**：
- `state.last_candidate` = 当前候选
- `state.last_worker_verdicts` = Worker 判决
- `state.last_judgement` = 最终判决
- `state.last_b_pool` = B 池（如果失败）
- `state.hpa_last` = HPA 信号（用于调试）

#### `stop_or_continue`
**作用**：决定继续或停止

**停止条件**：
```yaml
如果 state.done == true 或 state.iteration >= max_depth:
  → 停止，输出结果
否则:
  → 递归调用自身（回到阶段 1）
```

**递归参数**：传递所有参数，包括更新后的 `state`

---

## 🎨 工作流可视化

```
┌─────────────────────────────────────────────────────────────┐
│                    初始化阶段                                │
│  ensure_state → init_state (提取公理、假设池)              │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                    HPA 准备阶段                              │
│  hpa_prepare (计算 scan_target, 嵌入定理)                  │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                   选择候选假设                                │
│  ensure_candidate → propose_hypothesis (从 existing_hyp)   │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                   嵌入和检索                                  │
│  embed_candidate → retrieve_relevant_facts                 │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                   侦察阶段（并行）                            │
│  build_scouts → refute_scout → aggregate_scout             │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                   HPA 证据计算                                │
│  compute_hpa_evidence → compute_associator                  │
└──────┬──────────────────────────────────────────────────────┘
       │
       ▼
┌─────────────────────────────────────────────────────────────┐
│                   侦察门控（决策点）                          │
│  scout_gate: 强反驳? → B池 | 通过? → 验证 | 不通过 → B池   │
└──────┬──────────────────┬──────────────────┬───────────────┘
       │                  │                  │
       │                  ▼                  │
       │         ┌─────────────────┐         │
       │         │   验证路径       │         │
       │         │ MAKER → 投票    │         │
       │         └────────┬─────────┘         │
       │                  │                  │
       │                  ▼                  │
       │         ┌─────────────────┐         │
       │         │  proved?        │         │
       │         │ 是→提升 | 否→B池│         │
       │         └────────┬─────────┘         │
       │                  │                  │
       └──────────────────┴──────────────────┘
                          │
                          ▼
              ┌───────────────────────┐
              │   B 池选择            │
              │ choose_b_from_pool    │
              └───────────┬───────────┘
                          │
                          ▼
              ┌───────────────────────┐
              │   提升为定理           │
              │ promote_to_theorem    │
              └───────────┬───────────┘
                          │
                          ▼
              ┌───────────────────────┐
              │   递归或停止           │
              │ stop_or_continue      │
              └───────────────────────┘
```

---

## 🔑 关键设计特点

### 1. **假设池管理**
- 所有假设都从 `state.existing_hypothesis` 中选择
- `state.seen_hypotheses` 记录已尝试的假设（去重）
- 选择时自动排除已见过的假设

### 2. **HPA 三元组应用**

#### **Rotation (扫描 Θ)**
- **位置**：`hpa_prepare` 步骤
- **作用**：基于迭代次数和黄金比例提供确定性探索方向
- **避免共振**：无理旋转确保不会重复访问相同区域

#### **Factorization (因子化)**
- **位置**：所有 LLM 步骤
- **要求**：每个假设必须包含
  - `depends_on`：最小依赖集
  - `factor_sequence`：有序推理路径
- **作用**：用于嵌入计算和关联子计算

#### **Projection (投影/间隙 δ)**
- **位置**：验证和评估步骤
- **规则**：
  - 不接受隐藏假设
  - 任何缺失的引理必须明确声明为 `gap_or_counterexample`
  - 如果存在 gap，`accept=false` 或 `proved=false`

### 3. **多智能体协作**
- **Coordinator**：提出假设、聚合结果、决策
- **Workers**：并行评估、验证、投票
- **共识机制**：需要至少 K 个 Worker 同意才能通过

### 4. **双重验证机制**
- **第一层：侦察阶段**：快速并行评估，识别强反驳，计算 HPA 证据
- **第二层：MAKER 验证**：结构化论证，严格依赖引用，最终判决

---

## 📊 状态对象结构

```json
{
  "iteration": 0,                    // 当前迭代次数
  "max_iterations": 10,              // 最大迭代次数
  "axioms": ["O1: ...", "A1: ..."], // 公理列表
  "assumptions": [                   // 额外假设（如 S1）
    {"id": "S1", "statement": "...", "motivation": "..."}
  ],
  "theorems": [                      // 已证明的定理
    {"id": "T1", "statement": "...", "proof": "...", "depends_on": [...]}
  ],
  "existing_hypothesis": "...",     // 用户提供的假设池（关键！）
  "current_hypothesis": {...},      // 当前候选假设
  "seen_hypotheses": [...],          // 已尝试的假设（去重）
  "focus": "...",                    // 用户目标/探索方向
  "seed_hypothesis": "...",          // 用户种子假设
  "last_candidate": {...},           // 最后候选
  "last_worker_verdicts": [...],     // 最后 Worker 判决
  "last_b_pool": [...],              // 最后 B 池
  "last_judgement": {...},           // 最后验证判决
  "history": [...],                  // 历史记录
  "done": false,                     // 是否完成
  "status": "running"                // 状态
}
```

---

## 🔑 关键参数

### 共识参数
- `k`: 共识阈值（默认 3）
- `N`: Worker 数量（通常 2K-1 = 5）
- `max_rounds`: 投票最大轮数（默认 10）

### HPA 参数
- `hpa_alpha`: 黄金比例倒数（默认 0.618）
- `hpa_seed_phase`: 种子相位（默认 0.0）
- `hpa_beta_model`: Beta 模型（log_phase | omega_phase | random_prime_phase）
- `min_coherence`: 最小一致性阈值
- `max_gap_norm`: 最大间隙范数
- `max_associator_mean`: 最大关联子均值

### 控制参数
- `max_depth`: 最大迭代深度
- `top_k_facts`: 检索相关事实的数量
- `similarity`: 投票相似度阈值

---

## 💡 工作流特点

1. **确定性探索**：HPA 提供无随机性的探索方向
2. **显式依赖**：所有推理路径必须明确，避免隐藏假设
3. **快速收敛**：通过 HPA 指标和投票机制快速达成共识
4. **最小修复**：失败时选择最小改动的新假设
5. **可追溯性**：完整记录所有尝试的假设和证明过程
6. **假设池管理**：所有假设从用户提供的池中选择，确保可控性

---

## 📝 输出结果

工作流最终输出：
- `status`: 运行状态（running | completed | failed | limit）
- `iteration`: 迭代次数
- `state`: 完整状态对象
- `theorems`: 已证明的定理列表
- `current_hypothesis`: 当前假设
- `last_candidate`: 最后候选
- `last_judgement`: 最后判决
