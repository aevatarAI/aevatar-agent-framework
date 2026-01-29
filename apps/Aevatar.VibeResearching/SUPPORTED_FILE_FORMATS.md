# 支持的文件格式

## 📋 概述

运行 `boot.sh` 后，用户可以通过文件上传功能上传文件。系统支持两种上传方式，每种方式支持不同的文件格式。

---

## 🎯 文件上传端点

### 1. 普通文件上传 (`/api/sessions/{sessionId}/uploads`)

**用途**: 上传文件作为附件，不进行知识提取

**支持的文件格式**:

| 类型 | 扩展名 | 说明 |
|------|--------|------|
| **文本文件** | `.md`, `.txt`, `.json` | Markdown、纯文本、JSON 文件 |
| **PDF 文档** | `.pdf` | PDF 文档 |
| **图片文件** | `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp` | 图片文件（不提取文本） |

**限制**:
- 单个文件最大: **15MB**
- 每次请求最多: **12 个文件**

**代码位置**: `UploadsStore.cs` (第 18-23 行)

---

### 2. 知识提取上传 (`/api/sessions/{sessionId}/uploads/extract`)

**用途**: 上传文件并自动提取知识要点，创建 KnowledgeNode

**支持的文件格式**:

| 类型 | 扩展名 | 说明 |
|------|--------|------|
| **文本文件** | `.txt` | 纯文本文件 |
| **Markdown** | `.md` | Markdown 文档 |
| **JSON** | `.json` | JSON 数据文件 |
| **CSV** | `.csv` | CSV 表格文件 |
| **PDF 文档** | `.pdf` | PDF 文档（文本提取） |

**限制**:
- 单个文件最大: **15MB**
- PDF 文本提取限制: 最多 **50,000 字符**
- PDF 文件大小警告: > 5MB 可能提取失败

**代码位置**: 
- `FileTextParser.cs` (第 10-18 行)
- `ResearchSessionsApi.Runtime.cs` (第 97-117 行)

---

## 📝 文件格式详细说明

### 文本文件 (`.txt`, `.md`, `.json`, `.csv`)

**处理方式**:
- 直接读取文件内容
- 使用 UTF-8 编码（自动检测 BOM）
- 最大读取: 50,000 字符（超出部分会被截断）

**支持情况**:
- ✅ 普通上传: `.md`, `.txt`, `.json`
- ✅ 知识提取: `.txt`, `.md`, `.json`, `.csv`

---

### PDF 文档 (`.pdf`)

**处理方式**:
- 使用简单的 PDF 文本提取算法
- 提取 PDF 中的文本内容
- 转换为文本后进行分析

**限制**:
- ⚠️ **仅支持简单、未压缩的 PDF**
- ❌ 不支持加密的 PDF
- ❌ 不支持图像 PDF（扫描版）
- ❌ 不支持复杂压缩格式（FlateDecode、LZWDecode 等）
- ⚠️ 大文件（> 5MB）可能提取失败
- ⚠️ 非 ASCII 字符可能丢失（使用 ASCII 编码）

**代码位置**: `FileTextParser.cs` → `ExtractFromPdfAsync` (第 108-166 行)

**改进建议**:
- 生产环境建议使用 PdfPig 库（已在代码库中）
- 参考: `PDF_EXTRACTION_ISSUE.md`

---

### 图片文件 (`.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`)

**处理方式**:
- 仅作为附件上传
- **不进行文本提取**
- **不进行知识提取**

**支持情况**:
- ✅ 普通上传: 支持
- ❌ 知识提取: **不支持**

**代码位置**: `UploadsStore.cs` (第 18-23 行)

---

## 🔍 代码实现

### 文件格式检查

**普通上传** (`UploadsStore.cs`):
```csharp
private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".md", ".txt", ".json",
    ".png", ".jpg", ".jpeg", ".gif", ".webp",
    ".pdf"
};
```

**知识提取** (`FileTextParser.cs`):
```csharp
private static readonly HashSet<string> SupportedTextExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".txt", ".md", ".json", ".csv"
};

private static readonly HashSet<string> SupportedBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".pdf"
};
```

---

## 📊 格式支持对比表

