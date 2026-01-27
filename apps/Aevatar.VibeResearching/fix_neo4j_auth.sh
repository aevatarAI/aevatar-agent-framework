#!/bin/bash
# 修复 Neo4j 认证问题

set -e

echo "=========================================="
echo "Neo4j 认证问题修复脚本"
echo "=========================================="
echo ""

# 检查 Neo4j 是否运行
echo "1. 检查 Neo4j 状态..."
if command -v neo4j >/dev/null 2>&1; then
    neo4j status || echo "   ⚠️  Neo4j 未运行"
else
    echo "   ⚠️  neo4j 命令不可用（可能通过 Docker 运行）"
fi

echo ""
echo "2. 检查当前环境变量..."
echo "   NEO4J_URI: ${NEO4J_URI:-未设置}"
echo "   NEO4J_USERNAME: ${NEO4J_USERNAME:-未设置}"
echo "   NEO4J_PASSWORD: ${NEO4J_PASSWORD:+已设置（长度: ${#NEO4J_PASSWORD}）}${NEO4J_PASSWORD:-未设置}"

echo ""
echo "3. 检查 boot.sh 中的配置..."
BOOT_PASSWORD=$(grep "NEO4J_PASSWORD=" boot.sh | head -1 | sed 's/.*NEO4J_PASSWORD="${NEO4J_PASSWORD:-\(.*\)}".*/\1/' || echo "")
if [[ -n "$BOOT_PASSWORD" ]]; then
    echo "   boot.sh 中的默认密码: $BOOT_PASSWORD"
else
    echo "   ⚠️  无法从 boot.sh 中提取密码"
fi

echo ""
echo "=========================================="
echo "解决方案"
echo "=========================================="
echo ""
echo "方法 1: 修改 boot.sh（推荐）"
echo "   编辑 boot.sh，修改第 144 行的密码："
echo "   export NEO4J_PASSWORD=\"\${NEO4J_PASSWORD:-你的实际密码}\""
echo ""
echo "方法 2: 设置环境变量（临时）"
echo "   export NEO4J_PASSWORD=\"你的实际密码\""
echo "   ./boot.sh"
echo ""
echo "方法 3: 修改 Neo4j 密码（如果忘记密码）"
echo "   1. 停止 Neo4j"
echo "   2. 删除 Neo4j 数据目录中的 auth 文件"
echo "   3. 重启 Neo4j 并设置新密码"
echo ""
echo "=========================================="
echo "测试连接（需要密码）"
echo "=========================================="
echo ""
echo "如果你想测试 Neo4j 连接，可以运行："
echo "   cypher-shell -u neo4j -p '你的密码' -a bolt://localhost:7687"
echo ""
echo "或者使用 curl（HTTP API）："
echo "   curl -u neo4j:你的密码 http://localhost:7474/user/neo4j"
echo ""
