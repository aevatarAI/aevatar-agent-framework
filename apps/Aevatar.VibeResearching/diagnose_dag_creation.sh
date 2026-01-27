#!/bin/bash
# 诊断 DAG 节点创建问题

set -e

echo "=========================================="
echo "DAG 节点创建诊断"
echo "=========================================="
echo ""

# 检查后端是否运行
if ! lsof -ti :5678 >/dev/null 2>&1; then
    echo "❌ 后端未运行，请先启动后端"
    exit 1
fi

echo "✅ 后端正在运行"
echo ""

# 获取最新的 session
LATEST_SESSION=$(curl -s http://localhost:5678/api/sessions | jq -r '.sessions[0].sessionId' 2>/dev/null || echo "")
if [[ -z "$LATEST_SESSION" ]]; then
    echo "❌ 无法获取 session"
    exit 1
fi

echo "📋 最新 Session: $LATEST_SESSION"
echo ""

# 检查 trace 中是否有 dag_builder
echo "1. 检查 dag_builder 是否执行..."
TRACE_RESPONSE=$(curl -s "http://localhost:5678/api/sessions/$LATEST_SESSION/trace" 2>/dev/null || echo "")
if [[ -z "$TRACE_RESPONSE" ]]; then
    echo "   ⚠️  无法获取 trace（可能还没有运行过 research round）"
else
    DAG_BUILDER_FOUND=$(echo "$TRACE_RESPONSE" | jq -r '.rounds[]?.perAgent[]? | select(.agent == "dag_builder") | .agent' 2>/dev/null | head -1 || echo "")
    if [[ -n "$DAG_BUILDER_FOUND" ]]; then
        echo "   ✅ dag_builder 已执行"
        echo ""
        echo "   dag_builder 输出摘要："
        echo "$TRACE_RESPONSE" | jq -r '.rounds[]?.perAgent[]? | select(.agent == "dag_builder") | .highlights[0:3][]' 2>/dev/null | head -5 | sed 's/^/      - /' || echo "      无输出"
    else
        echo "   ❌ dag_builder 未执行（可能不在 worker 列表中）"
    fi
fi

echo ""
echo "2. 检查后端日志中的关键消息..."
echo ""
echo "   请查看后端终端输出，查找以下关键日志："
echo ""
echo "   📌 dag_builder 执行："
echo "      - [vibe.dag_builder] Step started"
echo "      - [vibe.dag_builder] Step finished"
echo ""
echo "   📌 dag_builder 输出解析："
echo "      - [DagConsensus] dag_builder output length: X"
echo "      - [DagConsensus] Failed to parse dag_builder output"
echo "      - [DagConsensus] Parsed candidate: nodes=X, edges=Y"
echo "      - [DagConsensus] Parsed candidate is empty"
echo ""
echo "   📌 节点创建："
echo "      - [DagStore] ApplyMutationAsync starting: dagId=global, nodeCount=X"
echo "      - [DagStore] Creating PlanNode: nodeId=..."
echo "      - [DagStore] Upserting KnowledgeNode: nodeId=..."
echo ""
echo "   📌 错误消息："
echo "      - [DagStore] Failed to query Neo4j"
echo "      - [DagConsensus] dag consensus error"
echo ""

echo "3. 检查可能的失败原因..."
echo ""
echo "   如果看到 '[DagConsensus] Failed to parse dag_builder output'："
echo "   → dag_builder 输出格式错误或不是有效的 JSON"
echo ""
echo "   如果看到 '[DagConsensus] Parsed candidate is empty'："
echo "   → dag_builder 输出解析成功，但没有节点或边"
echo ""
echo "   如果看到 '[DagConsensus] dag consensus error'："
echo "   → 共识验证失败（maker 或 verifier-quorum 失败）"
echo ""
echo "   如果没有看到 '[DagStore] ApplyMutationAsync starting'："
echo "   → 共识验证未通过，节点未被创建"
echo ""

echo "4. 手动检查步骤..."
echo ""
echo "   请在后端终端输出中搜索以下关键词："
echo "   - 'dag_builder output length'"
echo "   - 'Failed to parse'"
echo "   - 'Parsed candidate'"
echo "   - 'ApplyMutationAsync'"
echo "   - 'Upserting KnowledgeNode'"
echo ""

echo "=========================================="
echo "诊断完成"
echo "=========================================="
echo ""
echo "💡 建议："
echo "   1. 查看后端终端输出，查找上述关键日志"
echo "   2. 如果 dag_builder 输出为空，检查 dag_builder 是否在 plan 的 workers 列表中"
echo "   3. 如果解析失败，检查 dag_builder 的输出格式是否符合 JSON 要求"
echo "   4. 如果共识失败，检查 DagConsensus 配置和日志"
