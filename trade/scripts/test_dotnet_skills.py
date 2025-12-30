#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Trade - DotNet File Skills Smoke Test
====================================
目标：
- 自动巡检 `Aevatar.Trade/Tools/DotNetSkills` 下的 dotnet-file skills
- 默认只测试安全接口（requiresConfirmation/isDangerous=false），避免误下单/误改配置
- 通过 Trade.Api 已暴露的 `/api/ai-wars/{toolName}` 端点执行（推荐：复用服务端的 env）

使用示例：
  python3 trade/scripts/test_dotnet_skills.py --api-base https://localhost:7100
  python3 trade/scripts/test_dotnet_skills.py --api-base https://localhost:7100 --include-dangerous

说明：
- 本脚本依赖系统自带 `curl`，并默认 `-k` 跳过本地 https 自签证书校验。
- 需要服务端已启动（Aevatar.Trade.Api），并已加载 WEEX_* 环境变量。
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple


ROOT = Path(__file__).resolve().parents[1]  # trade/
SKILLS_DIR = ROOT / "Aevatar.Trade" / "Tools" / "DotNetSkills"


@dataclass
class ToolManifest:
    name: str
    requires_confirmation: bool
    is_dangerous: bool
    required_params: List[str]
    param_names: List[str]
    file_path: Path
    route: Optional[str] = None


def _extract_manifest_json(text: str) -> Optional[str]:
    """
    Extract JSON between:
      /*aevatar_tool
      { ... }
      */
    """
    m = re.search(r"/\*aevatar_tool\s*(\{[\s\S]*?\})\s*\*/", text, re.MULTILINE)
    if not m:
        return None
    return m.group(1)


def load_manifests() -> List[ToolManifest]:
    manifests: List[ToolManifest] = []
    for cs in sorted(SKILLS_DIR.rglob("*.cs")):
        try:
            txt = cs.read_text(encoding="utf-8")
        except Exception:
            continue

        raw = _extract_manifest_json(txt)
        if not raw:
            continue

        try:
            obj = json.loads(raw)
        except Exception:
            # Manifest JSON invalid -> skip (shouldn't happen)
            continue

        name = str(obj.get("name", "")).strip()
        if not name:
            continue

        requires_confirmation = bool(obj.get("requiresConfirmation", False))
        is_dangerous = bool(obj.get("isDangerous", False))

        required_params: List[str] = []
        params = obj.get("parameters", {}) or {}
        req = params.get("required", []) or []
        if isinstance(req, list):
            required_params = [str(x) for x in req if str(x).strip()]

        param_names: List[str] = []
        items = params.get("items", {}) or {}
        if isinstance(items, dict):
            param_names = [str(k) for k in items.keys() if str(k).strip()]

        manifests.append(
            ToolManifest(
                name=name,
                requires_confirmation=requires_confirmation,
                is_dangerous=is_dangerous,
                required_params=required_params,
                param_names=param_names,
                file_path=cs,
            )
        )
    return manifests


DEFAULT_PARAM_VALUES: Dict[str, Any] = {
    # Common
    "symbol": "cmt_btcusdt",
    "coin": "USDT",
    # Market
    "granularity": "15m",
    "limit": 100,
    "priceType": "LAST",
    # Common paging
    "pageNo": 1,
    "pageSize": 20,
    # Some endpoints use different naming
    "startTime": None,
    "endTime": None,
}


def build_body(required_params: List[str], param_names: List[str], symbol: str) -> Tuple[Optional[Dict[str, Any]], Optional[str]]:
    """
    Build a minimal request body that satisfies `required_params`.
    Returns (body_dict_or_None, reason_if_skipped).
    """
    body: Dict[str, Any] = {}
    for p in required_params:
        key = p.strip()
        if not key:
            continue

        if key == "symbol":
            body["symbol"] = symbol
            continue

        if key in DEFAULT_PARAM_VALUES:
            v = DEFAULT_PARAM_VALUES[key]
            # Some APIs treat null as "omit"; but for required fields, null is bad.
            if v is None:
                return None, f"missing default for required param: {key}"
            body[key] = v
            continue

        return None, f"missing default for required param: {key}"

    # Best-effort: if tool supports a symbol param (even optional), set it to reduce payload size.
    if "symbol" not in body and any(k.lower() == "symbol" for k in param_names):
        body["symbol"] = symbol

    # Best-effort: if tool supports coin (even optional), set to USDT.
    if "coin" not in body and any(k.lower() == "coin" for k in param_names):
        body["coin"] = DEFAULT_PARAM_VALUES.get("coin", "USDT")

    return body, None


