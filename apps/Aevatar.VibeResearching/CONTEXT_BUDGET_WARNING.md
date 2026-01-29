# Context Budget Warning 说明

## 🔍 警告信息

```
warn: VibeResearching.Vibe.VibePlannerAgent[0]
      Context budget warning. AgentId=VibePlannerAgent:sra-912ed45d98684b10940ce22cfc275a55-planner-deepseek RequestId=8b7f7a133c32456a84ee8ea871
```

---

## 📋 什么是 Context Budget Warning？

**Context Budget Warning**（上下文预算警告）是一个**监控性警告**，表示当前 LLM 请求的上下文大小超过了预设的警告阈值。

### 关键特点

- ✅ **仅警告，不阻止执行** - 请求会正常继续
- ✅ **可观测性工具** - 帮助监控上下文使用情况
- ✅ **自动触发** - 由内置的 `ContextBudgetMonitorHook` 自动检测

---

## 🎯 触发条件

警告会在以下任一条件满足时触发：

### 1. 消息数量超过阈值

**默认阈值**: **64 条消息**

当 LLM 请求中的消息总数（包括系统提示词、用户提示词、历史消息）≥ 64 条时触发。

### 2. 字符数量超过阈值

**默认阈值**: **200,000 字符**

当 LLM 请求的总字符数（包括系统提示词、用户提示词、所有消息内容）≥ 200,000 字符时触发。

---

## 📊 计算方式

**代码位置**: `src/Aevatar.Agents.AI.Core/Hooks/BuiltIn/ContextBudgetMonitorHook.cs`

```csharp
// 计算消息数量
var messageCount = req.Messages?.Count ?? 0;

// 计算总字符数
var totalChars = (req.SystemPrompt?.Length ?? 0) + (req.UserPrompt?.Length ?? 0);
if (req.Messages != null && req.Messages.Count > 0)
{
    totalChars += req.Messages.Sum(m => m.Content?.Length ?? 0);
}

// 检查是否超过阈值
var warnByMessages = messageCount >= context.Policy.ContextMessageWarn;  // 默认 64
var warnByChars = totalChars >= context.Policy.ContextCharsWarn;        // 默认 200,000
```

---

## 🔍 为什么会出现这个警告？

### 常见原因

1. **DAG 内容过多**
   - DAG 快照包含大量节点和边
   - Plan 上下文包含大量计划节点
   - Knowledge 节点内容过多

2. **Materials Context 过大**
   - 上传的文件内容过多
   - Materials 服务注入的上下文过大
   - 多个文件的内容合并后超过阈值

3. **历史对话过长**
   - 会话中积累了大量的历史消息
   - 多轮对话导致上下文累积

4. **Planner Output 过长**
   - Planner 的输出被传递给 Reasoner
   - 输出内容超过预期长度

---

## ⚠️ 影响

### 当前影响

- ✅ **请求会正常执行** - 警告不会阻止请求
- ⚠️ **可能影响性能** - 上下文过大可能导致：
  - LLM 响应变慢
  - Token 消耗增加
  - 成本上升

### 潜在问题

- ❌ **可能超出 LLM 上下文窗口限制** - 某些模型有硬限制
- ❌ **响应质量可能下降** - 上下文过大可能影响模型理解
- ❌ **成本增加** - 更多 token = 更高成本

---

## 🔧 解决方案

### 方案 1: 调整警告阈值（如果警告过于频繁）

**配置文件**: `appsettings.json` 或通过代码配置

```json
{
  "AevatarAgentHookOptions": {
    "ContextMessageWarn": 100,      // 提高消息数量阈值
    "ContextCharsWarn": 300000      // 提高字符数量阈值
  }
}
```

**代码配置**:
```csharp
services.Configure<AevatarAgentHookOptions>(options =>
{
    options.ContextMessageWarn = 100;
    options.ContextCharsWarn = 300_000;
});
```

---

### 方案 2: 减少上下文大小

#### 2.1 限制 Materials Context

