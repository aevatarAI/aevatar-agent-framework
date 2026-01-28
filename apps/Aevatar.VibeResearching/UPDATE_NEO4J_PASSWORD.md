# Neo4j 密码更新指南

## 📋 概述

本文档说明如何更新 Neo4j 密码，并确保 `boot.sh` 中的配置与实际密码一致。

---

## 🔍 当前配置

**boot.sh 中的默认密码** (第 144 行):
```bash
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI}"
```

**说明**: 
- 如果环境变量 `NEO4J_PASSWORD` 已设置，使用环境变量的值
- 否则使用默认值：`gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI`

---

## ✅ 更新密码的方法

### 方法 1: 使用 change_neo4j_password.sh（推荐）

**前提**: 你需要知道当前的 Neo4j 密码

**步骤**:

1. **运行脚本**:
   ```bash
   cd apps/Aevatar.VibeResearching
   ./change_neo4j_password.sh
   ```

2. **按提示输入**:
   - 当前密码
   - 新密码
   - 确认新密码

3. **脚本会自动修改密码**

4. **更新 boot.sh**:
   ```bash
   # 编辑 boot.sh，修改第 144 行
   export NEO4J_PASSWORD="${NEO4J_PASSWORD:-你的新密码}"
   ```

5. **重启后端**:
   ```bash
   ./boot.sh
   ```

---

### 方法 2: 使用 cypher-shell 直接修改

**前提**: 你需要知道当前的 Neo4j 密码

**步骤**:

1. **修改密码**:
   ```bash
   echo "ALTER USER neo4j SET PASSWORD '你的新密码';" | \
     cypher-shell -u neo4j -p '当前密码' -a bolt://localhost:7687
   ```

2. **验证密码**:
   ```bash
   echo "RETURN 1 as test;" | \
     cypher-shell -u neo4j -p '你的新密码' -a bolt://localhost:7687
   ```

3. **更新 boot.sh**:
   ```bash
   # 编辑 boot.sh，修改第 144 行
   export NEO4J_PASSWORD="${NEO4J_PASSWORD:-你的新密码}"
   ```

4. **重启后端**:
   ```bash
   ./boot.sh
   ```

---

### 方法 3: 通过 Neo4j Browser（图形界面）

**前提**: 你可以访问 Neo4j Browser

**步骤**:

1. **打开 Neo4j Browser**:
   ```
   http://localhost:7474
   ```

2. **登录**:
   - 用户名: `neo4j`
   - 密码: 当前密码

3. **运行 Cypher 命令**:
   ```cypher
   ALTER USER neo4j SET PASSWORD '你的新密码';
   ```

4. **更新 boot.sh**:
   ```bash
   # 编辑 boot.sh，修改第 144 行
   export NEO4J_PASSWORD="${NEO4J_PASSWORD:-你的新密码}"
   ```

5. **重启后端**:
   ```bash
   ./boot.sh
   ```

---

### 方法 4: 使用环境变量（临时，不推荐）

**适用场景**: 临时测试，不想修改 boot.sh

**步骤**:

1. **设置环境变量**:
   ```bash
   export NEO4J_PASSWORD="你的新密码"
   ```

2. **运行 boot.sh**:
   ```bash
   ./boot.sh
   ```

**注意**: 
- 这种方式只在当前终端会话有效
- 关闭终端后需要重新设置
- 不推荐用于生产环境

---

## 🔧 更新 boot.sh 配置

### 步骤 1: 编辑 boot.sh

```bash
# 使用你喜欢的编辑器
vim boot.sh
# 或
nano boot.sh
# 或
code boot.sh  # VS Code
```

### 步骤 2: 找到第 144 行

```bash
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-gVwt5cwZ-vKisdvGS6z6r94iE9DXE1yV_yvPL-vs5qI}"
```

### 步骤 3: 修改为新密码

```bash
export NEO4J_PASSWORD="${NEO4J_PASSWORD:-你的新密码}"
```

**说明**:
- 保留 `${NEO4J_PASSWORD:-...}` 格式，这样环境变量优先
- 将默认值改为你的新密码

