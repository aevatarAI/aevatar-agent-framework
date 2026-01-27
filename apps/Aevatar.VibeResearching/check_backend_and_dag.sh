#!/bin/bash
# 检查后端状态和 DAG 创建情况

set -e

echo "=========================================="
echo "后端和 DAG 诊断脚本"
echo "=========================================="
echo ""

# 1. 检查后端是否运行
echo "1. 检查后端进程和端口..."
BACKEND_PID=$(lsof -ti :5678 2>/dev/null || echo "")
if [[ -n "$BACKEND_PID" ]]; then
    echo "   ✅ 后端正在运行 (PID: $BACKEND_PID)"
else
    echo "   ❌ 后端未运行（端口 5678 未被占用）"
    echo "   请运行 ./boot.sh 启动后端"
    exit 1
fi

# 2. 检查后端健康状态
echo ""
echo "2. 检查后端健康状态..."
HEALTH_RESPONSE=$(curl -s http://localhost:5678/health 2>/dev/null || echo "ERROR")
if [[ "$HEALTH_RESPONSE" == "ERROR" ]]; then
    echo "   ❌ 无法连接到后端健康检查端点"
    echo "   后端可能还在启动中，请稍等..."
    exit 1
else
    echo "   ✅ 后端健康检查通过"
fi

# 3. 检查是否有活跃的 session
echo ""
echo "3. 检查活跃的 session..."
SESSIONS=$(curl -s http://localhost:5678/api/sessions 2>/dev/null | jq -r '.sessions | length' || echo "0")
if [[ "$SESSIONS" == "0" ]]; then
    echo "   ⚠️  没有活跃的 session"
    echo "   需要先创建 session 并发送消息"
else
    echo "   ✅ 找到 $SESSIONS 个 session"
    curl -s http://localhost:5678/api/sessions | jq -r '.sessions[] | "   - SessionId: \(.sessionId), CreatedAt: \(.createdAt)"'
fi

# 4. 检查 DAG 状态
echo ""
echo "4. 检查 DAG 状态..."
DAG_RESPONSE=$(curl -s http://localhost:5678/api/dag/global 2>/dev/null || echo "ERROR")
if [[ "$DAG_RESPONSE" == "ERROR" ]]; then
    echo "   ❌ 无法获取 DAG 数据"
    exit 1
fi

NODE_COUNT=$(echo "$DAG_RESPONSE" | jq -r '.dag.nodes | length' || echo "0")
EDGE_COUNT=$(echo "$DAG_RESPONSE" | jq -r '.dag.edges | length' || echo "0")

echo "   DAG 节点数: $NODE_COUNT"
echo "   DAG 边数: $EDGE_COUNT"

if [[ "$NODE_COUNT" == "0" ]]; then
    echo ""
    echo "   ⚠️  DAG 为空（0 节点）"
    echo ""
    echo "   可能的原因："
    echo "   1. 还没有运行过 research round"
    echo "      → 解决方案：通过前端发送消息触发 research round"
    echo ""
    echo "   2. Research round 运行了但没有创建节点"
    echo "      → 检查日志中是否有以下消息："
    echo "        - [DagStore] ApplyMutationAsync starting"
    echo "        - [DagStore] Creating PlanNode"
    echo "        - [DagStore] Upserting KnowledgeNode"
    echo ""
    echo "   3. Neo4j 连接失败"
    echo "      → 检查 Neo4j 是否运行：neo4j status"
    echo "      → 检查环境变量：echo \$NEO4J_PASSWORD"
    echo ""
else
    echo "   ✅ DAG 有数据"
    echo ""
    echo "   前 5 个节点 ID："
    echo "$DAG_RESPONSE" | jq -r '.dag.nodes[0:5][] | "   - \(.id): \(.label // .type // "unknown")"' || echo "   无法解析节点信息"
fi

# 5. 检查最近的日志（如果存在）
echo ""
echo "5. 检查最近的日志..."
LOG_DIR="logs"
if [[ -d "$LOG_DIR" ]] && [[ -n "$(ls -A $LOG_DIR/*.log 2>/dev/null)" ]]; then
    echo "   最近的 DAG 相关日志："
    tail -n 50 "$LOG_DIR"/*.log 2>/dev/null | grep -E "(DagStore|ApplyMutation|PlanNode|KnowledgeNode)" | tail -n 10 || echo "   未找到相关日志"
else
    echo "   ⚠️  日志目录不存在或为空"
    echo "   日志可能输出到标准输出（stdout）"
fi

echo ""
echo "=========================================="
echo "诊断完成"
echo "=========================================="
