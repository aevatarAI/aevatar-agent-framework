# 如何更新 Cursor IDE 到最新版本

## 📋 概述

Cursor IDE 是基于 VS Code 的 AI 代码编辑器。本文档说明如何在 macOS 上更新 Cursor 到最新版本。

---

## 🔄 更新方法

### 方法 1: 自动更新（推荐）

**Cursor 通常会自动检查更新**：

1. **打开 Cursor**
2. **检查更新菜单**：
   - 点击菜单栏：`Cursor` → `Check for Updates...`
   - 或者使用快捷键：`Cmd + Shift + P` → 输入 "Check for Updates"

3. **如果有更新可用**：
   - Cursor 会提示您有新版本
   - 点击 "Update" 或 "Restart to Update"
   - Cursor 会自动下载并安装更新

---

### 方法 2: 手动下载更新

如果自动更新不起作用，可以手动下载：

1. **访问 Cursor 官网**：
   - 打开浏览器，访问：https://cursor.sh/
   - 或者直接访问下载页面

2. **下载最新版本**：
   - 点击 "Download" 按钮
   - 选择 macOS 版本（Intel 或 Apple Silicon）

3. **安装更新**：
   - 下载完成后，打开 `.dmg` 文件
   - 将 `Cursor.app` 拖拽到 `/Applications` 文件夹
   - 如果提示替换，点击 "Replace"

4. **重启 Cursor**：
   - 关闭所有 Cursor 窗口
   - 重新打开 Cursor

---

### 方法 3: 使用 Homebrew（如果通过 Homebrew 安装）

如果您是通过 Homebrew 安装的 Cursor：

```bash
# 更新 Homebrew
brew update

# 升级 Cursor
brew upgrade --cask cursor
```

---

## 🔍 检查当前版本

### 方法 1: 通过 Cursor 菜单

1. 打开 Cursor
2. 点击菜单栏：`Cursor` → `About Cursor`
3. 查看版本号

### 方法 2: 通过命令行

```bash
# 检查版本
/usr/bin/defaults read /Applications/Cursor.app/Contents/Info.plist CFBundleShortVersionString

# 或者查看完整信息
/usr/libexec/PlistBuddy -c "Print CFBundleShortVersionString" /Applications/Cursor.app/Contents/Info.plist
```

### 方法 3: 通过 Cursor 命令面板

1. 按 `Cmd + Shift + P`
2. 输入 "About"
3. 选择 "About Cursor"
4. 查看版本信息

---

## ⚙️ 更新设置

### 启用自动更新

1. 打开 Cursor
2. 按 `Cmd + ,` 打开设置
3. 搜索 "update"
4. 确保以下设置已启用：
   - `update.mode`: `default` 或 `start`
   - `update.enableWindowsBackgroundUpdates`: `true`（如果适用）

---

## 🐛 更新问题排查

### 问题 1: 更新失败

**可能原因**:
- 网络连接问题
- 权限问题
- 磁盘空间不足

**解决方法**:
1. 检查网络连接
2. 确保有足够的磁盘空间（至少 500MB）
3. 尝试手动下载更新

---

### 问题 2: 更新后无法启动

**可能原因**:
- 更新过程中文件损坏
- 扩展冲突

**解决方法**:
1. **完全卸载并重新安装**:
   ```bash
   # 删除 Cursor
   rm -rf /Applications/Cursor.app
   
   # 删除用户数据（可选，会丢失设置）
   rm -rf ~/Library/Application\ Support/Cursor
   
   # 重新下载并安装
   ```

2. **检查扩展**:
   - 禁用所有扩展
   - 逐个启用，找出冲突的扩展

---

### 问题 3: 更新后设置丢失

**解决方法**:
1. **备份设置**:
   ```bash
   # 备份设置目录
   cp -r ~/Library/Application\ Support/Cursor ~/Library/Application\ Support/Cursor.backup
   ```

2. **恢复设置**:
   ```bash
   # 恢复设置
   cp -r ~/Library/Application\ Support/Cursor.backup/User ~/Library/Application\ Support/Cursor/User
   ```

---

## 📝 更新日志

### 查看更新日志

1. 访问 Cursor 官网：https://cursor.sh/
2. 查看 "Changelog" 或 "Release Notes"
3. 了解新功能和修复

---

## 💡 最佳实践

### 1. 定期更新

- **建议**: 每周检查一次更新
- **原因**: 获得最新功能和 bug 修复

### 2. 更新前备份

- **备份设置**: `~/Library/Application Support/Cursor/User/settings.json`
- **备份扩展列表**: 记录已安装的扩展

### 3. 测试更新

- **更新后**: 测试常用功能
- **如有问题**: 查看更新日志，了解是否有破坏性更改

---

## 🔗 相关链接

- **Cursor 官网**: https://cursor.sh/
- **下载页面**: https://cursor.sh/download
- **GitHub**: https://github.com/getcursor/cursor
- **文档**: https://docs.cursor.sh/

---

## 📊 版本检查命令

### 快速检查版本

```bash
# 检查 Cursor 版本
/usr/bin/defaults read /Applications/Cursor.app/Contents/Info.plist CFBundleShortVersionString

# 检查 Cursor 构建号
/usr/bin/defaults read /Applications/Cursor.app/Contents/Info.plist CFBundleVersion

# 查看完整信息
plutil -p /Applications/Cursor.app/Contents/Info.plist | grep -E "CFBundleShortVersionString|CFBundleVersion"
```

---

## 🎯 总结

### 推荐更新流程

1. **自动更新**（最简单）:
   - `Cursor` → `Check for Updates...`
   - 点击 "Update"

2. **手动下载**（如果自动更新失败）:
   - 访问 https://cursor.sh/
   - 下载最新版本
   - 拖拽到 `/Applications`

3. **Homebrew**（如果通过 Homebrew 安装）:
   - `brew upgrade --cask cursor`

### 更新后检查

- ✅ 版本号是否正确
- ✅ 设置是否保留
- ✅ 扩展是否正常
- ✅ 常用功能是否正常

---

*最后更新: 2025-01-28*
