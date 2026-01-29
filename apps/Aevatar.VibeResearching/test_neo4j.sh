#!/bin/bash
# Neo4j 快速测试脚本

# 不使用 set -e，允许部分测试失败

echo "=========================================="
echo "Neo4j 运行状态测试"
echo "=========================================="
echo ""

# 1. 检查服务状态
echo "1. 检查 Neo4j 服务状态..."
if neo4j status >/dev/null 2>&1; then
    STATUS=$(neo4j status 2>&1)
    echo "   ✅ $STATUS"
else
    echo "   ❌ Neo4j 未运行"
    echo "   请运行: neo4j start"
    exit 1
fi
echo ""

# 2. 检查端口监听
echo "2. 检查端口监听..."
if lsof -i :7687 >/dev/null 2>&1; then
    echo "   ✅ Bolt 端口 (7687) 正在监听"
    lsof -i :7687 | grep LISTEN | head -1 | awk '{print "      PID: " $2 ", 用户: " $3}'
else
    echo "   ❌ Bolt 端口 (7687) 未监听"
fi

if lsof -i :7474 >/dev/null 2>&1; then
    echo "   ✅ HTTP 端口 (7474) 正在监听"
else
    echo "   ⚠️  HTTP 端口 (7474) 未监听（可能正常，取决于配置）"
fi
echo ""

# 3. 测试 HTTP API
echo "3. 测试 HTTP API..."
HTTP_RESPONSE=$(curl -s http://localhost:7474 2>&1)
if echo "$HTTP_RESPONSE" | grep -q "neo4j_version"; then
    VERSION=$(echo "$HTTP_RESPONSE" | grep -o '"neo4j_version":"[^"]*"' | head -1)
    echo "   ✅ HTTP API 可访问"
    echo "      $VERSION"
else
    echo "   ⚠️  HTTP API 不可访问（可能正常，取决于配置）"
fi
echo ""

# 4. 测试连接（使用 boot.sh 中的密码）
echo "4. 测试 Bolt 连接..."
PASSWORD="${NEO4J_PASSWORD:-gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI}"

# 尝试使用 cypher-shell（如果可用）
if command -v cypher-shell >/dev/null 2>&1; then
    if echo "RETURN 1 as test;" | cypher-shell -u neo4j -p "$PASSWORD" -a bolt://localhost:7687 >/dev/null 2>&1; then
        echo "   ✅ Bolt 连接成功"
        
        # 查询节点数
        NODE_COUNT=$(echo "MATCH (n) RETURN count(n) as nodeCount;" | \
            cypher-shell -u neo4j -p "$PASSWORD" -a bolt://localhost:7687 --format plain 2>/dev/null | tail -1)
        if [[ -n "$NODE_COUNT" ]]; then
            echo "      当前节点数: $NODE_COUNT"
        fi
    else
        echo "   ❌ Bolt 连接失败（可能是密码错误）"
        echo "      错误信息:"
        echo "RETURN 1 as test;" | cypher-shell -u neo4j -p "$PASSWORD" -a bolt://localhost:7687 2>&1 | head -3
    fi
else
    echo "   ⚠️  cypher-shell 不可用，跳过连接测试"
fi
echo ""

# 5. 检查环境变量
echo "5. 检查环境变量..."
echo "   NEO4J_URI: ${NEO4J_URI:-未设置 (默认: bolt://localhost:7687)}"
echo "   NEO4J_USERNAME: ${NEO4J_USERNAME:-未设置 (默认: neo4j)}"
if [[ -n "$NEO4J_PASSWORD" ]]; then
    echo "   NEO4J_PASSWORD: 已设置（长度: ${#NEO4J_PASSWORD}）"
else
    echo "   NEO4J_PASSWORD: 未设置（使用 boot.sh 中的默认值）"
fi
echo "   NEO4J_DATABASE: ${NEO4J_DATABASE:-未设置 (默认: neo4j)}"
echo ""

# 6. 总结
echo "=========================================="
echo "测试总结"
echo "=========================================="
echo ""

if neo4j status >/dev/null 2>&1 && lsof -i :7687 >/dev/null 2>&1; then
    echo "✅ Neo4j 服务正在运行"
    echo ""
    echo "下一步操作:"
    echo "1. 如果连接测试失败，检查密码是否正确"
    echo "2. 使用 Neo4j Browser 测试: http://localhost:7474"
    echo "3. 运行后端应用: ./boot.sh"
    echo ""
    echo "详细测试指南请查看: TEST_NEO4J.md"
else
    echo "❌ Neo4j 未正常运行"
    echo ""
    echo "请检查:"
    echo "1. 运行: neo4j start"
    echo "2. 等待 10-30 秒后再次运行此脚本"
    echo "3. 查看日志: brew services info neo4j"
fi
echo ""
