# NovelOS Frontend (Tauri + React)

哥，这个目录是 `novel/` 的桌面前端：**Tauri + React + Monaco**，目标是提供接近 Cursor/VSCode 的写作编辑体验，并通过 **Protobuf 契约**与 `.NET sidecar` 通信（SSE + Protobuf JSON）。

---

## 你本机需要安装什么（macOS）

### 1) Xcode 命令行工具（Tauri 依赖）

```bash
xcode-select --install
```

### 2) Rust 工具链（Tauri 后端）

- 安装 rustup（官方推荐）：`https://rustup.rs`
- 安装后确认：

```bash
rustc --version
cargo --version
```

**最低版本建议**：Rust 依赖链（Tauri v2 + URL/ICU 等）需要较新的 `rustc`。如果你的版本偏旧，直接升级：

```bash
rustup update
```

### 3) Node.js（前端构建）

- 建议 **Node.js 20+**（LTS）

```bash
node -v
npm -v
```

### 4) protoc（用于生成 TypeScript Protobuf 代码）

本项目 UI↔Sidecar 的跨边界消息必须 Protobuf（见 `novel/protos/*.proto`）。

```bash
protoc --version
```

如果你用 Homebrew（推荐）：

```bash
brew install protobuf@3
```

---

## 开发启动（v1）

### 1) 启动 sidecar

```bash
cd novel/src/Aevatar.Novel.Sidecar
dotnet run
```

默认监听一般是 `http://127.0.0.1:5678`（也可通过 `ASPNETCORE_URLS` 覆盖）。
（推荐用 `http://localhost:5678`，避免少数环境下 IPv4/IPv6 loopback 差异。）

### 2) 启动 Tauri 前端

```bash
cd novel/frontend
npm install
npm run dev
```

如果 sidecar 不在默认地址，设置环境变量：

```bash
export VITE_NOVEL_SIDECAR_URL="http://localhost:5678"
```

---

## 智能创作（Smart Continue，v1）

前端右侧「智能体」面板支持 **智能续写一段**：会把 `rules/objects/roles` + 当前文件草稿尾部作为上下文交给 LLM，然后返回一段续写内容并自动插入到当前文件末尾（不自动保存）。

### 1) 配置 API Key（推荐用环境变量）

Sidecar 读取 `NovelAI` 配置（`novel/src/Aevatar.Novel.Sidecar/appsettings.json`），也支持用环境变量覆盖：

```bash
# OpenAI-compatible endpoint (DeepSeek/OpenAI/自建网关等)
export NovelAI__Endpoint="https://api.deepseek.com"
export NovelAI__ApiKey="YOUR_KEY"
export NovelAI__Model="deepseek-chat"
```

### 2) 使用方式

- **打开一个正文/设定/规则文件**
- 在右侧输入作者指令，按 Enter 或点「续写」
- 或直接点「智能续写」（不带指令）

---

## 关键设计（为什么这么做）

- **文件是唯一真相源（SSOT）**：编辑器直接读写 `.txt/.md` 文件；sidecar 只做监听、索引和派生产物。
- **跨边界消息只认 Protobuf**：HTTP 传 Protobuf JSON，SSE 推 Protobuf JSON（见 `novel/protos/novel_sidecar.proto` + `novel/src/Aevatar.Novel.Sidecar/Program.cs`）。


