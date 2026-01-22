# Aevatar.Agents.Cognitive — Tests（覆盖面评审）

> 说明：本文件为“覆盖面评审”视角的测试清单，基于 `test/` 中对本项目的引用关系 + 测试文件关键字归纳。
> 目标是快速识别覆盖点与缺口；并不等同于完整覆盖率报告。

## 关联测试工程
- `test/Aevatar.Agents.Cognitive.Tests/Aevatar.Agents.Cognitive.Tests.csproj`

## 覆盖面矩阵（按功能点）

| 功能点 | 适用性 | 结论 | 证据（测试文件，节选） |
|---|---|---|---|
| 运行时/生命周期 | 不适用 | N/A | — |
| 事件/订阅/流 | 不适用 | N/A | — |
| 序列化/Protobuf | 不适用 | N/A | — |
| 持久化/存储 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Cognitive.Tests/WorkspaceReadFileExecutorTests.cs` |
| AI/LLM | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Cognitive.Tests/WorkflowCallAndAgentOverrideTests.cs` |
| 工具调用 | 适用 | ⚠️ 未发现（可能存在间接覆盖或待补充） | — |
| Hooks/观测 | 不适用 | N/A | — |
| CQRS/EventSourcing | 不适用 | N/A | — |
| AG-UI/SSE | 不适用 | N/A | — |

## 结论与建议
- **总体**：存在覆盖缺口（按关键字归纳），建议补充：
  - 工具调用

## 如何运行

```bash
dotnet test aevatar-agent-framework.slnx -v minimal
```
