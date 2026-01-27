#!/bin/bash
# 重置 Neo4j 密码脚本

set -e

echo "=========================================="
echo "Neo4j 密码重置脚本"
echo "=========================================="
echo ""
echo "⚠️  警告：这会删除当前的 Neo4j 认证信息！"
echo "   所有用户和权限设置将被重置。"
echo ""
read -p "是否继续？(y/N): " confirm

if [[ "$confirm" != "y" && "$confirm" != "Y" ]]; then
    echo "已取消"
    exit 0
fi

echo ""
echo "1. 停止 Neo4j..."
neo4j stop || echo "   Neo4j 未运行或无法停止"

echo ""
echo "2. 检查 Neo4j 版本和数据目录..."

# Neo4j 5.x 可能使用不同的认证机制
# 检查多个可能的位置
NEO4J_DATA_DIR=""
AUTH_FILE=""

# 尝试 Neo4j 4.x 的位置
if [[ -d "/opt/homebrew/var/neo4j/data/dbms" ]]; then
    NEO4J_DATA_DIR="/opt/homebrew/var/neo4j/data/dbms"
    AUTH_FILE="$NEO4J_DATA_DIR/auth"
elif [[ -d "/usr/local/var/neo4j/data/dbms" ]]; then
    NEO4J_DATA_DIR="/usr/local/var/neo4j/data/dbms"
    AUTH_FILE="$NEO4J_DATA_DIR/auth"
elif [[ -d "$HOME/neo4j/data/dbms" ]]; then
    NEO4J_DATA_DIR="$HOME/neo4j/data/dbms"
    AUTH_FILE="$NEO4J_DATA_DIR/auth"
fi

# Neo4j 5.x 可能将认证信息存储在数据库中
if [[ -z "$NEO4J_DATA_DIR" ]] || [[ ! -f "$AUTH_FILE" ]]; then
    echo "   ⚠️  未找到传统的 auth 文件（可能是 Neo4j 5.x）"
    echo ""
    echo "   Neo4j 5.x 使用数据库存储认证信息，无法通过删除文件重置密码。"
    echo ""
    echo "   替代方案："
    echo "   1. 如果你知道当前密码，使用 Cypher Shell 修改密码："
    echo "      cypher-shell -u neo4j -p '当前密码' -a bolt://localhost:7687"
    echo "      然后运行: ALTER USER neo4j SET PASSWORD '新密码';"
    echo ""
    echo "   2. 或通过 Neo4j Browser (http://localhost:7474) 修改密码"
    echo ""
    echo "   3. 如果完全忘记密码，需要重置整个数据库（会丢失所有数据）："
    echo "      neo4j stop"
    echo "      rm -rf /opt/homebrew/var/neo4j/data/databases/neo4j"
    echo "      rm -rf /opt/homebrew/var/neo4j/data/transactions/neo4j"
    echo "      neo4j start"
    echo "      首次登录设置新密码"
    echo ""
    exit 1
fi

echo "   找到数据目录: $NEO4J_DATA_DIR"

echo ""
echo "3. 备份并删除认证文件..."
if [[ -f "$AUTH_FILE" ]]; then
    BACKUP_FILE="$AUTH_FILE.backup.$(date +%Y%m%d_%H%M%S)"
    cp "$AUTH_FILE" "$BACKUP_FILE"
    echo "   已备份到: $BACKUP_FILE"
    rm "$AUTH_FILE"
    echo "   ✅ 认证文件已删除"
else
    echo "   ⚠️  认证文件不存在（可能已经重置过）"
fi

echo ""
echo "4. 启动 Neo4j..."
neo4j start

echo ""
echo "=========================================="
echo "✅ 重置完成"
echo "=========================================="
echo ""
echo "下一步："
echo "1. 等待 Neo4j 启动（约 10-30 秒）"
echo "2. 打开 Neo4j Browser: http://localhost:7474"
echo "3. 使用以下凭据登录："
echo "   用户名: neo4j"
echo "   密码: neo4j（首次登录会要求修改）"
echo "4. 设置新密码（建议使用: gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI）"
echo "5. 更新 boot.sh 中的密码"
echo ""
