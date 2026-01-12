# 开发脚本（Dev Tooling）

## `start.sh`（一键重启 trade system）

用途：**kill 掉旧的 trade system 进程**（AppHost / Trading API / Frontend），然后拉起新的 Aspire AppHost。

### 使用方式

```bash
cd trade
chmod +x ./start.sh

# 默认：不 build（更快）
./start.sh

# 可选：先 build 再启动
./start.sh --build
```

### 它会清理哪些端口

- `20888`：Aspire AppHost 内部管理端口（orchestration）
- `15888`：Aspire Dashboard
- `7100`：Trading API
- `5173`：Frontend（Vite dev）

> 说明：脚本采用“按端口 kill”策略，避免误杀无关的 dotnet 进程。


