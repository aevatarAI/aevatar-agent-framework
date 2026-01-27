#!/bin/bash
# 修复 Neo4j Bolt 连接器配置

echo "=========================================="
echo "修复 Neo4j Bolt 连接器配置"
echo "=========================================="
echo ""

NEO4J_HOME=$(brew --prefix neo4j 2>/dev/null)
if [ -z "$NEO4J_HOME" ]; then
    echo "❌ Neo4j 未通过 Homebrew 安装"
    exit 1
fi

CONFIG_FILE="$NEO4J_HOME/libexec/conf/neo4j.conf"
if [ ! -f "$CONFIG_FILE" ]; then
    echo "❌ 配置文件不存在: $CONFIG_FILE"
    exit 1
fi

echo "配置文件: $CONFIG_FILE"
echo ""

# 备份配置文件
BACKUP_FILE="${CONFIG_FILE}.backup.$(date +%Y%m%d_%H%M%S)"
cp "$CONFIG_FILE" "$BACKUP_FILE"
echo "✅ 已备份配置文件到: $BACKUP_FILE"
echo ""

# 检查并修复 Bolt 配置
echo "检查 Bolt 配置..."

# Neo4j 5.x 使用 server.bolt.* 格式
# Neo4j 4.x 使用 dbms.connector.bolt.* 格式

# 检查是否已经有启用的 Bolt 配置
if grep -qE "^server\.bolt\.listen_address|^dbms\.connector\.bolt\.listen_address" "$CONFIG_FILE"; then
    echo "✅ Bolt 配置已存在"
else
    echo "⚠️  Bolt 配置被注释或不存在，正在修复..."
    
    # 尝试取消注释 Neo4j 5.x 格式的配置
    if grep -q "^#server\.bolt\.listen_address" "$CONFIG_FILE"; then
        echo "检测到 Neo4j 5.x 格式配置，正在启用..."
        sed -i '' 's/^#server\.bolt\.listen_address=:7687/server.bolt.listen_address=0.0.0.0:7687/' "$CONFIG_FILE"
        sed -i '' 's/^#server\.bolt\.advertised_address=:7687/server.bolt.advertised_address=0.0.0.0:7687/' "$CONFIG_FILE"
        echo "✅ 已启用 Neo4j 5.x Bolt 配置"
    # 尝试取消注释 Neo4j 4.x 格式的配置
    elif grep -q "^#dbms\.connector\.bolt\.listen_address" "$CONFIG_FILE"; then
        echo "检测到 Neo4j 4.x 格式配置，正在启用..."
        sed -i '' 's/^#dbms\.connector\.bolt\.enabled=true/dbms.connector.bolt.enabled=true/' "$CONFIG_FILE"
        sed -i '' 's/^#dbms\.connector\.bolt\.listen_address=:7687/dbms.connector.bolt.listen_address=0.0.0.0:7687/' "$CONFIG_FILE"
        echo "✅ 已启用 Neo4j 4.x Bolt 配置"
    else
        echo "⚠️  未找到 Bolt 配置，添加新配置..."
        # 添加 Neo4j 5.x 格式配置
        echo "" >> "$CONFIG_FILE"
        echo "# Bolt connector (enabled by fix_neo4j_bolt.sh)" >> "$CONFIG_FILE"
        echo "server.bolt.listen_address=0.0.0.0:7687" >> "$CONFIG_FILE"
        echo "server.bolt.advertised_address=0.0.0.0:7687" >> "$CONFIG_FILE"
        echo "✅ 已添加 Bolt 配置"
    fi
fi

echo ""
echo "验证配置..."
if grep -qE "^server\.bolt\.listen_address|^dbms\.connector\.bolt\.listen_address" "$CONFIG_FILE"; then
    echo "✅ Bolt 配置已启用:"
    grep -E "^server\.bolt\.listen_address|^dbms\.connector\.bolt\.listen_address|^dbms\.connector\.bolt\.enabled" "$CONFIG_FILE" | grep -v "^#" | head -3
else
    echo "❌ Bolt 配置仍然未启用"
    exit 1
fi

echo ""
echo "=========================================="
echo "重启 Neo4j 服务"
echo "=========================================="
echo ""

# 停止 Neo4j
echo "停止 Neo4j 服务..."
brew services stop neo4j 2>&1 | grep -v "^Warning" || true
sleep 2

# 启动 Neo4j
echo "启动 Neo4j 服务..."
brew services start neo4j 2>&1 | grep -v "^Warning" || true

echo ""
echo "⏳ 等待 Neo4j 启动（约 30 秒）..."
sleep 30

echo ""
echo "验证连接..."
if nc -zv localhost 7687 2>&1 | grep -q "succeeded"; then
    echo "✅ Bolt 端口 (7687) 现在可以访问了！"
else
    echo "⚠️  Bolt 端口仍然无法访问"
    echo "请检查 Neo4j 日志: brew services info neo4j"
fi

echo ""
echo "=========================================="
echo "修复完成"
echo "=========================================="
echo ""
echo "如果问题仍然存在，请："
echo "1. 查看 Neo4j 日志: tail -f ~/Library/Logs/Homebrew/neo4j.log"
echo "2. 检查服务状态: brew services info neo4j"
echo "3. 手动启动: $NEO4J_HOME/bin/neo4j start"
echo ""
