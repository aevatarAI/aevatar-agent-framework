#!/bin/bash
# DAG 诊断脚本 - 检查 DAG 图为什么没有显示

echo "=========================================="
echo "DAG 诊断脚本"
echo "=========================================="
echo ""

# 1. 检查 API 端点是否可访问
echo "1. 检查 DAG API 端点..."
echo ""

# 获取第一个 session ID
SESSION_ID=$(curl -s http://localhost:5173/api/sessions | jq -r '.sessions[0].sessionId // empty' 2>/dev/null)

if [ -z "$SESSION_ID" ]; then
    echo "❌ 无法获取 session ID"
    echo "   请确保后端服务正在运行，并且至少有一个 session"
    echo ""
else
    echo "✅ 找到 session ID: $SESSION_ID"
    echo ""
    
    # 2. 检查 Global DAG API
    echo "2. 检查 Global DAG API (/api/dag/global)..."
    GLOBAL_DAG=$(curl -s http://localhost:5173/api/dag/global 2>/dev/null)
    if [ $? -eq 0 ]; then
        NODE_COUNT=$(echo "$GLOBAL_DAG" | jq -r '.dag.nodes | length' 2>/dev/null || echo "0")
        EDGE_COUNT=$(echo "$GLOBAL_DAG" | jq -r '.dag.edges | length' 2>/dev/null || echo "0")
        echo "   Global DAG: nodes=$NODE_COUNT, edges=$EDGE_COUNT"
        if [ "$NODE_COUNT" = "0" ]; then
            echo "   ⚠️  Global DAG 没有节点"
        fi
    else
        echo "   ❌ 无法访问 Global DAG API"
    fi
    echo ""
    
    # 3. 检查 Session DAG API
    echo "3. 检查 Session DAG API (/api/sessions/$SESSION_ID/dag)..."
    SESSION_DAG=$(curl -s "http://localhost:5173/api/sessions/$SESSION_ID/dag" 2>/dev/null)
    if [ $? -eq 0 ]; then
        SESSION_NODE_COUNT=$(echo "$SESSION_DAG" | jq -r '.dag.nodes | length' 2>/dev/null || echo "0")
        SESSION_EDGE_COUNT=$(echo "$SESSION_DAG" | jq -r '.dag.edges | length' 2>/dev/null || echo "0")
        DAG_ID=$(echo "$SESSION_DAG" | jq -r '.dagId // empty' 2>/dev/null)
        echo "   Session DAG: nodes=$SESSION_NODE_COUNT, edges=$SESSION_EDGE_COUNT, dagId=$DAG_ID"
        if [ "$SESSION_NODE_COUNT" = "0" ]; then
            echo "   ⚠️  Session DAG 没有节点"
        fi
    else
        echo "   ❌ 无法访问 Session DAG API"
    fi
    echo ""
    
    # 4. 检查 Neo4j 环境变量
    echo "4. 检查 Neo4j 配置..."
    if [ -z "$NEO4J_URI" ]; then
        echo "   ⚠️  NEO4J_URI 未设置"
    else
        echo "   ✅ NEO4J_URI=$NEO4J_URI"
    fi
    
    if [ -z "$NEO4J_USERNAME" ]; then
        echo "   ⚠️  NEO4J_USERNAME 未设置"
    else
        echo "   ✅ NEO4J_USERNAME=$NEO4J_USERNAME"
    fi
    
    if [ -z "$NEO4J_PASSWORD" ]; then
        echo "   ⚠️  NEO4J_PASSWORD 未设置"
    else
        echo "   ✅ NEO4J_PASSWORD=***"
    fi
    
    if [ -z "$NEO4J_DATABASE" ]; then
        echo "   ⚠️  NEO4J_DATABASE 未设置（将使用默认值）"
    else
        echo "   ✅ NEO4J_DATABASE=$NEO4J_DATABASE"
    fi
    echo ""
    
    # 5. 检查后端日志中的 DAG 相关日志
    echo "5. 检查后端日志（如果可用）..."
    echo "   请查看后端日志中是否有以下日志："
    echo "   - [DagStore] BuildSnapshotFromGraphAsync"
    echo "   - [DagStore] GetSnapshotForListAsync"
    echo "   - KnowledgeGraph 连接错误"
    echo ""
    
    # 6. 检查是否有 DAG snapshot 文件
    echo "6. 检查 DAG snapshot 文件..."
    WORKSPACE_ROOT=$(find apps/Aevatar.VibeResearching -type d -name "workspace" 2>/dev/null | head -1)
    if [ -n "$WORKSPACE_ROOT" ]; then
        SNAPSHOT_FILES=$(find "$WORKSPACE_ROOT" -name "snapshot.json" -type f 2>/dev/null)
        if [ -n "$SNAPSHOT_FILES" ]; then
            echo "   ✅ 找到 snapshot 文件："
            echo "$SNAPSHOT_FILES" | while read -r file; do
                NODE_COUNT=$(jq -r '.nodes | length' "$file" 2>/dev/null || echo "0")
                EDGE_COUNT=$(jq -r '.edges | length' "$file" 2>/dev/null || echo "0")
                echo "      $file: nodes=$NODE_COUNT, edges=$EDGE_COUNT"
            done
        else
            echo "   ⚠️  未找到 snapshot.json 文件"
        fi
    else
        echo "   ⚠️  未找到 workspace 目录"
    fi
    echo ""
fi

echo "=========================================="
echo "诊断完成"
echo "=========================================="
echo ""
echo "可能的原因："
echo "1. KnowledgeGraph (Neo4j) 未连接或配置错误"
echo "2. DAG 节点尚未创建（需要运行 research round）"
echo "3. API 端点返回空数据"
echo "4. 前端无法访问后端 API"
echo ""
echo "建议的修复步骤："
echo "1. 确保 Neo4j 环境变量已正确设置"
echo "2. 运行一个 research round 以创建 DAG 节点"
echo "3. 检查后端日志中的错误信息"
echo "4. 验证前端可以访问后端 API（检查 CORS 配置）"
