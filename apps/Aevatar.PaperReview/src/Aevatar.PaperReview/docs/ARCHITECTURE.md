# Aevatar.PaperReview 架构文档

## 目录结构

```
Aevatar.PaperReview/
├── Events/                      # 事件处理
│   ├── ProgressEventMapper.cs         # 进度事件映射工具类
│   └── SseEventSender.cs              # SSE 事件发送
├── Models/                      # 数据模型
│   ├── ReviewSession.cs               # 会话与事件定义
│   └── StageTracker.cs                # 阶段跟踪器
├── Prompty/                     # Prompty 解析器（轻量级实现）
│   ├── PromptyFile.cs                 # Prompty 文件模型
│   └── PromptyLoader.cs               # 加载与渲染
├── prompts/                     # 提示词模板（混合格式）
│   ├── review-task.prompty           # LLM 提示词 (Prompty 格式)
│   └── review-report.scriban         # 报告生成 (Scriban 格式)
├── Services/                    # 服务层
│   ├── PaperReviewService.cs          # 核心服务（会话 + 流程编排）
│   ├── PaperUploadService.cs          # 文件上传与 PDF 解析
│   ├── ReviewPromptProvider.cs        # 混合模板加载渲染
│   ├── ReviewEventBridge.cs           # 进度事件转换
│   └── SupabaseService.cs             # 持久化
├── wwwroot/                     # 前端资源
│   ├── index.html                     # 主页面
│   ├── app.js                         # 主逻辑
│   ├── styles.css                     # 样式
│   ├── result.html                    # 结果页
│   └── trace.*                        # LLM 追踪页
└── Program.cs                   # 入口 + API 路由
```

## 核心架构

**PaperReview 完全基于 Cognitive Mesh DSL**，通过服务拆分实现关注点分离。

### 提示词模板策略

采用 **混合方案**：根据用途选择最合适的模板引擎。

| 模板类型 | 格式 | 引擎 | 原因 |
|---------|------|------|------|
| LLM Prompt | `.prompty` | 自研轻量级 Prompty | 元数据管理、VS Code 支持、未来 Semantic Kernel 集成 |
| 报告生成 | `.scriban` | Scriban | 强大的格式化能力、复杂逻辑支持 |

#### Prompty 格式（LLM Prompt）

```yaml
---
name: paper-review-task
description: Multi-dimensional academic paper review
model:
  api: chat
  parameters:
    max_tokens: 4000
    temperature: 0.3
inputs:
  venue_type:
    type: string
    description: Type of venue
  paper_content:
    type: string
---
system:
You are a Senior Editor for a top-tier {{ venue_type }}.

user:
Review this paper: {{ paper_content }}
```

优势：
- YAML front matter 描述输入参数和模型配置
- system/user 消息分离
- VS Code Prompty 扩展实时预览
- 未来可无缝迁移到 Semantic Kernel

#### Scriban 格式（报告生成）

```scriban
# Report
**Status:** {{ if success }}✓ Completed{{ else }}✗ Failed{{ end }}
**Duration:** {{ duration | math.format "F1" }}s
```

优势：
- 强大的过滤器和函数
- 复杂条件和循环
- 通用文本渲染

### 服务职责

| 服务 | 职责 | 行数 |
|------|------|------|
| `PaperReviewService` | 会话管理、流程编排 | ~500 |
| `PaperUploadService` | 文件上传、PDF/MD/TXT 解析 | ~150 |
| `PromptyLoader` | Prompty 文件解析与渲染 | ~260 |
| `ReviewPromptProvider` | 混合模板加载渲染（Prompty + Scriban） | ~160 |
| `ReviewEventBridge` | ReasoningProgress → UI 事件转换 | ~550 |

### 1. PaperReviewService
**职责**: 会话生命周期与评审流程

```csharp
// 核心方法
CreateSessionAsync()      // 创建会话
StartReviewAsync()        // 启动评审
GetEventStreamAsync()     // SSE 事件流
```

**不再包含**:
- 文件处理 → 移至 `PaperUploadService`
- 提示词构建 → 移至 `ReviewPromptProvider`
- 进度转换 → 移至 `ReviewEventBridge`

### 2. PaperUploadService
**职责**: 文件上传与文本提取

```csharp
UploadAsync()          // 上传文件到 uploads/
LoadContentAsync()     // 从 uploadId 加载内容
ExtractPdfText()       // PdfPig 提取 + regex fallback
```

