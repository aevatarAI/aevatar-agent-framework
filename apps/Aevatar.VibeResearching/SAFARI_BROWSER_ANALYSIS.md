# Safari 浏览器运行情况分析指南

## 🔍 如何在 Safari 中分析应用运行情况

由于我无法直接访问你的 Safari 浏览器，这里提供完整的诊断方法。

---

## 📋 Safari 开发者工具使用

### 1. 打开 Safari 开发者工具

**方法 1: 菜单栏**
- `Safari` → `设置` → `高级` → 勾选 `在菜单栏中显示"开发"菜单`
- 然后 `开发` → `显示 Web 检查器`

**方法 2: 快捷键**
- `Cmd + Option + I` (macOS)

**方法 3: 右键菜单**
- 在页面上右键 → `检查元素`

---

### 2. 检查 Console（控制台）

**位置**: 开发者工具 → `控制台` 标签

**查看内容**:
- ❌ **红色错误** - JavaScript 错误
- ⚠️ **黄色警告** - 警告信息
- ℹ️ **蓝色信息** - 日志信息

**常见错误类型**:
```
Failed to fetch          // API 连接失败
TypeError: ...           // JavaScript 类型错误
ReferenceError: ...      // 变量未定义
SyntaxError: ...         // 语法错误
CORS error               // 跨域问题
```

---

### 3. 检查 Network（网络）

**位置**: 开发者工具 → `网络` 标签

**查看内容**:
- 资源加载状态（200 = 成功，404 = 未找到，500 = 服务器错误）
- API 请求状态
- SSE 连接状态（EventSource）

**关键检查项**:
1. **页面资源** (`/`, `/src/main.tsx` 等)
   - 状态码应该是 `200`
   - 如果失败，检查路径和服务器

2. **API 请求** (`/api/sessions`, `/api/dag` 等)
   - 状态码应该是 `200` 或 `201`
   - 如果 `404`，后端可能未运行
   - 如果 `CORS error`，检查 CORS 配置

3. **SSE 连接** (`/api/sessions/{id}/agui/events`)
   - 类型应该是 `eventsource`
   - 状态应该是 `200`（持续连接）
   - 如果失败，检查后端 SSE 端点

---

### 4. 检查 Elements（元素）

**位置**: 开发者工具 → `元素` 标签

**查看内容**:
- HTML 结构是否正确
- `#root` 元素是否存在
- React 组件是否正确挂载

**检查方法**:
```html
<!-- 应该看到 -->
<body>
  <div id="root">
    <!-- React 组件内容 -->
  </div>
</body>
```

如果 `#root` 是空的，说明 React 未正确渲染。

---

## 🔍 Safari 特定问题

### 1. SSE (Server-Sent Events) 支持

**Safari 支持**: ✅ Safari 完全支持 SSE

**检查方法**:
- Network 标签 → 查找 `eventsource` 类型的请求
- 应该看到持续连接（状态 200）

**如果 SSE 失败**:
- 检查后端是否运行
- 检查 CORS 配置
- 检查网络连接

---

### 2. WebSocket 支持

**注意**: 当前应用使用 SSE，不是 WebSocket

**Safari 支持**: ✅ Safari 支持 WebSocket（但应用未使用）

---

### 3. CSS 兼容性

**Safari 特定 CSS**:
代码中已包含 Safari 兼容的 CSS：

```css
/* index.css */
-webkit-font-smoothing: antialiased;  /* Safari 字体平滑 */
-webkit-backdrop-filter: blur(16px);   /* Safari 背景模糊 */
::-webkit-scrollbar { ... }            /* Safari 滚动条样式 */
```

**兼容性**: ✅ 已针对 Safari 优化

---

### 4. JavaScript 特性支持

**使用的现代特性**:
- ES Modules (`import/export`) ✅ Safari 支持
- Async/Await ✅ Safari 支持
- Fetch API ✅ Safari 支持
- EventSource (SSE) ✅ Safari 支持

**React 19**: ✅ Safari 支持

---

## 📊 诊断步骤

### Step 1: 检查应用是否运行

```bash
# 1. 检查后端
curl http://localhost:5678/health
# 应该返回: ok

# 2. 检查前端
curl http://localhost:5173
# 应该返回 HTML 内容

# 3. 检查端口
lsof -i :5678  # 后端
lsof -i :5173  # 前端
```

---

### Step 2: 在 Safari 中打开应用

1. **打开 Safari**
2. **访问**: `http://localhost:5173`
3. **打开开发者工具** (`Cmd + Option + I`)

---

### Step 3: 检查 Console

**查看是否有错误**:

```javascript
// 常见错误示例
Failed to fetch http://localhost:5678/api/sessions
// → 后端未运行或 CORS 问题

Uncaught TypeError: Cannot read property 'xxx' of undefined
// → JavaScript 代码错误

Module not found: Can't resolve '@/components/...'
// → 路径或依赖问题
```

**记录所有错误信息**，特别是：
- 错误类型（TypeError, ReferenceError 等）
- 错误位置（文件名和行号）
- 错误消息

---

### Step 4: 检查 Network

**查看资源加载**:

1. **页面资源**:
   - `/` - 应该 200
   - `/src/main.tsx` - 应该 200
   - CSS/JS 文件 - 应该 200

2. **API 请求**:
   - `/api/sessions` - 应该 200
   - `/api/dag/global` - 应该 200

