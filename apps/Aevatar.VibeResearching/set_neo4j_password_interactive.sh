#!/bin/bash
# 交互式设置 Neo4j 密码

set -e

echo "=========================================="
echo "Neo4j 密码设置（交互式）"
echo "=========================================="
echo ""
echo "Neo4j 密码已过期，需要修改"
echo ""
echo "📋 密码信息："
echo "   当前默认密码: neo4j"
echo "   新密码: gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI"
echo ""
echo "⚠️  重要提示："
echo "   脚本会提示你输入密码，密码输入时不会显示（这是正常的）"
echo ""
read -p "按 Enter 开始设置密码..."

echo ""
echo "正在连接 Neo4j..."
echo ""
echo "📝 接下来会提示你输入："
echo "   1. 当前密码: neo4j (输入后按 Enter)"
echo "   2. 新密码: gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI (输入后按 Enter)"
echo "   3. 确认新密码: gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI (输入后按 Enter)"
echo ""
echo "开始连接..."
echo ""

cypher-shell -u neo4j -p 'neo4j' -a bolt://localhost:7687 --change-password

echo ""
echo "=========================================="
echo "密码设置完成"
echo "=========================================="
echo ""
echo "验证密码..."
if echo "RETURN 1 as test;" | cypher-shell -u neo4j -p 'gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI' -a bolt://localhost:7687 >/dev/null 2>&1; then
    echo "✅ 密码设置成功！"
    echo ""
    echo "下一步："
    echo "1. 重启后端: ./boot.sh"
    echo "2. 检查日志，确认不再有认证错误"
else
    echo "❌ 密码验证失败，请检查密码是否正确设置"
fi
echo ""
