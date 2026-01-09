## Architecture (src/Aevatar.Secrets.Client)

目的：为 “Secrets API” 提供强类型 .NET 访问层，统一错误处理与 DTO，避免上层应用重复造轮子。

### 文件结构

```
src/Aevatar.Secrets.Client/
├── Aevatar.Secrets.Client.csproj
├── AevatarSecretsClient.cs      # HttpClient wrapper + DTO
├── README.md
└── docs/README.md               # 本文
```


