#!/bin/bash
# 使用正确的 Java 环境启动 Neo4j

echo "=========================================="
echo "启动 Neo4j（使用 Java 21）"
echo "=========================================="
echo ""

# 设置 Java 环境变量
JAVA_HOME=$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home
if [ ! -d "$JAVA_HOME" ]; then
    echo "❌ Java 21 未找到，请先安装:"
    echo "   brew install openjdk@21"
    exit 1
fi

export JAVA_HOME="$JAVA_HOME"
export PATH="$JAVA_HOME/bin:$PATH"

echo "✅ Java 环境:"
echo "   JAVA_HOME: $JAVA_HOME"
java -version 2>&1 | head -1
echo ""

# 检查 Neo4j 状态
NEO4J_HOME=$(brew --prefix neo4j)
cd "$NEO4J_HOME/libexec"

echo "检查 Neo4j 状态..."
if ./bin/neo4j status 2>&1 | grep -q "running"; then
    echo "✅ Neo4j 已在运行"
else
    echo "启动 Neo4j..."
    ./bin/neo4j start
    echo ""
    echo "⏳ 等待 Neo4j 启动（约 30 秒）..."
    sleep 30
fi

echo ""
echo "验证连接..."
if nc -zv localhost 7687 2>&1 | grep -q "succeeded"; then
    echo "✅ Bolt 端口 (7687) 可访问"
    echo ""
    echo "Neo4j Browser: http://localhost:7474"
    echo "Bolt URI: bolt://localhost:7687"
else
    echo "❌ Bolt 端口无法访问"
    echo "请检查 Neo4j 日志: tail -f $NEO4J_HOME/libexec/logs/neo4j.log"
fi

echo ""
echo "=========================================="
echo "提示：要永久设置 Java 环境变量，请添加到 ~/.zshrc:"
echo "export JAVA_HOME=\"$JAVA_HOME\""
echo "export PATH=\"\$JAVA_HOME/bin:\$PATH\""
echo "=========================================="
echo ""
