---
name: context7-docs
description: Lookup up-to-date library docs via Context7 MCP (resolve-library-id / get-library-docs).
allowed-tools:
  - resolve-library-id
  - get-library-docs
---

## When to use
- User asks "how to use X" and you need up-to-date official docs/snippets.

## Procedure
1) If tools `resolve-library-id` / `get-library-docs` are not available, tell the user to configure Context7 MCP.
2) Call `resolve-library-id` with `libraryName` (from the user query).
3) Choose the best match and call `get-library-docs` with:
   - `context7CompatibleLibraryID`
   - `mode`: use `code` for APIs/examples, `info` for conceptual overview
   - optional `topic` to narrow down
4) Answer the user and quote key snippets.