3. **SSE 连接**:
   - `/api/sessions/{id}/agui/events` - 应该 200 (eventsource)

**如果请求失败**:
- 查看状态码（404, 500, CORS error）
- 查看响应内容
- 检查请求 URL 是否正确

---

### Step 5: 检查 Elements

**查看 HTML 结构**:

```html
<!-- 正常情况 -->
<body>
  <div id="root">
    <div data-reactroot>
      <!-- React 应用内容 -->
    </div>
  </div>
</body>
```

**如果 `#root` 为空**:
- React 未正确挂载
- JavaScript 错误阻止渲染
- 检查 Console 错误

---

## 🐛 常见 Safari 问题

### 问题 1: 页面空白

**可能原因**:
1. JavaScript 错误
2. API 连接失败
3. React 未正确挂载

**解决方法**:
1. 查看 Console 错误
2. 检查 Network 请求状态
3. 检查后端是否运行

---

### 问题 2: API 请求失败

**可能原因**:
1. 后端未运行
2. CORS 配置问题
3. 网络连接问题

**解决方法**:
```bash
# 检查后端
curl http://localhost:5678/health

# 检查 CORS 配置
grep -A 5 "Cors" appsettings.json
```

---

### 问题 3: SSE 连接失败

**可能原因**:
1. 后端未运行
2. SSE 端点错误
3. 网络问题

**解决方法**:
```bash
# 测试 SSE 端点
curl -N http://localhost:5678/api/sessions/{sessionId}/agui/events
```

---

### 问题 4: 样式显示异常

**可能原因**:
1. CSS 未加载
2. Safari 特定样式问题
3. TailwindCSS 未正确编译

**解决方法**:
1. 检查 Network 中 CSS 文件是否加载
2. 检查 Elements 中样式是否正确应用
3. 查看 Console 是否有 CSS 相关错误

---

## 🔧 Safari 开发者工具快捷键

| 操作 | 快捷键 |
|------|--------|
| 打开/关闭开发者工具 | `Cmd + Option + I` |
| 刷新页面 | `Cmd + R` |
| 硬刷新（清除缓存） | `Cmd + Shift + R` |
| 切换到 Console | `Cmd + Option + C` |
| 切换到 Network | `Cmd + Option + R` |
| 切换到 Elements | `Cmd + Option + E` |

---

## 📊 性能分析

### Safari 性能工具

**位置**: 开发者工具 → `时间线` 或 `性能` 标签

**查看内容**:
- 页面加载时间
- JavaScript 执行时间
- 网络请求时间
- 渲染性能

---

## 🔍 快速诊断脚本

创建以下脚本进行快速诊断：

```bash
#!/bin/bash
# Safari 浏览器诊断脚本

echo "=== Safari 浏览器诊断 ==="
echo ""
echo "1. 检查后端:"
curl -s http://localhost:5678/health && echo " ✅" || echo " ❌"

echo ""
echo "2. 检查前端:"
curl -s http://localhost:5173 | head -1 | grep -q "<!doctype" && echo " ✅" || echo " ❌"

echo ""
echo "3. 检查 API:"
curl -s http://localhost:5678/api/sessions | head -1 | grep -q "\[\|\{" && echo " ✅" || echo " ❌"

echo ""
echo "4. Safari 检查建议:"
echo "   - 打开 Safari 开发者工具 (Cmd+Option+I)"
echo "   - 查看 Console 标签的错误"
echo "   - 查看 Network 标签的请求状态"
echo "   - 访问: http://localhost:5173"
```

---

## 📝 Safari 检查清单

在 Safari 中打开应用后，检查以下内容：

- [ ] **页面是否正常加载**（不是空白）
- [ ] **Console 是否有错误**（红色错误）
- [ ] **Network 中资源是否加载成功**（状态 200）
- [ ] **API 请求是否成功**（`/api/sessions` 等）
- [ ] **SSE 连接是否建立**（eventsource 类型，状态 200）
- [ ] **React 组件是否正确渲染**（Elements 中看到内容）
- [ ] **样式是否正确显示**（不是乱码或错位）

---

## 💡 如果发现问题

### 1. 记录错误信息

从 Safari 开发者工具中复制：
- Console 中的所有错误
- Network 中失败的请求详情
- 错误截图（如果有）

### 2. 检查后端日志

```bash
tail -f logs/*.log | grep -i "error\|exception\|failed"
```

### 3. 检查前端构建

```bash
cd sisyphus-frontend
npm run build
# 查看是否有构建错误
```

---

## 🔗 相关文档

- `WHITE_SCREEN_ISSUE.md` - 网页空白问题诊断
- `sisyphus-frontend/README.md` - 前端开发指南
- `docs/FRONTEND_SESSION_API.md` - 前端 API 文档

---

## 📋 总结

**Safari 浏览器分析步骤**:

1. ✅ **打开开发者工具** (`Cmd + Option + I`)
2. ✅ **查看 Console** - 检查 JavaScript 错误
3. ✅ **查看 Network** - 检查资源加载和 API 请求
4. ✅ **查看 Elements** - 检查 HTML 结构和 React 渲染
5. ✅ **记录问题** - 复制错误信息和请求详情

**当前状态**: 后端和前端都未运行，需要先启动应用。

---

*最后更新: 2025-01-28*
