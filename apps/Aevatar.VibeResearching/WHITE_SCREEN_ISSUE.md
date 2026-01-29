# 网页空白问题诊断指南

## 🔴 问题描述

运行 `boot.sh` 后，打开网页显示空白（白屏）。

---

## 🔍 可能的原因

### 1. 前端未启动或启动失败 ⚠️ **最常见**

**症状**: 
- 浏览器显示空白页面
- 无法访问 `http://localhost:5173`

**检查方法**:
```bash
# 检查前端进程是否运行
ps aux | grep "vite\|node.*5173"

# 检查端口是否被占用
lsof -i :5173

# 查看 boot.sh 输出，确认前端是否启动
```

**解决方案**:
```bash
# 确保 Node.js 已安装
node --version
npm --version

# 如果未安装
brew install node

# 手动启动前端
cd apps/Aevatar.VibeResearching/sisyphus-frontend
npm install
npm run dev
```

---

### 2. JavaScript 错误导致页面无法渲染

**症状**:
- 浏览器显示空白页面
- 浏览器控制台（F12）有红色错误信息

**检查方法**:
1. 打开浏览器开发者工具（F12 或 Cmd+Option+I）
2. 查看 **Console** 标签页
3. 查看 **Network** 标签页，检查资源加载是否失败

**常见错误**:
- `Failed to fetch` - API 连接失败
- `Cannot read property 'xxx' of undefined` - 代码错误
- `Module not found` - 依赖缺失
- `CORS error` - 跨域问题

**解决方案**:
- 根据控制台错误信息修复
- 检查后端是否正常运行
- 检查 CORS 配置

---

### 3. API 连接失败

**症状**:
- 页面加载但显示空白
- 控制台显示 API 请求失败
- Network 标签显示 404 或连接错误

**检查方法**:
```bash
# 检查后端是否运行
curl http://localhost:5678/health

# 应该返回: ok
```

**解决方案**:
```bash
# 确保后端正在运行
cd apps/Aevatar.VibeResearching
./boot.sh --backend-only

# 在另一个终端启动前端
cd sisyphus-frontend
npm run dev
```

---

### 4. 端口冲突

**症状**:
- 前端无法启动
- 端口被占用错误

**检查方法**:
```bash
# 检查端口占用
lsof -i :5173  # 前端端口
lsof -i :5678  # 后端端口

# 查看 boot.sh 输出
```

**解决方案**:
```bash
# 使用不同端口
FRONTEND_PORT=5174 BACKEND_PORT=5679 ./boot.sh

# 或停止占用端口的进程
kill -9 $(lsof -ti :5173)
kill -9 $(lsof -ti :5678)
```

---

### 5. 依赖未安装

**症状**:
- 前端启动失败
- `node_modules` 不存在或损坏

**检查方法**:
```bash
cd apps/Aevatar.VibeResearching/sisyphus-frontend
ls -la node_modules
```

**解决方案**:
```bash
cd apps/Aevatar.VibeResearching/sisyphus-frontend
rm -rf node_modules package-lock.json
npm install
```

---

### 6. 环境变量配置错误

**症状**:
- API 请求失败
- 无法连接到后端

**检查方法**:
```bash
# 查看 vite.config.ts 中的配置
cat sisyphus-frontend/vite.config.ts | grep -A 5 "backendTarget"
```

**解决方案**:
```bash
# 确保环境变量正确设置
export VITE_API_BASE_URL="http://localhost:5678"
export BACKEND_PORT="5678"
export PORT="5173"

# 或使用 boot.sh（会自动设置）
./boot.sh
```

---

### 7. CORS 配置问题

**症状**:
- 页面加载但 API 请求失败
- 控制台显示 CORS 错误

**检查方法**:
查看浏览器控制台的 Network 标签，检查请求是否返回 CORS 错误。

**解决方案**:
检查 `appsettings.json` 中的 CORS 配置：

```json
{
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:5173",
      "https://sisyphus.aelf.dev"
    ]
  }
}
```

确保前端端口在允许列表中。

---

## 🔧 诊断步骤

### Step 1: 检查服务状态

```bash
# 1. 检查后端是否运行
curl http://localhost:5678/health
# 应该返回: ok

# 2. 检查前端是否运行
curl http://localhost:5173
# 应该返回 HTML 内容

# 3. 检查进程
ps aux | grep -E "dotnet|vite|node" | grep -v grep
```

---

### Step 2: 检查浏览器控制台

1. **打开浏览器开发者工具**（F12 或 Cmd+Option+I）
2. **查看 Console 标签**:
   - 是否有红色错误？
   - 是否有警告？
