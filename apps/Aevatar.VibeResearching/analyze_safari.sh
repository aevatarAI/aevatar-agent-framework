#!/bin/bash
# Safari 浏览器运行情况分析脚本

set -e

echo "=========================================="
echo "Safari 浏览器运行情况分析"
echo "=========================================="
echo ""

# 1. 检查应用运行状态
echo "1. 检查应用运行状态..."
echo ""

# 后端检查
BACKEND_HEALTH=$(curl -s http://localhost:5678/health 2>&1 || echo "FAILED")
if [[ "$BACKEND_HEALTH" == "ok" ]]; then
    echo "   ✅ 后端运行正常"
elif [[ "$BACKEND_HEALTH" == "FAILED" ]]; then
    echo "   ❌ 后端未运行"
else
    echo "   ⚠️  后端响应异常: $BACKEND_HEALTH"
fi

# 前端检查
FRONTEND_RESPONSE=$(curl -s http://localhost:5173 2>&1 | head -1 || echo "FAILED")
if echo "$FRONTEND_RESPONSE" | grep -q "<!doctype"; then
    echo "   ✅ 前端运行正常"
elif [[ "$FRONTEND_RESPONSE" == "FAILED" ]]; then
    echo "   ❌ 前端未运行"
else
    echo "   ⚠️  前端响应异常"
fi
echo ""

# 2. 检查端口
echo "2. 检查端口监听..."
if lsof -i :5678 >/dev/null 2>&1; then
    echo "   ✅ 后端端口 5678 正在监听"
else
    echo "   ❌ 后端端口 5678 未监听"
fi

if lsof -i :5173 >/dev/null 2>&1; then
    echo "   ✅ 前端端口 5173 正在监听"
else
    echo "   ❌ 前端端口 5173 未监听"
fi
echo ""

# 3. 测试 API
echo "3. 测试 API 端点..."
API_TEST=$(curl -s http://localhost:5678/api/sessions 2>&1 | head -3 || echo "FAILED")
if echo "$API_TEST" | grep -q "\[\]\|sessions\|error"; then
    echo "   ✅ API 端点可访问"
else
    echo "   ❌ API 端点不可访问"
fi
echo ""

# 4. Safari 检查指南
echo "=========================================="
echo "Safari 浏览器检查指南"
echo "=========================================="
echo ""
echo "请在 Safari 中执行以下步骤："
echo ""
echo "1. 打开应用:"
echo "   http://localhost:5173"
echo ""
echo "2. 打开开发者工具:"
echo "   方法 1: Safari → 设置 → 高级 → 勾选'显示开发菜单'"
echo "   方法 2: 快捷键 Cmd + Option + I"
echo "   方法 3: 右键页面 → '检查元素'"
echo ""
echo "3. 检查 Console（控制台）:"
echo "   - 切换到'控制台'标签"
echo "   - 查看是否有红色错误"
echo "   - 记录所有错误信息"
echo ""
echo "4. 检查 Network（网络）:"
echo "   - 切换到'网络'标签"
echo "   - 刷新页面 (Cmd + R)"
echo "   - 查看资源加载状态"
echo "   - 检查 API 请求状态"
echo "   - 查找 SSE 连接 (eventsource 类型)"
echo ""
echo "5. 检查 Elements（元素）:"
echo "   - 切换到'元素'标签"
echo "   - 查看 #root 元素是否有内容"
echo "   - 检查 React 组件是否正确渲染"
echo ""
echo "=========================================="
echo "常见问题诊断"
echo "=========================================="
echo ""

if [[ "$BACKEND_HEALTH" != "ok" ]] || ! echo "$FRONTEND_RESPONSE" | grep -q "<!doctype"; then
    echo "⚠️  应用未运行，请先启动："
    echo ""
    echo "   cd apps/Aevatar.VibeResearching"
    echo "   ./boot.sh"
    echo ""
    echo "然后重新运行此脚本进行分析。"
else
    echo "✅ 应用运行正常"
    echo ""
    echo "如果 Safari 中仍有问题，请："
    echo "1. 查看 Safari 开发者工具的 Console 错误"
    echo "2. 查看 Network 标签的请求状态"
    echo "3. 检查是否有 CORS 错误"
    echo "4. 尝试硬刷新 (Cmd + Shift + R)"
fi
echo ""
