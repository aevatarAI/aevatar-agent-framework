# Cognitive Mesh DSL 规范

> **目标**：把“自然语言认知拓扑”收敛成**可声明、可验证、可回放**的最小指令集，为 Cognitive Mesh 的 `Cognitive Compiler` 提供确定性的输入。

---

## 0. 没有 DSL 会怎样？
- **无法重现**：纯自然语言指令不可版本化也不可 diff，Mesh 拓扑在不同时间点可能完全不同，Million-Step 回放失去根基。
- **状态漂移**：Strategy/Node/Budget 等关键信息散落在 prompt 中，无法与 Orleans 的持久化状态对齐，生成-验证-提交链条形同虚设。
- **验证缺位**：没有结构化 Schema，就无法在进入 Actor 集群前做静态校验或静默演练，CriticAgent 只能事后补锅。
- **安全隐患**：自由文本可植入任意隐式指令或超规参数，导致算力滥用、权限绕过甚至代码注入。
- **不可观测**：缺乏 DSL 的系统无法建立审计日志、无法对比变更，更无法针对特定拓扑做负载或成本预测。

> 结论：DSL 不是附加功能，而是 Cognitive Mesh 得以“可控、可回放、可扩展”的唯一途径。

---

## 1. 设计原则
- **最小可用**：优先定义覆盖 80% 场景的核心字段，避免一次性暴露全部内部抽象。
- **强类型映射**：DSL 中的每个字段都要对应 C# 中的 record/class，保持 Schema 与代码一一对应。
- **可验证**：所有输入在进入 Orleans 之前必须通过 JSON Schema + 语义校验，禁止“动态”字段绕过约束。
- **可进化**：每次 Schema 变更都必须带版本号与迁移策略，保证旧 DSL 可继续解析或平滑退出。

---

## 2. 最小 Schema（v0.1 提案）

```jsonc
{
  "dsl_version": "0.1",
  "goal": {
    "name": "string",
    "success_metric": "string | null"
  },
  "strategy": "cot | tot | got | uot_comb | uot_expl | uot_trans",
  "budget": {
    "max_steps": 1000,
    "token_limit": 2_000_000
  },
  "nodes": [
    {
      "id": "explorer",
      "type": "DivergentAgent",
      "params": {
        "branch_width": 5
      }
    }
  ],
  "edges": [
    {
      "from": "explorer",
      "to": "judge",
      "channel": "submit_hypothesis"
    }
  ],
  "constraints": [
    {
      "type": "confidence_threshold",
      "value": 0.9
    }
  ]
}
```

### 字段约束
- `strategy`：限定为枚举，Transformative 策略必须至少包含一个 `MetaAgent` 节点。
- `nodes[*].type`：对应注册的 Agent 原型（如 `DivergentAgent`、`CriticAgent`），禁止自由命名。
- `edges`：默认单向；如需广播，使用 `channel` 声明驱动器。
- `constraints`：首批仅开放 `confidence_threshold`、`max_iterations` 两种类型，逐步扩展。

---

## 3. C# 强类型承载

```csharp
public record GoalSpec(string Name, string? SuccessMetric);

public enum StrategyKind { Cot, Tot, Got, UotComb, UotExpl, UotTrans }

public record NodeSpec(
    string Id,
    AgentPrototype Prototype,
    IReadOnlyDictionary<string, JsonElement> Params);

public record EdgeSpec(string From, string To, string Channel);

public record ConstraintSpec(string Type, JsonElement Payload);

public record MeshDefinition(
    string DslVersion,
    GoalSpec Goal,
    StrategyKind Strategy,
    BudgetSpec Budget,
    IReadOnlyList<NodeSpec> Nodes,
    IReadOnlyList<EdgeSpec> Edges,
    IReadOnlyList<ConstraintSpec> Constraints);
```

### 校验机制
1. **结构校验**：基于 `JsonSchema.Net`/`System.Text.Json.Schema` 验证字段存在性与类型。
2. **注解校验**：使用 `DataAnnotations` 或 FluentValidation 保证业务约束（ID 唯一、channel 存在等）。
3. **语义校验**：自定义 `IMeshSemanticRule`，如“Transformative 策略必须声明目标重写器”。