| 文件格式 | 普通上传 | 知识提取 | 说明 |
|---------|---------|---------|------|
| `.txt` | ✅ | ✅ | 纯文本文件 |
| `.md` | ✅ | ✅ | Markdown 文档 |
| `.json` | ✅ | ✅ | JSON 数据 |
| `.csv` | ❌ | ✅ | CSV 表格（仅知识提取） |
| `.pdf` | ✅ | ✅ | PDF 文档（有限支持） |
| `.png` | ✅ | ❌ | 图片（仅普通上传） |
| `.jpg` | ✅ | ❌ | 图片（仅普通上传） |
| `.jpeg` | ✅ | ❌ | 图片（仅普通上传） |
| `.gif` | ✅ | ❌ | 图片（仅普通上传） |
| `.webp` | ✅ | ❌ | 图片（仅普通上传） |

---

## 🚀 使用示例

### 前端调用（知识提取）

```typescript
import { uploadWithExtraction } from '@/lib/axiom-client'

// 上传文件并提取知识
const file = document.querySelector('input[type="file"]').files[0]
const result = await uploadWithExtraction(sessionId, file, {
  providerName: 'deepseek', // 可选
  maxKnowledgePoints: 0 // 0 = 无限制，提取所有
})
```

**支持的文件**: `.txt`, `.md`, `.json`, `.csv`, `.pdf`

---

### 前端调用（普通上传）

```typescript
// 通过表单上传
const formData = new FormData()
formData.append('file', file)

const response = await fetch(`/api/sessions/${sessionId}/uploads`, {
  method: 'POST',
  body: formData
})
```

**支持的文件**: `.md`, `.txt`, `.json`, `.pdf`, `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`

---

## ⚠️ 常见问题

### Q1: 为什么 CSV 文件只能用于知识提取？

**答案**: CSV 文件在 `UploadsStore` 的允许列表中不存在，但在 `FileTextParser` 中支持。如果需要上传 CSV 作为附件，需要修改 `UploadsStore.cs`。

---

### Q2: PDF 提取失败怎么办？

**可能原因**:
1. PDF 使用了压缩（FlateDecode）
2. PDF 是扫描版（图像）
3. PDF 已加密
4. 文件太大（> 5MB）

**解决方案**:
- 参考: `PDF_EXTRACTION_ISSUE.md`
- 建议使用 PdfPig 库改进提取

---

### Q3: 如何添加新的文件格式支持？

**步骤**:

1. **普通上传**: 修改 `UploadsStore.cs`
   ```csharp
   private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
   {
       // ... 现有格式
       ".docx", ".xlsx" // 添加新格式
   };
   ```

2. **知识提取**: 修改 `FileTextParser.cs`
   ```csharp
   // 添加文本格式
   private static readonly HashSet<string> SupportedTextExtensions = new(StringComparer.OrdinalIgnoreCase)
   {
       // ... 现有格式
       ".docx" // 添加新格式
   };
   
   // 或添加二进制格式
   private static readonly HashSet<string> SupportedBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
   {
       // ... 现有格式
       ".xlsx" // 添加新格式
   };
   ```

3. **实现提取逻辑**: 在 `ExtractTextAsync` 方法中添加处理逻辑

---

## 📋 文件大小限制

| 限制项 | 值 | 说明 |
|--------|-----|------|
| **单个文件最大** | 15MB | 所有文件格式 |
| **每次请求最多文件数** | 12 个 | 普通上传 |
| **文本提取最大字符数** | 50,000 | PDF/文本文件 |
| **PDF 大小警告阈值** | 5MB | 超过可能提取失败 |

**代码位置**: `UploadsStore.cs` (第 25-26 行)

---

## 🔧 验证文件格式

### 后端验证

```csharp
// 检查是否支持知识提取
if (!FileTextParser.IsSupported(file.FileName))
    return Results.BadRequest(new { error = $"unsupported file type: {Path.GetExtension(file.FileName)}" });

// 检查是否允许上传
var ext = Path.GetExtension(name);
if (!AllowedExtensions.Contains(ext))
    throw new ArgumentException($"unsupported file extension: '{ext}'", nameof(file));
```

---

## 📚 相关文档

- `PDF_EXTRACTION_ISSUE.md` - PDF 提取问题分析
- `NO_KNOWLEDGE_POINTS_ISSUE.md` - 知识提取失败问题
- `FileTextParser.cs` - 文件文本提取实现
- `UploadsStore.cs` - 文件上传存储实现

---

*最后更新: 2025-01-28*
