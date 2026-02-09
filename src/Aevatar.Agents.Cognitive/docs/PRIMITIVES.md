# Cognitive DSL 原语（面向“尽可能不浪费 token”）

目标：把 **确定性**、**可验证**、**可复用** 的处理逻辑从 LLM prompt 里拿出来，让 LLM 只做它擅长的“生成/推理/证明草案”，减少无意义 token 消耗。

注意：`transform` / `retrieve_facts` 目前是 **Coordinator-only** 原语；不要把它们放到 `fan_out.step` 里（Worker 侧只负责执行 `llm_call` 任务）。

## 原语一：`transform`（0 token）

适用：对 `fan_out` 返回的 Worker 结果做聚合、计数、去重、查找、简单归一化 —— 这些都不该喂给 LLM。

### 配置

```yaml
- id: aggregate_verdicts
  type: transform
  ops:
    - op: count_where
      from: worker_verdicts
      where: { field: "accept", equals: true }
      store: accept_count

    - op: count_where
      from: worker_verdicts
      where: { field: "strong_refutation", equals: true }
      store: strong_refutation_count

    - op: select_many
      from: worker_verdicts
      field: "proposed_b"
      store: b_candidates_raw

    - op: distinct
      from: b_candidates_raw
      key: "statement"
      store: b_candidates
```

### 支持的 op

- **`eval`**：计算表达式（Scriban），返回 bool/number/string（常用于 done/status 等控制流变量）
  - `expr`, `store`
- **`set`**：设置一个值（支持模板/变量引用）
  - `value`, `store`
- **`set_path`**：设置嵌套字典字段（用于 0-token 更新 `state.*`，替代“让 LLM 回写整份 state”）
  - `target`(变量名), `path`(点路径), `value`, `store`(可选)
- **`set_path_if`**：条件写入（`if` 为真才执行 `set_path`）
  - `if`(表达式/模板), `target`, `path`, `value`, `store`(可选)
- **`inc_path`**：数值自增（常用于 `state.iteration += 1`）
  - `target`, `path`, `by`(可选, default 1), `store`(可选)
- **`append_path`**：向 `target.path` 的 list 追加元素（不存在则自动创建 list）
  - `target`, `path`, `value`, `store`(可选)
- **`append_unique_path`**：追加但去重（按字符串 normalize：trim + 折叠空白 + lower）
  - `target`, `path`, `value`, `store`(可选)
- **`count_where`**：计数（等值匹配）
  - `from`, `where.field`, `where.equals`, `store`
- **`any_where`**：是否存在满足条件的元素（等值匹配）
  - `from`, `where.field`, `where.equals`, `store`
- **`all_where`**：是否所有元素满足条件（等值匹配）
  - `from`, `where.field`, `where.equals`, `store`
- **`filter_where`**：过滤（等值匹配）
  - `from`, `where.field`, `where.equals`, `store`
- **`filter_not_in`**：过滤掉“已见过”的候选（典型用于去掉已处理 hypotheses）
  - `from`, `key`(可选), `not_in`, `not_in_key`(可选), `store`
- **`select`**：map 单字段
  - `from`, `field`, `store`
- **`select_many`**：map + flatten（字段是 list 时）
  - `from`, `field`, `store`
- **`take`**：取前 N 个
  - `from`, `n`, `store`
- **`take_last`**：取后 N 个（典型用于“只给 LLM 最近 30 条定理 statement”，避免 prompt 膨胀）
  - `from`, `n`, `store`
- **`distinct`**：去重（按元素本身或 key 字段）
  - `from`, `key`(可选), `store`
- **`project_fields`**：投影字段（把 list<object> 变成 list<dict>，只保留指定 keys，减少 prompt 噪声）
  - `from`, `fields`(string[]), `store`
- **`normalize`**：字符串归一化（trim + 折叠空白 + lower）
  - `value`, `store`
- **`append`**：向列表追加一个值
  - `to`, `value`, `store`(可选)
- **`lookup_by_id`**：在列表里按 id 过滤（典型用于 depends_on → 取出依赖节点详情）
  - `from`, `ids`, `id_field`(可选, default `id`), `store`
- **`make_workers`**：确定性生成 N 个 worker assignment（替代每轮用 LLM“编造角色列表”）
  - `n`, `id_prefix`(可选, default `worker-`), `profiles`(可选：[{role,angle},...]), `store`

## 原语二：`retrieve_facts`（默认 0 token）

适用：从“已有定理/公理/事实库”里取 top-k 相关项，避免把整库塞进 prompt。

### 配置

```yaml
- id: retrieve_relevant_facts
  type: retrieve_facts
  query: "{{ candidate.statement }}"
  source: "state.theorems"
  text_field: "statement"
  id_field: "id"
  top_k: 20
  mode: lexical
  store: relevant_facts
```

### 输出

`store` 变量会写入一个 list，每项为：

```yaml
- { id: "...", statement: "...", score: 0.123 }
```

### mode

- **`lexical`（默认）**：基于 token set 的 Jaccard，相似度 0 则丢弃；完全不调用模型，0 token。
- **`embedding`（预留）**：未来可接入 embedding generator 做语义检索（会产生额外调用/开销）。

---

## 通用护栏字段（建议写进 workflow defaults）

这些字段用于“防硬卡 + 控输出 + 保证 JSON 可解析”，属于 **系统可靠性** 而不是“优化”。

### 1) `defaults`（工作流级默认参数）

DSL 支持在 workflow 顶层声明 `defaults`，按 step type 注入默认参数（可被具体 step 覆盖）。

```yaml
defaults:
  llm_call:
    max_length: 102400
    strict_parse: true
    timeout_seconds: 180
    idle_timeout_seconds: 30

  fan_out:
    timeout_seconds: 600
    include_failures: true

  vote:
    red_flag:
      strategy: english
      max_length: 4096
    max_red_flags: 20
```

### 2) `llm_call` 护栏字段

- **`max_length`**：输出最大字符数，超出直接 red-flag（默认 102400）
- **`strict_parse`**：当 `output: json/json_array` 时，解析失败视为 red-flag（默认 true）
- **`timeout_seconds`**：总超时（默认 180s，Worker/Coordinator 均生效）
- **`idle_timeout_seconds`**：流式时“无 token”空闲超时（默认 30s）

### 3) `fan_out` 护栏字段

- **`timeout_seconds`**：fan_out 等待全部子任务终态完成的总超时（默认 600s）
- **`include_failures`**：是否把失败/解析失败的子任务也作为结果项返回（默认 false）

当 `include_failures=true` 时，失败项会以结构化对象形式进入结果列表（便于 `transform` 统计/记录）：  
`{ success:false, error:\"...\", worker_id:\"...\", step_id:\"...\" }`


