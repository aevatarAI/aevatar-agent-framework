# Aevatar.Agents.Sessions.Tests Architecture

## 结构树 (Structure)

```
test/Aevatar.Agents.Sessions.Tests/
├── Aevatar.Agents.Sessions.Tests.csproj   # 测试项目定义
├── SessionsTestFixture.cs                 # 测试 DI/Runtime/LLM stub
├── SessionWorkflowTests.cs                # Workflow YAML 加载 + 运行验证
└── docs/
    └── ARCHITECTURE.md                    # 架构说明与变更日志
```

## 设计决策 (Decisions)

1. **本地运行时 (Local runtime)**
   - 使用 `UseLocalRuntime()` 避免分布式依赖。
   - 让 Session 加载与 agent 创建保持可预测与快速。

2. **最小化 LLM Stub**
   - 通过 `TestLLMProviderFactory` 返回固定响应。
   - 保证 workflow “可运行”而不依赖真实 API。

3. **临时 workflow 目录**
   - 每次测试写入独立 YAML，避免跨测试污染。
   - 结束后清理，保持环境干净。

## 开发规范 (Guidelines)

- 测试必须是 deterministic；禁止网络与外部服务依赖。
- 使用 `Shouldly` 做断言，保证可读性。
- workflow YAML 必须包含 `edges` 与 `constraints`（允许空数组）。

## 变更日志 (Changelog)

- 2026-01-27: 初始化 Sessions 测试项目，覆盖 workflow YAML 加载与运行。