**配置文件**: `appsettings.json`

```json
{
  "Materials": {
    "MaxContextChars": 10000,      // 减少最大上下文字符数（默认 18000）
    "MaxPerDocChars": 3000          // 减少每个文档的字符数（默认 6000）
  }
}
```

#### 2.2 限制 DAG 内容

- 减少 DAG 快照中的节点数量
- 限制 Plan 上下文中包含的计划节点数量
- 优化 Knowledge 节点的内容长度

#### 2.3 限制历史消息

- 实现消息截断机制
- 只保留最近的 N 条消息
- 使用摘要代替完整历史

---

### 方案 3: 禁用警告（不推荐）

**配置文件**: `appsettings.json`

```json
{
  "AevatarAgentHookOptions": {
    "DisabledHooks": ["ContextBudgetMonitorHook"]
  }
}
```

**⚠️ 不推荐**: 失去监控能力，无法及时发现上下文问题。

---

## 📈 监控和诊断

### 查看警告详情

警告日志包含以下信息：

```
Context budget warning. 
AgentId={AgentId} 
RequestId={RequestId} 
Messages={Messages}                    // 实际消息数量
TotalChars={Chars}                     // 实际字符数
WarnMessages>={WarnMessages}           // 消息数量阈值
WarnChars>={WarnChars}                 // 字符数量阈值
```

### 查找警告日志

```bash
# 查找所有上下文预算警告
grep -i "Context budget warning" logs/*.log

# 查找特定 Agent 的警告
grep "VibePlannerAgent.*Context budget warning" logs/*.log

# 查看最近的警告
grep "Context budget warning" logs/*.log | tail -20
```

---

## 🔍 诊断步骤

### 1. 检查警告频率

```bash
# 统计警告数量
grep -c "Context budget warning" logs/*.log

# 查看警告分布
grep "Context budget warning" logs/*.log | \
  awk '{print $NF}' | sort | uniq -c
```

### 2. 分析上下文组成

查看日志中的详细信息，了解：
- 消息数量是多少？
- 字符数量是多少？
- 哪个 Agent 触发最多？

### 3. 检查配置

```bash
# 查看当前配置
grep -A 5 "AevatarAgentHookOptions\|Materials" appsettings.json
```

---

## 📊 默认阈值参考

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `ContextMessageWarn` | 64 | 消息数量警告阈值 |
| `ContextCharsWarn` | 200,000 | 字符数量警告阈值 |
| `MaxToolOutputChars` | 16,000 | Tool 输出最大字符数 |

**代码位置**: `src/Aevatar.Agents.AI.Core/Hooks/AevatarAgentHookOptions.cs`

---

## 💡 最佳实践

### 1. 监控警告频率

- 如果警告频繁出现，考虑调整阈值或优化上下文
- 如果警告很少，说明当前配置合理

### 2. 优化上下文大小

- 使用摘要代替完整内容
- 限制历史消息数量
- 优化 DAG 快照大小

### 3. 根据模型调整

不同 LLM 模型有不同的上下文窗口限制：
- GPT-4: 128K tokens
- Claude 3: 200K tokens
- DeepSeek: 32K tokens

根据使用的模型调整阈值。

---

## 🔗 相关文档

- `PROMPTS.md` - 提示词长度限制说明
- `AGENT_PROMPTS.md` - Agent 提示词详细说明
- `docs/AI_HOOKS_HARNESS_GUIDE_zh.md` - Hook 系统指南

---

## 📝 总结

**Context Budget Warning** 是一个**监控性警告**，用于提醒上下文使用情况。它：

- ✅ **不会阻止请求执行**
- ✅ **帮助监控和优化**
- ⚠️ **可能需要关注** - 如果频繁出现，考虑优化上下文大小

**建议**: 
- 如果警告偶尔出现，可以忽略
- 如果警告频繁出现，需要优化上下文或调整阈值
- 如果警告导致实际错误，需要立即处理

---

*最后更新: 2025-01-28*
