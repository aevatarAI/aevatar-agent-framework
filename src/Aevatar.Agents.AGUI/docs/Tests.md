# Aevatar.Agents.AGUI — Tests（覆盖面评审）

> 说明：本文件为“覆盖面评审”视角的测试清单，基于 `test/` 中对本项目的引用关系 + 测试文件关键字归纳。
> 目标是快速识别覆盖点与缺口；并不等同于完整覆盖率报告。

## 关联测试工程
- `test/Aevatar.Agents.AGUI.Tests/Aevatar.Agents.AGUI.Tests.csproj`

## 覆盖面矩阵（按功能点）

| 功能点 | 适用性 | 结论 | 证据（测试文件，节选） |
|---|---|---|---|
| 运行时/生命周期 | 不适用 | N/A | — |
| 事件/订阅/流 | 不适用 | N/A | — |
| 序列化/Protobuf | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.AGUI.Tests/AgUiBootstrapTests.cs`, `test/Aevatar.Agents.AGUI.Tests/AgUiEventsTests.cs` |
| 持久化/存储 | 不适用 | N/A | — |
| AI/LLM | 不适用 | N/A | — |
| 工具调用 | 不适用 | N/A | — |
| Hooks/观测 | 不适用 | N/A | — |
| CQRS/EventSourcing | 不适用 | N/A | — |
| AG-UI/SSE | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.AGUI.Tests/AgUiBootstrapTests.cs`, `test/Aevatar.Agents.AGUI.Tests/AgUiEventsTests.cs` |

## 结论与建议
- **现状**：已补齐该项目的核心单测，覆盖 `AgUiBootstrap.CollectAssistantMessagesAsync` 的关键行为：
  - assistant 消息过滤（仅收集带 `step_id` 的 assistant 最终内容）
  - lane 路由（`ResolveLaneId`）与 `MaxAssistantMessages` 尾部窗口截断
  - 同 step 的“latest wins”合并策略、Orleans-style id（`TypeName:RawId`）查找、CreateAsync fallback
- **补充**：补了一组事件模型（`AgUiEvents.cs`）的稳定性测试（Type 常量稳定 + JSON 可序列化）。
- **备注**：本项目只负责“协议事件模型 + snapshot 聚合”，SSE 连接/断线重连的端点行为通常在具体系统的 API Host 中测试。

## 如何运行

```bash
dotnet test aevatar-agent-framework.slnx -v minimal
```