---

## 4. Cognitive Compiler 流水线
1. **模板引导**：向用户返回固定模板或 DSL 表单，以减少自由文本解析开销。
2. **LLM 解析（可选）**：若用户仍以自然语言描述，使用 LLM 输出 JSON，并要求其严格遵守 Schema。
3. **Schema 验证**：解析后立即运行结构校验；若失败，LLM 必须自修或提示用户修正。
4. **语义检查**：运行规则集，生成 `MeshDefinition`。错误时抛出 `DslCompilationException`，携带定位信息。
5. **持久化审计**：成功的 DSL 版本 + Hash 需记录在事件流中，方便变更回溯。

---

## 5. 版本化与测试
- **版本号**：在 DSL 顶层携带 `dsl_version`。解析器根据版本路由到对应的 Schema/Rule 套件。
- **回归用例**：为每个策略类型提供典型 DSL 样例，存放在 `apps/Aevatar.CognitiveMesh/docs/examples/v0.1/`。
- **CI 校验**：新增 DSL/Schema 变更必须伴随解析器单测，覆盖成功与失败用例。
- **迁移策略**：当需要弃用字段时，提供 `DslMigration` 服务，将老 DSL 自动映射到新版结构或显式拒绝。

---

## 6. 工具与可观测性
- **CLI/Tooling**：提供 `dotnet tool mesh validate path/to/file.yaml`，在本地执行 Schema+语义验证。
- **IDE/LSP**：后续可用 `Language Server Protocol` 提供补全和错误提示，降低编写门槛。
- **诊断日志**：编译器需要记录每一步的输入输出（含 LLM 解析结果），方便故障定位与安全审计。

---

## 7. 后续演进
1. 引入 `SubMesh`/`ReusableTemplate`，但仅在 v0.2 之后、主 DSL 稳定后开放。
2. 为 `constraints` 增加“可执行断言”类型（脚本或规则引擎），强化 Zero-Error Loop 的可信度。
3. 将 DSL 与权限系统绑定，限制不同角色可声明的节点/预算，避免滥用算力。

---

## 8. DSL 与 LLM 的协作
- **职责分离**：LLM 负责把自然语言意图解析成候选结构，DSL 负责用强类型 Schema 限定可执行的 Mesh 拓扑，二者并行而非对立。
- **编译前端**：LLM 是 `Cognitive Compiler` 的语义前端，可输出 JSON 片段；但其结果必须通过 DSL 的 Schema + 语义规则验证，否则拒绝执行。
- **安全护栏**：DSL 提供“可审计、可重放”的约束，防止 prompt 插入隐含指令、私自扩大预算。LLM 在护栏内发挥创造力，而不是独揽调度权。
- **失败自修**：当验证失败时，可让 LLM 根据校验错误重新生成结构，形成自动化的 prompt→DSL→验证→修复闭环。
- **审计协同**：DSL 版本号、哈希和 LLM 输入一同写入事件流，既能回放认知过程，也能定位由 LLM 造成的偏差。

---

## 9. 参考实现（C#）
- **项目位置**：`/aevatar-agent-framework/src/Aevatar.CognitiveMesh.Dsl`
- **核心类型**：
  - `MeshDefinition`/`GoalSpec`/`BudgetSpec` 等强类型模型。
  - `CognitiveDslCompiler`：提供 `Compile(string json)`、`CompileAsync(Stream)` 等入口，自动执行结构与语义校验。
  - `IMeshSemanticRule`：可插拔校验接口，默认实现包含「节点唯一」「边引用」「Transformative 元节点」等规则。

### 快速示例
```csharp
var compiler = new CognitiveDslCompiler();
var definition = compiler.Compile(File.ReadAllText("mesh.json"));

Console.WriteLine($"Strategy: {definition.Strategy}");
Console.WriteLine($"Nodes: {definition.Nodes.Count}");
```

任何校验失败都会抛出 `DslCompilationException`，其中 `Errors` 属性列出具体原因，方便反馈给 LLM 或前端表单。

> **结论**：只有把 DSL 收敛成最小可验证单元，Cognitive Mesh 才能兑现“零错误、可回放、可扩展”的架构承诺。


