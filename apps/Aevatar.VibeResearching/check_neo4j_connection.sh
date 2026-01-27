#!/bin/bash
# Neo4j 连接诊断脚本

echo "=========================================="
echo "Neo4j 连接诊断"
echo "=========================================="
echo ""

# 1. 检查 HTTP 端口
echo "1. 检查 HTTP 端口 (7474)..."
HTTP_RESPONSE=$(curl -s http://localhost:7474 2>&1)
if echo "$HTTP_RESPONSE" | grep -q "neo4j_version"; then
    echo "   ✅ HTTP 端口可访问"
    echo "$HTTP_RESPONSE" | grep -o '"neo4j_version":"[^"]*"' | head -1
else
    echo "   ❌ HTTP 端口不可访问"
fi
echo ""

# 2. 检查 Bolt 端口
echo "2. 检查 Bolt 端口 (7687)..."
if command -v nc &> /dev/null; then
    if nc -zv localhost 7687 2>&1 | grep -q "succeeded"; then
        echo "   ✅ Bolt 端口可访问"
    else
        echo "   ❌ Bolt 端口不可访问"
        echo "   错误信息:"
        nc -zv localhost 7687 2>&1 | grep -v "^Connection"
    fi
else
    echo "   ⚠️  nc 命令不可用，跳过端口检查"
fi
echo ""

# 3. 检查环境变量
echo "3. 检查环境变量..."
echo "   NEO4J_URI: ${NEO4J_URI:-未设置}"
echo "   NEO4J_USERNAME: ${NEO4J_USERNAME:-未设置}"
echo "   NEO4J_PASSWORD: ${NEO4J_PASSWORD:+已设置（隐藏）}"
echo "   NEO4J_DATABASE: ${NEO4J_DATABASE:-未设置}"
echo ""

# 4. 检查 Neo4j 配置（如果通过 Homebrew 安装）
if command -v brew &> /dev/null; then
    NEO4J_HOME=$(brew --prefix neo4j 2>/dev/null)
    if [ -n "$NEO4J_HOME" ]; then
        echo "4. 检查 Neo4j 配置..."
        CONFIG_FILE="$NEO4J_HOME/libexec/conf/neo4j.conf"
        if [ -f "$CONFIG_FILE" ]; then
            echo "   配置文件: $CONFIG_FILE"
            echo "   Bolt 监听地址:"
            grep -E "^dbms\.connector\.bolt\.listen_address|^dbms\.connector\.bolt\.enabled" "$CONFIG_FILE" 2>/dev/null | head -2 || echo "   未找到 Bolt 配置"
        else
            echo "   配置文件未找到"
        fi
    fi
    echo ""
fi

# 5. 尝试使用 cypher-shell 连接（如果可用）
if command -v cypher-shell &> /dev/null; then
    echo "5. 尝试使用 cypher-shell 连接..."
    PASSWORD="${NEO4J_PASSWORD:-password}"
    if echo "RETURN 1;" | cypher-shell -u "${NEO4J_USERNAME:-neo4j}" -p "$PASSWORD" -a "${NEO4J_URI:-bolt://localhost:7687}" 2>&1 | grep -q "1"; then
        echo "   ✅ cypher-shell 连接成功"
    else
        echo "   ❌ cypher-shell 连接失败"
    fi
    echo ""
fi

echo "=========================================="
echo "诊断完成"
echo "=========================================="
echo ""
echo "如果 Bolt 端口不可访问，请检查："
echo "1. Neo4j 配置文件中的 Bolt 监听地址"
echo "2. 防火墙设置"
echo "3. Neo4j 服务是否完全启动"
echo ""
echo "修复建议："
echo "1. 检查 Neo4j 日志: brew services info neo4j"
echo "2. 重启 Neo4j: brew services restart neo4j"
echo "3. 检查配置文件中的 dbms.connector.bolt.listen_address"
echo ""
