# Aevatar.Agents.Cognitive Docs

This folder contains documentation for `Aevatar.Agents.Cognitive`.

- **README.md**: This index
- **Tests.md**: Test coverage notes (Chinese)

Add any design notes, usage guides, troubleshooting, and integration docs here.

## Workflows

- `workflows/trade-cycle.yaml`: 交易场景专用（先写结构化报告，再输出决策 JSON；不做 tool calling）。
- `workflows/trade-risk.yaml`: 交易场景风控专用（输出 RiskManager 兼容的 JSON；不做 tool calling）。
