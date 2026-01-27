#!/bin/bash
# 验证 boot.sh 配置是否正确

echo "=========================================="
echo "验证 boot.sh 配置"
echo "=========================================="
echo ""

# 1. 检查 boot.sh 中的 Neo4j 配置
echo "1. 检查 boot.sh 中的 Neo4j 配置..."
if grep -q "NEO4J_URI" boot.sh; then
    echo "   ✅ NEO4J_URI 已配置"
    NEO4J_URI_LINE=$(grep "NEO4J_URI" boot.sh | head -1)
    echo "      配置: $NEO4J_URI_LINE"
else
    echo "   ❌ NEO4J_URI 未配置"
fi

if grep -q "NEO4J_USERNAME" boot.sh; then
    echo "   ✅ NEO4J_USERNAME 已配置"
    NEO4J_USERNAME_LINE=$(grep "NEO4J_USERNAME" boot.sh | head -1)
    echo "      配置: $NEO4J_USERNAME_LINE"
else
    echo "   ❌ NEO4J_USERNAME 未配置"
fi

if grep -q "NEO4J_PASSWORD" boot.sh; then
    echo "   ✅ NEO4J_PASSWORD 已配置"
    NEO4J_PASSWORD_LINE=$(grep "NEO4J_PASSWORD" boot.sh | head -1)
    echo "      配置: $NEO4J_PASSWORD_LINE"
    
    # 检查是否是默认密码
    if echo "$NEO4J_PASSWORD_LINE" | grep -q 'password.*#'; then
        echo "   ⚠️  警告: 使用的是默认密码 'password'"
        echo "      请确保这是你的实际 Neo4j 密码，或修改为实际密码"
    fi
else
    echo "   ❌ NEO4J_PASSWORD 未配置"
fi

# 2. 检查 Java 环境配置
echo ""
echo "2. 检查 Java 环境配置..."
if grep -q "JAVA_HOME" boot.sh; then
    echo "   ✅ JAVA_HOME 配置已添加"
    JAVA_CONFIG=$(grep -A 5 "Java Environment" boot.sh | head -6)
    echo "      配置片段:"
    echo "$JAVA_CONFIG" | sed 's/^/      /'
else
    echo "   ❌ JAVA_HOME 配置未添加"
fi

# 3. 检查 Neo4j 服务状态
echo ""
echo "3. 检查 Neo4j 服务状态..."
if nc -zv localhost 7687 2>&1 | grep -q "succeeded"; then
    echo "   ✅ Neo4j Bolt 端口 (7687) 可访问"
else
    echo "   ❌ Neo4j Bolt 端口 (7687) 不可访问"
    echo "      请运行: ./start_neo4j_with_java.sh"
fi

# 4. 检查环境变量（如果已设置）
echo ""
echo "4. 检查当前环境变量..."
if [ -n "${NEO4J_URI:-}" ]; then
    echo "   NEO4J_URI: $NEO4J_URI"
else
    echo "   NEO4J_URI: 未设置（将使用 boot.sh 中的默认值）"
fi

if [ -n "${NEO4J_USERNAME:-}" ]; then
    echo "   NEO4J_USERNAME: $NEO4J_USERNAME"
else
    echo "   NEO4J_USERNAME: 未设置（将使用 boot.sh 中的默认值）"
fi

if [ -n "${NEO4J_PASSWORD:-}" ]; then
    echo "   NEO4J_PASSWORD: 已设置（长度: ${#NEO4J_PASSWORD}）"
else
    echo "   NEO4J_PASSWORD: 未设置（将使用 boot.sh 中的默认值 'password'）"
fi

# 5. 检查语法
echo ""
echo "5. 检查 boot.sh 语法..."
if bash -n boot.sh 2>&1; then
    echo "   ✅ boot.sh 语法正确"
else
    echo "   ❌ boot.sh 语法错误"
    bash -n boot.sh 2>&1 | head -5
fi

# 6. 总结
echo ""
echo "=========================================="
echo "配置检查总结"
echo "=========================================="
echo ""

# 检查关键配置
MISSING_CONFIG=0

if ! grep -q "NEO4J_URI" boot.sh; then
    echo "❌ 缺少 NEO4J_URI 配置"
    MISSING_CONFIG=1
fi

if ! grep -q "NEO4J_USERNAME" boot.sh; then
    echo "❌ 缺少 NEO4J_USERNAME 配置"
    MISSING_CONFIG=1
fi

if ! grep -q "NEO4J_PASSWORD" boot.sh; then
    echo "❌ 缺少 NEO4J_PASSWORD 配置"
    MISSING_CONFIG=1
fi

if ! grep -q "JAVA_HOME" boot.sh; then
    echo "❌ 缺少 JAVA_HOME 配置"
    MISSING_CONFIG=1
fi

if [ $MISSING_CONFIG -eq 0 ]; then
    echo "✅ 所有必需配置都已添加"
    echo ""
    echo "⚠️  重要提醒："
    echo "   1. 确保 Neo4j 正在运行: ./start_neo4j_with_java.sh"
    echo "   2. 修改 boot.sh 第 144 行，将 'password' 替换为你的实际 Neo4j 密码"
    echo "   3. 或者，在运行前设置: export NEO4J_PASSWORD='your_password'"
    echo ""
    echo "✅ 配置完成后，运行 ./boot.sh 应该可以正常启动"
    echo "✅ 发送消息触发 research round 后，DAG 图应该会显示"
else
    echo "❌ 配置不完整，请检查上述缺失项"
fi

echo ""
