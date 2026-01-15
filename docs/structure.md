# Repository Structure

本仓库是单一 monorepo，通过“分域 + 分层 + 统一入口”避免迷路。

## 顶层职责

- `src/`：Aevatar Agent Framework 核心库（禁止混入业务 app）
- `test/`：框架级测试（app 测试在 app 内）
- `apps/`：稳定、可运行的系统级应用
- `experimental/`：规划中/开发中的应用
- `examples/`：演示与教程（不得被生产代码依赖）
- `agents/`：业务 Agent 实现集合
- `plugins/`：插件扩展
- `platform/`：平台级基础能力
- `tools/`：构建与工具链
- `docs/`：全仓入口文档与规范

## 结构树（简化）

```
.
├── src/                  # Framework 核心库
├── test/                 # Framework 测试
├── apps/                 # 稳定应用
├── experimental/         # 规划/开发中应用
├── examples/             # 示例与教程
├── agents/               # 业务 Agent
├── plugins/              # 插件
├── platform/             # 平台能力
├── tools/                # 工具链
└── docs/                 # 文档入口
```

## 边界规则（必须遵守）

1. `src/` 只放框架核心；app 不得反向依赖 `examples/`
2. `apps/` 与 `experimental/` 只存系统级应用
3. `examples/` 仅用于示例与教学，不参与生产依赖
4. app 内部自行维护 `docs/` 与 `boot.sh`
5. `apps/` 目录统一使用 `Aevatar.*` 命名，AppHost 置于应用目录内

## 变更记录

- 移除根目录遗留的 `cognitive-mesh/` 与 `notebook/` 构建产物目录，统一入口在 `apps/` / `experimental/`
- 根目录 `aevatar-agent-framework.slnx` 仅保留 `src/` 与 `test/`，示例独立为 `examples/aevatar-examples.slnx`
- `apps/` 目录统一使用 `Aevatar.*` 命名，并将 AppHost 收敛到各自应用目录
- `Aevatar.VibeResearching` → `Aevatar.VibeResearching`，`Aevatar.Trade` → `Aevatar.Trading`