3. **查看 Network 标签**:
   - 资源是否加载成功？
   - API 请求是否失败？
   - 状态码是什么？

---

### Step 3: 检查日志

```bash
# 查看后端日志
tail -f logs/*.log

# 查看前端启动日志（在运行 boot.sh 的终端）
# 应该看到类似输出:
# Starting frontend (Vite :5173)
# VITE v5.x.x  ready in xxx ms
```

---

### Step 4: 手动测试

```bash
# 1. 测试后端
curl http://localhost:5678/health

# 2. 测试前端
curl http://localhost:5173

# 3. 测试 API 代理
curl http://localhost:5173/api/sessions
```

---

## ✅ 快速修复

### 方法 1: 重新启动所有服务

```bash
cd apps/Aevatar.VibeResearching

# 停止所有进程
pkill -f "dotnet.*VibeResearching"
pkill -f "vite"

# 等待几秒
sleep 3

# 重新启动
./boot.sh
```

---

### 方法 2: 分别启动前后端

**终端 1 - 后端**:
```bash
cd apps/Aevatar.VibeResearching
./boot.sh --backend-only
```

**终端 2 - 前端**:
```bash
cd apps/Aevatar.VibeResearching/sisyphus-frontend
npm install  # 如果需要
npm run dev
```

---

### 方法 3: 清理并重新安装

```bash
cd apps/Aevatar.VibeResearching/sisyphus-frontend

# 清理
rm -rf node_modules package-lock.json .vite

# 重新安装
npm install

# 启动
npm run dev
```

---

## 🔍 常见错误和解决方案

### 错误 1: "Cannot GET /"

**原因**: 路由配置问题或前端未正确启动

**解决**: 
- 确保访问 `http://localhost:5173/`（带斜杠）
- 或访问 `http://localhost:5173/app`（应用页面）

---

### 错误 2: "Failed to fetch"

**原因**: API 连接失败

**解决**:
```bash
# 检查后端是否运行
curl http://localhost:5678/health

# 检查 CORS 配置
grep -A 5 "Cors" appsettings.json
```

---

### 错误 3: "Module not found"

**原因**: 依赖未安装或版本不匹配

**解决**:
```bash
cd sisyphus-frontend
rm -rf node_modules
npm install
```

---

### 错误 4: "Port already in use"

**原因**: 端口被占用

**解决**:
```bash
# 查找占用端口的进程
lsof -i :5173
lsof -i :5678

# 停止进程
kill -9 <PID>

# 或使用不同端口
FRONTEND_PORT=5174 ./boot.sh
```

---

## 📋 检查清单

运行以下命令进行完整诊断：

```bash
cd apps/Aevatar.VibeResearching

echo "=== 1. 检查后端 ==="
curl -s http://localhost:5678/health && echo " ✅ 后端正常" || echo " ❌ 后端未运行"

echo ""
echo "=== 2. 检查前端 ==="
curl -s http://localhost:5173 > /dev/null && echo " ✅ 前端正常" || echo " ❌ 前端未运行"

echo ""
echo "=== 3. 检查端口 ==="
lsof -i :5678 | grep LISTEN && echo " ✅ 后端端口正常" || echo " ❌ 后端端口未监听"
lsof -i :5173 | grep LISTEN && echo " ✅ 前端端口正常" || echo " ❌ 前端端口未监听"

echo ""
echo "=== 4. 检查进程 ==="
ps aux | grep -E "dotnet.*VibeResearching|vite" | grep -v grep && echo " ✅ 进程运行中" || echo " ❌ 进程未运行"

echo ""
echo "=== 5. 检查依赖 ==="
[ -d "sisyphus-frontend/node_modules" ] && echo " ✅ node_modules 存在" || echo " ❌ node_modules 不存在"
```

---

## 🚀 推荐操作流程

1. **打开浏览器开发者工具**（F12）
2. **查看 Console 标签**，记录所有错误
3. **查看 Network 标签**，检查资源加载
4. **运行诊断脚本**（上面的检查清单）
5. **根据错误信息采取相应措施**

---

## 📚 相关文档

- `BOOT_SH_WORKFLOW.md` - boot.sh 工作流程说明
- `sisyphus-frontend/README.md` - 前端开发指南
- `docs/FRONTEND_SESSION_API.md` - 前端 API 文档

---

## 💡 预防措施

1. **确保 Node.js 已安装**: `node --version`
2. **确保依赖已安装**: `cd sisyphus-frontend && npm install`
3. **使用 boot.sh 启动**: 它会自动处理环境变量和端口配置
4. **检查端口冲突**: 确保 5678 和 5173 端口可用

---

*最后更新: 2025-01-28*
