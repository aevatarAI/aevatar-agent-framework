# TECH_DEBT

- [ ] 将 Secrets/Config UI 的资产抽成可复用包（当前仅与 tools 共享源码）。
- [ ] 增加最小化的配置导入/导出流程（仅本地文件）。
- [ ] 拆分 `Program.cs`：按领域拆分 endpoints（LLM/Embeddings/WebSearch/SkillsMP/Crypto/Agents/Raw JSON）。
- [ ] 拆分 `wwwroot/aevatar-secrets-ui.js`：模块化 UI 逻辑并引入构建产物。
