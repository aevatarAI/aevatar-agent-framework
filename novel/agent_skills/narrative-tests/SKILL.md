---
name: narrative-tests
description: Help the author write narrative unit tests (rules/constraints) and interpret failures with a concrete fix roadmap.
---

## When to use
- Author wants stronger control: “改一处就知道后面哪里拽歪”
- After any major deviation or canon change

## Test writing principles
- Tests are **author intent made explicit**, not bureaucracy.
- Prefer rules that are:
  - specific (who/what/when)
  - checkable (can locate failure in concrete text)
  - stable (won’t change every chapter)

## Recommended categories
- **Canon rules**: power system, forbidden knowledge, hard constraints
- **Timeline rules**: ordering, causality, “X must happen before Y”
- **Character rules**: motivation consistency, relationship milestones
- **Information revelation**: “before chapter N, reader must not know X”

## Output files
- `narrative_tests.md`: test suite definition (author editable)
- `narrative_test_report.md`: run result (failures + location + fix options)

## v1 Test Format (deterministic)

当前实现（v1）支持在 `narrative_tests.md` 中嵌入一个 ` ```json ` 代码块：

```json
{
  "suiteId": "default",
  "cases": [
    {
      "id": "no-spoiler-before-1",
      "severity": "ERROR",
      "type": "must_not_contain_before_chapter",
      "untilChapter": 1,
      "pattern": "SPOILER",
      "message": "Chapter 1 must not contain SPOILER."
    }
  ]
}
```

支持的 `type`（v1）：
- `must_contain`
- `must_not_contain`
- `must_not_contain_before_chapter`

## Failure handling (must produce options)
For each failure, provide:
1) **where** it breaks (chapter/beat/excerpt)
2) **why** it breaks (rule violated, drift introduced by what change)
3) **routes** (choose 1):
   - minimal patch (small local edit)
   - propagate change (update outline/canon + future chapters)
   - branch rewrite (keep mainline stable)


