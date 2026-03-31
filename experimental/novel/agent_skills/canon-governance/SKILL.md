---
name: canon-governance
description: "Govern canon changes by requiring explicit reason, impact analysis, and retroactive policy, then produce an actionable propagation plan. Use when an author wants to modify canon elements (rules, power systems, key facts, naming, world constraints) or when a deviation implies canon drift that needs a decision: update canon or rewrite text back to canon."
---

# Canon Governance

Manage and govern changes to canonical story elements — world rules, power systems, key facts, naming conventions, and world constraints — ensuring consistency across all narrative artifacts.

## When to use

- Author wants to change canon (rules, power system, key facts, naming, world constraints).
- A deviation implies a canon drift and you must decide: update canon, or rewrite text back to canon.
- A narrative test fails due to a contradiction with established canon.
- Multiple story branches have diverged on a canonical fact.

## Workflow

1. **Classify the change**: Determine whether this is a true canon change or a local rewrite that doesn't affect canon.
2. **Collect required fields** (all must be explicit before proceeding):
   - **change**: what changes (before → after)
   - **reason**: why this change is worth it (story value)
   - **scope**: which volumes/stories/chapters are affected
   - **retroactive policy**: fix old chapters now / accept inconsistency / create branch and rewrite later
   - **risk**: what could break (timeline, character motivation, existing payoffs)
3. **If canon change**:
   - List all affected artifacts (outline, chapters, ledger, tests).
   - Propose 2–3 propagation plans (minimal fix vs thorough update vs branch-and-rewrite).
   - Estimate effort for each plan.
4. **Generate output files**:
   - `canon_change_request.md` — the proposal with all required fields filled.
   - `canon_change_impact.md` — impact map, fix plan, and options comparison.
5. **Update Narrative Tests** if necessary to prevent future drift.

## Example

**Input**: "Change the magic system so spells require verbal components instead of gestures."

**Output** (`canon_change_request.md`):
```markdown
## Canon Change Request
- Change: Spell activation from gesture-based → verbal components
- Reason: Enables dramatic tension in silence/stealth scenes
- Scope: Volumes 1-3, chapters 2, 7, 14, 22
- Retroactive policy: Fix old chapters now (7 passages)
- Risk: Chapter 14 stealth arc loses its core mechanic; needs rewrite
```

## Output format

- `canon_change_request.md` — structured proposal with all required fields
- `canon_change_impact.md` — impact map listing every affected artifact, a fix plan per propagation option, and a recommended option with rationale
