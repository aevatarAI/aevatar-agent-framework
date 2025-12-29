---
name: deviation-impact-analysis
description: Turn "AI draft -> author edits" diffs into structured deviation items, an author-intent prompt, and a concrete propagation plan.
---

## When to use
- After author edits a chapter draft (especially via external editor like Cursor)
- Before auto-continue to future chapters

## Outputs
- `deviation_report.md` (human readable summary)
- `deviation_prompt.md` (one-click prompt/instructions)
- `change_impact_report.md` (what future outline/chapters/canon/ledger will be affected)
- `backup_options_report.md` (conservative vs aggressive vs branch)

## Structured deviation categories (must classify)
- plot / character / tone / info density / canon setting / timeline / style / pacing

## Procedure
1) Extract diff signals and cluster them into 5-20 deviation items (avoid noise).
2) Summarize author intent in a short instruction that can drive future rewriting.
3) Scan future outline/chapters/canon/ledger:
   - list concrete touch points (file/section) that need change
4) Produce 2-3 backup routes and recommend one.


