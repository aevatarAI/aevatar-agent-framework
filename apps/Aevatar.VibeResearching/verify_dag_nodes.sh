#!/bin/bash
# 验证 DAG 节点是否已创建

echo "=========================================="
echo "验证 DAG 节点创建"
echo "=========================================="
echo ""

# 1. 检查 API 返回
echo "1. 检查 API 返回的节点数量..."
NODE_COUNT=$(curl -s http://localhost:5173/api/dag/global | jq -r '.dag.nodes | length' 2>/dev/null || echo "0")
echo "   API 返回节点数: $NODE_COUNT"
echo ""

# 2. 检查后端日志（如果可用）
echo "2. 检查后端日志中的节点创建记录..."
echo "   请查看程序日志中是否有以下消息："
echo "   - [VibeOrchestrator] Applying milestones DAG mutation"
echo "   - [DagStore] ApplyMutationAsync starting"
echo "   - [DagStore] Creating PlanNode"
echo "   - [DagStore] PlanNode created successfully"
echo "   - [DagStore] Upserting KnowledgeNode"
echo "   - [DagStore] KnowledgeNode upserted successfully"
echo "   - [DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=X, planNodes=Y"
echo ""

# 3. 检查是否有 research round 运行过
echo "3. 检查是否有 research round 运行过..."
echo "   请确认："
echo "   - 是否发送了消息触发 research round？"
echo "   - Brief generation 是否完成？"
echo "   - Dag_builder 是否执行？"
echo ""

# 4. 检查 Neo4j 连接
echo "4. 检查 Neo4j 连接..."
if nc -zv localhost 7687 2>&1 | grep -q "succeeded"; then
    echo "   ✅ Neo4j Bolt 端口可访问"
else
    echo "   ❌ Neo4j Bolt 端口不可访问"
    echo "   请运行: ./start_neo4j_with_java.sh"
fi
echo ""

# 5. 检查环境变量
echo "5. 检查环境变量..."
echo "   NEO4J_URI: ${NEO4J_URI:-未设置}"
echo "   NEO4J_USERNAME: ${NEO4J_USERNAME:-未设置}"
echo "   NEO4J_PASSWORD: ${NEO4J_PASSWORD:+已设置}"
echo ""

if [ "$NODE_COUNT" = "0" ]; then
    echo "=========================================="
    echo "诊断结果：DAG 为空"
    echo "=========================================="
    echo ""
    echo "可能的原因："
    echo "1. 还没有运行过 research round"
    echo "2. Research round 运行了但节点创建失败"
    echo "3. Neo4j 连接问题（虽然端口可访问）"
    echo ""
    echo "解决步骤："
    echo "1. 确保 Neo4j 正在运行: ./start_neo4j_with_java.sh"
    echo "2. 运行程序并发送一个消息触发 research round"
    echo "3. 查看后端日志确认节点创建成功"
    echo "4. 等待几秒后再次检查 API"
    echo ""
else
    echo "=========================================="
    echo "✅ DAG 中有 $NODE_COUNT 个节点"
    echo "=========================================="
    echo ""
    echo "如果前端仍然不显示，请检查："
    echo "1. 前端是否正确连接到后端 API"
    echo "2. 浏览器控制台是否有错误"
    echo "3. 前端 DAG 组件是否正确渲染"
fi
