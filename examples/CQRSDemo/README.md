# CQRS Demo - 状态投影测试

## 概述

本 Demo 用于测试 Aevatar Agent Framework 的 CQRS 状态投影能力。

**CQRSDemo 是纯测试客户端**，通过 HTTP 调用 API 验证 CQRS 流程。

---

## 测试场景

| 场景 | HttpApi 运行时 | Silo | 说明 |
|------|---------------|------|------|
| **Local** | Local (内存) | ❌ 不需要 | 开发/单元测试 |
| **Orleans** | Orleans | ✅ 需要 | 分布式集成测试 |
| **MassTransit** | Orleans + MassTransit | ✅ 需要 | 企业集成测试 |

### 架构图

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         CQRS 测试架构                                    │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   ┌───────────────┐                                                      │
│   │  CQRSDemo     │   纯测试客户端 (HTTP 调用)                           │
│   │  (测试脚本)   │                                                      │
│   └───────┬───────┘                                                      │
│           │ HTTP                                                         │
│           ▼                                                              │
│   ┌───────────────────────────────────────────────────────────────┐     │
│   │                    HttpApi.Host                                │     │
│   │  ┌─────────────────┐  ┌─────────────────┐  ┌───────────────┐  │     │
│   │  │ AgentController │  │StateQueryController│ │HealthController│ │     │
│   │  └────────┬────────┘  └────────┬────────┘  └───────────────┘  │     │
│   └───────────┼────────────────────┼──────────────────────────────┘     │
│               │                    │                                     │
│               ▼                    │                                     │
│   ┌─────────────────────────────┐  │                                     │
│   │  Agent Runtime              │  │                                     │
│   │  ┌────────────────────────┐ │  │     ┌─────────────────────────┐    │
│   │  │ Local: LocalGAgentActor│ │  │     │    Elasticsearch        │    │
│   │  │ Orleans: GAgentGrain   │ │  │     │  (读模型/查询)          │    │
│   │  └───────────┬────────────┘ │  │     └────────────▲────────────┘    │
│   │              │              │  │                  │                  │
│   │              ▼              │  │                  │                  │
│   │  ┌────────────────────────┐ │  │                  │                  │
│   │  │ OnStateChangedAsync    │ │  │                  │                  │
│   │  │ → IStateProjector      │─┼──┼──────────────────┘                  │
│   │  └────────────────────────┘ │  │                                     │
│   └─────────────────────────────┘  │                                     │
│                                    │                                     │
│   [如果 Orleans 运行时]            │                                     │
│   ┌─────────────────────────────┐  │                                     │
│   │  Silo (Orleans)             │  │                                     │
│   │  - Orleans Cluster          │  │                                     │
│   │  - Stream Provider          │  │                                     │
│   │  - Grain Storage            │  │                                     │
│   └─────────────────────────────┘  │                                     │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 场景 1：Local 运行时（开发测试）

**只启动 HttpApi**，使用内存运行时，无需 Silo。

### Step 1: 启动基础设施 (可选)

```bash
# 如果要测试 ES 投影
docker run -d --name es -p 9200:9200 -e "discovery.type=single-node" elasticsearch:8.11.0
```

### Step 2: 启动 HttpApi (Local 模式)

```bash
cd apps/Aevatar.App/src/Aevatar.App.HttpApi.Host

# 使用 Local 运行时
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

**配置** (`appsettings.Development.json`):
```json
{
  "AgentRuntime": {
    "RuntimeType": "Local"
  },
  "Elasticsearch": {
    "Url": "http://localhost:9200",
    "IndexPrefix": "aevatar-state"
  }
}
```

### Step 3: 运行测试

```bash
cd examples/CQRSDemo
dotnet run -- --api https://localhost:44345
```

---

## 场景 2：Orleans 运行时（集成测试）

**启动 Silo + HttpApi**，使用 Orleans 分布式运行时。

### Step 1: 启动基础设施

```bash
cd apps/Aevatar.App
docker-compose up -d  # MongoDB + ES + RabbitMQ
```

### Step 2: 启动 Silo

```bash
cd apps/Aevatar.App/src/Aevatar.Silo
dotnet run
```

### Step 3: 启动 HttpApi (Orleans 模式)

```bash
cd apps/Aevatar.App/src/Aevatar.App.HttpApi.Host

