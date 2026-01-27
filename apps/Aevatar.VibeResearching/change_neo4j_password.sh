#!/bin/bash
# 修改 Neo4j 密码脚本（如果知道当前密码）

set -e

echo "=========================================="
echo "Neo4j 密码修改脚本"
echo "=========================================="
echo ""
echo "此脚本使用 Cypher Shell 修改 Neo4j 密码"
echo "前提：你需要知道当前的 Neo4j 密码"
echo ""

read -p "请输入当前 Neo4j 密码: " -s CURRENT_PASSWORD
echo ""

read -p "请输入新密码: " -s NEW_PASSWORD
echo ""

read -p "确认新密码: " -s CONFIRM_PASSWORD
echo ""

if [[ "$NEW_PASSWORD" != "$CONFIRM_PASSWORD" ]]; then
    echo "❌ 两次输入的密码不一致"
    exit 1
fi

if [[ -z "$NEW_PASSWORD" ]]; then
    echo "❌ 新密码不能为空"
    exit 1
fi

echo ""
echo "正在修改密码..."

# 使用 Cypher Shell 修改密码
echo "ALTER USER neo4j SET PASSWORD '$NEW_PASSWORD';" | \
    cypher-shell -u neo4j -p "$CURRENT_PASSWORD" -a bolt://localhost:7687 2>&1

if [[ $? -eq 0 ]]; then
    echo ""
    echo "=========================================="
    echo "✅ 密码修改成功"
    echo "=========================================="
    echo ""
    echo "下一步："
    echo "1. 更新 boot.sh 中的密码（如果需要）"
    echo "2. 重启后端: ./boot.sh"
    echo ""
else
    echo ""
    echo "❌ 密码修改失败"
    echo ""
    echo "可能的原因："
    echo "1. 当前密码不正确"
    echo "2. Neo4j 未运行"
    echo "3. 连接失败"
    echo ""
    echo "如果忘记密码，请使用 reset_neo4j_password.sh（会重置数据库）"
    exit 1
fi
