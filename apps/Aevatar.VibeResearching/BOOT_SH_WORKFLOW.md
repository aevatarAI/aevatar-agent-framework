# boot.sh 工作流程分析

## 📋 概述

`boot.sh` 是 VibeResearching 应用的主启动脚本，负责启动后端 API 服务和前端开发服务器。

## 🔄 执行流程

### 1. 初始化和参数解析

**位置**: 第 1-50 行

```bash
# 设置错误处理
set -euo pipefail

# 解析命令行参数
--backend-only    # 只启动后端
--frontend-only   # 只启动前端
--help            # 显示帮助信息
```

**关键变量**:
- `RUN_BACKEND=1` (默认)
- `RUN_FRONTEND=1` (默认)
- `BACKEND_PORT=5678`
- `FRONTEND_PORT=5173`

### 2. 目录和路径设置

**位置**: 第 52-70 行

```bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
BACKEND_DIR="$PROJECT_ROOT/src/Aevatar.VibeResearching.Api"
FRONTEND_DIR="$PROJECT_ROOT/sisyphus-frontend"
```

**作用**: 确定项目根目录、后端目录和前端目录的绝对路径。

### 3. 健康检查函数定义

**位置**: 第 72-95 行

```bash
wait_for_http_ok() {
    # 等待 HTTP 端点返回 200 OK
    # 超时时间: $2 秒
    # URL: $1
}
```

**用途**: 等待后端/前端服务启动完成。

### 4. 清理函数定义

**位置**: 第 97-120 行

```bash
cleanup() {
    # 清理函数：停止所有后台进程
    # 捕获 SIGINT/SIGTERM 信号
}
```

**用途**: 优雅地停止所有启动的服务。

### 5. 环境变量配置

**位置**: 第 122-155 行

#### 5.1 LLM 配置
```bash
export LLM_PROVIDER="${LLM_PROVIDER:-deepseek}"
export LLM_API_KEY="${LLM_API_KEY:-}"
```

#### 5.2 Neo4j 配置
```bash
export NEO4J_URI="${NEO4J_URI:-bolt://localhost:7687}"
export NEO4J_USERNAME="${NEO4J_USERNAME:-neo4j}"
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI}"
export NEO4J_DATABASE="${NEO4J_DATABASE:-neo4j}"
```

#### 5.3 Java 环境配置
```bash
if [[ -z "${JAVA_HOME:-}" ]]; then
    # 尝试从 Homebrew 找到 openjdk@21
    JAVA_HOME_CANDIDATE="$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home"
    if [[ -n "${JAVA_HOME_CANDIDATE:-}" ]] && [[ -d "$JAVA_HOME_CANDIDATE" ]]; then
        export JAVA_HOME="$JAVA_HOME_CANDIDATE"
        export PATH="$JAVA_HOME/bin:$PATH"
    fi
fi
```

**作用**: 
- 配置 LLM 提供商和 API 密钥
- 配置 Neo4j 连接信息（URI、用户名、密码、数据库）
- 设置 Java 环境（Neo4j 需要）

### 6. 后端启动

**位置**: 第 157-170 行

**条件**: `RUN_BACKEND=1`

**流程**:
```bash
1. 切换到后端目录
2. 设置 ASPNETCORE_URLS 环境变量
3. 后台运行 dotnet run --no-launch-profile
4. 保存后端进程 ID (BACKEND_PID)
5. 等待后端健康检查端点响应 (http://localhost:5678/health)
```

**关键点**:
- 后端在后台运行（`&`）
- 使用 `wait_for_http_ok` 等待启动完成
- 超时时间: 30 秒

### 7. 前端启动

**位置**: 第 172-200 行

**条件**: `RUN_FRONTEND=1`

**流程**:
```bash
1. 检查 npm 是否可用
2. 切换到前端目录
3. 检查 node_modules 是否存在
   - 如果不存在，运行 npm install
4. 后台运行 npm run dev
5. 保存前端进程 ID (FRONTEND_PID)
6. 等待前端开发服务器响应 (http://localhost:5173)
```

**关键点**:
- 前端在后台运行（`&`）
- 自动安装依赖（如果 node_modules 不存在）
- 使用 `wait_for_http_ok` 等待启动完成
- 超时时间: 30 秒

### 8. 启动完成提示

**位置**: 第 202-220 行

```bash
echo "=========================================="
echo "✅ 启动完成"
echo "=========================================="
echo ""
echo "后端: http://localhost:5678"
echo "前端: http://localhost:5173"
echo ""
echo "按 Ctrl+C 停止所有服务"
```

### 9. 等待和清理

**位置**: 第 222-224 行

```bash
# 等待用户中断 (Ctrl+C)
wait

# cleanup 函数会被 trap 调用，停止所有服务
```

## 🔍 关键函数详解

### wait_for_http_ok

