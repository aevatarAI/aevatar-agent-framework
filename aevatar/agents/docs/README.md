# Agents YAMLs (Global Roles)

Global role configurations consumed by `GlobalAgentYamlRegistry`. These YAMLs define system prompts and event modules for the Vibe workflows.

## Structure

```
aevatar/agents/
  planner.yaml                # planner role prompt
  reasoner.yaml               # reasoner role prompt
  librarian.yaml              # librarian role prompt
  verifier.yaml               # verifier role prompt
  dag_builder.yaml            # dag_builder role prompt (JSON-only output)
  paper_editor.yaml           # paper_editor role prompt (JSON-only output)
  research_assistant.yaml     # research_assistant role prompt
  workflow_coordinator.yaml   # base coordinator event modules
  vibe_coordinator.yaml       # workflow coordinator + vibe_* step modules
```

## Design Notes

- **Workflow-first**: cognitive workflows reference these roles via `agent:` fields.
- **Coordinator split**: `vibe_coordinator` extends the base workflow modules with Vibe-specific step modules.
- **Provider overrides**: per-role providers can be set in YAML (applied at runtime).

## Change Log

- 2026-01-30: Added Vibe role YAMLs + `vibe_coordinator` for workflow step modules.
