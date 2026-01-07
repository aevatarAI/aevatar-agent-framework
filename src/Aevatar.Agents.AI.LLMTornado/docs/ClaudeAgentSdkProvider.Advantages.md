# Claude Agent SDK Provider 的优势与适用场景

本文件解释：为什么要在 Aevatar 中引入 `ProviderType = "claude_agent_sdk"`，以及什么情况下它 **比直接调用 Claude 模型 API 更合适**。

## 先把话说清：两种“用 Claude”的方式

在 Aevatar 里，你通常会遇到两条路线：

- **路线 A：直接调用 Claude 模型 API（推荐默认）**
  - 选择：`Aevatar.Agents.AI.LLMTornado` + `ProviderType = "anthropic"` / `"claude"`
  - 本质：把 Claude 当成“普通 LLM”，Aevatar 主导 tool-loop（如果你启用了它）

- **路线 B：Claude Agent SDK Provider（本文件主角）**
  - 选择：`Aevatar.Agents.AI.LLMTornado` + `ProviderType = "claude_agent_sdk"`
  - 本质：把“agent 编排”交给 Claude Agent SDK，通过 **进程外 runner** 执行，Aevatar 把它当成一个 provider

> 关键边界：`claude_agent_sdk` provider **不会**把 Aevatar 的 tools/function-calling 映射给 Claude Agent SDK，也 **不会**返回 `AevatarFunctionCall`，避免双重 tool-loop 叠加。

## 选择 `claude_agent_sdk` 的核心优势

### 1) 复用 Claude Agent SDK 的“工程化 agent 能力”

当你需要的不是“一个模型回答”，而是“一个 agent 系统”时，Claude Agent SDK 的优势开始显现：

- **Subagents / plugins 的组织方式**：把复杂任务拆成可组合的子 agent + 插件生态
- **settings sources / 项目级配置**：通过 `.claude/*` 读取项目配置（文件即真相源，可审计）
- **权限与执行策略**：把“能做什么”收敛在 Claude Agent SDK 的权限系统里（更贴近 agent 工程）
- **对长任务友好**：更偏工程的会话/执行模型（与普通“单轮 completion”心智不同）

### 2) 将“编排复杂度”隔离在进程外，保持 Aevatar 内核干净

- Aevatar 仍然以 `IAevatarLLMProvider` 的方式接入，无需污染现有 LLMTornado 的 Anthropic/OpenAI 路径
- 进程外 runner 让 Node/Python 生态成为可选依赖，不会被拉进 .NET 依赖树
- 失败域更清晰：runner 崩了就是一次调用失败（可重试、有超时、有诊断），不会把进程内状态打成麻花

### 3) 更贴合 File-SSoT 与可审计的协作方式

如果团队已经在用 Claude Code/Claude Agent SDK 的项目结构：

- `.claude/*`、插件目录、以及相关的约束配置，本来就是文件化的
- `claude_agent_sdk` provider 可以直接吃这些资产，让 Aevatar 复用，而不是把同样的东西“再实现一套”

## 什么时候更适合用 `claude_agent_sdk`

把它当成一个“更像工程 agent 的后端”，适用场景通常是：

- **你已经有 Claude Agent SDK 的资产**：已有 `.claude` 配置、subagents、plugins，希望“无痛搬进 Aevatar”
- **任务是强编排/强工具生态的长任务**：多阶段、多约束、多插件协作，且你愿意把编排交给 Claude Agent SDK
- **你希望把执行环境隔离**：外部 runner 便于容器化/沙箱化/最小权限运行（尤其是命令/文件工具敏感时）
- **你希望 Aevatar 保持 tool-loop 边界**：让 Aevatar 做“上层业务与事件系统”，让 Claude Agent SDK 做“下层 agent 编排”

## 什么时候不要用它（改用别的）

下面这些情况通常 **不需要** `claude_agent_sdk`：

- **你只是要 Claude 模型能力**（最常见）
  - 用 `ProviderType = "anthropic"` / `"claude"`（LLMTornado）
  - 复杂度更低、依赖更少、延迟更小

- **你需要 Aevatar 严格主导工具治理/审计**（例如只允许框架内置工具、统一权限与日志）
  - 用 Aevatar 的 tool-loop（并选一个支持 function-calling 的 provider 路线）
  - `claude_agent_sdk` 的设计目标就是避免把 Aevatar tools 映射进去

- **你的部署环境不允许进程外依赖**（不能运行 Node/Python、不能接受外部 runner）
  - 选纯 .NET 的 provider 路线

- **你追求极致吞吐/极致低延迟**
  - 进程外 runner 会引入额外开销；更适合用 in-process 的模型 provider

## 快速决策清单（30 秒）

如果以下问题多数回答“是”，更适合 `claude_agent_sdk`：

- 是否已有 `.claude/*` 与 Claude Agent SDK 的 subagents/plugins 资产？
- 是否需要 Claude Agent SDK 的权限/插件生态来驱动复杂编排？
- 是否更愿意把“编排复杂度”隔离到进程外（更易沙箱化）？
- 是否能接受不把 Aevatar tools 映射进 Claude Agent SDK（避免双 tool-loop）？

否则，默认选 `anthropic/claude` 模型调用更简单、更稳定。

## 相关文档

- 配置与 runner 协议：`ClaudeAgentSdkProvider.md`