```bash
wait_for_http_ok() {
    local url=$1
    local max_wait=${2:-30}
    local elapsed=0
    
    while [[ $elapsed -lt $max_wait ]]; do
        if curl -sf "$url" >/dev/null 2>&1; then
            return 0  # 成功
        fi
        sleep 1
        elapsed=$((elapsed + 1))
    done
    
    return 1  # 超时
}
```

**作用**: 轮询 HTTP 端点，直到返回 200 OK 或超时。

### cleanup

```bash
cleanup() {
    echo ""
    echo "正在停止服务..."
    
    # 停止后端
    if [[ -n "${BACKEND_PID:-}" ]]; then
        kill "$BACKEND_PID" 2>/dev/null || true
    fi
    
    # 停止前端
    if [[ -n "${FRONTEND_PID:-}" ]]; then
        kill "$FRONTEND_PID" 2>/dev/null || true
    fi
    
    exit 0
}
```

**作用**: 捕获 SIGINT/SIGTERM 信号，优雅地停止所有服务。

## 📊 执行时序图

```
启动 boot.sh
    │
    ├─> 解析参数
    │
    ├─> 设置目录路径
    │
    ├─> 配置环境变量
    │   ├─> LLM 配置
    │   ├─> Neo4j 配置
    │   └─> Java 环境
    │
    ├─> 启动后端 (如果 RUN_BACKEND=1)
    │   ├─> dotnet run (后台)
    │   └─> 等待健康检查
    │
    ├─> 启动前端 (如果 RUN_FRONTEND=1)
    │   ├─> npm install (如果需要)
    │   ├─> npm run dev (后台)
    │   └─> 等待开发服务器
    │
    ├─> 显示启动完成信息
    │
    └─> 等待用户中断 (Ctrl+C)
        └─> cleanup() 停止所有服务
```

## 🔑 关键环境变量

### LLM 配置
- `LLM_PROVIDER`: LLM 提供商（默认: deepseek）
- `LLM_API_KEY`: LLM API 密钥

### Neo4j 配置
- `NEO4J_URI`: Neo4j 连接 URI（默认: bolt://localhost:7687）
- `NEO4J_USERNAME`: Neo4j 用户名（默认: neo4j）
- `NEO4J_PASSWORD`: Neo4j 密码（默认: gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI）
- `NEO4J_DATABASE`: Neo4j 数据库名（默认: neo4j）

### Java 环境
- `JAVA_HOME`: Java 主目录（自动检测 openjdk@21）
- `PATH`: 包含 `$JAVA_HOME/bin`

### .NET 配置
- `ASPNETCORE_URLS`: ASP.NET Core 监听地址（默认: http://localhost:5678）

## ⚠️ 注意事项

1. **错误处理**: 使用 `set -euo pipefail`，任何错误都会导致脚本退出
2. **后台进程**: 后端和前端都在后台运行，进程 ID 被保存用于清理
3. **健康检查**: 使用轮询方式等待服务启动，超时时间 30 秒
4. **信号处理**: 捕获 SIGINT/SIGTERM，优雅地停止所有服务
5. **Java 环境**: 自动检测和设置 Java 环境（Neo4j 需要）
6. **依赖安装**: 前端依赖自动安装（如果 node_modules 不存在）

## 🐛 常见问题

### 1. 后端启动失败
- **原因**: 端口被占用、依赖缺失、配置错误
- **解决**: 检查端口 5678、运行 `dotnet restore`、检查环境变量

### 2. 前端启动失败
- **原因**: npm 未安装、node_modules 损坏、端口被占用
- **解决**: 安装 Node.js、删除 node_modules 重新安装、检查端口 5173

### 3. Neo4j 连接失败
- **原因**: Neo4j 未运行、密码错误、端口不匹配
- **解决**: 启动 Neo4j、检查密码、确认端口配置

### 4. Java 环境问题
- **原因**: JAVA_HOME 未设置、openjdk@21 未安装
- **解决**: 安装 openjdk@21 (`brew install openjdk@21`)、手动设置 JAVA_HOME

## 📝 使用示例

### 启动完整服务（后端 + 前端）
```bash
./boot.sh
```

### 只启动后端
```bash
./boot.sh --backend-only
```

### 只启动前端
```bash
./boot.sh --frontend-only
```

### 使用自定义端口
```bash
BACKEND_PORT=8080 FRONTEND_PORT=3000 ./boot.sh
```

### 使用自定义 LLM 提供商
```bash
LLM_PROVIDER=openai LLM_API_KEY=your_key ./boot.sh
```

## 🔄 重启流程

如果需要重启服务：

1. **停止当前服务**: 按 `Ctrl+C` 或运行 `cleanup`
2. **重新运行**: `./boot.sh`

或者手动停止：

```bash
# 停止后端
kill $(lsof -ti :5678)

# 停止前端
kill $(lsof -ti :5173)
```
