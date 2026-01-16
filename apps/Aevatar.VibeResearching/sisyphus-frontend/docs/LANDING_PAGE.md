# Sisyphus Landing Page Architecture

## Overview

Landing Page 是 Sisyphus 研究平台的入口页面，展示产品核心价值和全量 DAG 可视化。

## Routes

| Path | Component | Description |
|------|-----------|-------------|
| `/` | `LandingPage` | Landing Page（默认） |
| `/app` | `App` | 研究功能页面 |

## Page Structure

```
LandingPage
├── HeroSection          # Hero 区域 + 动画 DAG 背景
├── PillarsSection       # 三大核心特性（DAG/Agents/Verify）
├── DagShowcaseSection   # 全量 DAG 交互展示
│   └── LandingDagViewer # ReactFlow DAG 查看器
└── WorkflowSection      # 研究流程时间线
└── CtaFooter            # CTA + Footer
```

## File Structure

```
src/pages/landing/
├── index.tsx              # Landing Page 入口
├── hero-section.tsx       # Hero 区域
├── hero-dag-background.tsx # Hero 背景动画
├── pillars-section.tsx    # 三大支柱
├── dag-showcase-section.tsx # DAG 展示区
├── landing-dag-viewer.tsx # DAG 查看器组件
├── workflow-section.tsx   # 工作流展示
└── cta-footer.tsx         # CTA 底部
```

## Key Components

### LandingDagViewer

简化版的 `WorkflowTopology`，复用：
- `@xyflow/react` - DAG 渲染
- `cyber-node.tsx` - 节点样式
- `dag-layout.ts` - 布局算法
- `node-legend.tsx` - 过滤器

特性：
- **真实数据**：从后端 API `/api/dag/global` 获取
- 全屏模式
- 节点过滤（Plan/Knowledge/Active）
- 节点详情侧边栏
- CTA 跳转
- Loading / Empty / Error 状态 UI

### 数据来源

| 状态 | 显示内容 |
|------|----------|
| **Loading** | 加载动画 + "Loading Knowledge Graph..." |
| **有数据** | 交互式 DAG 图 |
| **无数据** | Empty State - 引导用户创建第一个研究 |
| **Error** | 错误提示 + 重试按钮 |

API 端点：
```
GET /api/dag/global → 全局知识图谱（跨所有 Session）
```

## Design System

延续主应用的 Cyberpunk / Nebula Warm 主题：

```css
--bg-base: #0c0f14;      /* 深蓝黑背景 */
--neon-cyan: #7dd3fc;    /* 主色 */
--neon-gold: #fcd34d;    /* 强调色 */
--neon-purple: #c4b5fd;  /* 辅助色 */
```

字体：
- `Orbitron` - 标题
- `Space Grotesk` - 正文
- `JetBrains Mono` - 代码/数据

## Animation

使用 `framer-motion` 实现：
- Staggered reveal（渐次显现）
- Scroll-triggered animations
- Hero 背景 DAG mesh 动画

## Navigation Flow

```
Landing Page (/)
    ↓ [Start Research] button
    ↓ [Explore DAG] scroll
    ↓ CTA in DAG sidebar
    ↓ Footer CTA
Research App (/app)
```


