# Architecture Notes (ClaudeAgentSdkProviderDemo)

## Purpose

This demo exists to make the **behavioral differences** of `ProviderType = "claude_agent_sdk"` tangible and reproducible (offline):

- `.claude/*` project settings (`projectRoot`)
- plugins
- allow-list permissions (`allowedTools`)
- process isolation (runner)
- streaming markers

## Structure

```
examples/ClaudeAgentSdkProviderDemo/
├── Program.cs                       # runs baseline/minimal/full comparisons + streaming
├── BaselineDeterministicProvider.cs # in-process baseline provider (no runner/.claude/plugins)
├── appsettings.json                 # declares claude_agent_sdk providers (demo-only)
├── runner/                          # mock runner (Node, no deps)
├── plugins/                         # demo plugins (deterministic transforms)
└── demo_project/
    ├── .claude/                     # demo project-level settings (File-SSoT)
    ├── data/                        # permission-gated read input
    └── output/                      # permission-gated write target
```

## Key boundary

`claude_agent_sdk` provider is runner-based and **does not** map Aevatar function-calling/tool-loop into the runner.
It must never return `AevatarFunctionCall` to avoid stacking two independent orchestrators.


