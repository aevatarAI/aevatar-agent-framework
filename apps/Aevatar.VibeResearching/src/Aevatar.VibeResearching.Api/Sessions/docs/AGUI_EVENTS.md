# Vibe Researching AG-UI Events (Frontend Guide)

This document explains how to consume AG-UI events from Vibe Researching,
including **Cognitive maker consensus progress** (DAG consensus).

## 1) SSE endpoint

```
GET /api/sessions/{sessionId}/agui/events
```

Response is `text/event-stream`. Each event is a single JSON object:

```
data: { ...json... }

```

Notes:
- No replay: the stream is live only.
- Bootstrap events are sent immediately after connect (snapshots).

## 2) Bootstrap events (sent on connect)

These are **best-effort** and may be missing if data is not available.

- `MESSAGES_SNAPSHOT`: last assistant/user messages (for chat UI).
- `CUSTOM`:
  - `aevatar.vibe.message_meta_snapshot`
  - `aevatar.ui.tools_snapshot`
  - `aevatar.ui.run_steps_snapshot`
  - `aevatar.scientific.session`
  - `aevatar.vibe.brief_snapshot`
  - `aevatar.vibe.delivery_snapshot`
  - `aevatar.vibe.dag_snapshot`
  - `aevatar.vibe.trace_snapshot`
  - `aevatar.vibe.agents_snapshot`
  - `aevatar.vibe.agent_providers_snapshot`

## 3) Live AG-UI event types

Standard AG-UI events:
- `RUN_STARTED`, `RUN_FINISHED`, `RUN_ERROR`
- `STEP_STARTED`, `STEP_FINISHED`
- `TEXT_MESSAGE_START`, `TEXT_MESSAGE_CONTENT`, `TEXT_MESSAGE_END`
- `STATE_SNAPSHOT`, `STATE_DELTA`
- `CUSTOM` (extension point)

## 3.1) Step names (core workflow)

These appear in `STEP_STARTED` / `STEP_FINISHED`:

- `vibe.pivot_detection`
- `vibe.materials`
- `vibe.ra_plan`
- `vibe.planner`
- `vibe.reasoner`
- `vibe.librarian`
- `vibe.verifier`
- `vibe.dag_builder`
- `vibe.dag_consensus`
- `vibe.delivery`
- `vibe.summary`

Loop / orchestration helpers:

- `vibe.loop.round_{i}` (goal loop)
- `vibe.milestone_{index}` (milestone loop)
- `vibe.mesh.{role}` (mesh-driven nodes)

## 4) Cognitive maker consensus progress (DAG consensus)

When `Vibe:DagConsensus:Mode = maker`, DAG consensus runs the Cognitive
workflow `maker.yaml`. Its progress is projected into AG-UI:

### 4.1 Step lifecycle

`STEP_STARTED` / `STEP_FINISHED` are emitted for each consensus step.
`stepName` uses the prefix **`dag_consensus:`**.

Examples:
- `dag_consensus:check_atomic`
- `dag_consensus:decompose`
- `dag_consensus:execute_subtasks`
- `dag_consensus:compose`
- `dag_consensus:LOADING`
- `dag_consensus:EXECUTING`
- `dag_consensus:COMPLETE`

### 4.2 Custom event payload

For each progress tick, a `CUSTOM` event is emitted:

```
{
  "type": "CUSTOM",
  "timestamp": 1730000000000,
  "name": "aevatar.workflow.execution_event",
  "value": {
    "phase": "vote",
    "nodeId": "dag_consensus:decompose",
    "message": "Round 1: 2/3 votes",
    "status": "running",
    "timestamp": 1730000000000,
    "fields": {
      "status": "running",
      "progress": 0.3,
      "execution_id": "run_123",
      "workflow_name": "maker",
      "step_type": "vote",
      "depth": 0,
      "vote_round": 1,
      "vote_max_rounds": 10,
      "vote_k": 3,
      "vote_current_votes": 2,
      "tokens_used": 1200,
      "llm_calls": 5
    }
  }
}
```

Key fields inside `value.fields`:

- `status`: `pending` | `running` | `completed` | `failed` | `cancelled`
- `progress`: `0..1`
- `execution_id`: run id (same as request run id)
- `workflow_name`: `"maker"`
- `step_type`: `"llm_call"` | `"vote"` | `"fan_out"` | `"workflow_call"` | ...
- `depth`, `vote_*`, `parallel_*`
- `tokens_used`, `llm_calls`, `prompt_tokens`, `completion_tokens`

