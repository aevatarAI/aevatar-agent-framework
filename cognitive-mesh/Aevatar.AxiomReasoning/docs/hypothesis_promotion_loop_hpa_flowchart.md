# Hypothesis Promotion Loop (HPL) + HPA 工作流详解

## 工作流流程图

```mermaid
graph TD
    Start([开始]) --> InitState{State 存在?}
    InitState -->|否| InitLLM[init_state: LLM 提取<br/>- axioms<br/>- focus<br/>- seed_hypothesis<br/>- existing_hypothesis]
    InitState -->|是| HPAPrepare
    InitLLM --> HPAPrepare
    
    HPAPrepare[hpa_prepare:<br/>- scan_target Θ<br/>- embed_list 定理] --> EnsureCandidate{current_hypothesis<br/>存在?}
    
    EnsureCandidate -->|是| EmbedCandidate
    EnsureCandidate -->|否| ProposeHypothesis[propose_hypothesis:<br/>从 existing_hypothesis<br/>选择假设 A]
    ProposeHypothesis --> EmbedCandidate
    
    EmbedCandidate[embed_candidate:<br/>嵌入 HPA 空间] --> RetrieveFacts[retrieve_relevant_facts:<br/>检索相关事实]
    
    RetrieveFacts --> BuildScouts[build_scouts:<br/>创建 N 个 Workers]
    
    BuildScouts --> RefuteScout[refute_scout:<br/>并行评估假设 A<br/>- accept/refute<br/>- proof/gap<br/>- depends_on]
    
    RefuteScout --> AggregateScout[aggregate_scout:<br/>聚合判决<br/>- accept_count<br/>- strong_refutation_count]
    
    AggregateScout --> ComputeHPA[compute_hpa_evidence:<br/>计算 HPA 指标<br/>- coherence<br/>- gap_norm]
    
    ComputeHPA --> ComputeAssoc[compute_associator:<br/>计算关联子<br/>- associator_mean]
    
    ComputeAssoc --> ScoutGate{scout_gate:<br/>通过门控?}
    
    ScoutGate -->|否| EnsureBPoolScout{B 池存在?}
    EnsureBPoolScout -->|否| ProposeFallbackBScout[propose_fallback_b_scout:<br/>从 existing_hypothesis<br/>选择 3-5 个备选]
    ProposeFallbackBScout --> ChooseBFromPool
    EnsureBPoolScout -->|是| ChooseBFromPool[choose_b_from_pool:<br/>选择下一个 B]
    ChooseBFromPool --> EmbedCandidate
    
    ScoutGate -->|是| DecideVerify{决定验证?}
    
    DecideVerify -->|是| MakerArg[maker_argumentation:<br/>MAKER 结构化论证]
    MakerArg --> VerifyMaker[verify_maker_solution:<br/>投票验证]
    
    VerifyMaker --> DecidePromote{proved?}
    
    DecidePromote -->|是| Promote[promote_to_theorem:<br/>提升为定理<br/>- 添加到 theorems<br/>- iteration++]
    DecidePromote -->|否| EnsureBPoolVerify{B 池存在?}
    
    EnsureBPoolVerify -->|否| ProposeFallbackBVerify[propose_fallback_b_verify_failed:<br/>从 existing_hypothesis<br/>选择备选]
    ProposeFallbackBVerify --> ChooseBFromPool
    EnsureBPoolVerify -->|是| ChooseBFromPool
    
    Promote --> UpdateState[update_state:<br/>- 记录到 history<br/>- 清空 current_hypothesis<br/>- 更新 seen_hypotheses]
    
    UpdateState --> StopOrContinue{停止条件?<br/>done || iteration >= max_depth}
    
    StopOrContinue -->|继续| Recurse[recurse:<br/>递归调用工作流]
    Recurse --> HPAPrepare
    
    StopOrContinue -->|停止| End([结束<br/>输出结果])
    
    DecideVerify -->|否| EnsureBPoolNoVerify{B 池存在?}
    EnsureBPoolNoVerify -->|否| ProposeFallbackBNoVerify[propose_fallback_b_no_verify:<br/>从 existing_hypothesis<br/>选择备选]
    ProposeFallbackBNoVerify --> ChooseBFromPool
    EnsureBPoolNoVerify -->|是| ChooseBFromPool

    style Start fill:#90EE90
    style End fill:#FFB6C1
    style Promote fill:#87CEEB
    style ScoutGate fill:#FFD700
    style DecideVerify fill:#FFD700
    style DecidePromote fill:#FFD700
```

## 关键步骤详解

### 1. 初始化阶段

```yaml
ensure_state → init_state (如果 state 不存在)
```

**职责**：
- 从 `raw_task` 提取公理、目标、种子假设、现有假设池
- 初始化 state 对象（iteration=0, theorems=[], seen_hypotheses=[]）

### 2. HPA 准备阶段

```yaml
hpa_prepare:
  - scan_target: 计算扫描目标 Θ（基于 iteration 和黄金比例 α=φ⁻¹）
  - embed_list: 将已有定理嵌入复平面/八元数空间
```

**作用**：为后续的假设评估提供确定性探索方向

### 3. 假设选择阶段

```yaml
ensure_candidate → propose_hypothesis (如果需要)
```