def curl_post_json(url: str, body: Dict[str, Any], timeout_s: int) -> Tuple[int, str, str]:
    """
    Use curl so we can easily ignore local https dev cert.
    Returns (exit_code, stdout, stderr).
    """
    payload = json.dumps(body, ensure_ascii=False)
    cmd = [
        "curl",
        "-sS",
        "-k",
        "--max-time",
        str(timeout_s),
        "-H",
        "Content-Type: application/json",
        "-X",
        "POST",
        url,
        "--data-binary",
        payload,
    ]
    p = subprocess.run(cmd, capture_output=True, text=True)
    return p.returncode, p.stdout, p.stderr


def interpret_result(tool_name: str, http_body: str) -> Tuple[str, str]:
    """
    Trade.Api /api/ai-wars/{toolName} returns JSON like:
      { success, exitCode, toolName, file, data, stderr }
    """
    try:
        obj = json.loads(http_body)
    except Exception:
        return "FAIL", "response is not valid JSON"

    # Top-level success preferred
    if isinstance(obj, dict):
        # Safety guard response (confirm required)
        if "hint" in obj and "error" in obj and "tool" in obj:
            hint = str(obj.get("hint") or "")
            if "confirm=true" in hint:
                return "SKIP", "requires ?confirm=true"

        success = obj.get("success")
        if success is True:
            return "PASS", "ok"
        if success is False:
            # try to surface nested http status if present
            data = obj.get("data")
            if isinstance(data, dict):
                hs = data.get("httpStatus")
                raw = data.get("raw")
                if hs is not None:
                    return "FAIL", f"tool failed (httpStatus={hs})"
                if raw:
                    return "FAIL", "tool failed (see raw)"
            return "FAIL", "tool failed"

        # Some versions may omit top-level success; fallback to exitCode==0
        exit_code = obj.get("exitCode")
        if exit_code == 0:
            return "PASS", "ok(exitCode=0)"
        return "FAIL", f"unknown result shape for {tool_name}"

    return "FAIL", "unexpected JSON shape"


def curl_get(url: str, timeout_s: int) -> Tuple[int, str, str]:
    cmd = ["curl", "-sS", "-k", "--max-time", str(timeout_s), "-X", "GET", url]
    p = subprocess.run(cmd, capture_output=True, text=True)
    return p.returncode, p.stdout, p.stderr


def _get_any(obj: Dict[str, Any], keys: List[str], default: Any = None) -> Any:
    for k in keys:
        if k in obj:
            return obj[k]
    # case-insensitive fallback
    lower = {str(k).lower(): k for k in obj.keys()}
    for k in keys:
        kk = lower.get(str(k).lower())
        if kk is not None:
            return obj[kk]
    return default


