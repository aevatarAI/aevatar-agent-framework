#!/bin/bash
# 网页空白问题诊断脚本

set -e

echo "=========================================="
echo "网页空白问题诊断"
echo "=========================================="
echo ""

# 1. 检查后端
echo "1. 检查后端服务..."
BACKEND_HEALTH=$(curl -s http://localhost:5678/health 2>&1 || echo "FAILED")
if [[ "$BACKEND_HEALTH" == "ok" ]]; then
    echo "   ✅ 后端健康检查通过"
elif [[ "$BACKEND_HEALTH" == "FAILED" ]]; then
    echo "   ❌ 后端无法连接"
else
    echo "   ⚠️  后端响应异常: $BACKEND_HEALTH"
fi

# 检查后端端口
if lsof -i :5678 >/dev/null 2>&1; then
    BACKEND_PID=$(lsof -ti :5678 | head -1)
    echo "   ✅ 后端端口 5678 正在监听 (PID: $BACKEND_PID)"
else
    echo "   ❌ 后端端口 5678 未监听"
fi
echo ""

# 2. 检查前端
echo "2. 检查前端服务..."
FRONTEND_RESPONSE=$(curl -s http://localhost:5173 2>&1 | head -5 || echo "FAILED")
if echo "$FRONTEND_RESPONSE" | grep -q "<!doctype"; then
    echo "   ✅ 前端可访问，返回 HTML"
elif [[ "$FRONTEND_RESPONSE" == "FAILED" ]]; then
    echo "   ❌ 前端无法连接"
else
    echo "   ⚠️  前端响应异常:"
    echo "$FRONTEND_RESPONSE" | head -3 | sed 's/^/      /'
fi

# 检查前端端口
if lsof -i :5173 >/dev/null 2>&1; then
    FRONTEND_PID=$(lsof -ti :5173 | head -1)
    echo "   ✅ 前端端口 5173 正在监听 (PID: $FRONTEND_PID)"
else
    echo "   ❌ 前端端口 5173 未监听"
fi
echo ""

# 3. 检查进程
echo "3. 检查相关进程..."
DOTNET_PROCESSES=$(ps aux | grep -E "dotnet.*VibeResearching" | grep -v grep | wc -l | tr -d ' ')
VITE_PROCESSES=$(ps aux | grep -E "vite|node.*5173" | grep -v grep | wc -l | tr -d ' ')

if [[ "$DOTNET_PROCESSES" -gt 0 ]]; then
    echo "   ✅ 找到 $DOTNET_PROCESSES 个后端进程"
else
    echo "   ❌ 未找到后端进程"
fi

if [[ "$VITE_PROCESSES" -gt 0 ]]; then
    echo "   ✅ 找到 $VITE_PROCESSES 个前端进程"
else
    echo "   ❌ 未找到前端进程"
fi
echo ""

# 4. 检查前端依赖
echo "4. 检查前端依赖..."
FRONTEND_DIR="sisyphus-frontend"
if [[ -d "$FRONTEND_DIR" ]]; then
    if [[ -d "$FRONTEND_DIR/node_modules" ]]; then
        echo "   ✅ node_modules 存在"
    else
        echo "   ❌ node_modules 不存在（需要运行 npm install）"
    fi
    
    if [[ -f "$FRONTEND_DIR/package.json" ]]; then
        echo "   ✅ package.json 存在"
    else
        echo "   ❌ package.json 不存在"
    fi
else
    echo "   ❌ 前端目录不存在: $FRONTEND_DIR"
fi
echo ""

# 5. 检查浏览器控制台建议
echo "5. 浏览器检查建议..."
echo "   请打开浏览器开发者工具（F12 或 Cmd+Option+I）:"
echo "   - 查看 Console 标签，检查是否有 JavaScript 错误"
echo "   - 查看 Network 标签，检查资源加载情况"
echo "   - 访问: http://localhost:5173"
echo ""

# 6. 测试 API 代理
echo "6. 测试 API 代理..."
API_TEST=$(curl -s http://localhost:5173/api/sessions 2>&1 | head -3 || echo "FAILED")
if echo "$API_TEST" | grep -q "sessions\|\[\]\|error"; then
    echo "   ✅ API 代理工作正常"
elif [[ "$API_TEST" == "FAILED" ]]; then
    echo "   ❌ API 代理失败"
else
    echo "   ⚠️  API 代理响应: $API_TEST"
fi
echo ""

# 7. 总结和建议
echo "=========================================="
echo "诊断总结"
echo "=========================================="
echo ""

if [[ "$BACKEND_HEALTH" == "ok" ]] && echo "$FRONTEND_RESPONSE" | grep -q "<!doctype"; then
    echo "✅ 服务看起来正常运行"
    echo ""
    echo "如果页面仍然空白，请："
    echo "1. 打开浏览器开发者工具（F12）"
    echo "2. 查看 Console 标签的错误信息"
    echo "3. 查看 Network 标签的请求状态"
    echo "4. 尝试硬刷新（Cmd+Shift+R 或 Ctrl+Shift+R）"
elif [[ "$BACKEND_HEALTH" != "ok" ]]; then
    echo "❌ 后端未正常运行"
    echo ""
    echo "建议操作："
    echo "1. 检查后端日志: tail -f logs/*.log"
    echo "2. 重新启动后端: ./boot.sh --backend-only"
    echo "3. 检查 Neo4j 连接: ./test_neo4j.sh"
elif ! echo "$FRONTEND_RESPONSE" | grep -q "<!doctype"; then
    echo "❌ 前端未正常运行"
    echo ""
    echo "建议操作："
    echo "1. 检查 Node.js: node --version"
    echo "2. 安装依赖: cd sisyphus-frontend && npm install"
    echo "3. 手动启动前端: npm run dev"
else
    echo "⚠️  部分服务可能有问题"
    echo ""
    echo "建议："
    echo "1. 查看上面的详细检查结果"
    echo "2. 检查浏览器控制台错误"
    echo "3. 重新启动所有服务: ./boot.sh"
fi
echo ""
