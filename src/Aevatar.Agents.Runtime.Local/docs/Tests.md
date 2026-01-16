# Aevatar.Agents.Runtime.Local — Tests（覆盖面评审）

> 说明：本文件为“覆盖面评审”视角的测试清单，基于 `test/` 中对本项目的引用关系 + 测试文件关键字归纳。
> 目标是快速识别覆盖点与缺口；并不等同于完整覆盖率报告。

## 关联测试工程
- `test/Aevatar.Agents.Core.Tests/Aevatar.Agents.Core.Tests.csproj`
- `test/Aevatar.Agents.Local.Tests/Aevatar.Agents.Local.Tests.csproj`
- `test/Aevatar.Agents.Twitter.Tests/Aevatar.Agents.Twitter.Tests.csproj`
- `test/Aevatar.Agents.Workflow.Tests/Aevatar.Agents.Workflow.Tests.csproj`
- `apps/Aevatar.PaperReview/test/Aevatar.PaperReview.Tests/Aevatar.PaperReview.Tests.csproj`

## 覆盖面矩阵（按功能点）

| 功能点 | 适用性 | 结论 | 证据（测试文件，节选） |
|---|---|---|---|
| 运行时/生命周期 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Core.Tests/GAgentActorBaseTests.cs`, `test/Aevatar.Agents.Core.Tests/GAgentBaseTests.cs`, `test/Aevatar.Agents.Core.Tests/IntegrationTests.cs`, `test/Aevatar.Agents.Core.Tests/Context/AgentContextSerializerTests.cs`, `test/Aevatar.Agents.Core.Tests/Context/AgentContextPropagatorTests.cs`, `test/Aevatar.Agents.Core.Tests/Context/AgentContextScopeTests.cs` … |
| 事件/订阅/流 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Core.Tests/EventHandlerTests.cs`, `test/Aevatar.Agents.Core.Tests/EventPublishingTests.cs`, `test/Aevatar.Agents.Core.Tests/GAgentActorBaseTests.cs`, `test/Aevatar.Agents.Core.Tests/PointToPointCommunicationTests.cs`, `test/Aevatar.Agents.Core.Tests/GAgentBaseTests.cs`, `test/Aevatar.Agents.Core.Tests/ExceptionHandlerTests.cs` … |
| 序列化/Protobuf | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Core.Tests/EventHandlerTests.cs`, `test/Aevatar.Agents.Core.Tests/ParentChildRelationshipTests.cs`, `test/Aevatar.Agents.Core.Tests/EventPublishingTests.cs`, `test/Aevatar.Agents.Core.Tests/GAgentActorBaseTests.cs`, `test/Aevatar.Agents.Core.Tests/PointToPointCommunicationTests.cs`, `test/Aevatar.Agents.Core.Tests/GAgentBaseTests.cs` … |
| 持久化/存储 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Core.Tests/EventHandlerTests.cs`, `test/Aevatar.Agents.Core.Tests/ParentChildRelationshipTests.cs`, `test/Aevatar.Agents.Core.Tests/EventPublishingTests.cs`, `test/Aevatar.Agents.Core.Tests/GAgentActorBaseTests.cs`, `test/Aevatar.Agents.Core.Tests/PointToPointCommunicationTests.cs`, `test/Aevatar.Agents.Core.Tests/GAgentBaseTests.cs` … |
| AI/LLM | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Core.Tests/Memory/FileMemoryVectorIndexTests.cs`, `test/Aevatar.Agents.Core.Tests/Fixtures/CoreTestFixture.cs`, `test/Aevatar.Agents.Workflow.Tests/WorkerTestGAgentTests.cs`, `apps/Aevatar.PaperReview/test/Aevatar.PaperReview.Tests/PaperReviewIntegrationTests.cs`, `apps/Aevatar.PaperReview/test/Aevatar.PaperReview.Tests/ReviewEventBridgeTests.cs`, `apps/Aevatar.PaperReview/test/Aevatar.PaperReview.Tests/PhaseMapperTests.cs` … |
| 工具调用 | 不适用 | N/A | — |
| Hooks/观测 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Core.Tests/ExceptionHandlerTests.cs`, `test/Aevatar.Agents.Core.Tests/BaseSubscriptionManagerTests.cs`, `test/Aevatar.Agents.Core.Tests/Tracing/ProjectingExecutionTraceStoreTests.cs`, `test/Aevatar.Agents.Core.Tests/MemoryGraph/ExecutionTraceMemoryProjectorTests.cs` |
| CQRS/EventSourcing | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Core.Tests/InMemoryEventStoreTests.cs`, `test/Aevatar.Agents.Core.Tests/EventSourcingProtectionTests.cs`, `test/Aevatar.Agents.Core.Tests/DependencyInjection/AevatarBuilderTests.cs`, `test/Aevatar.Agents.Core.Tests/MemoryGraph/ExecutionTraceMemoryProjectorTests.cs` |
| AG-UI/SSE | 不适用 | N/A | — |

## 结论与建议
- **总体**：主要功能点均能找到测试证据（按关键字归纳）。

## 如何运行

```bash
dotnet test aevatar-agent-framework.slnx -v minimal
```