### 3. PromptyLoader
**职责**: 解析和渲染 Prompty 文件（轻量级实现）

```csharp
Load()              // 加载 .prompty 文件，解析 YAML + 模板
Render()            // 渲染模板，返回 PromptyRenderResult
RenderAsText()      // 渲染为纯文本（兼容旧 API）
```

**设计决策**：自己实现 Prompty 解析，避免引入 Semantic Kernel 重依赖。

### 4. ReviewPromptProvider
**职责**: 混合模板管理

```csharp
// LLM Prompt (Prompty)
RenderReviewTask()           // 渲染评审任务，返回 PromptyRenderResult
RenderReviewTaskAsText()     // 渲染为纯文本
GetReviewTaskModelConfig()   // 获取模型配置

// 报告生成 (Scriban)
RenderReport()               // 渲染评审报告
```

### 5. ReviewEventBridge
**职责**: Cognitive DSL 进度 → UI 事件

```csharp
HandleProgress()       // 处理 ReasoningProgress
PhaseMapper.Map()      // 阶段映射

// 发射的事件类型:
// - WorkerStartedEvent
// - LlmCallStartEvent
// - LlmStreamingEvent
// - VotingRoundEvent
// - ConsensusEvent
// - StageLogEvent
// ...
```

### 6. PhaseMapper
**职责**: Cognitive DSL 阶段 → ReviewPhase

```csharp
// 映射优先级:
// 1. stepId 关键词 (check_atomic → Assessing)
// 2. phase 前缀 (DECOMPOSE → Decomposing)
// 3. stepType (vote → Voting)
// 4. 默认 Solving
```

## 设计原则

1. **单一职责**: 每个服务只做一件事
2. **模板外置**: 提示词在文件中，方便修改
3. **混合策略**: 根据场景选择最合适的模板引擎
4. **轻量依赖**: 自研 Prompty 解析，避免重依赖
5. **依赖注入**: 所有服务通过 DI 组合
6. **可测试性**: 服务可独立测试

## 数据流

```
用户上传论文
     │
     ▼
┌─────────────────────┐
│  PaperUploadService │ ──提取文本──▶ paper content
└─────────────────────┘
     │
     ▼
┌─────────────────────┐
│ PaperReviewService  │ ──创建──▶ ReviewSession
└─────────────────────┘
     │
     ▼
┌─────────────────────┐
│ ReviewPromptProvider│ ──渲染(.prompty)──▶ 评审任务提示词
└─────────────────────┘
     │
     ▼
┌─────────────────────┐
│  CognitiveStrategy  │ ──执行──▶ maker.yaml 工作流
└─────────────────────┘
     │
     ▼ (ReasoningProgress)
┌─────────────────────┐
│  ReviewEventBridge  │ ──转换──▶ UI 事件 (SSE)
└─────────────────────┘
     │
     ▼
┌─────────────────────┐
│  ReviewSession.     │
│  EventChannel       │ ──推送──▶ 前端 (app.js)
└─────────────────────┘
```

## 事件类型

| 事件类型 | 用途 | 触发时机 |
|---------|------|----------|
| ProgressEvent | 进度更新 | 每次 phase 变化 |
| PhaseChangeEvent | 阶段变更 | 阶段切换时 |
| StageLogEvent | 阶段日志 | 阶段开始/完成 |
| WorkerStartedEvent | Worker 启动 | LLM 调用开始 |
| WorkerCompletedEvent | Worker 完成 | LLM 调用完成 |
| LlmCallStartEvent | LLM 调用开始 | 同上 |
| LlmStreamingEvent | LLM 流式输出 | 流式 token |
| LlmCallCompleteEvent | LLM 调用完成 | 同上 |
| VotingRoundEvent | 投票轮次 | 每轮投票 |
| ConsensusEvent | 达成共识 | 投票完成 |
| ResultEvent | 最终结果 | 评审完成 |
| ErrorEvent | 错误信息 | 异常发生 |

## 扩展点

1. **自定义 LLM 提示词**: 修改 `prompts/review-task.prompty`
2. **自定义报告模板**: 修改 `prompts/review-report.scriban`
3. **新事件类型**: 在 `ReviewSession.cs` 定义 + `ReviewEventBridge` 发射
4. **新文件格式**: 在 `PaperUploadService` 添加解析器
5. **前端定制**: 修改 `wwwroot/app.js`
6. **Semantic Kernel 集成**: 替换 `PromptyLoader` 为 SK 原生实现
