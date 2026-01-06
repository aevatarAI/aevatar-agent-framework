# Architecture Notes (ClaudeAgentSdkProviderDemo)

## Purpose

This demo exists to make the **behavioral differences** of `ProviderType = "claude_agent_sdk"` tangible and reproducible:

- project settings (mock: `.claude/*`; real: `CLAUDE.md` via `settingSources=['project']`)
- plugins (mock demo only; real mode keeps plugins empty by default)
- allow-list permissions (`allowedTools`)
- process isolation (runner)
- streaming markers

## Structure

```
examples/ClaudeAgentSdkProviderDemo/
├── Program.cs                       # runs baseline/minimal/full comparisons + streaming
├── BaselineDeterministicProvider.cs # in-process baseline provider (no runner/.claude/plugins)
├── appsettings.json                 # declares claude_agent_sdk providers (demo-only)
├── runner/                          # runners (mock + real) + package.json for Agent SDK install
├── plugins/                         # mock-mode demo plugins (deterministic transforms)
└── demo_project/
    ├── .claude/                     # demo project-level settings (File-SSoT)
    ├── CLAUDE.md                    # real-mode project instructions (settingSources=['project'])
    ├── data/                        # permission-gated read input
    └── output/                      # permission-gated write target
```

## Modes

- Mock (default): `CLAUDE_AGENT_SDK_DEMO_MODE` unset → uses `runner/mock_claude_agent_sdk_runner.mjs` (offline)
- Real (optional): `CLAUDE_AGENT_SDK_DEMO_MODE=real` → uses `runner/real_claude_agent_sdk_runner.mjs` (calls official SDK)

## Key boundary

`claude_agent_sdk` provider is runner-based and **does not** map Aevatar function-calling/tool-loop into the runner.
It must never return `AevatarFunctionCall` to avoid stacking two independent orchestrators.


