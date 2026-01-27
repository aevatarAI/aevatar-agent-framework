#!/bin/bash
# Neo4j 启动脚本

echo "=========================================="
echo "Neo4j 启动脚本"
echo "=========================================="
echo ""

# 检查 Docker 是否可用
if command -v docker &> /dev/null; then
    echo "✅ 检测到 Docker"
    echo ""
    
    # 检查 Neo4j 容器是否已存在
    if docker ps -a --format '{{.Names}}' | grep -q "^neo4j$"; then
        echo "📦 发现已存在的 Neo4j 容器"
        if docker ps --format '{{.Names}}' | grep -q "^neo4j$"; then
            echo "✅ Neo4j 容器已在运行"
            echo ""
            echo "Neo4j Browser: http://localhost:7474"
            echo "Bolt URI: bolt://localhost:7687"
            echo "默认用户名: neo4j"
            echo "默认密码: 请查看容器日志或重置密码"
            exit 0
        else
            echo "🔄 启动 Neo4j 容器..."
            docker start neo4j
            sleep 5
            echo "✅ Neo4j 容器已启动"
        fi
    else
        echo "📦 创建新的 Neo4j 容器..."
        docker run -d \
            --name neo4j \
            -p 7474:7474 \
            -p 7687:7687 \
            -e NEO4J_AUTH=neo4j/password \
            -e NEO4J_PLUGINS='["apoc"]' \
            neo4j:latest
        
        echo "⏳ 等待 Neo4j 启动（约 30 秒）..."
        sleep 30
        
        echo "✅ Neo4j 容器已创建并启动"
    fi
    
    echo ""
    echo "Neo4j Browser: http://localhost:7474"
    echo "Bolt URI: bolt://localhost:7687"
    echo "用户名: neo4j"
    echo "密码: password"
    echo ""
    echo "⚠️  首次登录后需要修改密码"
    echo ""
    
elif command -v brew &> /dev/null; then
    echo "✅ 检测到 Homebrew"
    echo ""
    
    # 检查是否已安装 Neo4j
    if brew list neo4j &> /dev/null; then
        echo "✅ Neo4j 已通过 Homebrew 安装"
        echo ""
        echo "启动 Neo4j..."
        brew services start neo4j
        echo ""
        echo "Neo4j Browser: http://localhost:7474"
        echo "Bolt URI: bolt://localhost:7687"
        echo "默认用户名: neo4j"
        echo "默认密码: 请查看 ~/.neo4j/neo4j.conf 或重置密码"
    else
        echo "📦 安装 Neo4j..."
        brew install neo4j
        echo ""
        echo "启动 Neo4j..."
        brew services start neo4j
        echo ""
        echo "Neo4j Browser: http://localhost:7474"
        echo "Bolt URI: bolt://localhost:7687"
        echo "默认用户名: neo4j"
        echo "默认密码: 首次启动时会提示设置"
    fi
    
else
    echo "❌ 未检测到 Docker 或 Homebrew"
    echo ""
    echo "请选择以下方式之一安装 Neo4j："
    echo ""
    echo "1. 使用 Docker（推荐）:"
    echo "   docker run -d --name neo4j -p 7474:7474 -p 7687:7687 -e NEO4J_AUTH=neo4j/password neo4j:latest"
    echo ""
    echo "2. 使用 Homebrew:"
    echo "   brew install neo4j"
    echo "   brew services start neo4j"
    echo ""
    echo "3. 从官网下载:"
    echo "   https://neo4j.com/download/"
    echo ""
    exit 1
fi

echo ""
echo "=========================================="
echo "设置环境变量（如果尚未设置）:"
echo "=========================================="
echo "export NEO4J_URI=bolt://localhost:7687"
echo "export NEO4J_USERNAME=neo4j"
echo "export NEO4J_PASSWORD=password  # 或你设置的密码"
echo "export NEO4J_DATABASE=neo4j"
echo ""
echo "验证连接:"
echo "curl http://localhost:7474"
echo ""
