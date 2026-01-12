### novel/ 目录结构（骨架）

目标：让 Novel 产品作为一个独立子模块生长，避免“代码长出来但没人知道它们干嘛”的架构失忆。

```text
novel/
  docs/
    PLAN.md
      - 产品愿景/功能面/多智能体拆分/Cognitive Mesh DSL 工作流/里程碑
    PRD.md
      - 产品需求文档：范围/用户旅程/验收标准/非功能与风险（对齐可交付版本）
    STORAGE.md
      - 存储与同步规范：文件 SSOT；正文 txt；其他产物 md；SQLite 仅索引可重建
    STRUCTURE.md
      - 本文件：目录树 + 每个目录一句话职责
  agent_skills/
    README.md
      - Skills 总览：SOP/模板/rubric（按需加载）
    */SKILL.md
      - 单个技能：如伏笔台账/设定治理/读者画像/单元测试/分支合并/偏离影响分析
  src/
    README.md
      - .NET sidecar 实现说明与运行方式
    Aevatar.Novel.Contracts/
      - Protobuf 契约生成（从 novel/protos 生成 C# 类型）
    Aevatar.Novel.Sidecar/
      - ASP.NET Core sidecar：API + 文件监听 + 索引 + 工作流编排入口
  frontend/
    README.md
      - 前端启动说明与本机依赖（Rust/Node/protoc）+ 与 sidecar 连接方式
    src/
      - React UI：Monaco 编辑器 + 文件树 + 智能体面板（SSE 事件流）
    src-tauri/
      - Tauri Rust 壳：插件初始化 + capabilities 权限范围
    scripts/
      - Protobuf TS 代码生成（protoc + protoc-gen-es）
  protos/
    novel_assets.proto
      - 核心资产模型：Novel/Volume/Story/Chapter + Style + Settings + ArtifactRef
    novel_pipeline.proto
      - 流水线控制面：全自动运行 + 可协商暂停点 + 一键继续
    novel_sidecar.proto
      - Sidecar ↔ UI：ProjectRoot/SSOT 文件变更事件的最小契约
    docs/
      README.md
        - Protobuf 契约说明与兼容性规则
```

### 即将新增（Phase 0/1 会补齐）

- `novel/evals/`：评测基准与回归用例（prompt/模型/上下文策略变更必须过闸门）


