# Neo4j 密码重置指南

## 🔴 忘记密码？重置 Neo4j 密码

如果你忘记了 Neo4j 密码，可以使用以下方法重置。

---

## ⚠️ 重要提示

**重置密码的影响**:
- ✅ 会重置认证信息
- ⚠️ **不会删除数据库数据**（只重置认证）
- ✅ 重置后可以使用初始密码 `neo4j` 登录

---

## 方法 1: 使用重置脚本（推荐）

### 步骤 1: 运行重置脚本

```bash
cd /Users/chronoai/aevatar-agent-framework/apps/Aevatar.VibeResearching
./reset_neo4j_password.sh
```

脚本会：
1. 停止 Neo4j
2. 备份并删除认证文件
3. 重启 Neo4j
4. 提示你下一步操作

### 步骤 2: 等待 Neo4j 启动

```bash
# 等待 10-30 秒
sleep 15

# 检查状态
neo4j status
```

### 步骤 3: 登录并设置新密码

**选项 A: 使用 Neo4j Browser（推荐）**

1. 打开浏览器: **http://localhost:7474**
2. 使用以下凭据登录：
   - **用户名**: `neo4j`
   - **初始密码**: `neo4j`
3. 首次登录会要求修改密码
4. 设置新密码（建议使用: `gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI`）

**选项 B: 使用 Cypher Shell**

```bash
# 使用初始密码登录
cypher-shell -u neo4j -p 'neo4j' -a bolt://localhost:7687

# 在交互式 shell 中运行
ALTER USER neo4j SET PASSWORD '你的新密码';
```

### 步骤 4: 更新 boot.sh 中的密码

编辑 `boot.sh` 第 144 行：

```bash
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-你的新密码}"
```

### 步骤 5: 验证新密码

```bash
# 测试连接
echo "RETURN 1;" | \
  cypher-shell -u neo4j -p '你的新密码' -a bolt://localhost:7687

# 或运行测试脚本
./test_neo4j.sh
```

---

## 方法 2: 手动重置（如果脚本不工作）

### 步骤 1: 停止 Neo4j

```bash
neo4j stop
```

### 步骤 2: 删除认证文件

**macOS Homebrew 安装**:
```bash
rm /opt/homebrew/var/neo4j/data/dbms/auth
```

**其他安装方式**:
```bash
# 检查数据目录
ls -la /opt/homebrew/var/neo4j/data/dbms/
# 或
ls -la /usr/local/var/neo4j/data/dbms/
```

### 步骤 3: 启动 Neo4j

```bash
neo4j start

# 等待启动
sleep 15
neo4j status
```

### 步骤 4: 登录并设置新密码

参考方法 1 的步骤 3。

---

## 方法 3: Neo4j 5.x（如果上述方法不工作）

Neo4j 5.x 可能将认证信息存储在数据库中，无法通过删除文件重置。

### 选项 A: 重置整个数据库（⚠️ 会丢失所有数据）

```bash
# 停止 Neo4j
neo4j stop

# 删除数据库（⚠️ 警告：会丢失所有数据）
rm -rf /opt/homebrew/var/neo4j/data/databases/neo4j
rm -rf /opt/homebrew/var/neo4j/data/transactions/neo4j

# 启动 Neo4j
neo4j start

# 等待启动后，使用初始密码 neo4j 登录并设置新密码
```

### 选项 B: 如果你知道当前密码

```bash
# 使用当前密码登录
cypher-shell -u neo4j -p '当前密码' -a bolt://localhost:7687

# 修改密码
ALTER USER neo4j SET PASSWORD '新密码';
```

---

## 📋 完整重置流程

```bash
# 1. 进入项目目录
cd /Users/chronoai/aevatar-agent-framework/apps/Aevatar.VibeResearching

# 2. 运行重置脚本
./reset_neo4j_password.sh

# 3. 等待 Neo4j 启动（约 15-30 秒）
sleep 20

# 4. 检查状态
neo4j status

# 5. 打开 Neo4j Browser 设置新密码
# http://localhost:7474
# 用户名: neo4j
# 初始密码: neo4j

# 6. 更新 boot.sh
# 编辑 boot.sh 第 144 行，设置新密码

# 7. 验证
./test_neo4j.sh
```

---

## 🔍 验证重置是否成功

### 1. 检查服务状态

```bash
neo4j status
```

**预期**: `Neo4j is running at pid <PID>`

### 2. 使用初始密码登录

```bash
echo "RETURN 1;" | \
  cypher-shell -u neo4j -p 'neo4j' -a bolt://localhost:7687
```

**预期**: 返回 `1`（如果还未修改密码）

### 3. 使用新密码登录

```bash
echo "RETURN 1;" | \
  cypher-shell -u neo4j -p '你的新密码' -a bolt://localhost:7687
```

**预期**: 返回 `1`

---

## 💡 常见问题

### Q1: 重置后仍然无法登录？

**检查**:
1. Neo4j 是否完全启动（等待 30 秒）
2. 是否使用了正确的初始密码 `neo4j`
3. 查看 Neo4j 日志: `tail -f /opt/homebrew/var/log/neo4j/neo4j.log`

### Q2: 重置后数据库数据还在吗？

**答案**: 是的，重置密码**不会删除数据库数据**，只会重置认证信息。

### Q3: 如何确认是 Neo4j 4.x 还是 5.x？

```bash
# 检查版本
neo4j version

# 或查看 HTTP API
curl http://localhost:7474 | grep neo4j_version
```

### Q4: 重置后如何恢复数据？

**答案**: 如果数据丢失，需要从备份恢复。重置密码本身不会删除数据。

---

## 🚀 推荐密码

建议使用与 `boot.sh` 中一致的密码，方便管理：

```bash
gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI
```

---

## 📝 重置后检查清单

- [ ] Neo4j 服务正在运行
- [ ] 可以使用初始密码 `neo4j` 登录
- [ ] 已设置新密码
- [ ] `boot.sh` 中的密码已更新
- [ ] 使用新密码可以连接
- [ ] 后端应用可以正常连接 Neo4j

---

*最后更新: 2025-01-28*