def try_load_tools_from_api(api_base: str, timeout_s: int) -> Optional[List[ToolManifest]]:
    """
    Prefer the running service's tool index so we test the *actual* manifests the server is using:
      GET /api/ai-wars  -> { count, tools: [ { name, requiresConfirmation, isDangerous, parameters, route, file } ] }
    """
    url = api_base.rstrip("/") + "/api/ai-wars"
    rc, out, err = curl_get(url, timeout_s=timeout_s)
    if rc != 0:
        return None

    try:
        payload = json.loads(out)
    except Exception:
        return None

    if not isinstance(payload, dict):
        return None

    tools = _get_any(payload, ["tools"], default=None)
    if not isinstance(tools, list):
        return None

    manifests: List[ToolManifest] = []
    for t in tools:
        if not isinstance(t, dict):
            continue

        name = str(_get_any(t, ["name", "Name"], "")).strip()
        if not name:
            continue

        requires_confirmation = bool(_get_any(t, ["requiresConfirmation", "RequiresConfirmation"], False))
        is_dangerous = bool(_get_any(t, ["isDangerous", "IsDangerous"], False))
        route = str(_get_any(t, ["route", "Route"], "")).strip() or None

        params = _get_any(t, ["parameters", "Parameters"], default={}) or {}
        required_params: List[str] = []
        param_names: List[str] = []
        if isinstance(params, dict):
            req = _get_any(params, ["required", "Required"], default=[]) or []
            if isinstance(req, list):
                required_params = [str(x) for x in req if str(x).strip()]

            items = _get_any(params, ["items", "Items"], default={}) or {}
            if isinstance(items, dict):
                param_names = [str(k) for k in items.keys() if str(k).strip()]

        # File path from API is display-only; keep a placeholder Path.
        file_disp = str(_get_any(t, ["file", "File"], "")).strip()
        file_path = Path(file_disp) if file_disp else Path("<api>")

        manifests.append(
            ToolManifest(
                name=name,
                requires_confirmation=requires_confirmation,
                is_dangerous=is_dangerous,
                required_params=required_params,
                param_names=param_names,
                file_path=file_path,
                route=route,
            )
        )

    return manifests if manifests else None


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--api-base", default=os.environ.get("TRADE_API_BASE", "https://localhost:7100"),
                    help="Trade.Api base URL (default: https://localhost:7100 or env TRADE_API_BASE)")
    ap.add_argument("--symbol", default=os.environ.get("TRADE_SYMBOL", "cmt_btcusdt"),
                    help="Default symbol for required symbol params (default: cmt_btcusdt)")
    ap.add_argument("--timeout", type=int, default=30, help="Per-request timeout seconds (default: 30)")
    ap.add_argument("--include-dangerous", action="store_true",
                    help="Also run skills marked requiresConfirmation/isDangerous (⚠️ may modify account / place orders)")
    ap.add_argument("--confirm-dangerous", action="store_true",
                    help="Actually execute dangerous endpoints with ?confirm=true (⚠️ will perform real actions)")
    ap.add_argument("--sleep-ms", type=int, default=150, help="Sleep between calls to avoid rate limits (default: 150ms)")
    ap.add_argument("--json-out", default=str(ROOT / "skill-test-report.json"),
                    help="Write JSON report to this path (default: trade/skill-test-report.json)")
    ap.add_argument("--discovery", choices=["auto", "api", "local"], default="auto",
                    help="How to discover tool manifests (default: auto = try API then local scan)")
    args = ap.parse_args()

    api_base = args.api_base.rstrip("/")
    symbol = args.symbol
    timeout_s = max(5, args.timeout)

    manifests: Optional[List[ToolManifest]] = None
    if args.discovery in ("auto", "api"):
        manifests = try_load_tools_from_api(api_base, timeout_s=timeout_s)
    if manifests is None and args.discovery in ("auto", "local"):
        manifests = load_manifests()
    if not manifests:
        print("No dotnet skills found.", file=sys.stderr)
        return 2

    total = 0
    passed = 0
    failed = 0
    skipped = 0

    results: List[Dict[str, Any]] = []

    for m in manifests:
        # Only AI Wars endpoints are exposed via /api/ai-wars/{toolName}.
        # Non-ai-wars skills (e.g. weex_get_balances) are left as SKIP by default.
        if not m.name.startswith("weex_ai_"):
            results.append(
                {
                    "tool": m.name,
                    "status": "SKIP",
                    "reason": "not exposed via /api/ai-wars (non-weex_ai_ tool)",
                    "file": str(m.file_path),
                }
            )
            skipped += 1
            continue

        is_danger = m.requires_confirmation or m.is_dangerous
        if is_danger and not args.include_dangerous:
            results.append(
                {
                    "tool": m.name,
                    "status": "SKIP",
                    "reason": "dangerous/requiresConfirmation (use --include-dangerous to run)",
                    "file": str(m.file_path),
                }
            )
            skipped += 1
            continue

        if is_danger and args.include_dangerous and not args.confirm_dangerous:
            results.append(
                {
                    "tool": m.name,
                    "status": "SKIP",
                    "reason": "dangerous/requiresConfirmation (use --confirm-dangerous to execute)",
                    "file": str(m.file_path),
                }
            )
            skipped += 1
            continue

        body, reason = build_body(m.required_params, m.param_names, symbol)
        if reason is not None:
            results.append(
                {
                    "tool": m.name,
                    "status": "SKIP",
                    "reason": reason,
                    "file": str(m.file_path),
                }
            )
            skipped += 1
            continue

        total += 1
        route = m.route or f"/api/ai-wars/{m.name}"
        confirm_flag = "true" if (is_danger and args.confirm_dangerous) else "false"
        url = f"{api_base}{route}?confirm={confirm_flag}"
        rc, out, err = curl_post_json(url, body or {}, timeout_s=timeout_s)

        if rc != 0:
            failed += 1
            results.append(
                {
                    "tool": m.name,
                    "status": "FAIL",
                    "reason": f"curl_failed(rc={rc})",
                    "curl_stderr": err.strip(),
                    "file": str(m.file_path),
                    "url": url,
                }
            )
        else:
            status, why = interpret_result(m.name, out)
            if status == "PASS":
                passed += 1
                results.append(
                    {
                        "tool": m.name,
                        "status": "PASS",
                        "detail": why,
                        "file": str(m.file_path),
                        "url": url,
                    }
                )
            elif status == "SKIP":
                skipped += 1
                results.append(
                    {
                        "tool": m.name,
                        "status": "SKIP",
                        "reason": why,
                        "file": str(m.file_path),
                        "url": url,
                    }
                )
            else:
                failed += 1
                results.append(
                    {
                        "tool": m.name,
                        "status": "FAIL",
                        "reason": why,
                        "file": str(m.file_path),
                        "url": url,
                        "response_preview": out[:2000],
                    }
                )

        time.sleep(max(0, args.sleep_ms) / 1000.0)

    report = {
        "apiBase": api_base,
        "symbol": symbol,
        "tested": total,
        "passed": passed,
        "failed": failed,
        "skipped": skipped,
        "results": results,
    }

    out_path = Path(args.json_out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"Tested={total} Passed={passed} Failed={failed} Skipped={skipped}")
    print(f"Report: {out_path}")

    return 0 if failed == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())