---

## 🧪 测试连接

### 方法 1: 使用 cypher-shell

```bash
echo "RETURN 1 as test;" | \
  cypher-shell -u neo4j -p '你的新密码' -a bolt://localhost:7687
```

**成功输出**: 应该看到 `1` 或类似的结果

**失败输出**: 会显示认证错误

---

### 方法 2: 使用 HTTP API

```bash
curl -u neo4j:你的新密码 http://localhost:7474/user/neo4j
```

**成功输出**: 返回用户信息 JSON

**失败输出**: 返回 401 Unauthorized

---

### 方法 3: 使用 verify_neo4j_connection.sh

```bash
export NEO4J_PASSWORD="你的新密码"
./verify_neo4j_connection.sh
```

---

## 📋 验证修复

更新密码后，检查以下内容：

### 1. 后端日志

**检查是否有认证错误**:
```bash
# 查看日志
grep -i "authentication\|unauthorized\|Failed to query Neo4j" logs/*.log
```

**应该看到**:
- ✅ `[DagStore] BuildSnapshotFromGraphAsync (global): knowledgeNodes=...`
- ❌ 不应该看到: `Failed to query Neo4j for global DAG: ... authentication failure`

---

### 2. DAG API 查询

```bash
curl http://localhost:5678/api/dag/global | jq '.dag.nodes | length'
```

**应该返回**: 节点数量（可能为 0，但不会报错）

---

### 3. 创建测试节点

运行一次 research round，检查是否能创建节点：

```bash
# 查看日志
grep -i "Creating PlanNode\|Creating KnowledgeNode" logs/*.log
```

---

## 🔄 重启后端

**重要**: 修改密码后，**必须重启后端**才能生效！

### 步骤 1: 停止当前后端

```bash
# 查找后端进程
ps aux | grep dotnet | grep VibeResearching

# 停止进程（替换 PID）
kill <PID>
```

### 步骤 2: 重新运行 boot.sh

```bash
./boot.sh
```

---

## 💡 常见问题

### Q1: 忘记当前密码怎么办？

**解决方案**:

1. **使用 reset_neo4j_password.sh**（会重置数据库）:
   ```bash
   ./reset_neo4j_password.sh
   ```

2. **或手动重置**:
   - 停止 Neo4j: `neo4j stop`
   - 删除认证文件: `rm /opt/homebrew/var/neo4j/data/dbms/auth`
   - 重启 Neo4j: `neo4j start`
   - 首次登录设置新密码（初始密码: `neo4j`）

---

### Q2: 修改密码后仍然连接失败？

**检查清单**:

1. ✅ Neo4j 服务是否运行？
   ```bash
   neo4j status
   ```

2. ✅ 密码是否正确？
   ```bash
   echo "RETURN 1;" | cypher-shell -u neo4j -p '你的密码' -a bolt://localhost:7687
   ```

3. ✅ boot.sh 中的密码是否更新？
   ```bash
   grep NEO4J_PASSWORD boot.sh
   ```

4. ✅ 后端是否重启？
   ```bash
   # 检查后端进程是否使用新配置
   ps aux | grep dotnet
   ```

---

### Q3: 如何在不修改 boot.sh 的情况下使用不同密码？

**使用环境变量**:

```bash
export NEO4J_PASSWORD="你的密码"
./boot.sh
```

**注意**: 环境变量的优先级高于 boot.sh 中的默认值

---

## 📊 总结

### 更新密码的完整流程

1. ✅ **修改 Neo4j 密码**（使用上述方法之一）
2. ✅ **更新 boot.sh**（修改第 144 行）
3. ✅ **测试连接**（使用 cypher-shell 或 HTTP API）
4. ✅ **重启后端**（确保新配置生效）
5. ✅ **验证修复**（检查日志和 DAG API）

### 推荐方法

- **如果知道当前密码**: 使用方法 1（`change_neo4j_password.sh`）
- **如果忘记密码**: 使用方法 3（重置认证文件）
- **临时测试**: 使用方法 4（环境变量）

---

*最后更新: 2025-01-28*
