---
name: python-calc
description: “Evaluate arithmetic expressions precisely by calling the py_calc tool. Use when a user needs exact calculation results, especially for fractions, large numbers, or high-precision decimals like 1/7, 2**128, or (1+2)*3.”
allowed-tools: py_calc
---

# Python Calc

Perform precise arithmetic evaluation using the `py_calc` tool — never estimate or guess numerical results.

## When to use

- User asks for a precise calculation, especially fractions or long decimals (e.g., `1/7`, `(1+2)*3`, `2**128`).
- Any arithmetic where mental math or estimation is insufficient — results must be verifiable.
- User explicitly requests a specific number of decimal places.

## Workflow

1. **Parse the expression** from the user's request.
2. **Call `py_calc`** with:
   - `expr` — the user's arithmetic expression (passed as-is).
   - `precision` — number of decimal places (default: 50; if the user asks for N digits, use at least N + 5).
3. **Return the result**, noting the precision used if relevant.

## Example

**User**: “What is 1/7 to 20 decimal places?”

**Steps**:
1. Call `py_calc` with `expr: “1/7”`, `precision: 25`
2. Result: `0.1428571428571428571428571`
3. Respond: “1/7 = 0.14285714285714285714 (20 decimal places)”

**User**: “Calculate 2**128”

**Steps**:
1. Call `py_calc` with `expr: “2**128”`, `precision: 50`
2. Result: `340282366920938463463374607431768211456`
3. Respond with the exact integer result.

## Constraints

- `py_calc` only allows a safe AST subset: numbers, parentheses, and operators `+`, `-`, `*`, `/`, `**`.
- Variables and function calls are not supported — only literal arithmetic expressions.
