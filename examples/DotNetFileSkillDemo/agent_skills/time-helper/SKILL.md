---
name: time-helper
description: “Provide accurate current time information by calling the get_time tool. Use when a user asks about the current time, timezone, UTC time, or local time in any language.”
allowed-tools: get_time
---

# Time Helper

Retrieve and present accurate current time information using the `get_time` tool.

## When to use

- User asks about the current time (e.g., “现在几点”, “what time is it”, “current time”).
- User asks about timezones or UTC time.
- User needs to compare local time with UTC.
- Any time-related query where accuracy matters (never estimate or guess).

## Workflow

1. Call the `get_time` tool with empty parameters.
2. Parse the tool result which returns `utc`, `local`, and `timezone` fields.
3. Format the response based on what the user asked:
   - Default: show local time and timezone.
   - If user explicitly asks for UTC: include both UTC and local time.
   - If user asks for a comparison: show both side by side.

## Example

**User**: “现在几点？” (What time is it?)

**Steps**:
1. Call `get_time` → `{ “utc”: “2025-01-15T08:30:00Z”, “local”: “2025-01-15T16:30:00”, “timezone”: “Asia/Shanghai” }`
2. Respond: “现在是 16:30 (Asia/Shanghai 时区)”

**User**: “What's the current UTC time?”

**Steps**:
1. Call `get_time` → same result
2. Respond: “UTC: 08:30 | Local: 16:30 (Asia/Shanghai)”

## Output format

- Always include local time and timezone name.
- If user explicitly asks for UTC, include UTC alongside local time.
- Use 24-hour format for clarity.
