# Cognitive Mesh Workspace

> All Cognitive Mesh artifacts now live under this directory. Use it as the single source of truth for docs, DSL libraries, Aspire hosts, and future runtime services.

## Structure

```text
cognitive-mesh/
├── docs/                                  # Product vision, PRD, architecture, DSL spec
├── Aevatar.CognitiveMesh/                 # 通用认知平台 (Web UI + Services)
├── Aevatar.CognitiveMesh.Abstractions/    # 核心抽象层 (IReasoningStrategy, etc.)
├── Aevatar.CognitiveMesh.Dsl/             # DSL 编译器
├── Aevatar.PaperReview/                   # 论文评审平台 (垂直场景应用)
└── README.md
```

## Projects

### Aevatar.CognitiveMesh
通用认知网格平台，支持多种思维策略：
- Direct - 直接 LLM 调用
- MAKER - 多专家共识
- UoT (C/E/T) - Universe of Thought 创意推理
- Cognitive DSL - DSL 驱动的工作流

### Aevatar.PaperReview
基于 MAKER 策略的学术论文评审平台：
- 多评审类型（快速/详细/深度/修改建议）
- 多会议适配（NeurIPS, ACL, CVPR...）
- 实时进度追踪
- 专业评审报告生成

## Quick Start

```bash
# 运行通用平台
cd Aevatar.CognitiveMesh
dotnet run
# → http://localhost:5678

# 运行论文评审
cd Aevatar.PaperReview
dotnet run
# → http://localhost:5001
```

## Next Steps
- Flesh out **CognitiveMesh** with more reasoning strategies
- Polish **PaperReview** UI and review quality
- Eventually merge PaperReview features into CognitiveMesh as a built-in scenario
- Keep **docs/** updated whenever architecture or hosting contracts evolve
