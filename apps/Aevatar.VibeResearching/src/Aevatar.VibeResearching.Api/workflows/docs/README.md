## VibeResearching Workflows

This directory contains the Cognitive workflows used by VibeResearching.
SessionRuntime selects a workflow by `mode` and runs it via the Session API.

### Mode -> Workflow Mapping

- `chat` -> `vibe_chat.yaml`
- `vibe_researching` -> `vibe_researching.yaml`
- `single` -> `vibe_single.yaml`
- `axiom` -> `vibe_axiom.yaml`
- `vibe_loop` / `vibe_goal_loop` -> `vibe_goal_loop.yaml`
- `vibe` -> `vibe_researching.yaml`
- `milestone` / `vibe_milestone` / `research` -> `vibe_milestone.yaml`

### Structure

```
workflows/
  vibe_researching.yaml   # Multi-agent research pipeline (planner -> reasoner -> ...)
  vibe_chat.yaml          # Single-step research assistant chat
  vibe_single.yaml        # One-shot wrapper for vibe_researching
  vibe_milestone.yaml     # Milestone-mode wrapper for vibe_researching
  vibe_goal_loop.yaml     # Bounded loop wrapper for vibe_researching
  vibe_axiom.yaml         # Axiom-based reasoning (single-pass)
  maker.yaml              # Consensus workflow used by vibe_researching
  docs/README.md          # This document
```

### Dependencies

- API routing is handled in:
  - `apps/Aevatar.VibeResearching/src/Aevatar.VibeResearching.Api/Sessions/Api/ResearchSessionsApi.InputAndFacts.cs`
- Workflow calls:
  - `vibe_single.yaml`, `vibe_milestone.yaml`, `vibe_goal_loop.yaml` call `vibe_researching.yaml`
  - `vibe_researching.yaml` calls `maker.yaml` when `dag_consensus_mode == "maker"`
