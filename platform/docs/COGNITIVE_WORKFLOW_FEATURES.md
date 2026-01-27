# Cognitive Workflow 功能与路线图（基于当前代码）

本文基于当前 Cognitive Mesh / Platform / Workshop 代码梳理已有能力，并补充一份面向未来的功能畅想。重点覆盖：
Load agent YAML、Load workflow YAML、Run workflows、Draw workflow graph、Hermes 路由与产出 YAML。

## 现有能力（当前代码已具备）

### 1) Load agent YAML file（加载角色 YAML）
- **加载路径与优先级**：平台侧会按角色名规范化后在本地 `./aevatar/agents` 与全局 `~/.aevatar/agents` 中查找 YAML/YML。
- **YAML 生效方式**：角色 YAML 会注入到 Agent 的 system prompt、模型参数与工具/技能配置中。
- **示例入口（Workshop）**：`/api/agent/yaml` 支持 GET（读取角色 YAML）与 POST（保存 YAML，并可创建会话）。

### 2) Load workflow YAML file（加载/编译工作流 YAML）
- **编译能力**：平台侧支持 JSON/YAML 直接编译为 MeshDefinition，且会合并允许的角色列表（全局/本地角色 YAML）。
- **示例入口（Workshop）**：`/api/workflow/yaml` 保存 YAML 到 `~/.aevatar/workflows`，并触发编译 + 载入，返回编译结果与图谱数据。

### 3) Run workflows（运行工作流）
- **最小可用执行链**：WorkflowEngine 负责生成执行计划，WorkflowExecutor 执行计划并处理 Hermes 路由。
- **两种执行路径**：  
  - **Hermes 节点存在** → HermesRouter 决策（选/创建）→ 运行下游 workflow 或单角色。  
  - **无 Hermes 节点** → 以第一个节点类型作为角色执行（最小 MVP 运行）。

### 4) Draw workflow graph（工作流图谱绘制）
- **图谱快照模型**：WorkflowMeshService 将 MeshDefinition 转为 `WorkflowGraphSnapshot`（节点、边、层级深度）。
- **示例入口（Workshop）**：`/api/workflow/graph` 返回最近一次加载的图谱快照，供前端绘制 DAG。

### 5) Hermes AI Agent（路由 + 产出 YAML）
- **Hermes 角色定位**：根据用户意图选择已有 workflow，或生成新的 workflow/agent YAML。
- **工具受控写入**：Hermes 通过 file_read/file_write/mesh_normalize 三个工具完成“生成-校验-落盘”闭环，并受路径白名单限制。
- **默认引导配置**：启动时自动引导 Hermes 的默认 YAML 与 workflow（agent_router / agent_creator）。

## 平台关键实现摘录（节选）

### Agent YAML 加载路径（平台侧）
```csharp
// platform/src/Aevatar.Platform.Core/Workflow/RoleAgentRunner.cs
private static AgentYamlConfig? TryLoadYamlForRole(string role, WorkflowRunInput input)
{
    var key = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
    if (key.Length == 0)
        return null;

    var loader = new AgentYamlConfigLoader();
    var localDir = Path.Combine(input.WorkingDirectory, "aevatar", "agents");
    var globalDir = Path.Combine(input.ConfigDirectory, "agents");

    var localYaml = Path.Combine(localDir, $"{key}.yaml");
    var localYml = Path.Combine(localDir, $"{key}.yml");
    var globalYaml = Path.Combine(globalDir, $"{key}.yaml");
    var globalYml = Path.Combine(globalDir, $"{key}.yml");

    return loader.TryLoadFromFile(localYaml)
           ?? loader.TryLoadFromFile(localYml)
           ?? loader.TryLoadFromFile(globalYaml)
           ?? loader.TryLoadFromFile(globalYml);
}
```

### Hermes 产出 YAML 的创建流程（平台侧 Prompt 约束）
```csharp
// platform/src/Aevatar.Platform.Core/Workflow/HermesRouter.cs
// Creation pipeline (strict order):
// 1) Re-organize the user's request into a clear goal + constraints.
// 2) Decide whether a single agent can handle it; if yes, set selected_workflow="direct" and create/ensure ONE agent YAML.
// 3) If multi-step is required, draft agent YAMLs (missing roles only).
// 4) Draft workflow DSL v0.1 (minimal 1-3 nodes).
// 5) Call mesh_normalize with the workflow content and fix any errors until ok=true.
// 6) Use file_write to write agent YAML(s) to ~/.aevatar/agents and workflow to ~/.aevatar/workflows.
```

### Hermes 工具注册（平台侧）
```csharp
// platform/src/Aevatar.Platform.Core/Workflow/HermesAIGAgent.cs
protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
{
    await base.RegisterToolsAsync(cancellationToken);
    await RegisterToolAsync(new FileReadTool(_fileToolOptions), cancellationToken: cancellationToken);
    await RegisterToolAsync(new FileWriteTool(_fileToolOptions), cancellationToken: cancellationToken);
    await RegisterToolAsync(new MeshNormalizeTool(_configDirectory, _fileToolOptions.WorkingDirectory), cancellationToken: cancellationToken);
}
```

## 端到端路径（现有最小闭环）
1) **用户提供/更新 YAML** → 保存到本地或全局目录。  
2) **YAML/JSON 编译** → 生成 MeshDefinition 并校验。  
3) **生成执行计划** → WorkflowEngine 进行拓扑排序与计划生成。  
4) **执行** → WorkflowExecutor 处理 Hermes 路由或单角色运行。  
5) **输出图谱** → WorkflowGraphSnapshot 提供给前端可视化。

## 未来畅想（平台级路线图）
- **真正的多节点编排**：并行节点、依赖约束、回退重试、预算守卫与可恢复执行。
- **图谱驱动的创作与审计**：可视化编辑、版本化、Diff/Merge、策略验证与合规模板。
- **Hermes 生态化**：Workflow 模板库、角色/技能市场、意图驱动的自动组装与召回。
- **跨运行时的统一调度**：Local/Orleans/ProtoActor 上一致的执行语义与自动扩缩容。
- **观测与评估一体化**：链路追踪、成本/延迟指标、回放与 A/B 评测。
- **安全与治理**：工具调用策略、YAML 签名与审批、执行沙箱与权限分级。
- **知识与记忆网络**：Memory Graph + Context Compression，让 workflow 能“记住”并持续优化。

---
本文定位为“代码事实 + 能力蓝图”。如需进一步落地，可围绕 DSL 扩展、执行引擎、UI 与评测体系拆成迭代计划。
