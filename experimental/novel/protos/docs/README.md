### experimental/novel/protos - Protobuf 契约目录

这里存放 **Novel 产品的跨边界 Protobuf 契约**（UI ↔ Sidecar ↔ Multi-Agent ↔ Cognitive Mesh）。

- **铁律**：任何跨越边界的类型必须用 Protobuf 定义（State / Event / Configuration / Stream Messages / EventSourcing Events）。
- **文件**：
  - `novel_assets.proto`：小说资产模型 + 配置资产（模型/预算/自动继续策略）+ 偏离结构化条目 + 故事单元测试契约 + 写作会话元信息 + Setup/Payoff Ledger artifact kinds + 通用 ArtifactRef
  - `novel_pipeline.proto`：全自动流水线控制面事件（Start/Pause/Resume/Gate 决策/产物通知 + 偏离→影响分析一键请求/产物 + 结构化偏离条目 + 故事单元测试运行 + 会话写作日志更新）
  - `novel_sidecar.proto`：Sidecar ↔ UI 最小契约（ProjectRoot + SSOT 文件变更 + 单元测试完成 + 偏离影响分析完成 + Canon Governance 事件 + Rewrite Branch/Merge APIs/事件 + Setup/Payoff Ledger scan 事件 + 文件读写 API + SidecarEvent envelope）

### 兼容性规则（别踩坑）

- **只允许新增字段**：不要改字段类型，不要复用/重排字段编号。
- **编号空洞允许**：为未来扩展预留空间，比“挤编号”更安全。
- **大文本别塞事件里**：优先用 `TextBlobRef.uri` 引用（事件只放 preview/摘要）。


