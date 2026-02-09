# Aevatar.CognitiveMesh.Dsl Architecture

## 结构树 (Structure)

```
src/Aevatar.CognitiveMesh.Dsl/
├── CognitiveDslCompiler.cs   # DSL 编译器 + YAML/JSON 输入归一化
├── Models/                   # MeshDefinition + node/edge/constraint 数据模型
├── Options/                  # DSL 约束与 allowed types
└── Validation/               # 结构与语义校验规则
```

## 设计决策 (Decisions)

1. **输入归一化统一入口**
   - `MeshInputCoercer` 负责 YAML/JSON 归一化与节点类型提取。
   - 让 Sessions / Workshop / Platform 共用同一套解析规则，避免分叉。

2. **编译器专注于校验与标准化**
   - `CognitiveDslCompiler` 保持 compile/validate 的核心职责。
   - 输入处理逻辑与编译逻辑明确分层。

## 变更日志 (Changelog)

- 2026-01-27: 提取 `MeshInputCoercer`，统一 YAML → JSON 归一化逻辑。
