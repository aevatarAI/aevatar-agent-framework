---
name: rewrite-branch-merge
description: Manage rewrite branches: when to branch, how to compare, and how to merge back with minimal chaos.
---

## When to use
- A change is risky (canon/timeline/payoff) and might break the mainline.
- Author wants to explore multiple routes in parallel.

## Branching rules (simple, strict)
- Branch if:
  - canon changes would require editing many existing chapters
  - timeline constraints are violated and fix is non-local
  - payoff plan changes the story’s core promise

## Comparison checklist
- Narrative Tests: pass/fail delta
- Setup/Payoff ledger: debt ratio, broken setups
- Reader personas: confusion / payoff strength delta
- Author intent: does this branch align better with the deviation intent?

## Merge strategies
- **Pick winner**: choose one branch entirely (fast)
- **Selective merge**: pick best chapters/sections (requires strong diff discipline)
- **Dual timeline**: keep both as alternate route (rare; only if author wants)

## Output
- `branch_compare.md` (diff summary + test/persona/ledger comparison)
- `merge_plan.md` (chosen strategy + concrete steps)


