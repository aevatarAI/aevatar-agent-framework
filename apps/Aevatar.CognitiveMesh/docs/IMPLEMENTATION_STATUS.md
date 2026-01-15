# Cognitive Mesh 实现状态

> 本文档详细记录 Cognitive Mesh 的当前实现状态、已解决问题、已知限制

---

## 📁 项目结构

```
apps/Aevatar.CognitiveMesh/
├── src/Aevatar.CognitiveMesh/             # 主应用项目
├── CognitiveMesh.AppHost/                 # Aspire 编排
└── docs/                                  # 文档
    ├── README.md
    ├── ROADMAP.md                         # 路线图
    ├── IMPLEMENTATION_STATUS.md           # 本文档
    ├── ARCHITECTURE.md
    ├── CONCEPT.md
    ├── DSL.md
    ├── PRD.md
    └── WORKFLOW_UI.md

src/
├── Aevatar.Agents.Cognitive.Core/         # 抽象层
├── Aevatar.Agents.Cognitive.Content/      # 内容加载/项目存储
├── Aevatar.Agents.Cognitive.Streaming/    # 广播/流式工具
└── Aevatar.CognitiveMesh.Dsl/             # DSL 模块
```

---

## ✅ 已完成功能

### 1. 策略抽象层 (Abstractions)

| 组件 | 文件 | 状态 | 说明 |
|------|------|------|------|
| 策略接口 | `IReasoningStrategy.cs` | ✅ | 定义 `ExecuteAsync`、`ValidateOptions` |
| 策略枚举 | `StrategyKind.cs` | ✅ | CoT, ToT, GoT, UoT 变体, Maker |
| 配置模型 | `ReasoningOptions.cs` | ✅ | 统一配置 + 策略特定字段 |
| 进度模型 | `ReasoningProgress.cs` | ✅ | 统一进度 + MAKER/UoT 特有字段 |
| 结果模型 | `ReasoningResult.cs` | ✅ | 统一结果 + Trace 信息 |
| 验证结果 | `ValidationResult.cs` | ✅ | 选项验证 |

### 2. 策略适配器 (Strategies)

| 策略 | 适配器 | 状态 | 说明 |
|------|--------|------|------|
| MAKER | `MakerStrategy.cs` | ✅ | 适配 `IMakerExecutor` |
| UoT Combinational | `UoTStrategy.cs` | ✅ | 适配 `IUoTExecutor` (C-UoT) |
| UoT Exploratory | `EUoTStrategy.cs` | ✅ | 适配 `IUoTExecutor` (E-UoT) |
| UoT Transformative | `TUoTStrategy.cs` | ✅ | 适配 `IUoTExecutor` (T-UoT) |

### 3. 核心服务 (Services)

| 服务 | 文件 | 状态 | 说明 |
|------|------|------|------|
| 策略注册 | `src/Aevatar.Agents.Cognitive.Core/Strategies/StrategyRegistry.cs` | ✅ | DI 注入策略，按 Kind 查找 |
| 核心服务 | `CognitiveMeshService.cs` | ✅ | 项目管理、执行、事件流 |

### 4. API 端点 (Program.cs)

| 端点 | 方法 | 状态 | 说明 |
|------|------|------|------|
| `/api/projects` | GET | ✅ | 获取项目列表 |
| `/api/projects/{id}` | GET | ✅ | 获取单个项目 |
| `/api/projects/{id}/run` | POST | ✅ | 启动执行 |
| `/api/projects/{id}/status` | GET | ✅ | 获取运行状态 |
| `/api/projects/{id}/snapshot` | GET | ✅ | 获取运行快照 |
| `/api/projects/{id}/events` | GET | ✅ | SSE 事件流 |
| `/api/projects/{id}/files` | GET | ✅ | 获取生成文件列表 |
| `/api/projects/{id}/files/{category}/{name}` | GET | ✅ | 获取文件内容 |
| `/api/strategies` | GET | ✅ | 获取可用策略 |
| `/api/sample-problems` | GET | ✅ | 获取示例问题 |

### 5. 前端 UI (wwwroot)

| 功能 | 状态 | 说明 |
|------|------|------|
| 项目列表 | ✅ | 侧边栏展示内置 + 动态项目 |
| 状态芯片 | ✅ | Status, Phase, Depth, Tokens, Calls |
| 进度条 | ✅ | 整体进度百分比 |
| 系统日志 | ✅ | 实时事件流 |
| 任务表格 | ✅ | 展示分解的子任务 |
| 共识表格 | ✅ | 展示投票过程 |
| Worker 卡片 | ✅ | 展示并行 Worker 状态 |
| 文件浏览 | ✅ | 展示生成的文件 |
| 结果展示 | ✅ | Markdown 渲染最终结果 |

---

## 🐛 已解决问题

### 问题 1: SSE 事件字段丢失

**现象**：前端显示 `[undefined]` 而非阶段名称

**根因**：
```csharp
// ❌ 错误：使用编译时类型（基类），丢失派生类字段
JsonSerializer.Serialize(evt, jsonOptions)

// 输出：{"type":"progress","runId":"...","timestamp":"..."}
// 缺失：phase, message, depth 等字段
```

**修复**：
```csharp
// ✅ 正确：使用运行时类型，序列化所有字段
JsonSerializer.Serialize(evt, evt.GetType(), jsonOptions)

// 输出：{"type":"progress","runId":"...","phase":"Decomposing","message":"..."}
```

**位置**：`Program.cs` 第 218-222 行

---

### 问题 2: 论文审稿无内容

**现象**：LLM 回复 "请提供论文内容"

