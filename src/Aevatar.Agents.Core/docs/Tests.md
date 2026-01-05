# Aevatar.Agents.Core — Tests（覆盖面评审）

> 说明：本文件为“覆盖面评审”视角的测试清单，基于 `test/` 中对本项目的引用关系 + 测试文件关键字归纳。
> 目标是快速识别覆盖点与缺口；并不等同于完整覆盖率报告。

## 关联测试工程
- `test/Aevatar.Agents.AI.Core.Tests/Aevatar.Agents.AI.Core.Tests.csproj`
- `test/Aevatar.Agents.Abstractions.Tests/Aevatar.Agents.Abstractions.Tests.csproj`
- `test/Aevatar.Agents.Core.Tests.Agents/Aevatar.Agents.Core.Tests.Agents.csproj`
- `test/Aevatar.Agents.Core.Tests/Aevatar.Agents.Core.Tests.csproj`
- `test/Aevatar.Agents.Local.Tests/Aevatar.Agents.Local.Tests.csproj`
- `test/Aevatar.Agents.Orleans.Tests/Aevatar.Agents.Orleans.Tests.csproj`
- `test/Aevatar.Agents.ProtoActor.Tests/Aevatar.Agents.ProtoActor.Tests.csproj`
- `test/Aevatar.Agents.TestBase/Aevatar.Agents.TestBase.csproj`
- `test/Aevatar.Agents.Twitter.Tests/Aevatar.Agents.Twitter.Tests.csproj`
- `test/Aevatar.Agents.Workflow.Tests/Aevatar.Agents.Workflow.Tests.csproj`

## 覆盖面矩阵（按功能点）

| 功能点 | 适用性 | 结论 | 证据（测试文件，节选） |
|---|---|---|---|
| 运行时/生命周期 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Core.Tests.Agents/ExceptionTestAgent.cs`, `test/Aevatar.Agents.Core.Tests.Agents/BasicTestAgent.cs`, `test/Aevatar.Agents.Core.Tests.Agents/IntegrationTestAgents.cs`, `test/Aevatar.Agents.Core.Tests/GAgentActorBaseTests.cs`, `test/Aevatar.Agents.Core.Tests/GAgentBaseTests.cs`, `test/Aevatar.Agents.Core.Tests/IntegrationTests.cs` … |
| 事件/订阅/流 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.AI.Core.Tests/AIGAgentBaseTests.cs`, `test/Aevatar.Agents.Abstractions.Tests/EventEnvelopeTests.cs`, `test/Aevatar.Agents.Core.Tests.Agents/PublishingTestAgent.cs`, `test/Aevatar.Agents.Core.Tests.Agents/IntegrationTestAgents.cs`, `test/Aevatar.Agents.Core.Tests.Agents/ChildTestAgent.cs`, `test/Aevatar.Agents.Core.Tests.Agents/P2PTestAgent.cs` … |
| 序列化/Protobuf | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.AI.Core.Tests/AIGAgentBaseTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/StatePropertyAccessorTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/AgentSkillsToolTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/MemorySearchSemanticTests.cs`, `test/Aevatar.Agents.Abstractions.Tests/EventEnvelopeTests.cs`, `test/Aevatar.Agents.Core.Tests.Agents/BasicTestAgent.cs` … |
| 持久化/存储 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.AI.Core.Tests/AIGAgentBaseTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/MemorySearchSemanticTests.cs`, `test/Aevatar.Agents.Core.Tests.Agents/ConfigurableTestAgent.cs`, `test/Aevatar.Agents.Core.Tests/EventHandlerTests.cs`, `test/Aevatar.Agents.Core.Tests/ParentChildRelationshipTests.cs`, `test/Aevatar.Agents.Core.Tests/EventPublishingTests.cs` … |
| AI/LLM | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.AI.Core.Tests/AIGAgentBaseTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/CqrsStateQueryInjectionTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/AIGAgentKeysTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/AgentSkillsToolTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/MemorySearchSemanticTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/TestAgents/TestAIGAgent.cs` … |
| 工具调用 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.AI.Core.Tests/AgentSkillsToolTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/MemorySearchSemanticTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/TestAgents/AgentSkillsToolTestAgent.cs`, `test/Aevatar.Agents.AI.Core.Tests/Hooks/ToolOutputTruncationHookTests.cs` |
| Hooks/观测 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.AI.Core.Tests/Hooks/AevatarAgentHookPipelineTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/Hooks/ContextBudgetMonitorHookTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/Hooks/ToolOutputTruncationHookTests.cs`, `test/Aevatar.Agents.Core.Tests/ExceptionHandlerTests.cs`, `test/Aevatar.Agents.Core.Tests/BaseSubscriptionManagerTests.cs`, `test/Aevatar.Agents.Core.Tests/Tracing/ProjectingExecutionTraceStoreTests.cs` … |
| CQRS/EventSourcing | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.AI.Core.Tests/CqrsStateQueryInjectionTests.cs`, `test/Aevatar.Agents.AI.Core.Tests/MemorySearchSemanticTests.cs`, `test/Aevatar.Agents.Core.Tests/InMemoryEventStoreTests.cs`, `test/Aevatar.Agents.Core.Tests/EventSourcingProtectionTests.cs`, `test/Aevatar.Agents.Core.Tests/DependencyInjection/AevatarBuilderTests.cs`, `test/Aevatar.Agents.Core.Tests/MemoryGraph/ExecutionTraceMemoryProjectorTests.cs` … |
| AG-UI/SSE | 不适用 | N/A | — |

## 结论与建议
- **总体**：主要功能点均能找到测试证据（按关键字归纳）。

## 如何运行

```bash
dotnet test aevatar-agent-framework.slnx -v minimal
```
