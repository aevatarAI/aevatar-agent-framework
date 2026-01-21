# Paper Review Frontend PRD (maker)

## 目标
为论文评审提供完整可视化前端，使用 Cognitive Mesh 的 maker 工作流，支持上传、会话管理、实时执行可视化、流式 LLM 展示，以及评审结果查看/下载。

## 用户流程
1. 左侧 Session 列表点击“新建评审”。
2. 上传论文（md/pdf），创建新 Session 并自动进入评审。
3. 右侧实时展示：Pipeline、Stage Log、Workers 流式、Voting 共识、Live Progress。
4. 评审完成后出现“查看评审结果”按钮，进入结果页（目录、打印、下载）。

## 功能要求
### 上传
- 支持 md、pdf（可选粘贴文本）。
- 上传后创建 sessionId，自动选中并进入评审。

### Session 列表
- 左侧栏列出全部 Session（标题/状态/时间），可切换查看；顶部按钮新建。
- 切换 Session 时恢复对应状态缓存。

### Pipeline
- 阶段：submit → analyze → decompose → execute → vote → compose → complete。
- decompose→execute→vote→compose 可能循环，需显示当前轮次/回环计数。
- 高亮当前/已完成/失败。

### Stage Log
- 时间顺序列表，概要信息；点击展开查看详情（完整 message、stepId、phase、tokens、llm calls、错误）。

### Workers
- 一 workerId 一卡片；卡片展示：名称（coordinator 或 worker N）、provider、状态、流式 LLM response（token 累积）。
- 流式：收到 LlmStreamingEvent 即更新卡片文本；完成后标记 completed。
- 点击卡片弹窗查看 LLM chat history；每条记录可折叠（details），含 system/user/response、tokens、时间、状态；保留最近 N 条（默认 10）。

### Voting Consensus
- 显示当前轮次、K、需要票数、领先票、是否达成共识、最大轮次；可用进度条/列表。

### Live Progress / Stats
- 当前 Phase/Step，整体进度百分比，tokens 总数，LLM calls 总数，当前 Step Id。

### 结果页
- 评审完成后出现“查看评审结果”按钮。
- 结果页包含目录（可锚点/侧目录）、打印、下载（md/pdf），展示最终报告。

## 事件与数据对接（预期）
- SSE：Progress/Stage/WorkerStarted/LlmStreaming/LlmCallComplete/Voting/Result。
- 流式事件字段：workerId（聚合键）、accumulatedContent（优先）、token、isLastToken。
- Worker 完成事件：同一 workerId 聚合。
- Voting：轮次、K、votesNeeded、leaderVotes 等。

## 状态管理
- 每个 Session 缓存：workers map、llmCalls、logs、timeline、stats。
- 切换 Session 时加载对应缓存，不影响 SSE。

## 空态与错误
- 上传/创建失败提示；SSE 断开提示并提供重连。
- 无数据显示 placeholder。

## 性能
- 流式更新节流（30–100 ms）。
- 长文本滚动容器；日志/历史默认折叠减少噪音。

## 待确认（选项）
- 结果下载格式：md、pdf，是否需要 docx。
- History 保留条数（默认 10，可配置）。
- Voting 展示样式（列表 vs 进度条）。 
