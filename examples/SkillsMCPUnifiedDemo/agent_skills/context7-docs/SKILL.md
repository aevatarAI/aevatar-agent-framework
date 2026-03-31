---
name: context7-docs
description: "Look up up-to-date library documentation via Context7 MCP using resolve-library-id and get-library-docs tools. Use when a user asks how to use a library, framework, or API and you need current official docs or code snippets."
allowed-tools: resolve-library-id, get-library-docs
---

# Context7 Docs

Fetch up-to-date library documentation and code snippets via the Context7 MCP server.

## When to use

- User asks "how to use X" and you need current official docs or snippets.
- User references a specific library API and you want to verify the latest signature.
- User is debugging an issue that may stem from outdated API usage.
- Any situation where your training data might be stale for a specific library version.

## Workflow

1. **Check tool availability**: If `resolve-library-id` and `get-library-docs` are not available, tell the user to configure the Context7 MCP server and stop.
2. **Resolve the library**: Call `resolve-library-id` with `libraryName` extracted from the user's query (e.g., "react", "express", "lodash").
3. **Select the best match** from the returned results based on the user's context.
4. **Fetch documentation**: Call `get-library-docs` with:
   - `context7CompatibleLibraryID` — from the resolved match.
   - `mode` — use `code` for API references and examples, `info` for conceptual overviews.
   - `topic` (optional) — narrow results to a specific topic if the user's question is focused.
5. **Respond** with the answer, quoting key snippets from the docs.

## Example

**User**: "How do I use the `useEffect` hook in React?"

**Steps**:
1. Call `resolve-library-id` with `libraryName: "react"` → returns `{ id: "react/react", ... }`
2. Call `get-library-docs` with `context7CompatibleLibraryID: "react/react"`, `mode: "code"`, `topic: "useEffect"`
3. Respond with the relevant documentation and code example from the result.

## Output format

- Quote relevant code snippets directly from the fetched docs.
- Cite the library version when available.
- If no results match, suggest alternative search terms or manual doc links.
