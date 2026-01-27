#!/bin/bash
# 查找 DAG 相关问题的脚本

echo "=========================================="
echo "DAG 问题诊断"
echo "=========================================="
echo ""

echo "当前状态："
echo "  - Backend: ✅ 运行中"
echo "  - Neo4j: ✅ 可访问"
echo "  - DAG 节点数: 0 ⚠️"
echo "  - 会话数: 13"
echo ""

echo "=========================================="
echo "请检查程序日志中的以下关键消息："
echo "=========================================="
echo ""

echo "1. 🔍 Neo4j 连接错误（如果看到，说明密码错误）："
echo "   [DagStore] Failed to query Neo4j for global DAG: dagId=global, error=..."
echo ""

echo "2. 🔍 Brief Generation（Plan 节点创建）："
echo "   [VibeOrchestrator] Applying milestones DAG mutation: sessionId=..., nodeCount=X"
echo "   [DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X"
echo "   [DagStore] Creating PlanNode: nodeId=..., label=..."
echo ""

echo "3. 🔍 Dag Builder（Knowledge 节点创建）："
echo "   [vibe.dag_builder] Step started"
echo "   [DagConsensus] dag_builder output length: X, hasValue: true/false"
echo "   [DagConsensus] Parsed candidate: nodes=X, edges=Y"
echo "   [DagConsensus] Failed to parse dag_builder output  ← 如果看到这个，说明解析失败"
echo "   [DagConsensus] Parsed candidate is empty  ← 如果看到这个，说明没有节点"
echo ""

echo "4. 🔍 DAG Consensus（应用节点）："
echo "   [vibe.dag_consensus] Step started"
echo "   [DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X"
echo "   [DagStore] Upserting KnowledgeNode: nodeId=..., label=..., type=..."
echo "   [DagStore] Failed to upsert dag node  ← 如果看到这个，说明节点创建失败"
echo ""

echo "5. 🔍 DAG 查询（确认节点存在）："
echo "   [DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=X, planNodes=Y, edges=Z"
echo ""

echo "=========================================="
echo "下一步操作："
echo "=========================================="
echo ""

echo "请分享程序日志中与以下相关的所有消息："
echo "  - [DagStore]"
echo "  - [DagConsensus]"
echo "  - [VibeOrchestrator] Applying milestones"
echo "  - [vibe.dag_builder]"
echo "  - [vibe.dag_consensus]"
echo ""

echo "或者，如果你还没有运行过 research round："
echo "  1. 在前端发送一条消息触发 research round"
echo "  2. 等待 research round 完成"
echo "  3. 再次检查: curl http://localhost:5678/api/dag/global | jq '.dag.nodes | length'"
echo ""
