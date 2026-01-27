#!/bin/bash
# 检查 DAG 相关日志

echo "=========================================="
echo "DAG 节点创建日志检查"
echo "=========================================="
echo ""

BACKEND_URL="${BACKEND_URL:-http://localhost:5678}"

# 1. 检查后端是否运行
echo "1. 检查后端状态..."
if curl -s "$BACKEND_URL/health" >/dev/null 2>&1; then
    echo "   ✅ Backend 正在运行"
else
    echo "   ❌ Backend 未运行"
    echo "   请先运行: ./boot.sh"
    exit 1
fi

# 2. 检查 DAG API
echo ""
echo "2. 检查 DAG API..."
DAG_RESPONSE=$(curl -s "$BACKEND_URL/api/dag/global" 2>&1)
if echo "$DAG_RESPONSE" | jq -e '.dag.nodes | length' >/dev/null 2>&1; then
    NODE_COUNT=$(echo "$DAG_RESPONSE" | jq -r '.dag.nodes | length')
    EDGE_COUNT=$(echo "$DAG_RESPONSE" | jq -r '.dag.edges | length')
    echo "   ✅ DAG API 响应正常"
    echo "   📊 当前状态: nodes=$NODE_COUNT, edges=$EDGE_COUNT"
    
    if [ "$NODE_COUNT" = "0" ]; then
        echo "   ⚠️  DAG 为空（nodes=0）"
        echo ""
        echo "   可能的原因："
        echo "   1. 还没有运行过 research round"
        echo "   2. Research round 运行了但节点创建失败"
        echo "   3. Neo4j 连接问题"
        echo "   4. Dag_builder 输出为空或无法解析"
    else
        echo "   ✅ DAG 中有 $NODE_COUNT 个节点"
    fi
else
    echo "   ❌ DAG API 响应格式错误"
    echo "   响应: ${DAG_RESPONSE:0:200}"
fi

# 3. 检查是否有会话
echo ""
echo "3. 检查是否有会话..."
SESSIONS_RESPONSE=$(curl -s "$BACKEND_URL/api/sessions" 2>&1)
if echo "$SESSIONS_RESPONSE" | jq -e '.sessions | length' >/dev/null 2>&1; then
    SESSION_COUNT=$(echo "$SESSIONS_RESPONSE" | jq -r '.sessions | length')
    echo "   📊 会话数量: $SESSION_COUNT"
    
    if [ "$SESSION_COUNT" -gt 0 ]; then
        FIRST_SESSION=$(echo "$SESSIONS_RESPONSE" | jq -r '.sessions[0].sessionId')
        echo "   📝 第一个会话 ID: $FIRST_SESSION"
        
        # 检查该会话的 DAG
        SESSION_DAG=$(curl -s "$BACKEND_URL/api/sessions/$FIRST_SESSION/dag" 2>&1)
        if echo "$SESSION_DAG" | jq -e '.dag.nodes | length' >/dev/null 2>&1; then
            SESSION_NODES=$(echo "$SESSION_DAG" | jq -r '.dag.nodes | length')
            echo "   📊 会话 DAG 节点数: $SESSION_NODES"
        fi
    else
        echo "   ⚠️  没有会话，请先发送消息创建会话"
    fi
else
    echo "   ⚠️  无法获取会话列表"
fi

# 4. 检查 Neo4j 连接
echo ""
echo "4. 检查 Neo4j 连接..."
if nc -zv localhost 7687 2>&1 | grep -q "succeeded"; then
    echo "   ✅ Neo4j Bolt 端口可访问"
else
    echo "   ❌ Neo4j Bolt 端口不可访问"
    echo "   请运行: ./start_neo4j_with_java.sh"
fi

# 5. 提供诊断建议
echo ""
echo "=========================================="
echo "诊断建议"
echo "=========================================="
echo ""

if [ "$NODE_COUNT" = "0" ]; then
    echo "DAG 为空，请检查以下内容："
    echo ""
    echo "1. 📋 查看程序日志，查找以下关键消息："
    echo ""
    echo "   ✅ 正常流程应该看到："
    echo "      [VibeOrchestrator] Applying milestones DAG mutation"
    echo "      [DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X"
    echo "      [DagStore] Creating PlanNode: nodeId=..."
    echo "      [vibe.dag_builder] Step started"
    echo "      [DagConsensus] dag_builder output length: X"
    echo "      [DagConsensus] Parsed candidate: nodes=X, edges=Y"
    echo "      [DagStore] Upserting KnowledgeNode: nodeId=..."
    echo ""
    echo "   ❌ 错误情况会看到："
    echo "      [DagStore] Failed to query Neo4j for global DAG: dagId=global, error=..."
    echo "      [DagConsensus] Failed to parse dag_builder output"
    echo "      [DagConsensus] Parsed candidate is empty"
    echo "      [DagStore] Failed to upsert dag node"
    echo ""
    echo "2. 🔍 如果没有看到任何节点创建日志："
    echo "   - 可能还没有运行过 research round"
    echo "   - 请在前端发送一条消息触发 research round"
    echo ""
    echo "3. 🔍 如果看到 Neo4j 错误："
    echo "   - 检查 Neo4j 密码是否正确"
    echo "   - 检查 Neo4j 是否正在运行"
    echo ""
    echo "4. 🔍 如果看到 dag_builder 输出为空："
    echo "   - 检查 dag_builder 是否被执行"
    echo "   - 检查 LLM 调用是否成功"
    echo ""
else
    echo "✅ DAG 中有节点，但前端可能没有刷新"
    echo "   请在前端点击 'Refresh' 按钮刷新 DAG 图"
fi

echo ""
