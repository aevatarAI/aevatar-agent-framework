# Framework Structure (src)

本文件描述 framework 层的 cognitive 组件边界与依赖关系。

## 结构树（聚焦 cognitive）

```
src/
├── Aevatar.Agents.Cognitive/           # 认知推理 Agent 实现
├── Aevatar.Agents.Cognitive.Core/      # 策略抽象/执行模型/结果类型
├── Aevatar.Agents.Cognitive.Content/   # 内容加载/项目存储/模板提示词
├── Aevatar.Agents.Cognitive.Streaming/ # 广播/流式工具（SSE fan-out）
└── Aevatar.CognitiveMesh.Dsl/          # Cognitive DSL 编译器
```

## 依赖边界

- Core：仅依赖基础框架与策略实现所需的 AI/Maker 模块，不依赖 Content
- Content：依赖 Core（任务定义/策略枚举），负责内容与项目配置
- Streaming：无额外依赖，提供通用广播工具
- Apps：只依赖 Core/Content/Streaming + Dsl，不再直接链接 app 内部代码

## 变更记录

- 新增 `Aevatar.Agents.Cognitive.Core` / `Content` / `Streaming`（框架层能力）
- 迁移原 `CognitiveMesh.Abstractions` 与 app 内通用实现到 framework
- 移除 `Aevatar.CognitiveMesh.Abstractions` 项目
- 将 `StrategyRegistry` 上移到 `Aevatar.Agents.Cognitive.Core`