**规则**：
- 优先使用 `state.current_hypothesis`
- 否则从 `state.existing_hypothesis` 中选择
- 必须包含 `depends_on` 和 `factor_sequence`
- 不能是已见过的假设

### 4. 侦察阶段（Scout Phase）

```yaml
build_scouts → refute_scout → aggregate_scout
```

**流程**：
1. 创建 N 个 Worker（并行）
2. 每个 Worker 评估假设 A：
   - `accept`: 是否接受
   - `strong_refutation`: 是否有强反驳
   - `proof/gap`: 证明草图或间隙描述
   - `depends_on`: 依赖的公理/定理 ID
   - `factor_sequence`: 有序推理路径
3. 聚合所有判决

### 5. HPA 证据计算

```yaml
compute_hpa_evidence → compute_associator
```

**计算内容**：
- `coherence`: 一致性指标
- `gap_norm`: 间隙范数（越小越好）
- `associator_mean`: 关联子均值（非结合性指标）

### 6. 侦察门控（Scout Gate）

```yaml
scout_gate: 决定是否进入验证阶段
```

**通过条件**（满足其一）：
- 强接受：`accept_count >= k` 且 `strong_refutation_count == 0`
- 软接受 + HPA 稳定：
  - `accept_count >= k-1`
  - `coherence >= min_coherence`
  - `gap_norm <= max_gap_norm`
  - `associator_mean <= max_associator_mean`

### 7. 验证阶段（如果通过门控）

```yaml
maker_argumentation → verify_maker_solution → decide_promote
```

**流程**：
1. **MAKER 论证**：生成结构化、依赖引用的论证
2. **验证投票**：多个 Worker 判断是否 `proved`
3. **提升决策**：如果 `proved=true`，提升为定理

### 8. B 池选择（如果失败）

```yaml
ensure_b_pool_* → propose_fallback_b_* (如果需要)
```

**规则**：
- 从 `state.existing_hypothesis` 中选择 3-5 个备选假设
- 必须解决当前的 refutation/gap 问题
- 不能是已见过的假设
- 优先选择最小改动的假设

### 9. 提升为定理

```yaml
promote_to_theorem:
  - 添加到 state.theorems
  - iteration++
  - 记录到 state.history
  - 清空 state.current_hypothesis
  - 添加到 state.seen_hypotheses
```

### 10. 递归或停止

```yaml
stop_or_continue:
  - 如果 done || iteration >= max_depth → 停止
  - 否则 → 递归调用自身
```

## HPA 三元组在工作流中的应用

### Rotation (扫描 Θ)
- **位置**：`hpa_prepare` 步骤
- **作用**：基于迭代次数和黄金比例计算扫描目标，避免共振
- **公式**：`Θ = (iteration × α + seed_phase) mod 2π`，其中 `α = φ⁻¹ ≈ 0.618`

### Factorization (因子化)
- **位置**：所有 LLM 步骤（propose_hypothesis, refute_scout, verify_maker_solution）
- **要求**：每个假设必须包含
  - `depends_on`: 最小依赖集（公理/定理 ID）
  - `factor_sequence`: 有序推理路径
- **作用**：用于嵌入计算和关联子计算

### Projection (投影/间隙 δ)
- **位置**：验证和评估步骤
- **规则**：
  - 不接受隐藏假设
  - 任何缺失的引理/假设必须明确声明为 `gap_or_counterexample`
  - 如果存在 gap，`accept=false` 或 `proved=false`

## 状态管理

### State 对象结构

```json
{
  "iteration": 0,
  "max_iterations": 10,
  "axioms": ["O1: ...", "A1: ..."],
  "assumptions": [{"id": "S1", "statement": "...", "motivation": "..."}],
  "theorems": [{"id": "T1", "statement": "...", "proof": "...", "depends_on": [...]}],
  "current_hypothesis": {"id": "H1", "statement": "...", "depends_on": [...], "factor_sequence": [...]},
  "seen_hypotheses": ["normalized_string_1", ...],
  "existing_hypothesis": "用户提供的假设池（多行或 JSON 数组）",
  "focus": "用户目标/探索方向",
  "seed_hypothesis": "用户种子假设",
  "last_candidate": {...},
  "last_worker_verdicts": [...],
  "last_b_pool": [...],
  "last_judgement": {"proved": bool, "reason": "...", ...},
  "history": [...],
  "done": false,
  "status": "running"
}
```

## 关键参数

### 共识参数
- `k`: 共识阈值（默认 3）
- `N`: Worker 数量（通常为 2K-1 = 5）
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

## 设计优势

1. **确定性探索**：HPA 提供无随机性的探索方向
2. **显式依赖**：所有推理路径必须明确，避免隐藏假设
3. **快速收敛**：通过 HPA 指标和投票机制快速达成共识
4. **最小修复**：失败时选择最小改动的新假设
5. **可追溯性**：完整记录所有尝试的假设和证明过程

## 输出结果

工作流最终输出：
- `status`: 运行状态（running | completed | failed | limit）
- `iteration`: 迭代次数
- `state`: 完整状态对象
- `theorems`: 已证明的定理列表
- `current_hypothesis`: 当前假设
- `last_candidate`: 最后候选
- `last_judgement`: 最后判决
