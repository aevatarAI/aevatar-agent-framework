# Aevatar.Agents.Persistence.Supabase — Tests（覆盖面评审）

> 说明：本文件为“覆盖面评审”视角的测试清单，基于 `test/` 中对本项目的引用关系 + 测试文件关键字归纳。
> 目标是快速识别覆盖点与缺口；并不等同于完整覆盖率报告。

## 关联测试工程
- `test/Aevatar.Agents.Persistence.Supabase.Tests/Aevatar.Agents.Persistence.Supabase.Tests.csproj`

## 覆盖面矩阵（按功能点）

| 功能点 | 适用性 | 结论 | 证据（测试文件，节选） |
|---|---|---|---|
| 运行时/生命周期 | 不适用 | N/A | — |
| 事件/订阅/流 | 不适用 | N/A | — |
| 序列化/Protobuf | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Persistence.Supabase.Tests/SupabaseConfigJsonTests.cs` |
| 持久化/存储 | 适用 | ✅ 已覆盖（至少有相关测试） | `test/Aevatar.Agents.Persistence.Supabase.Tests/SupabaseFactoryTests.cs`, `test/Aevatar.Agents.Persistence.Supabase.Tests/SupabaseConfigJsonTests.cs`, `test/Aevatar.Agents.Persistence.Supabase.Tests/SupabaseServiceCollectionExtensionsTests.cs`, `test/Aevatar.Agents.Persistence.Supabase.Tests/SupabaseSchemaScriptOptionTests.cs`, `test/Aevatar.Agents.Persistence.Supabase.Tests/SupabaseMemoryTests.cs`, `test/Aevatar.Agents.Persistence.Supabase.Tests/SupabaseSqlTests.cs` … |
| AI/LLM | 不适用 | N/A | — |
| 工具调用 | 不适用 | N/A | — |
| Hooks/观测 | 不适用 | N/A | — |
| CQRS/EventSourcing | 不适用 | N/A | — |
| AG-UI/SSE | 不适用 | N/A | — |

## 结论与建议
- **总体**：主要功能点均能找到测试证据（按关键字归纳）。

## 如何运行

```bash
dotnet test aevatar-agent-framework.slnx -v minimal
```
