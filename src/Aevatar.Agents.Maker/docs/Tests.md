# Aevatar.Agents.Maker — Tests（覆盖面评审）

> 说明：本文件为“覆盖面评审”视角的测试清单，基于 `test/` 中对本项目的引用关系 + 测试文件关键字归纳。
> 目标是快速识别覆盖点与缺口；并不等同于完整覆盖率报告。

## 关联测试工程
- `test/Aevatar.Agents.Maker.Tests/Aevatar.Agents.Maker.Tests.csproj`

## 覆盖面矩阵（按功能点）

| 功能点 | 适用性 | 结论 | 证据（测试文件，节选） |
|---|---|---|---|
| 运行时/生命周期 | 适用 | ⚠️ 未发现（可能存在间接覆盖或待补充） | — |
| 事件/订阅/流 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Maker.Tests/MakerCoreLogicTests.cs` |
| 序列化/Protobuf | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Maker.Tests/DefaultDecomposerTests.cs`, `test/Aevatar.Agents.Maker.Tests/RedFlagStrategyTests.cs` |
| 持久化/存储 | 不适用 | N/A | — |
| AI/LLM | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Maker.Tests/DefaultDecomposerTests.cs`, `test/Aevatar.Agents.Maker.Tests/DefaultComposerTests.cs`, `test/Aevatar.Agents.Maker.Tests/RedFlagHandlerTests.cs`, `test/Aevatar.Agents.Maker.Tests/DefaultSolverTests.cs`, `test/Aevatar.Agents.Maker.Tests/MakerCoreLogicTests.cs`, `test/Aevatar.Agents.Maker.Tests/RedFlagStrategyTests.cs` |
| 工具调用 | 适用 | ⚠️ 未发现（可能存在间接覆盖或待补充） | — |
| Hooks/观测 | 不适用 | N/A | — |
| CQRS/EventSourcing | 不适用 | N/A | — |
| AG-UI/SSE | 不适用 | N/A | — |

## 结论与建议
- **总体**：存在覆盖缺口（按关键字归纳），建议补充：
  - 运行时/生命周期
  - 工具调用

## 如何运行

```bash
dotnet test aevatar-agent-framework.slnx -v minimal
```
