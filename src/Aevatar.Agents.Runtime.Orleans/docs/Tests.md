# Aevatar.Agents.Runtime.Orleans — Tests（覆盖面评审）

> 说明：本文件为“覆盖面评审”视角的测试清单，基于 `test/` 中对本项目的引用关系 + 测试文件关键字归纳。
> 目标是快速识别覆盖点与缺口；并不等同于完整覆盖率报告。

## 关联测试工程
- `test/Aevatar.Agents.Orleans.Tests/Aevatar.Agents.Orleans.Tests.csproj`
- `test/Aevatar.Agents.TestBase/Aevatar.Agents.TestBase.csproj`

## 覆盖面矩阵（按功能点）

| 功能点 | 适用性 | 结论 | 证据（测试文件，节选） |
|---|---|---|---|
| 运行时/生命周期 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Orleans.Tests/OrleansActorFactoryTests.cs`, `test/Aevatar.Agents.Orleans.Tests/EventSourcing/OrleansEventStoreTests.cs`, `test/Aevatar.Agents.Orleans.Tests/Stream/UnifiedGrainEventPublisherTests.cs`, `test/Aevatar.Agents.Orleans.Tests/Stream/OrleansStreamFactoryTests.cs`, `test/Aevatar.Agents.Orleans.Tests/Streaming/OrleansStreamTests.cs`, `test/Aevatar.Agents.TestBase/EventSourcingTestExtensions.cs` … |
| 事件/订阅/流 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Orleans.Tests/OrleansActorFactoryTests.cs`, `test/Aevatar.Agents.Orleans.Tests/Stream/UnifiedGrainEventPublisherTests.cs`, `test/Aevatar.Agents.Orleans.Tests/Stream/OrleansStreamFactoryTests.cs`, `test/Aevatar.Agents.Orleans.Tests/Streaming/OrleansStreamTests.cs` |
| 序列化/Protobuf | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Orleans.Tests/OrleansActorFactoryTests.cs`, `test/Aevatar.Agents.Orleans.Tests/EventSourcing/OrleansEventStoreTests.cs`, `test/Aevatar.Agents.Orleans.Tests/Stream/UnifiedGrainEventPublisherTests.cs`, `test/Aevatar.Agents.Orleans.Tests/Stream/OrleansStreamFactoryTests.cs`, `test/Aevatar.Agents.Orleans.Tests/Streaming/OrleansStreamTests.cs` |
| 持久化/存储 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Orleans.Tests/EventSourcing/OrleansEventStoreTests.cs`, `test/Aevatar.Agents.TestBase/EventSourcingTestExtensions.cs` |
| AI/LLM | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Orleans.Tests/OrleansActorFactoryTests.cs` |
| 工具调用 | 不适用 | N/A | — |
| Hooks/观测 | 不适用 | N/A | — |
| CQRS/EventSourcing | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Orleans.Tests/EventSourcing/OrleansEventStoreTests.cs`, `test/Aevatar.Agents.TestBase/EventSourcingTestExtensions.cs` |
| AG-UI/SSE | 不适用 | N/A | — |

## 结论与建议
- **总体**：主要功能点均能找到测试证据（按关键字归纳）。

## 如何运行

```bash
dotnet test aevatar-agent-framework.slnx -v minimal
```
