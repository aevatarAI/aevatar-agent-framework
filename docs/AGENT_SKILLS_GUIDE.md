## Agent Skills 集成与使用指南

本指南说明：如何在 **Aevatar `AIGAgentBase`** 中集成与使用 **Agent Skills（SKILL.md）**，以及当前实现支持的功能边界与安全约束。

参考：
- [Agent Skills overview](https://agentskills.io/home)
- [Claude Agent Skills docs](https://docs.claude.com/en/docs/agents-and-tools/agent-skills)

---

## 设计目标

- **按需加载**：不要把所有 SOP/知识都塞进 System Prompt；让模型需要时再加载对应 skill。
- **工具化**：skill 通过工具（`skills_list`/`skills_load`）暴露给模型，避免耦合在 prompt 拼接里。
- **可控风险**：`allowed-tools` 变成“硬约束”（不仅是文档字段），避免 skill 滥用高危工具。
- **可组合**：skill 可以指导调用 dotnet-file tools / MCP tools / 内置 tools，并可通过 allowlist 约束范围。

---

## 术语

- **Skill Root**：技能根目录，包含多个 skill 文件夹。
- **Skill Folder**：单个技能目录，至少包含 `SKILL.md`。
- **`SKILL.md`**：技能入口文件，包含 YAML front matter（元信息）+ 正文（流程/规范）。
- **Agent Skills Tools**：Aevatar 暴露给 LLM 的两个工具：`skills_list`、`skills_load`。
- **dotnet-file tool**：`*.cs` 单文件工具，通过 `.NET 10` 的 `dotnet run --file` 执行（`/*aevatar_tool ... */` manifest）。
- **MCP tool**：由 MCP server 提供的工具（通过 `ToolManager.RegisterMCPServerAsync(...)` 注册）。

---

## Skills vs AI tool / MCP tool（实现与维护对比）

先把概念拉直：**Skills 是“知识/流程层”，tools 是“执行层”**。Skills 不替代 AI tool / MCP tool，而是通过 `skills_list/skills_load` 做技能发现与正文按需加载，并可用 `allowed-tools` 在本次 `ChatAsync/ChatStreamAsync` 的 tool-loop 内收紧可用工具集合（见下文“硬约束语义”一节）。

### 对比表（按“实现 & 维护”视角）

| 维度 | Agent Skills（SKILL.md） | AI tool（内置/自定义工具） | MCP tool（MCP Server 工具） |
| --- | --- | --- | --- |
| **主要载体** | `SKILL.md`（YAML front matter + 正文 SOP） | 宿主侧 C# 工具实现 + tool schema | MCP server 对外暴露的 tool |
| **能力定位** | “告诉模型怎么做”：流程、规范、模板、排错步骤 | “让模型能做事”：本地执行动作 | “让模型能做事”：外部系统/远端执行动作 |
| **如何暴露给模型** | 先注册 `skills_list/skills_load` 两个入口 tool | 通过 `AIGAgentBase.RegisterToolsAsync(...)` 注册 | 通过 `ToolManager.RegisterMCPServerAsync(...)` 注册 server |
| **是否按需加载** | ✅ 是：`skills_load` 才把正文加载进上下文 | ❌ 否：通常每次请求都提前把工具定义给模型 | ❌ 否：注册后工具定义可见；调用时再执行 |
| **“不提前注册每个工具”的程度** | ✅ 可：skill 目录内 dotnet-file tools 可在 `skills_load(register_tools=true)` 时自动导入 | ❌ 不行：每个工具都要显式注册/维护 | ❌ 不行：必须先注册 server（工具清单由 server 提供） |
| **安全与权限** | ✅ `allowed-tools` 可做“硬约束”（仅当前 tool-loop） | 取决于宿主侧权限/拦截/参数校验 | 取决于 server/宿主权限；也可被 Skills allowlist 再次收紧 |
| **典型场景** | SOP/Runbook、代码规范、故障排查、模板化任务 | 文件操作、内部 API、计算/转换、受控本地能力 | 调用外部 SaaS、企业工具、检索/数据库、跨系统集成 |

### 选型建议（非常实用）

- **优先 Skills**：当问题本质是“流程/规范/模板复用”，且你希望内容可独立迭代（改 `SKILL.md` 即生效）。
- **使用 AI tool / MCP tool**：当问题需要“真实动作执行”（读写、调用服务、查数据）。
- **组合使用**：让 Skills 规定“何时调用哪个工具 + 参数规范 + 失败时怎么回退”，并用 `allowed-tools` 把可用工具收紧到最小集合。

---

## 集成方式（在 AIGAgentBase 里）

### 代码入口

Agent Skills 集成实现位于：
- `src/Aevatar.Agents.AI.Core/AIGAgentBase.AgentSkills.cs`

并通过 `AIGAgentBase.RegisterToolsAsync(...)`（见 `AIGAgentBase.Tools.cs`）按开关启用：
- `EnableAgentSkills == true` 时注册 `skills_list`、`skills_load`
- 默认 `EnableAgentSkills == false`

### 开启 Agent Skills（推荐方式）

在你的 Agent 构造函数中：

```csharp
public sealed class MyAgent : AIGAgentBase
{
    public MyAgent()
    {
        EnableAgentSkills = true;
        AddAgentSkillsRoot("agent_skills"); // 相对路径或绝对路径
    }
}
```

也可用环境变量配置多个 root（支持 `;` 或 `:` 分隔）：

```bash
export AEVATAR_AGENT_SKILLS_DIRS="/abs/skills;/abs/more-skills"
```

### 开关与行为

- **EnableAgentSkills**：是否向模型暴露 `skills_list/skills_load`（默认关闭）
- **AgentSkillsAutoRegisterDotNetFileTools**：`skills_load` 时是否自动导入 skill 目录内的 dotnet-file tools（默认开启）

---

## 功能使用（模型侧：怎么“用起来”）

### 1) `skills_list`：发现技能

模型调用 `skills_list` 后会拿到：
- **roots**：当前生效的 skill roots（来自 `AddAgentSkillsRoot` + `AEVATAR_AGENT_SKILLS_DIRS`）
- **skills[]**：每个 skill 的 `name/description/allowedTools/path/hasDotNetTools` 等摘要

适用场景：
- 用户提出需求是**流程型**/组织知识型（SOP/规范/模板）
- 模型不确定是否存在可复用技能

### 2) `skills_load`：加载技能正文 +（可选）导入工具

参数：
- **name**（必填）：skill 名称（优先匹配 `SKILL.md` front matter 的 `name`，其次匹配文件夹名）
- **register_tools**（可选）：是否导入该 skill 目录内的 dotnet-file tools（默认使用 `AgentSkillsAutoRegisterDotNetFileTools`）
- **max_chars**（可选）：返回正文最大字符数（默认 16000，范围 1000~128000）

返回字段（关键）：
- **markdown**：`SKILL.md` 正文（front matter 已剥离）
- **allowedTools**：front matter 的 allowlist（若存在）
- **dotnetToolFiles / registeredTools / skipped**：导入 dotnet-file tools 的结果（若启用）

---

## SKILL.md 格式（Aevatar 当前支持）

### 必需字段

- **name**：技能标识（建议小写/短横线风格，如 `time-helper`）
- **description**：一句话描述“这是什么技能 + 何时使用”

### 可选字段：allowed-tools（强烈建议）

`allowed-tools` 是 **工具白名单**，用于限制 skill 运行时可调用的工具集合。

示例：

```yaml
---
name: time-helper
description: Provide accurate current time by calling get_time tool.
allowed-tools:
  - get_time
---
```

### YAML 解析边界（重要）

当前实现是“够用的 YAML 子集解析”，支持：
- `key: value`
- `key: |`（块文本，常用于长 description）
- `key:` + `- item`（列表，主要用于 `allowed-tools`）

建议：
- front matter 必须在文件开头，以 `---` 开始和结束
- key 不要缩进（必须从第 1 列开始）

---

## `allowed-tools` 的硬约束语义（关键 feature）

当模型在一次 `ChatAsync/ChatStreamAsync` 调用中执行 tool-loop：
- 如果某轮调用了 `skills_load` 且返回 `allowedTools` 非空
  - **下一轮开始**：LLM 看到的 Function Definitions 会被过滤为 allowlist 内工具
  - **执行层**：即使模型 hallucinate 调用 allowlist 外工具，也会被拒绝（返回失败的 ToolExecutionResult）

边界：
- **作用域仅限当前一次 Chat 的 tool-loop**  
  下一次新的 `ChatAsync` 会重新构建 request，不会自动继承上一轮 allowlist。
  - 这意味着：一次对话里先用 `time-helper`（只允许 `get_time`），下一条用户消息仍然可以使用 `system_info`（因为是新的 Chat 调用）。

---

## skill 目录内的 dotnet-file tools（可选能力）

如果你希望 skill “自带工具能力”，可以把 `.cs` 单文件工具放到 skill 目录（或子目录）里，并在文件头部放置：
- `/*aevatar_tool { ... } */` JSON manifest

`skills_load(register_tools=true)` 会：
- 扫描 skill 目录内 `*.cs`（递归，最多 32 个，且文件头 16KB 内包含 `/*aevatar_tool` 才认为是工具）
- 自动注册为 Tool（通过 `dotnet run --file` 执行）

注意：
- 这是高危能力（等价于“运行本地代码”），请配合 `allowed-tools` 与可信目录使用。

---

## MCP tools 与 Agent Skills 的组合

你可以：
- 在 skill 正文中规定“遇到某类任务先调用 MCP 工具再决策”
- 在 `allowed-tools` 中把 MCP 工具名加入 allowlist（从而实现“skill 只能用某几个 MCP 工具”）

---

## 安全建议（必读）

- **默认关闭是正确的**：`EnableAgentSkills=false` 避免默认暴露文件系统读取能力。
- **只挂载可信 root**：Skill root 应当是版本控制目录或只读目录。
- **强制使用 allowed-tools**：让 skill 在执行层有硬边界。
- **谨慎开启 auto-import**：`AgentSkillsAutoRegisterDotNetFileTools=true` 会把 skill 目录内的 C# 文件变成可执行工具。

---

## 常见问题排查

- **skills_list 为空**
  - **检查**：是否 `EnableAgentSkills=true`
  - **检查**：`AddAgentSkillsRoot(...)` / `AEVATAR_AGENT_SKILLS_DIRS` 指向的目录是否存在
  - **检查**：skill 目录内是否真的有 `SKILL.md`

- **skills_load 找不到 skill**
  - **匹配规则**：先匹配 front matter 的 `name`，再匹配文件夹名

- **allowed-tools 生效后工具被拒绝**
  - **原因**：工具不在 allowlist 内
  - **修复**：把工具名加入 `allowed-tools`，或在新的 Chat 请求中重新选择/加载 skill

- **dotnet-file 工具没有被导入**
  - **检查**：`.cs` 文件里是否包含 `/*aevatar_tool ... */`
  - **检查**：`register_tools=true` 或 `AgentSkillsAutoRegisterDotNetFileTools=true`

---

## 参考 demo

- `examples/DotNetFileSkillDemo/`：dotnet-file tools + Agent Skills + allowlist（最小闭环）
- `examples/SkillsMCPUnifiedDemo/`：dotnet-file tools + Agent Skills + MCP tools（统一 demo）