### 4.3 Frontend recommendations

- Filter by `name == "aevatar.workflow.execution_event"`.
- Use `nodeId` prefix `dag_consensus:` to scope to DAG consensus only.
- Use `value.fields.execution_id` to group by run.
- Use `STEP_STARTED/STEP_FINISHED` for timeline, and `CUSTOM` for detail
  (vote counts, progress, token stats).

### 4.4 Proposal -> consensus UI mapping (recommended)

Use `aevatar.workflow.execution_event` to build a **proposal panel** and a
**consensus progress panel**:

- **Proposal cards**: `nodeId` matches `dag_consensus:{stepId}.gen[n]` and
  `step_type == "llm_call"`. Use `fields.assistant_response` for streaming
  content, and `status` to mark `running/completed/failed`.
- **Vote progress**: `nodeId` matches `dag_consensus:{stepId}` with
  `step_type == "vote"`. Use `vote_round`, `vote_k`, `vote_current_votes`,
  `vote_max_rounds`, `progress`.
- **Parent linkage**: derive parent by stripping `.gen[n]`, or use
  `fields.parent_step_id` when present.
- **Consensus reached**: when `vote_current_votes >= vote_k`, or when the
  vote step receives `STEP_FINISHED` / `status=completed`.
- **Explicit winner**: read `winner_proposal_id` / `winner_hash` from
  `value.fields` on the vote step completion event. The winner content is
  the vote step's `assistant_response`.
- **Batch size**: `parallel_total` indicates how many proposals are created
  in the current batch; you can render placeholders or a mini progress bar.

Note: `STEP_STARTED/STEP_FINISHED` are de-duplicated for streaming events, so
use `CUSTOM` events to update proposal text in real time.

## 5) Pivot AG-UI events

Pivot events are emitted as **AG-UI typed events** (not CUSTOM):

- `PIVOT_DETECTED`
- `PIVOT_STARTED`
- `PIVOT_PROGRESS`
- `PIVOT_COMPLETED`
- `PIVOT_ERROR`
- `PIVOT_CLARIFICATION_REQUEST`

These share a stable `pivotId` and `sessionId`.

Example (PIVOT_STARTED):

```
{
  "type": "PIVOT_STARTED",
  "timestamp": 1730000000000,
  "sessionId": "s_123",
  "pivotId": "pivot_1730000000000",
  "oldDirection": "topic A",
  "newDirection": "topic B"
}
```

Progress stages (`PIVOT_PROGRESS.stage`):

- `CancellingPlans`
- `PreservingKnowledge`
- `NotifyingAgents`
- `UpdatingDag`

## 6) Vibe-specific CUSTOM events (non-exhaustive)

These are emitted for UI-specific cards and snapshots:

- `aevatar.vibe.message_meta`
- `aevatar.vibe.message_meta_snapshot`
- `aevatar.ui.tools_snapshot`
- `aevatar.ui.run_steps_snapshot`
- `aevatar.vibe.brief_snapshot`
- `aevatar.vibe.brief_updated`
- `aevatar.vibe.delivery_snapshot`
- `aevatar.vibe.dag_snapshot`
- `aevatar.vibe.trace_snapshot`
- `aevatar.vibe.agents_snapshot`
- `aevatar.vibe.agent_providers_snapshot`
- `aevatar.vibe.plan_dag_written` (legacy, currently disabled)
- `aevatar.vibe.milestones_plan_dag_written`
- `aevatar.vibe.dag_updated`
- `aevatar.vibe.mesh_started`
- `aevatar.vibe.mesh_node_started`
- `aevatar.vibe.mesh_node_finished`
- `aevatar.vibe.mesh_missing`
- `aevatar.vibe.mesh_saved`
- `aevatar.vibe.milestone_started`
- `aevatar.vibe.milestone_error`
- `aevatar.vibe.milestone_finished`
- `aevatar.vibe.round_summary`
- `aevatar.vibe.agent_status_report`

Scientific / system signals:

- `aevatar.scientific.session`
- `aevatar.scientific.run_canceled`
- `aevatar.scientific.run_interrupted`
- `aevatar.scientific.mcp_reconnect_started`
- `aevatar.scientific.mcp_reconnect_finished`
- `aevatar.scientific.mcp_reconnect_error`
- `aevatar.scientific.tools_snapshot`
- `aevatar.scientific.fact_proposed`
