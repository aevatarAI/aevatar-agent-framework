## SkillsMCPUnifiedDemo

目的：提供一个最小可跑的“统一 demo”，把 **Agent Skills / dotnet-file skills / MCP tools** 三条能力链串起来。

### 目录结构

```
examples/SkillsMCPUnifiedDemo/
├── Program.cs
├── UnifiedAgent.cs
├── SkillsMCPUnifiedDemo.csproj
├── appsettings.json
├── appsettings.secrets.json              # 不提交 git
├── Tools/
│   ├── TextStatsTool.cs                  # in-proc tool: text_stats
│   ├── JsonPrettifyTool.cs               # in-proc tool: json_prettify
│   └── SlugifyTool.cs                    # in-proc tool: slugify
├── agent_skills/
│   ├── time-helper/SKILL.md
│   ├── env-helper/SKILL.md
│   ├── file-reader/SKILL.md
│   ├── file-searcher/SKILL.md
│   ├── text-analyzer/SKILL.md
│   ├── json-pretty/SKILL.md
│   ├── slugify-helper/SKILL.md
│   ├── context7-docs/SKILL.md
│   └── mcp-filesystem-browse/SKILL.md
├── skills/
│   ├── get_time.cs
│   ├── system_info.cs
│   ├── get_env.cs
│   ├── file_read.cs
│   └── file_search.cs
└── docs/
    └── ARCHITECTURE.md
```

### 关键设计

- `UnifiedAgent`：继承 `AIGAgentBase`，在 `RegisterToolsAsync` 里统一注册：
  - Aevatar 内置 tools（state/event/memory + skills_list/skills_load）
  - in-proc tools（demo 内 C# 类注册）
  - dotnet-file tools（`dotnet run --file`）
  - MCP tools（docker filesystem / context7 / github，均为 best-effort）
- `allowed-tools`：当 `skills_load` 返回 `allowedTools`，本轮 tool-loop 内会启用 allowlist：
  - 过滤给 LLM 的 Functions
  - 拦截执行层（防止 hallucinate）
- `AEVATAR_DEMO_ROOT`：Program 会把运行时 CWD 写入该环境变量，供 dotnet-file 工具解析相对路径/挂载到 docker。

