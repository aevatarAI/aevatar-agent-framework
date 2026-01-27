#!/bin/bash
# 验证 Neo4j 连接和密码

set -e

echo "=========================================="
echo "Neo4j 连接验证"
echo "=========================================="
echo ""

PASSWORD="gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI"

echo "1. 测试 Bolt 连接..."
if echo "RETURN 1 as test;" | cypher-shell -u neo4j -p "$PASSWORD" -a bolt://localhost:7687 >/dev/null 2>&1; then
    echo "   ✅ Bolt 连接成功"
else
    echo "   ❌ Bolt 连接失败"
    echo "   错误信息："
    echo "RETURN 1 as test;" | cypher-shell -u neo4j -p "$PASSWORD" -a bolt://localhost:7687 2>&1 | head -3
    exit 1
fi

echo ""
echo "2. 测试查询..."
RESULT=$(echo "MATCH (n) RETURN count(n) as nodeCount;" | cypher-shell -u neo4j -p "$PASSWORD" -a bolt://localhost:7687 --format plain 2>/dev/null | tail -1)
if [[ -n "$RESULT" ]]; then
    echo "   ✅ 查询成功"
    echo "   当前节点数: $RESULT"
else
    echo "   ⚠️  查询失败或返回空结果"
fi

echo ""
echo "3. 检查环境变量..."
if [[ -n "$NEO4J_PASSWORD" ]]; then
    echo "   NEO4J_PASSWORD 已设置（长度: ${#NEO4J_PASSWORD}）"
    if [[ "$NEO4J_PASSWORD" == "$PASSWORD" ]]; then
        echo "   ✅ 环境变量中的密码与正确密码匹配"
    else
        echo "   ⚠️  环境变量中的密码与正确密码不匹配"
    fi
else
    echo "   ⚠️  NEO4J_PASSWORD 未设置"
fi

echo ""
echo "=========================================="
echo "✅ 验证完成"
echo "=========================================="
echo ""
echo "如果 Bolt 连接成功，说明密码正确。"
echo "如果后端仍有认证错误，请重启后端："
echo "  1. 停止当前后端进程"
echo "  2. 运行 ./boot.sh"
echo ""
