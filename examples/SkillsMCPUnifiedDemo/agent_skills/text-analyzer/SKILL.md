---
name: text-analyzer
description: Compute text stats via text_stats tool.
allowed-tools:
  - text_stats
---

## When to use
- User asks for quick stats: length, words, lines.

## Procedure
1) Call `text_stats` with the user's text.
2) Report `chars/words/lines` succinctly.


