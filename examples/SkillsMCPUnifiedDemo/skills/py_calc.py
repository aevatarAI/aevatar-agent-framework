#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""aevatar_tool
{
  "name": "py_calc",
  "description": "Evaluate a simple arithmetic expression using Python Decimal (safe AST subset).",
  "category": "Utility",
  "version": "1.0.0",
  "tags": ["python", "file", "tool", "math", "decimal"],
  "parameters": {
    "required": ["expr"],
    "items": {
      "expr": { "type": "string", "description": "Arithmetic expression, e.g. '1/7' or '(1+2)*3'. Allowed: numbers, (), + - * / **." },
      "precision": { "type": "integer", "description": "Decimal precision (default: 50). Range: 1..500." }
    }
  }
}
"""

from __future__ import annotations

# ============================================================
#  Python file tool: py_calc
# ============================================================
#
# 目标：
# - 演示 Aevatar 的 python-file tool（由 C# 侧 PythonFileSkillTool 启动）
# - 用 Python 标准库对简单算术表达式做“可执行验证”（避免 LLM 心算/胡算）
#
# 输入：STDIN JSON（与 ToolParameters 对齐）
# 输出：STDOUT JSON（建议只打印一段 JSON，方便宿主解析）
#
# 安全边界（非常重要）：
# - 这里只允许 AST 安全子集：数字、括号、+ - * / **、一元正负号
# - 禁止 Name/Call/Attribute/Subscript 等一切可执行/可访问环境的节点
#
# ============================================================

import ast
import json
import sys
from dataclasses import dataclass
from decimal import Decimal, getcontext
from typing import Any, Dict, Union


# ============================================================
#  AST safe evaluator (Decimal)
# ============================================================


AllowedNumber = Union[int, float, Decimal]


class UnsafeExpressionError(ValueError):
    pass


@dataclass(frozen=True)
class EvalResult:
    value: Decimal
    precision: int


def _as_decimal(x: Any) -> Decimal:
    if isinstance(x, Decimal):
        return x
    if isinstance(x, bool):
        raise UnsafeExpressionError("bool is not allowed")
    if isinstance(x, int):
        return Decimal(x)
    if isinstance(x, float):
        # Avoid float surprises: keep it but convert via repr to preserve user intent best-effort.
        return Decimal(repr(x))
    if isinstance(x, str):
        return Decimal(x)
    raise UnsafeExpressionError(f"unsupported literal type: {type(x).__name__}")


def _eval_node(node: ast.AST) -> Decimal:
    # Numbers
    if isinstance(node, ast.Constant):
        if isinstance(node.value, (int, float, str)):
            return _as_decimal(node.value)
        raise UnsafeExpressionError("only int/float/decimal-string constants are allowed")

    # Unary ops: +x, -x
    if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub)):
        v = _eval_node(node.operand)
        return v if isinstance(node.op, ast.UAdd) else -v

    # Binary ops
    if isinstance(node, ast.BinOp):
        left = _eval_node(node.left)
        right = _eval_node(node.right)

        if isinstance(node.op, ast.Add):
            return left + right
        if isinstance(node.op, ast.Sub):
            return left - right
        if isinstance(node.op, ast.Mult):
            return left * right
        if isinstance(node.op, ast.Div):
            return left / right
        if isinstance(node.op, ast.Pow):
            # Keep it conservative: allow integer exponent only, and limit range.
            # Decimal ** int is deterministic and safe.
            if right != right.to_integral_value():
                raise UnsafeExpressionError("power exponent must be integer")
            exp = int(right)
            if abs(exp) > 10_000:
                raise UnsafeExpressionError("power exponent too large")
            return left ** exp

        raise UnsafeExpressionError(f"operator not allowed: {type(node.op).__name__}")

    raise UnsafeExpressionError(f"expression node not allowed: {type(node).__name__}")


def eval_expr(expr: str, precision: int) -> EvalResult:
    expr = (expr or "").strip()
    if not expr:
        raise ValueError("expr is required")

    if precision < 1 or precision > 500:
        raise ValueError("precision must be in [1, 500]")

    getcontext().prec = precision

    tree = ast.parse(expr, mode="eval")
    if not isinstance(tree, ast.Expression):
        raise UnsafeExpressionError("invalid expression")

    # Reject any unexpected nodes early (defense in depth)
    for n in ast.walk(tree):
        if isinstance(n, (ast.Expression, ast.BinOp, ast.UnaryOp, ast.Constant)):
            continue
        if isinstance(n, (ast.Add, ast.Sub, ast.Mult, ast.Div, ast.Pow, ast.UAdd, ast.USub)):
            continue
        raise UnsafeExpressionError(f"node not allowed: {type(n).__name__}")

    v = _eval_node(tree.body)
    return EvalResult(value=v, precision=precision)


# ============================================================
#  IO helpers
# ============================================================


def _read_stdin_json() -> Dict[str, Any]:
    raw = sys.stdin.read()
    raw = raw.strip()
    if not raw:
        return {}
    try:
        obj = json.loads(raw)
    except Exception as e:
        raise ValueError(f"invalid JSON input: {e}") from e

    if not isinstance(obj, dict):
        raise ValueError("input JSON must be an object")
    return obj


def _write_json(obj: Any) -> None:
    sys.stdout.write(json.dumps(obj, ensure_ascii=False))
    sys.stdout.write("\n")


def main() -> int:
    try:
        args = _read_stdin_json()
        expr = str(args.get("expr") or "").strip()
        precision = int(args.get("precision") or 50)

        r = eval_expr(expr, precision=precision)
        _write_json(
            {
                "ok": True,
                "expr": expr,
                "precision": r.precision,
                "value": format(r.value, "f"),
            }
        )
        return 0
    except Exception as e:
        _write_json({"ok": False, "error": str(e)})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())


