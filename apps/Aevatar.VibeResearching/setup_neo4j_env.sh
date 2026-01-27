#!/bin/bash
# 设置 Neo4j 所需的环境变量

echo "=========================================="
echo "设置 Neo4j 环境变量"
echo "=========================================="
echo ""

# 查找 Java
JAVA_HOME=""
if command -v brew &> /dev/null; then
    # 尝试 openjdk@21
    if [ -d "$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home" ]; then
        JAVA_HOME="$(brew --prefix openjdk@21)/libexec/openjdk.jdk/Contents/Home"
    # 尝试 openjdk@17
    elif [ -d "$(brew --prefix openjdk@17)/libexec/openjdk.jdk/Contents/Home" ]; then
        JAVA_HOME="$(brew --prefix openjdk@17)/libexec/openjdk.jdk/Contents/Home"
    fi
fi

if [ -z "$JAVA_HOME" ] || [ ! -d "$JAVA_HOME" ]; then
    echo "❌ 未找到 Java，请先安装:"
    echo "   brew install openjdk@21"
    exit 1
fi

echo "✅ 找到 Java: $JAVA_HOME"
echo ""

# 设置环境变量
export JAVA_HOME="$JAVA_HOME"
export PATH="$JAVA_HOME/bin:$PATH"

echo "验证 Java:"
java -version 2>&1 | head -1
echo ""

# 设置 Neo4j 环境变量（如果未设置）
if [ -z "$NEO4J_URI" ]; then
    export NEO4J_URI="bolt://localhost:7687"
    echo "设置 NEO4J_URI=bolt://localhost:7687"
fi

if [ -z "$NEO4J_USERNAME" ]; then
    export NEO4J_USERNAME="neo4j"
    echo "设置 NEO4J_USERNAME=neo4j"
fi

if [ -z "$NEO4J_PASSWORD" ]; then
    export NEO4J_PASSWORD="password"
    echo "设置 NEO4J_PASSWORD=password"
    echo "⚠️  请确保这是你的 Neo4j 密码"
fi

if [ -z "$NEO4J_DATABASE" ]; then
    export NEO4J_DATABASE="neo4j"
    echo "设置 NEO4J_DATABASE=neo4j"
fi

echo ""
echo "=========================================="
echo "环境变量已设置"
echo "=========================================="
echo ""
echo "要永久设置这些变量，请添加到 ~/.zshrc:"
echo ""
echo "export JAVA_HOME=\"$JAVA_HOME\""
echo "export PATH=\"\$JAVA_HOME/bin:\$PATH\""
echo "export NEO4J_URI=\"bolt://localhost:7687\""
echo "export NEO4J_USERNAME=\"neo4j\""
echo "export NEO4J_PASSWORD=\"password\"  # 替换为你的密码"
echo "export NEO4J_DATABASE=\"neo4j\""
echo ""
echo "然后运行: source ~/.zshrc"
echo ""
