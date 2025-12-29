---
name: canon-governance
description: Govern canon changes: require explicit reason/impact/retroactive policy, and produce an actionable propagation plan.
---

## When to use
- Author wants to change canon (rules, power system, key facts, naming, world constraints).
- A deviation implies a canon drift and you must decide: update canon, or rewrite text back to canon.

## Required fields for a canon change request (must be explicit)
- **change**: what changes (before → after)
- **reason**: why this change is worth it (story value)
- **scope**: which volumes/stories/chapters are affected
- **retroactive policy**:
  - fix old chapters now / accept inconsistency / create branch and rewrite later
- **risk**: what could break (timeline, character motivation, existing payoffs)

## Output
- `canon_change_request.md` (proposal)
- `canon_change_impact.md` (impact map + fix plan + options)

## Procedure
1) Decide whether this is a canon change or a local rewrite.
2) If canon change:
   - list affected artifacts (outline, chapters, ledger, tests)
   - propose 2-3 propagation plans (minimal vs thorough vs branch)
3) Update Narrative Tests if necessary (to prevent future drift).


