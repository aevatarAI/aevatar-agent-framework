---
name: rewrite-branch-merge
description: "Manage rewrite branches for narrative projects — decide when to branch, compare branches using narrative tests and payoff ledgers, and merge back with minimal chaos. Use when a change is risky (canon, timeline, or payoff impact) and might break the mainline, or when the author wants to explore multiple story routes in parallel."
---

# Rewrite Branch Merge

Manage parallel rewrite branches for narrative projects, providing structured comparison and merge strategies to minimize story chaos.

## When to use

- A change is risky (canon/timeline/payoff) and might break the mainline.
- Author wants to explore multiple story routes in parallel.
- A canon governance decision resulted in a "branch and rewrite later" policy.
- Multiple competing fixes exist for a narrative test failure.

## Workflow

1. **Decide whether to branch** using these strict rules — branch if:
   - Canon changes would require editing many existing chapters.
   - Timeline constraints are violated and the fix is non-local.
   - Payoff plan changes the story’s core promise.
2. **Create the branch** with a clear label (e.g., `rewrite/magic-verbal-components`) and document the deviation intent.
3. **Compare branches** using this checklist:
   - Narrative Tests: pass/fail delta between branches.
   - Setup/Payoff ledger: debt ratio, broken setups count.
   - Reader personas: confusion score and payoff strength delta.
   - Author intent: does this branch align better with the deviation intent?
4. **Choose a merge strategy**:
   - **Pick winner** — choose one branch entirely (fastest, cleanest).
   - **Selective merge** — pick best chapters/sections from each (requires strong diff discipline).
   - **Dual timeline** — keep both as alternate routes (rare; only if the author explicitly wants it).
5. **Generate output files**:
   - `branch_compare.md` — diff summary with test/persona/ledger comparison table.
   - `merge_plan.md` — chosen strategy and concrete step-by-step merge actions.

## Example

**Input**: Two branches exist — `main` (gesture-based magic) and `rewrite/verbal-magic`.

**Comparison output** (`branch_compare.md`):
```markdown
| Metric               | main (gesture) | rewrite (verbal) | Delta   |
|----------------------|----------------|-------------------|---------|
| Narrative tests pass | 14/16          | 15/16             | +1      |
| Broken setups        | 0              | 1 (stealth arc)   | -1      |
| Reader confusion     | Low            | Medium (ch. 14)   | Worse   |
| Author intent match  | Moderate       | High              | Better  |

Recommendation: Selective merge — adopt verbal magic but preserve stealth arc from main.
```

## Output format

- `branch_compare.md` — structured comparison table with quantitative deltas
- `merge_plan.md` — chosen strategy, concrete steps, and a checklist of chapters to update
