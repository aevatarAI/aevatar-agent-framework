# Cognitive Mesh

> **认知网格** - DSL 驱动的认知架构构建器
> 
> **终极愿景**：一个 CognitiveGAgentBase + DSL = 任意认知策略

---

## 🎯 核心愿景

```
当前状态                          终极目标
─────────────────────────────────────────────────────────────────
每种策略 = 一个 C# 类            每种策略 = 一个 YAML 文件
   (硬编码 500+ 行)                  (声明式 50 行)

改策略 = 改代码 → 编译 → 部署    改策略 = 改 YAML → 热重载
```

将不同的 AI 推理策略（CoT、ToT、GoT、UoT、MAKER）统一在同一框架下：

- **统一抽象**：`IReasoningStrategy` 接口统一所有策略
- **统一执行**：`CognitiveMeshService` 管理项目和运行
- **统一进度**：`ReasoningProgress` 实时报告各策略状态
- **统一结果**：`ReasoningResult` 可比较、可分析
- **DSL 驱动**：`CognitiveGAgentBase` + YAML = 任意策略 (Phase 4)

---

## 📁 项目结构

```
cognitive-mesh/
├── Aevatar.CognitiveMesh.Abstractions/     # 核心抽象接口
│   ├── IReasoningStrategy.cs               # 推理策略接口
│   ├── StrategyKind.cs                     # 策略类型枚举
│   ├── ReasoningOptions.cs                 # 执行选项
│   ├── ReasoningProgress.cs                # 进度报告模型
│   ├── ReasoningResult.cs                  # 结果模型
│   ├── Content/                            # 内容加载抽象
│   │   ├── ContentSource.cs
│   │   ├── LoadedContent.cs
│   │   └── IContentLoader.cs
│   └── Tasks/                              # 任务模板抽象
│       ├── TaskTemplate.cs
│       └── TaskDefinition.cs
│
├── Aevatar.CognitiveMesh/                  # 主服务实现
│   ├── Program.cs                          # ASP.NET Core 入口
│   ├── Services/
│   │   ├── CognitiveMeshService.cs         # 核心服务
│   │   ├── ProjectStore.cs                 # 项目存储 (YAML)
│   │   ├── ContentLoader.cs                # 内容加载器
│   │   └── StrategyRegistry.cs             # 策略注册表
│   ├── Strategies/
│   │   ├── DirectStrategy.cs               # Direct 策略
│   │   ├── MakerStrategy.cs                # MAKER 适配器
│   │   ├── UoTStrategy.cs                  # C-UoT 适配器
│   │   ├── EUoTStrategy.cs                 # E-UoT 适配器
│   │   └── TUoTStrategy.cs                 # T-UoT 适配器
│   ├── projects/                           # 项目定义 (YAML)
│   ├── workflows/                          # [未来] DSL 工作流
│   └── wwwroot/                            # 前端 UI
│
└── docs/                                   # 设计文档
    ├── README.md                           # 本文档
    ├── ROADMAP.md                          # 渐进式路线图
    ├── COGNITIVE_AGENT_BASE_DESIGN.md      # 🆕 CognitiveGAgentBase 设计
    ├── CONTENT_LOADING_DESIGN.md           # 内容加载设计
    └── IMPLEMENTATION_STATUS.md            # 实现状态
```

---

## 🚀 快速开始

### 1. 配置 API Key

```bash
# 创建 appsettings.secrets.json
cat > cognitive-mesh/Aevatar.CognitiveMesh/appsettings.secrets.json << 'EOF'
{
  "LLMProviders": {
    "Providers": {
      "deepseek": { "ApiKey": "sk-your-key" }
    }
  }
}
EOF
```

### 2. 运行

```bash
cd cognitive-mesh/Aevatar.CognitiveMesh
dotnet run
```

### 3. 打开浏览器

访问 `http://localhost:5678`（如端口冲突可自行改）

---

## 🧠 支持的策略

| 策略 | 类型 | 状态 | 描述 |
|------|------|------|------|
| **Direct** | `Direct` | ✅ 可用 | 单次 LLM 调用，最简策略 |
| **MAKER** | `Maker` | ✅ 可用 | 分解-共识-合成，多 Agent 投票 |
| **C-UoT** | `UotCombinational` | ✅ 可用 | 类比检索 + 思维合成 |
| **E-UoT** | `UotExploratory` | ✅ 可用 | 探索域外思想，扩展思维边界 |
| **T-UoT** | `UotTransformative` | ✅ 可用 | 挑战隐藏假设，颠覆性创新 |
| **CoT** | `Cot` | 📋 规划中 | 线性推理链 |
| **ToT** | `Tot` | 📋 规划中 | 分支与剪枝 |

