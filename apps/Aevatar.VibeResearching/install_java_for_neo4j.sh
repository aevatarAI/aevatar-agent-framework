#!/bin/bash
# 为 Neo4j 安装 Java

echo "=========================================="
echo "为 Neo4j 安装 Java 运行环境"
echo "=========================================="
echo ""

# Neo4j 5.x 需要 Java 17 或更高版本
# Neo4j 4.x 需要 Java 11 或更高版本

if command -v brew &> /dev/null; then
    echo "✅ 检测到 Homebrew"
    echo ""
    
    # 检查是否已安装 Java
    if java -version 2>&1 | grep -q "version"; then
        echo "✅ Java 已安装:"
        java -version 2>&1 | head -1
        echo ""
        echo "检查版本兼容性..."
        JAVA_VERSION=$(java -version 2>&1 | head -1 | grep -oE 'version "[0-9]+' | grep -oE '[0-9]+')
        if [ -n "$JAVA_VERSION" ] && [ "$JAVA_VERSION" -ge 11 ]; then
            echo "✅ Java 版本 ($JAVA_VERSION) 兼容 Neo4j"
            exit 0
        else
            echo "⚠️  Java 版本 ($JAVA_VERSION) 可能不兼容，建议安装 Java 17+"
        fi
    else
        echo "📦 安装 Java 17 (推荐用于 Neo4j 5.x)..."
        echo ""
        
        # 尝试安装 OpenJDK 17
        if brew install openjdk@17 2>&1 | tee /tmp/java_install.log; then
            echo ""
            echo "✅ Java 17 安装成功"
            echo ""
            echo "设置 Java 环境变量..."
            
            # 添加到 PATH
            JAVA_HOME=$(brew --prefix openjdk@17)
            echo "JAVA_HOME: $JAVA_HOME"
            
            # 创建符号链接
            sudo ln -sfn "$JAVA_HOME/libexec/openjdk.jdk" /Library/Java/JavaVirtualMachines/openjdk-17.jdk
            
            echo ""
            echo "请运行以下命令设置环境变量（或添加到 ~/.zshrc）:"
            echo "export JAVA_HOME=\"$JAVA_HOME\""
            echo "export PATH=\"\$JAVA_HOME/bin:\$PATH\""
            echo ""
            echo "然后重新运行此脚本验证安装"
        else
            echo ""
            echo "❌ Java 安装失败"
            echo "请查看日志: cat /tmp/java_install.log"
            echo ""
            echo "或者手动安装:"
            echo "  brew install openjdk@17"
            echo "  brew install openjdk@21  # 或更新的版本"
            exit 1
        fi
    fi
else
    echo "❌ Homebrew 未安装"
    echo ""
    echo "请手动安装 Java:"
    echo "1. 访问 https://adoptium.net/ 下载 Java 17+"
    echo "2. 或使用 Homebrew: brew install openjdk@17"
    exit 1
fi

echo ""
echo "=========================================="
echo "安装完成"
echo "=========================================="
echo ""
echo "验证安装:"
echo "  java -version"
echo ""
echo "然后重启 Neo4j:"
echo "  brew services restart neo4j"
echo ""
