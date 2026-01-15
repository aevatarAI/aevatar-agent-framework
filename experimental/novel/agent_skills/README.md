### experimental/novel/agent_skills - 写作系统 Skills（SOP / 模板 / Rubric）

这些 Skills 的定位是 **“知识/流程层”**：把 SOP、模板、评分 rubric 从代码里剥离出来，让 AI 需要时用 `skills_load` 按需加载（见仓库 `docs/AGENT_SKILLS_GUIDE.md`）。

### 设计原则

- **Skill ≠ Agent**：Skill 不维护长期状态；长期职责/状态/审计由 Agent 承担。
- **Skill ≠ Tool**：Tool 才是执行层（读写文件/SQLite/生成 diff/合并算法等）；Skill 只规定“怎么做”和“何时调用哪些工具”。
- **文件是唯一真相源（SSOT）**：正文 `.txt`（不输出 Markdown 语法）；其他产物 `.md`；SQLite 仅索引可重建。

### 当前约定（v1）

- 每个 skill 文件夹至少包含 `SKILL.md`。
- 早期阶段：`allowed-tools` 暂不强制写死（避免工具命名尚未稳定导致 skill 把工具集过滤成空）。
  - 当 novel sidecar 的 tool set 稳定后，再为每个 skill 补齐 `allowed-tools` 白名单以增强安全性。