### 策略复杂度光谱

```
简单 ←────────────────────────────────────────────────→ 复杂

Direct    CoT/ToT    MAKER           UoT (C/E/T)
 │           │         │                  │
 1 call    线性链    递归分解           类比合成
                    并发投票           规则变异
```

---

## 🗺️ 演进路线

详见 [ROADMAP.md](./ROADMAP.md)

| 阶段 | 目标 | 状态 |
|------|------|------|
| **Phase 1** | 策略抽象层 | ✅ 完成 |
| **Phase 2** | 统一执行引擎 | ✅ 完成 |
| **Phase 2.5** | UoT 三重奏 (C/E/T-UoT) | ✅ 完成 |
| **Phase 3** | 内容加载 + 任务模板 | ✅ 完成 |
| **Phase 3.5** | **CognitiveGAgentBase** | 📋 设计中 |
| **Phase 4** | **DSL 引擎 + 热重载** | 📋 计划中 |
| **Phase 5** | 可视化增强 | 📋 计划中 |
| **Phase 6** | 高级功能 (断点续传) | 📋 计划中 |

---

## 📐 核心抽象

### IReasoningStrategy

```csharp
public interface IReasoningStrategy
{
    StrategyKind Kind { get; }
    
    Task<ReasoningResult> ExecuteAsync(
        string problem,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress = null,
        CancellationToken ct = default);
    
    ValidationResult ValidateOptions(ReasoningOptions options);
}
```

### ReasoningOptions

```csharp
// MAKER
options = ReasoningOptions.ForMaker(
    reliability: MakerReliability.High,
    maxLlmCalls: 100
);

// UoT Combinational
options = ReasoningOptions.ForUotCombinational(
    domainHint: "distributed systems",
    maxAnalogies: 5
);

// UoT Exploratory
options = ReasoningOptions.ForUotExploratory(
    maxOutsideThoughts: 10,
    explorationDirections: 3
);

// UoT Transformative
options = ReasoningOptions.ForUotTransformative(
    maxRuleSets: 3,
    minRadicality: 0.5f
);
```

---

## 🔌 API Endpoints

| Endpoint | Method | 描述 |
|----------|--------|------|
| `/api/strategies` | GET | 列出可用策略 |
| `/api/projects` | GET | 列出所有项目 |
| `/api/projects` | POST | 创建新项目 |
| `/api/projects/{id}` | DELETE | 删除项目 (归档) |
| `/api/projects/{id}/run` | POST | 启动执行 |
| `/api/projects/{id}/stop` | POST | 停止执行 |
| `/api/projects/{id}/status` | GET | 获取运行状态 |
| `/api/projects/{id}/events` | GET (SSE) | 实时事件流 |
| `/api/archive` | GET | 列出归档项目 |
| `/api/archive/{fileName}/restore` | POST | 恢复归档项目 |

---

## 📖 文档索引

| 文档 | 说明 |
|------|------|
| [ROADMAP.md](./ROADMAP.md) | 渐进式设计路线图，详细阶段规划 |
| [COGNITIVE_AGENT_BASE_DESIGN.md](./COGNITIVE_AGENT_BASE_DESIGN.md) | **🆕 CognitiveGAgentBase 设计**：原语、DSL、持久化 |
| [CONTENT_LOADING_DESIGN.md](./CONTENT_LOADING_DESIGN.md) | 内容加载与任务模板设计 |
| [IMPLEMENTATION_STATUS.md](./IMPLEMENTATION_STATUS.md) | 当前实现状态、已知限制 |

---

## 🎯 终极目标预览

### DSL 定义策略 (Phase 4)

```yaml
# workflows/maker.yaml
name: maker
steps:
  - id: check_atomic
    type: llm_call
    prompt: "Is this atomic? {{task}}"
    
  - id: process
    type: conditional
    condition: "{{is_atomic}}"
    if_true:
      - type: vote
        k: 2
        generator: { prompt: "Solve: {{task}}" }
    if_false:
      - type: vote
        generator: { prompt: "Decompose: {{task}}" }
      - type: fan_out
        for_each: subtasks
        step: { type: workflow_call, workflow: maker }
```

详见 [COGNITIVE_AGENT_BASE_DESIGN.md](./COGNITIVE_AGENT_BASE_DESIGN.md)

---

## 🔗 相关资源

- [MAKER Paper](https://arxiv.org/abs/2411.00332) - 分解-共识-合成理论
- [Universe of Thoughts](https://arxiv.org/html/2511.20471v2) - 创意推理框架
- [Aevatar Agent Framework](../../README.md) - 底层 Actor 框架

---

*Last Updated: 2025-12-04*