**根因**：
```csharp
// 内置项目定义
Task = "Review and improve the academic paper for publication quality."
// 只有指令，没有论文内容！
```

**修复**：执行时动态加载论文
```csharp
if (project.Id == "paper-review")
{
    var paperPath = Path.Combine(..., "articles", "minimal_axiomatic_ontology_universe.md");
    if (File.Exists(paperPath))
    {
        var paperContent = await File.ReadAllTextAsync(paperPath, ct);
        task = $"""
            Review and improve the following academic paper...
            
            ═══════════════════════════════════════════════════════════════
            PAPER TO REVIEW
            ═══════════════════════════════════════════════════════════════
            
            {paperContent}
            
            ═══════════════════════════════════════════════════════════════
            """;
    }
}
```

**位置**：`CognitiveMeshService.cs` `ExecuteAsync` 方法

---

### 问题 3: Provider 创建失败

**现象**：`Failed to create provider '': OpenAI API key is required`

**根因**：
1. 内置项目没有设置 `ProviderName`
2. 工厂方法返回 `null` 或空字符串

**修复**：
```csharp
// ReasoningOptions.cs - 工厂方法设置默认 provider
public static ReasoningOptions ForMaker(
    ...,
    string providerName = "deepseek") => new()
{
    ProviderName = providerName,
    ...
};

// MakerStrategy.cs - 传递 provider
var makerOptions = new MakerOptions
{
    ProviderName = options.ProviderName ?? "deepseek",
    ...
};
```

---

## ⚠️ 已知限制

### 1. 配置限制

| 限制 | 说明 | 计划 |
|------|------|------|
| API Key 硬编码 | 需要 `appsettings.secrets.json` | 支持环境变量 |
| Provider 固定 | 目前只支持 deepseek/claude | 动态发现 |

### 2. 功能限制

| 限制 | 说明 | 计划 |
|------|------|------|
| 无断点续跑 | 进程重启需重新执行 | Phase 5 |
| 无并行策略 | 只能串行执行单个策略 | Phase 5 |
| 无干预命令 | 用户无法暂停/重试 | Phase 4 |
| 无分解树可视化 | 只有日志，无图形化 | Phase 4 |

### 3. 前端限制

| 限制 | 说明 | 计划 |
|------|------|------|
| 无动态创建项目 | 只能使用内置项目 | 添加创建表单 |
| 无历史运行记录 | 刷新页面丢失 | 持久化存储 |
| 无对比分析 | 无法对比多次运行 | 评估框架 |

---

## 🔧 配置说明

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Aevatar": "Debug"
    }
  },
  "LLMProviders": {
    "Providers": {
      "deepseek": {
        "Type": "deepseek",
        "Model": "deepseek-chat",
        "ApiKey": ""  // 在 secrets 中配置
      },
      "claude": {
        "Type": "anthropic",
        "Model": "claude-3-5-sonnet-20241022",
        "ApiKey": ""
      }
    }
  },
  "MassTransit": {
    "Transport": "InMemory"
  }
}
```

### appsettings.secrets.json (需创建)

```json
{
  "LLMProviders": {
    "Providers": {
      "deepseek": {
        "ApiKey": "sk-your-deepseek-key"
      },
      "claude": {
        "ApiKey": "sk-your-anthropic-key"
      }
    }
  }
}
```

---

## 🧪 测试说明

### 手动测试

1. **启动应用**
   ```bash
   cd apps/Aevatar.CognitiveMesh/src/Aevatar.CognitiveMesh
   dotnet run
   ```

2. **访问 UI**
   ```
   http://localhost:5678
   ```

3. **测试项目**
   - 选择 "论文审稿" 或 "桥梁交通"
   - 点击 START 启动
   - 观察日志流和进度

### API 测试

```bash
# 获取项目列表
curl http://localhost:5678/api/projects

# 启动执行
curl -X POST http://localhost:5678/api/projects/paper-review/run

# 获取状态
curl http://localhost:5678/api/projects/paper-review/status

# SSE 事件流
curl http://localhost:5678/api/projects/paper-review/events
```

---

## 📊 性能基准

| 场景 | MAKER 配置 | 预计时间 | 预计 Token |
|------|-----------|---------|-----------|
| 简单任务 | K=1, MaxCalls=50 | 2-5 分钟 | ~100K |
| 中等任务 | K=2, MaxCalls=100 | 5-15 分钟 | ~300K |
| 复杂任务 | K=3, MaxCalls=500 | 15-30 分钟 | ~1M |
| 论文审稿 | K=3, MaxCalls=100 | 10-20 分钟 | ~500K |

---

## 📝 开发注意事项

### 添加新策略

1. 在 `StrategyKind` 枚举添加新类型
2. 创建 `XxxStrategy : IReasoningStrategy`
3. 实现 `ExecuteAsync` 和 `ValidateOptions`
4. 在 DI 中注册：`services.AddSingleton<IReasoningStrategy, XxxStrategy>()`
5. `StrategyRegistry` 会自动发现

### 添加新内置项目

在 `CognitiveMeshService.cs` 的 `BuiltInProjects` 数组添加：

```csharp
new MeshProject
{
    Id = "unique-id",
    Name = "显示名称",
    Description = "描述",
    Icon = "🎯",
    Strategy = StrategyKind.Maker,
    Task = "任务描述...",
    Options = ReasoningOptions.ForMaker(...)
}
```

### 调试 SSE

1. 浏览器 DevTools → Network → 筛选 EventStream
2. 观察 `data:` 行的 JSON 内容
3. 确认所有字段都存在

---

*Last Updated: 2025-12-04*

