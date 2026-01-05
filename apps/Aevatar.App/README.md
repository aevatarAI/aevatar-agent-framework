# App

业务服务器 - 专注业务逻辑，认证授权统一由AuthServer管理。

## 技术栈

- **ABP Framework**: 9.3.1
- **MongoDB Driver**: 3.0.0
- **.NET**: 9.0
- **OpenIddict**: OAuth 2.0 / OpenID Connect
- **LeptonXLite**: 4.3.1 (UI Theme)

## 快速启动

### 0. 前置准备

```bash
# 安装 AuthServer 前端依赖
cd src/Aevatar.AuthServer
npm install
```

### 1. 初始化数据库

```bash
cd src/Aevatar.App.DbMigrator
dotnet run
```

### 2. 启动 AuthServer (认证中心)

```bash
cd src/Aevatar.AuthServer
dotnet run
```

- 访问: https://localhost:44320
- 功能: 用户/角色/权限管理、OAuth 授权

### 3. 启动 App (业务服务)

```bash
cd src/Aevatar.App.HttpApi.Host
dotnet run
```

- 访问: https://localhost:44345/swagger
- 功能: 业务 API + JWT 认证

### 4. 默认账号

```
用户名: admin
密码: 1q2w3E*
```

---

## 架构

```
AuthServer (44320)           App (44345)
     ↓                              ↓
OpenID Connect Server          JWT Bearer 验证
用户/角色/权限管理               业务 API
LeptonXLite UI                  Swagger UI
         ↓                          ↓
    共享数据库 (MongoDB: AevatarBusiness)
```

### 核心组件

- **AuthServer**: 
  - OpenIddict Server (OAuth 2.0 / OIDC)
  - ABP Identity (用户/角色管理)
  - LeptonXLite 主题 (管理界面)
  - DataTables, Bootstrap 5 等前端组件

- **App**: 
  - 纯 API 服务（无 UI）
  - JWT Bearer 认证
  - 业务逻辑实现
  - Swagger API 文档

---

## API调用

### 认证测试API

```bash
# 1. 公共API（无需认证）
curl -k https://localhost:44345/api/app/auth-test/public-data

# 2. 获取Token
TOKEN=$(curl -k -s -X POST https://localhost:44320/connect/token \
  -d "grant_type=password" \
  -d "username=admin" \
  -d "password=1q2w3E*" \
  -d "client_id=App_App" \
  -d "scope=openid profile email roles Aevatar" \
  | jq -r '.access_token')

# 3. 受保护API（需要认证）
curl -k -H "Authorization: Bearer $TOKEN" \
  https://localhost:44345/api/app/auth-test/authenticated-data
```

测试结果：
- 无Token访问受保护API → HTTP 401 ✓
- 有Token访问受保护API → HTTP 200 ✓

---

## 配置说明

**appsettings.json:**
- `ConnectionStrings:Default`: mongodb://localhost:27017/AevatarBusiness (共享数据库)
- `AuthServer:Authority`: https://localhost:44320/
- `AuthServer:RequireHttpsMetadata`: false (开发环境)

### Runtime & Stream Configuration Conventions

To keep configs consistent across `Aevatar.Silo` and `Aevatar.App.HttpApi.Host`, use the following conventions:

- **Orleans runtime (cluster)**
  - `AgentRuntime:RuntimeType`: `Orleans` (or `Local` for local mode)
  - `AgentRuntime:Orleans:ClusterId` / `AgentRuntime:Orleans:ServiceId`: must match between Silo and Host
  - `AgentRuntime:Orleans:SiloPort` / `AgentRuntime:Orleans:GatewayPort`: Silo-only (Host does not need these)

- **Stream provider selection (streaming is MassTransit-only)**
  - `MessageStream:Provider`: set to `MassTransit`
  - When `MessageStream:Provider=MassTransit`, **Orleans Streaming is not used** (no Orleans Stream fallback in HttpApi.Host).

- **Kafka base config (single source of truth)**
  - `Kafka:BootstrapServers`: used by the MassTransit stream plugin
  - `MassTransit:Stream:Kafka:ConsumerGroupId`: keep per-runtime values (Silo and Host should use different consumer groups)

**认证方案:**
- JWT Bearer: API请求
- Cookie: Web UI请求
- Smart Selector: 根据请求头自动选择

---

## 权限管理

### 在AuthServer UI管理
1. 访问 https://localhost:44320
2. Administration → Identity → Roles/Users
3. Actions → Permissions

### Client API权限

如需API权限（如 `/api/identity/users`），需配置Client权限：

```javascript
// MongoDB命令
use AevatarBusiness
db.AbpPermissionGrants.insertMany([
  {
    TenantId: null,
    Name: "AbpIdentity.Users",
    ProviderName: "C",
    ProviderKey: "App_App",
    Granted: true
  }
  // ... 其他权限
])
```

或在代码中配置（推荐）：`OpenIddictDataSeedContributor.cs`

---

## 前端库管理

### AuthServer (有 UI)

使用 **npm** 管理前端依赖：

```bash
cd src/Aevatar.AuthServer
npm install  # 根据 package.json 安装依赖
```

安装后会生成 `wwwroot/libs/` 目录，包含：
- ABP Framework 核心 JS
- Bootstrap 5 (UI 框架)
- DataTables (数据表格)
- jQuery & Validation
- Luxon (日期时间，替代 moment.js)
- Font Awesome (图标)

**注意**: `wwwroot/libs/*` 已被 `.gitignore` 排除（除手动创建的兼容层外），每次克隆代码后需运行 `npm install`。

### App (无 UI)

纯 API 服务，不需要前端库。

---

## 中央包管理 (CPM)

项目使用 **Central Package Management** 统一管理 NuGet 包版本：

- 版本定义: `/Directory.Packages.props`（根目录）
- 项目引用: 无需指定 `Version` 属性

更新所有包版本：
```bash
# 只需修改 Directory.Packages.props 中的版本号
<PackageVersion Include="Volo.Abp.Core" Version="9.3.1" />
```

---

## 常见问题

### Q: MongoDB GUID 格式错误

**错误**: `Expected BsonBinarySubType to be UuidStandard, but it is UuidLegacy`

**原因**: MongoDB Driver 3.0.0 使用 `UuidStandard` 格式

**解决**: 
```bash
mongosh AevatarBusiness --eval "db.dropDatabase()"
cd src/Aevatar.App.DbMigrator && dotnet run
```

### Q: DataTables 文件找不到

**错误**: `Could not find '/libs/datatables.net/js/dataTables.min.js'`

**解决**: 
```bash
cd src/Aevatar.AuthServer
npm install
```

### Q: LeptonXLite 版本不匹配

确保版本一致：
- `package.json`: `@abp/aspnetcore.mvc.ui.theme.leptonxlite: ~4.3.1`
- `Directory.Packages.props`: `Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite: 4.3.1`

---

更多信息: 
- ABP Framework: https://docs.abp.io/
- OpenIddict: https://documentation.openiddict.com/
