# Paper Review

> AI-Powered Academic Paper Review Platform based on Cognitive Mesh

## Overview

Paper Review 是基于 Cognitive Mesh 构建的学术论文评审平台，利用 MAKER 系统的多专家共识机制，提供专业的论文评审服务。

## 特性

- 🎯 **多评审类型支持**
  - Quick Review - 快速预审
  - Detailed Review - 详细评审
  - Deep Analysis - 深度分析
  - Revision Suggestion - 修改建议

- 🏛️ **多会议/期刊适配**
  - AI Conference (NeurIPS, ICML, ICLR)
  - NLP Conference (ACL, EMNLP, NAACL)
  - CV Conference (CVPR, ICCV, ECCV)
  - Journal (JMLR, TPAMI)
  - Workshop

- ⚡ **MAKER 共识系统**
  - 多专家并行评审
  - 投票达成共识
  - 综合意见生成

- 📊 **实时进度追踪**
  - SSE 事件流
  - 时间线展示
  - Token 使用统计

## 快速开始

### 1. 配置 LLM Provider

编辑 `appsettings.secrets.json`:

```json
{
  "LLMProviders": {
    "Providers": {
      "default": {
        "Name": "default",
        "ProviderKind": "DeepSeek",
        "ModelId": "deepseek-chat",
        "ApiKey": "your-api-key"
      }
    }
  }
}
```

### 2. 运行

```bash
cd apps/Aevatar.PaperReview/src/Aevatar.PaperReview
dotnet run
```

### 3. 访问

- Web UI: http://localhost:5001
- API: http://localhost:5001/api/sessions

## 架构

```
Aevatar.PaperReview/
├── Program.cs                 # 应用入口
├── Services/
│   └── PaperReviewService.cs  # 核心评审服务
├── Models/
│   └── ReviewSession.cs       # 会话模型
└── wwwroot/                   # Web UI
    ├── index.html
    ├── styles.css
    └── app.js
```

## API

### 会话管理

```http
GET  /api/sessions              # 获取所有会话
POST /api/sessions              # 创建会话
POST /api/upload                # 上传论文
```

### 评审执行

```http
POST /api/sessions/{id}/review  # 开始评审
POST /api/sessions/{id}/stop    # 停止评审
GET  /api/sessions/{id}/status  # 获取状态
GET  /api/sessions/{id}/result  # 获取结果
GET  /api/sessions/{id}/events  # SSE 事件流
```

## Supabase 集成

评审结果可以自动上传到 Supabase 进行持久化存储。

### 1. 创建 Supabase 表

在 Supabase 控制台执行以下 SQL：

```sql
-- 创建评审记录表
CREATE TABLE paper_reviews (
  id UUID DEFAULT gen_random_uuid() PRIMARY KEY,
  session_id TEXT NOT NULL UNIQUE,
  paper_title TEXT NOT NULL,
  authors TEXT,
  review_type TEXT NOT NULL,
  venue_type TEXT NOT NULL,
  status TEXT NOT NULL,
  content TEXT,
  error TEXT,
  llm_calls INTEGER DEFAULT 0,
  total_tokens BIGINT DEFAULT 0,
  duration_seconds DOUBLE PRECISION DEFAULT 0,
  report_url TEXT,
  created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
  completed_at TIMESTAMP WITH TIME ZONE
);

-- 创建索引
CREATE INDEX idx_paper_reviews_session_id ON paper_reviews(session_id);
CREATE INDEX idx_paper_reviews_created_at ON paper_reviews(created_at DESC);

-- 启用 RLS
ALTER TABLE paper_reviews ENABLE ROW LEVEL SECURITY;

-- 允许匿名读写（根据需求调整）
CREATE POLICY "Allow anonymous access" ON paper_reviews
  FOR ALL USING (true) WITH CHECK (true);
```

### 2. 创建 Storage Bucket

在 Supabase Storage 创建一个名为 `reviews` 的 bucket，用于存储评审报告文件。

### 3. 配置连接

编辑 `appsettings.secrets.json`：

```json
{
  "Supabase": {
    "Enabled": true,
    "Url": "https://your-project.supabase.co",
    "Key": "your-anon-key",
    "ReviewsTable": "paper_reviews",
    "StorageBucket": "reviews"
  }
}
```

### 4. Cloud API

```http
GET /api/cloud/reviews?limit=50  # 获取云端历史
GET /api/cloud/status            # 检查 Supabase 状态
```

## 技术栈

- **Runtime**: Aevatar Agent Framework (Local Runtime)
- **Strategy**: MAKER System (Multi-Agent Consensus)
- **LLM**: MEAI (Microsoft.Extensions.AI)
- **Storage**: Supabase (PostgreSQL + Storage)
- **Frontend**: Vanilla JS + CSS
- **Observability**: OpenTelemetry

## 与 Cognitive Mesh 的关系

Paper Review 是 Cognitive Mesh 的**垂直场景应用**：

```
apps/
├── Aevatar.CognitiveMesh/           # 通用平台
└── Aevatar.PaperReview/             # 论文评审 (本项目)

src/
├── Aevatar.Agents.Cognitive.Core/        # 抽象层
├── Aevatar.Agents.Cognitive.Content/     # 内容/项目配置
└── Aevatar.CognitiveMesh.Dsl/            # DSL 编译器
```

未来规划：
1. 将 Paper Review 的专用能力抽象为通用组件
2. 并入 Cognitive Mesh 作为内置场景
3. MAKER 作为可选策略之一

## License

MIT
