#!/bin/bash
# DAG 图显示问题完整诊断脚本

echo "=========================================="
echo "DAG 图显示问题完整诊断"
echo "=========================================="
echo ""

# 1. 检查 Neo4j 服务状态
echo "1. 检查 Neo4j 服务状态..."
if nc -zv localhost 7687 2>&1 | grep -q "succeeded"; then
    echo "   ✅ Neo4j Bolt 端口 (7687) 可访问"
else
    echo "   ❌ Neo4j Bolt 端口 (7687) 不可访问"
    echo "   请运行: ./start_neo4j_with_java.sh"
    echo ""
fi

if curl -s http://localhost:7474 >/dev/null 2>&1; then
    echo "   ✅ Neo4j HTTP 端口 (7474) 可访问"
else
    echo "   ❌ Neo4j HTTP 端口 (7474) 不可访问"
    echo ""
fi

# 2. 检查环境变量
echo ""
echo "2. 检查 Neo4j 环境变量..."
if [ -z "$NEO4J_URI" ]; then
    echo "   ⚠️  NEO4J_URI 未设置（将使用默认值: bolt://localhost:7687）"
else
    echo "   ✅ NEO4J_URI=$NEO4J_URI"
fi

if [ -z "$NEO4J_USERNAME" ]; then
    echo "   ⚠️  NEO4J_USERNAME 未设置（将使用默认值: neo4j）"
else
    echo "   ✅ NEO4J_USERNAME=$NEO4J_USERNAME"
fi

if [ -z "$NEO4J_PASSWORD" ]; then
    echo "   ❌ NEO4J_PASSWORD 未设置（必需！）"
    echo "   请设置: export NEO4J_PASSWORD='your_password'"
else
    echo "   ✅ NEO4J_PASSWORD 已设置"
fi

# 3. 检查 Java 环境
echo ""
echo "3. 检查 Java 环境..."
JAVA_HOME=$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home 2>/dev/null
if [ -d "$JAVA_HOME" ]; then
    export JAVA_HOME="$JAVA_HOME"
    export PATH="$JAVA_HOME/bin:$PATH"
    if java -version 2>&1 | grep -q "version"; then
        echo "   ✅ Java 已配置: $(java -version 2>&1 | head -1)"
    else
        echo "   ❌ Java 未正确配置"
    fi
else
    echo "   ⚠️  Java 21 未找到，请安装: brew install openjdk@21"
fi

# 4. 检查 API 端点
echo ""
echo "4. 检查 API 端点..."
BACKEND_URL="${BACKEND_URL:-http://localhost:5678}"
if curl -s "$BACKEND_URL/health" >/dev/null 2>&1; then
    echo "   ✅ Backend 健康检查通过: $BACKEND_URL/health"
    
    # 检查 DAG API
    DAG_RESPONSE=$(curl -s "$BACKEND_URL/api/dag/global" 2>&1)
    if echo "$DAG_RESPONSE" | jq -e '.dag.nodes | length' >/dev/null 2>&1; then
        NODE_COUNT=$(echo "$DAG_RESPONSE" | jq -r '.dag.nodes | length')
        EDGE_COUNT=$(echo "$DAG_RESPONSE" | jq -r '.dag.edges | length')
        echo "   ✅ DAG API 响应正常: nodes=$NODE_COUNT, edges=$EDGE_COUNT"
        
        if [ "$NODE_COUNT" = "0" ]; then
            echo "   ⚠️  DAG 为空（nodes=0）- 这是正常的，如果还没有运行过 research round"
        fi
    else
        echo "   ❌ DAG API 响应格式错误或无法解析"
        echo "   响应: ${DAG_RESPONSE:0:200}"
    fi
else
    echo "   ❌ Backend 未运行或无法访问: $BACKEND_URL"
    echo "   请确保后端已启动"
fi

# 5. 检查程序日志关键消息
echo ""
echo "5. 检查程序日志关键消息..."
echo "   请查看程序日志中是否有以下消息："
echo ""
echo "   ✅ Brief Generation:"
echo "      [VibeOrchestrator] Applying milestones DAG mutation"
echo "      [DagStore] ApplyMutationAsync starting"
echo "      [DagStore] Creating PlanNode"
echo ""
echo "   ✅ Dag Builder:"
echo "      [vibe.dag_builder] Step started"
echo "      [DagConsensus] dag_builder output length: X"
echo "      [DagConsensus] Parsed candidate: nodes=X, edges=Y"
echo ""
echo "   ✅ DAG Consensus:"
echo "      [vibe.dag_consensus] Step started"
echo "      [DagStore] ApplyMutationAsync starting"
echo "      [DagStore] Upserting KnowledgeNode"
echo ""
echo "   ❌ 错误消息:"
echo "      [DagStore] Failed to query Neo4j for global DAG"
echo "      [DagConsensus] Failed to parse dag_builder output"
echo "      [DagConsensus] Parsed candidate is empty"
echo "      [DagStore] Failed to upsert dag node"

# 6. 检查是否有 research round 运行过
echo ""
echo "6. 检查是否有 research round 运行过..."
echo "   如果 DAG 为空（nodes=0），可能的原因："
echo "   - 还没有运行过 research round"
echo "   - Research round 运行了但节点创建失败"
echo "   - Neo4j 连接问题"
echo ""
echo "   解决方案："
echo "   1. 确保 Neo4j 正在运行: ./start_neo4j_with_java.sh"
echo "   2. 设置环境变量:"
echo "      export NEO4J_URI='bolt://localhost:7687'"
echo "      export NEO4J_USERNAME='neo4j'"
echo "      export NEO4J_PASSWORD='your_password'"
echo "   3. 运行程序并发送一个消息触发 research round"
echo "   4. 查看日志确认节点创建成功"

# 7. 检查 boot.sh 是否设置了环境变量
echo ""
echo "7. 检查 boot.sh 配置..."
if grep -q "NEO4J" boot.sh 2>/dev/null; then
    echo "   ⚠️  boot.sh 中没有设置 Neo4j 环境变量"
    echo "   建议在 boot.sh 中添加："
    echo "   export NEO4J_URI='bolt://localhost:7687'"
    echo "   export NEO4J_USERNAME='neo4j'"
    echo "   export NEO4J_PASSWORD='your_password'"
else
    echo "   ⚠️  boot.sh 中没有设置 Neo4j 环境变量"
fi

echo ""
echo "=========================================="
echo "诊断完成"
echo "=========================================="
echo ""
echo "下一步操作："
echo "1. 如果 Neo4j 未运行，请先启动: ./start_neo4j_with_java.sh"
echo "2. 设置环境变量（或修改 boot.sh 添加环境变量）"
echo "3. 重新运行程序"
echo "4. 发送消息触发 research round"
echo "5. 查看日志确认节点创建"
echo ""
