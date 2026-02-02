## VibeResearching Workflows

This directory contains the Cognitive workflows used by VibeResearching.
SessionRuntime selects a workflow by `mode` and runs it via the Session API.

### Mode -> Workflow Mapping

- `chat` -> `vibe_chat.yaml`
- `vibe_researching` -> `vibe_researching.yaml`
- `single` -> `vibe_single.yaml`
- `axiom` -> `vibe_axiom.yaml`
- `vibe_loop` / `vibe_goal_loop` -> `vibe_goal_loop.yaml`
- `vibe` / `milestone` / `vibe_milestone` / `research` -> `vibe_milestone.yaml`

### Structure

```
workflows/
  vibe_researching.yaml   # Multi-agent research pipeline (context -> workers -> dag apply -> trace)
  vibe_chat.yaml          # Single-step research assistant chat
  vibe_single.yaml        # One-shot wrapper for vibe_researching
  vibe_milestone.yaml     # Milestone-mode wrapper (calls vibe_goal_loop)
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

### Vibe Researching (Key Steps)

`vibe_researching.yaml` now owns the per-round pipeline. It relies on step modules:

- `vibe_context`: load plan/materials/DAG stats + paper excerpts
- `vibe_pivot_detection`: detect direction changes (pivot)
- `planner` / `reasoner` / `librarian` / `verifier`: core worker roles
- `vibe_librarian_effects`: persist librarian facts + axioms
- `dag_builder` + `dag_consensus`: propose + gate DAG mutation
- `vibe_dag_apply`: apply accepted mutation to DagStore
- `paper_editor` + `vibe_delivery_apply`: apply paper patches + delivery snapshots
- `round_summary` + `vibe_trace_append`: generate summary + append trace

### Workflow Inputs

`session_id` and `run_id` are injected by `SessionWorkflowRunner` when the workflow declares these inputs.
