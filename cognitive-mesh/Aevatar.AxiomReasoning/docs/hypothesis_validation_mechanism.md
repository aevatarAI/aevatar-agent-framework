# 假设验证机制说明

## 📋 问题

LLM 有时会返回不在 `state.existing_hypothesis` 中的假设，而不是从假设池中选择。

## ✅ 解决方案

我们添加了三层保护机制来确保 LLM 只能从 `existing_hypothesis` 中选择假设：

### 1. **Prompt 层加强约束**

在所有假设选择步骤中添加了：

#### **ABSOLUTE REQUIREMENT（绝对要求）**
```yaml
- **ABSOLUTE REQUIREMENT**: You MUST select hypotheses from state.existing_hypothesis.
- **DO NOT generate, invent, modify, or paraphrase any hypothesis.**
- **DO NOT create new statements that are not in the existing_hypothesis pool.**
```

#### **VALIDATION CHECKLIST（验证清单）**
LLM 在输出前需要自检：
- [ ] 是否在 "Existing hypotheses pool" 中找到了假设语句？
- [ ] 输出语句是否完全匹配（或规范化版本）池中的假设？
- [ ] 是否没有生成池中不存在的新语句？

#### **CRITICAL REMINDER（关键提醒）**
在每个假设池显示后添加：
- "Existing hypotheses pool" 是唯一的假设来源
- 必须从该池中选择，不要创建新语句

### 2. **输出 Schema 要求 `source_text` 字段**

修改了所有假设选择步骤的输出 schema，要求 LLM 输出 `source_text` 字段：

#### **单个假设选择** (`propose_hypothesis`)
```json
{
  "id": string,
  "statement": string,
  "motivation": string,
  "depends_on": [string],
  "factor_sequence": [string],
  "source_text": string  // ← 新增：必须包含原始假设文本
}
```

#### **多个假设选择** (`propose_fallback_b_*`)
```json
[
  {
    "statement": string,
    "motivation": string,
    "depends_on": [string],
    "factor_sequence": [string],
    "source_text": string  // ← 新增：必须包含原始假设文本
  }
]
```

**要求**：
- `source_text` 必须包含从 `existing_hypothesis` 中选择的**原始文本**
- 如果找不到匹配的假设，设置为 `"ERROR: No matching hypothesis found in pool"`

### 3. **后处理验证步骤**

在每个假设选择步骤后添加了验证步骤：

#### **`validate_candidate_source`** (在 `propose_hypothesis` 后)
```yaml
- id: validate_candidate_source
  type: transform
  ops:
    - 检查 candidate_raw 是否有效
    - 检查 existing_hypothesis 池是否存在
    - 验证 source_text 或 statement 是否在池中
    - 如果验证失败，设置错误消息
```

**验证逻辑**：
- 检查 `candidate_raw.source_text` 或 `candidate_raw.statement` 是否在 `state.existing_hypothesis` 中
- 如果不在，设置 `validation_status: "invalid"` 并修改 `statement` 为错误消息

#### **`validate_b_pool_*`** (在所有 `propose_fallback_b_*` 后)
```yaml
- id: validate_b_pool_scout / validate_b_pool_verify_failed / validate_b_pool_no_verify
  type: transform
  ops:
    - 检查 b_pool_raw 是否为有效数组
    - 检查 existing_hypothesis 池是否存在
    - 如果有效，使用 b_pool_raw；否则使用空数组 []
```

**验证逻辑**：
- 检查 `b_pool_raw` 是否为有效数组
- 如果 `existing_hypothesis` 池存在且数组有效，使用原始数组
- 否则使用空数组 `[]`

## 🔍 验证步骤位置

| 步骤 | 位置 | 验证内容 |
|------|------|----------|
| `validate_candidate_source` | 第 476 行 | 验证单个候选假设是否在池中 |
| `validate_b_pool_scout` | 第 748 行 | 验证侦察失败后的 B 池 |
| `validate_b_pool_verify_failed` | 第 1334 行 | 验证验证失败后的 B 池 |
| `validate_b_pool_no_verify` | 第 1552 行 | 验证不进入验证时的 B 池 |

## 📝 修改的步骤

### 1. `propose_hypothesis` (第 381 行)
- ✅ 添加了 `source_text` 字段要求
- ✅ 添加了 `validate_candidate_source` 验证步骤

### 2. `propose_fallback_b_scout` (第 620 行)
- ✅ 添加了 `source_text` 字段要求
- ✅ 添加了 `validate_b_pool_scout` 验证步骤

### 3. `propose_fallback_b_verify_failed` (第 1176 行)
- ✅ 添加了 `source_text` 字段要求
- ✅ 添加了 `validate_b_pool_verify_failed` 验证步骤

### 4. `propose_fallback_b_no_verify` (第 1350 行)
- ✅ 添加了 `source_text` 字段要求
- ✅ 添加了 `validate_b_pool_no_verify` 验证步骤

## 🎯 工作原理

### 流程示例：`propose_hypothesis`

```
1. LLM 选择假设
   ↓
2. 输出包含 source_text 的 JSON
   {
     "statement": "...",
     "source_text": "原始假设文本"  // ← LLM 必须提供
   }
   ↓
3. validate_candidate_source 验证
   - 检查 source_text 是否在 existing_hypothesis 中
   - 如果不在 → 设置错误消息
   ↓
4. 继续后续步骤（使用验证后的 candidate）
```

### 验证失败处理

如果验证失败：
- **单个假设**：设置 `statement` 为错误消息，`validation_status: "invalid"`
- **B 池**：使用空数组 `[]`，后续步骤会检测到并可能重新生成

## ⚠️ 注意事项

1. **字符串匹配**：验证使用 `contains()` 方法进行模糊匹配，允许规范化后的文本匹配
2. **错误处理**：如果验证失败，工作流会继续执行，但会在 `statement` 中标记错误
3. **空池处理**：如果 `existing_hypothesis` 为空，验证会跳过（允许使用 seed_hypothesis）

## 🔧 未来改进建议

如果需要更严格的验证，可以考虑：

1. **精确匹配**：使用更严格的字符串匹配算法（如编辑距离）
2. **索引验证**：要求 LLM 输出假设在池中的索引
3. **二次验证**：使用另一个 LLM 调用来验证假设是否真的在池中
4. **日志记录**：记录所有验证失败的情况，用于分析和改进 prompt
