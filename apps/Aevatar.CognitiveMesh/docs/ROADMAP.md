# Cognitive Mesh 演进路线图

> **终极愿景**：DSL 驱动的认知架构 + 真正的 Actor 并行

---

## 📊 Phase 总览

```
Phase 1: 策略抽象层          ✅ 完成
Phase 2: 统一执行引擎        ✅ 完成
Phase 2.5: UoT 三重奏        ✅ 完成 (C/E/T-UoT)
Phase 3: 内容加载+任务模板   ✅ 完成
Phase 3.5: Coordinator+Worker ✅ 完成 (真正的 Actor 并行)
Phase 4: DSL 热重载          📋 下一步
Phase 5: 可视化增强          📋 计划中
Phase 6: 高级功能            📋 计划中
```

---

## ✅ Phase 3.5: Coordinator + Worker (已完成)

**目标**：实现真正的 Actor 并行，而非进程内伪并发

### 架构

```
┌────────────────────────────────────────────┐
│   CognitiveCoordinatorGAgent (协调器)      │
│   - 解析 DSL 工作流                        │
│   - 分发任务 (Protobuf 事件)               │
│   - 收集结果 (事件驱动)                    │
└────────────────────────────────────────────┘
                    │ ExecuteStepRequestEvent
                    ↓
┌────────────────────────────────────────────┐
│   RoleAIGAgent + CognitiveStepExecutionHandler │
│   - 接收任务事件                           │
│   - 执行 LLM 调用                          │
│   - 返回结果事件                           │
│                                            │
│   由 Actor Runtime 调度 = 真正的并行       │
└────────────────────────────────────────────┘
```

### 交付物

| 文件 | 说明 |
|------|------|
| `CognitiveCoordinatorGAgent.cs` | 工作流协调器 |
| `CognitiveStepExecutionHandler.cs` | LLM 执行器 |
| `cognitive_messages.proto` | Protobuf 事件定义 |

---

## 📐 Phase 4: DSL 热重载 (下一步)

**目标**：支持 YAML 定义认知工作流，热重载执行

### 步骤类型

| 类型 | 说明 |
|------|------|
| `llm_call` | LLM 调用 |
| `fan_out` | 并行执行 |
| `vote_until_consensus` | 投票共识 |
| `conditional` | 条件分支 |
| `recursive_call` | 递归调用 |

---

## 📅 里程碑

| 里程碑 | 内容 | 状态 |
|--------|------|------|
| M1-M3 | 基础架构 | ✅ 完成 |
| M3.5 | Coordinator + Worker | ✅ 完成 |
| M4 | DSL 热重载 | 📋 下一步 |
| M5-M6 | 可视化 + 高级功能 | 📋 计划中 |

---

*Last Updated: 2025-12-04*