# 使用 Orleans 运行时
ASPNETCORE_ENVIRONMENT=Production dotnet run
```

**配置** (`appsettings.json`):
```json
{
  "AgentRuntime": {
    "RuntimeType": "Orleans"
  },
  "Orleans": {
    "ClusterId": "aevatar-cluster",
    "ServiceId": "aevatar-service"
  }
}
```

### Step 4: 运行测试

```bash
cd examples/CQRSDemo
dotnet run -- --api https://localhost:44345
```

---

## 场景 3：MassTransit 运行时（企业集成）

**启动 Silo (MassTransit) + HttpApi**，使用 RabbitMQ 消息流。

### Silo 配置

```json
{
  "MessageStream": {
    "Provider": "MassTransit"
  },
  "MassTransit": {
    "RabbitMQ": {
      "Host": "amqp://localhost:5672"
    }
  }
}
```

其他步骤同场景 2。

---

## CQRSDemo 测试命令

```bash
# 基本用法
dotnet run -- --api <HttpApi地址>

# 示例
dotnet run -- --api https://localhost:44345

# 完整参数
dotnet run -- --api https://localhost:44345 --verbose
```

---

## API 端点

### Agent 操作

| 方法 | 路径 | 描述 |
|------|------|------|
| POST | `/api/agents/create` | 创建 Agent |
| POST | `/api/agents/{id}/publish` | 发布事件 |
| GET | `/api/agents/{id}/state` | 获取 Agent 状态 |

### 状态查询 (StateQueryController)

| 方法 | 路径 | 描述 |
|------|------|------|
| GET | `/api/states/{agentType}/{agentId}` | 按 ID 查询状态 |
| POST | `/api/states/query` | Lucene 查询 |
| GET | `/api/states/{agentType}/count` | 计数查询 |

### 查询示例

```bash
# 1. 按 ID 查询
curl -k https://localhost:44345/api/states/UserAgent/123e4567-e89b-12d3-a456-426614174000

# 2. Lucene 查询
curl -k -X POST https://localhost:44345/api/states/query \
  -H "Content-Type: application/json" \
  -d '{
    "agentType": "UserAgent",
    "queryString": "loginCount:>5",
    "pageSize": 10
  }'

# 3. 计数
curl -k "https://localhost:44345/api/states/UserAgent/count?queryString=isActive:true"
```

---

## CQRS 投影流程

```
Agent Event → HandleEventAsync → OnStateChangedAsync → IStateProjector → Elasticsearch
```

**关键代码位置**:

| 文件 | 说明 |
|------|------|
| `src/Aevatar.Agents.Core/GAgentBase.TState.cs` | `OnStateChangedAsync` 钩子 |
| `src/Aevatar.Agents.Core/CQRS/ElasticsearchStateProjector.cs` | ES 投影器 |
| `apps/Aevatar.App/src/Aevatar.Silo/CQRS/` | Silo CQRS 配置 |
| `apps/Aevatar.App/src/Aevatar.App.HttpApi/Controllers/StateQueryController.cs` | 查询 API |

---

## 测试脚本

```bash
#!/bin/bash
# test_cqrs.sh
API_URL="${1:-https://localhost:44345}"

echo "=== CQRS Test ==="
echo "API: $API_URL"

# 1. 创建 Agent
echo "1. Creating agent..."
RESPONSE=$(curl -sk -X POST "$API_URL/api/agents/create" \
  -H "Content-Type: application/json" \
  -d '{"agentType": "UserAgent"}')
AGENT_ID=$(echo $RESPONSE | jq -r '.agentId')
echo "   Agent: $AGENT_ID"

# 2. 发布事件
echo "2. Publishing events..."
for i in {1..3}; do
  curl -sk -X POST "$API_URL/api/agents/$AGENT_ID/publish" \
    -H "Content-Type: application/json" \
    -d '{"eventType": "UserLoggedIn"}' > /dev/null
  echo "   Event $i"
done

# 3. 查询状态
sleep 1
echo "3. Query state:"
curl -sk "$API_URL/api/states/UserAgent/$AGENT_ID" | jq

echo "=== Done ==="
```

---

## 故障排查

### 状态未投影到 ES

```bash
# 检查 ES 索引
curl http://localhost:9200/_cat/indices?v

# 检查投影器日志
grep "StateProjector" logs/silo.log
```

### 版本冲突

**原因**: `wrapper.Version` 未递增

**修复**: 使用 `DateTime.UtcNow.Ticks` 作为版本（无 EventStore 时）

### 查询返回空

```bash
# 确认索引存在
curl "http://localhost:9200/aevatar-state-*/_search?pretty"
```
