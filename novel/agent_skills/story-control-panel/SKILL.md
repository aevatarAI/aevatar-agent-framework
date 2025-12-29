---
name: story-control-panel
description: Define what the Story Control Panel shows, how to compute signals, and how the author should act on alerts.
---

## When to use
- Author asks: “我现在这本书/这个 story 的状态怎样？后面哪里危险？哪些伏笔欠债？设定是否失控？”

## Inputs (SSOT)
- Chapter text: `.txt`
- Other artifacts: `.md`（大纲/台账/测试报告/偏离影响分析/会话日志）
- SQLite: index only (rebuildable)

## Output
- A dashboard-ready summary (for UI) with:
  - Progress (Volume/Story/Chapter)
  - Alerts (timeline/canon/test failures)
  - Hotspots (canon rule usage heat, repeated confusion points)
  - Backlog (setup/payoff debts)

## Signals (v1 minimal)
- **Progress**
  - Chapters drafted / reviewed / finalized
- **Hard Alerts** (must block auto-continue)
  - Timeline contradiction
  - Canon rule violation (explicitly tested)
  - Narrative Tests failed (severity=ERROR)
- **Soft Alerts** (should suggest options)
  - Too many unresolved setups (debt ratio)
  - Persona “confusion” score spikes

## Procedure (how to reason)
1) Start from hard alerts → locate where it breaks → propose minimal fix vs branch rewrite.
2) Then handle debt/backlog → propose payoff roadmap.
3) Finally handle optimization signals (persona/style/pacing).


