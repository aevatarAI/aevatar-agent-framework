---
name: python-calc
description: Evaluate arithmetic expressions precisely by calling py_calc (python-file tool).
allowed-tools:
  - py_calc
---

## When to use
- 用户要求精确计算（尤其是分数/长小数），例如 `1/7`、`(1+2)*3`、`2**128`。
- 不允许“心算/估算/臆测”，必须可执行验证。

## Procedure
1) Call `py_calc` with:
   - `expr`: 用户的表达式（原样）
   - `precision`: 需要的小数精度（默认 50；如果用户要求 N 位小数，建议 precision >= N + 5）
2) 用工具结果回答，必要时说明 precision。

## Notes
- `py_calc` 只允许安全 AST 子集（数字、括号、+ - * / **），不支持变量/函数调用。


