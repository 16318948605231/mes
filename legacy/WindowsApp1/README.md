# MES 系统交互框架 - 功能说明文档 (v2.0)

## ?? 项目概述

这是一个**完整、通用、易于扩展**的 MES（Manufacturing Execution System）系统交互框架，基于 VB.NET 构建。

框架提供了与 MES 系统进行 HTTP 通信、文件上传、数据转换、验证等一站式解决方案。

**?? v2.0 新增功能：**
- ? 完整的 JSON 序列化/反序列化（基于 Newtonsoft.Json）
- ? 泛型 HTTP 响应（自动反序列化）
- ? 取消令牌支持（可取消请求和上传）
- ? 缓存机制（内存缓存）
- ? 速率限制（防止频繁请求）
- ? HTTP 自动重定向
- ? 断点续传状态持久化

---

## ?? 核心功能

### 1. **HTTP 客户端**（MesHttpClient）
- ? 支持所有标准 HTTP 方法（GET、POST、PUT、DELETE、PATCH、HEAD、OPTIONS）
- ? **泛型响应支持**（自动反序列化 JSON 到指定类型）
- ? 内置自动重试机制
- ? 超时处理
- ? **取消令牌支持**（可取消长时间请求）
- ? 多种身份认证方式（Bearer Token、Basic Auth、API Key）
- ? **内存缓存**（提升重复请求性能）
- ? **速率限制**（防止被 API 限流）
- ? **自动重定向**（支持 HTTP 302/301）
- ? 代理支持
- ? SSL/TLS 自签名证书支持

### 2. **JSON 处理**（增强）
- ? 完整的 JSON 序列化/反序列化（Newtonsoft.Json）
- ? JSON 格式验证
- ? 安全解析（TryFromJson 不抛异常）
- ? 自定义序列化设置（忽略 null、日期格式等）

### 3. **缓存机制**（新增）
- ? 线程安全的内存缓存
- ? 可配置过期时间
- ? 自动清理过期缓存
- ? 缓存统计信息
- ? GET 请求自动缓存（可配置启用/禁用）

### 4. **速率限制**（新增）
- ? 最小请求间隔控制
- ? 时间窗口限制（如：60秒内最多100次）
- ? 自动等待和阻塞
- ? 请求统计
- ? 可动态启用/禁用

### 5. **文件上传服务**（MesFileUploadService）
#### 支持 5 种上传模式：
- ? **FormData**：表单上传（多文件）
- ? **Binary**：二进制上传（带文件哈希验证）
- ? **Base64**：Base64 编码上传
- ? **Multipart**：多部分上传（混合数据和文件）
- ? **Chunked**：分块上传（支持断点续传）

#### 功能特性：
- 文件哈希校验（MD5/SHA256）
- 上传进度跟踪
- **取消上传支持**（使用取消令牌）
- **断点续传**（状态持久化到文件）
- 批量上传（支持并行和顺序）
- 文件类型和大小验证
- 自定义字段附加

### 6. **数据模型和转换**
#### 支持的数据类型：
- ? 基本类型：String、Integer、Long、Double、Boolean、DateTime
- ? 可空类型（Nullable<T>）：支持设置字段为空
- ? 复杂类型：Dictionary、对象、数组
- ? JSON 序列化/反序列化
- ? 泛型类型转换

#### 数据模型：
- `MesRequest` - 通用请求模型（支持取消令牌）
- `MesResponse` - 通用响应模型
- `MesResponse(Of T)` - 泛型响应模型
- `MesFileUploadModel` - 单文件上传模型（支持取消令牌）
- `MesMultiFileUploadModel` - 批量上传模型
- `MesQueryModel` - 查询模型（带可空字段）

### 7. **数据验证**（MesValidator）
- ? 字符串验证（不为空、长度范围）
- ? 格式验证（Email、URL、电话等）
- ? 数值验证（数字、整数、范围）
- ? 日期验证
- ? 文件验证（存在、大小、扩展名）
- ? 集合验证
- ? 自定义验证

### 8. **日志记录**（MesLogger）
- ? 多级日志（Debug、Info、Warning、Error、Critical）
- ? 自动分日期日志文件
- ? 文件大小控制（自动归档）
- ? 请求/响应日志记录
- ? 上传进度日志
- ? 异常详细信息记录

### 9. **配置管理**（MesConfig）
- ? 单例模式
- ? API 基础 URL 配置
- ? 超时设置
- ? 重试策略配置
- ? 认证信息配置
- ? 代理配置
- ? 日志配置
- ? **缓存配置**（新增）
- ? **速率限制配置**（新增）

### 10. **异常处理**
- ? 框架异常体系
- ? HTTP 异常处理
- ? 上传异常处理
- ? 验证异常处理
- ? 配置异常处理
- ? **取消操作异常**（新增）
- ? 友好错误消息

---

## ?? 项目结构

```
WindowsApp1/
├─ Enums/
│  └─ MesEnums.vb                    # 所有枚举定义
├─ Models/
│  ├─ MesRequest.vb                  # 请求模型
│  ├─ MesResponse.vb                 # 响应模型
│  ├─ MesFileUploadModel.vb          # 上传模型
│  └─ MesQueryModel.vb               # 查询模型
├─ Services/
│  ├─ MesHttpClient.vb               # HTTP 客户端（核心）
│  ├─ MesFileUploadService.vb        # 文件上传服务（核心）
│  ├─ MesDataConverter.vb            # 数据转换
│  └─ MesValidator.vb                # 数据验证
├─ Infrastructure/
│  ├─ MesConfig.vb                   # 配置管理
│  ├─ MesException.vb                # 异常定义
│  └─ MesLogger.vb                   # 日志记录
├─ MesFramework.vb                   # 框架主入口
└─ MesFrameworkUsageExample.vb       # 使用示例
```

---

## ?? 快速开始

### 初始化框架

```vb
Dim framework = MesFramework.GetInstance()
framework.Initialize("http://api.mes.example.com")
```

### 发送 GET 请求

```vb
Dim response = Await framework.HttpClient.GetAsync("/api/orders")

If response.IsSuccess Then
    Console.WriteLine("成功: " & response.Data)
Else
    Console.WriteLine("失败: " & response.ErrorMessage)
End If
```

### 发送 POST 请求

```vb
Dim orderData = New With {
    .productCode = "PROD001",
    .quantity = 100
}

Dim response = Await framework.HttpClient.PostAsync("/api/orders", orderData)
```

### 使用可空字段查询

```vb
Dim query = New MesQueryModel With {
    .ProductCode = "PROD001",
    .StartDate = DateTime.Now.AddDays(-7),
    ' .EndDate 为 Nothing（可空）
    .PageIndex = 1,
    .PageSize = 50
}

Dim parameters = query.ToDictionary()  ' 只包含已设置字段
Dim response = Await framework.HttpClient.GetAsync("/api/orders", parameters)
```

### 上传单个文件

```vb
Dim uploadModel = New MesFileUploadModel With {
    .FilePath = "C:\temp\report.pdf",
    .UploadUrl = "http://api.mes.example.com/api/files/upload",
    .UploadMode = UploadMode.FormData,  ' 或 Base64, Binary, Multipart, Chunked
    .CustomFields = New Dictionary(Of String, Object) From {
        {"documentType", "report"}
    }
}

Dim response = Await framework.FileUploadService.UploadAsync(uploadModel)
```

### 批量上传文件

```vb
Dim multiUpload = New MesMultiFileUploadModel With {
    .UploadUrl = "http://api.mes.example.com/api/files/batch",
    .UploadMode = UploadMode.FormData,
    .EnableParallelUpload = True,
    .MaxParallelTasks = 3
}

multiUpload.AddFile("C:\temp\file1.jpg")
multiUpload.AddFile("C:\temp\file2.pdf")

Dim response = Await framework.FileUploadService.UploadMultipleAsync(multiUpload)
```

### 分块上传大文件

```vb
Dim uploadModel = New MesFileUploadModel With {
    .FilePath = "C:\temp\large_video.mp4",
    .UploadUrl = "http://api.mes.example.com/api/files/chunked",
    .UploadMode = UploadMode.Chunked,
    .ChunkSize = 1024 * 1024 * 10  ' 10MB 一个分块
}

Dim response = Await framework.FileUploadService.UploadAsync(uploadModel)
```

### 数据验证

```vb
' 验证 URL
If framework.Validator.ValidateUrl("http://api.example.com") Then
    Console.WriteLine("URL 有效")
End If

' 验证文件
Dim result = framework.Validator.ValidateFile(
    "C:\temp\file.pdf",
    1024 * 1024 * 100,  ' 最大 100MB
    ".pdf,.doc,.docx")

If result = FileValidationResult.Valid Then
    Console.WriteLine("文件有效")
End If
```

### 日志管理

```vb
' 设置日志级别
framework.SetLogLevel(LogLevel.Debug)

' 记录自定义日志
framework.Logger.Info("业务日志", "MY_APP")

' 获取最近日志
Dim logs = framework.GetRecentLogs(50)
Console.WriteLine(logs)
```

---

## ?? 高级功能

### 自定义请求配置

```vb
Dim request = New MesRequest With {
    .Url = "/api/orders",
    .Method = HttpMethod.Post,
    .Body = "{""productCode"": ""PROD001""}",
    .Timeout = 60000,
    .EnableRetry = True,
    .RetryCount = 5,
    .RetryDelayMs = 2000,
    .AuthType = AuthType.BearerToken,
    .AuthToken = "your-token-here"
}

request.AddHeader("X-Request-Id", Guid.NewGuid().ToString())
Dim response = Await framework.HttpClient.SendRequestAsync(request)
```

### 带身份认证的请求

```vb
' Bearer Token
framework.Initialize(
    "http://api.mes.example.com",
    "eyJhbGciOiJIUzI1NiIs...",
    AuthType.BearerToken)

' 或 API Key
framework.Initialize(
    "http://api.mes.example.com",
    "api-key-12345",
    AuthType.ApiKey)

' 之后的请求会自动添加认证信息
Dim response = Await framework.HttpClient.GetAsync("/api/protected")
```

### 监听上传进度

```vb
AddHandler framework.FileUploadService.ProgressChanged,
    Sub(sender, e)
        Console.WriteLine($"上传进度: {e.Progress}%")
    End Sub

' 执行上传...
```

### 数据类型转换

```vb
Dim json = framework.DataConverter.ToJson(orderData)
Dim dict = framework.DataConverter.ObjectToDictionary(orderData)
Dim converted = framework.DataConverter.ConvertValue("123", GetType(Integer))
```

---

## ?? 枚举类型说明

### HttpMethod（HTTP 方法）
- Get、Post、Put、Delete、Patch、Head、Options

### UploadMode（上传模式）
- FormData、Binary、Base64、Multipart、Chunked

### LogLevel（日志级别）
- Debug、Info、Warning、Error、Critical

### AuthType（认证类型）
- None、BasicAuth、BearerToken、ApiKey、OAuth2

### UploadStatus（上传状态）
- Pending、InProgress、Completed、Failed、Cancelled、Paused

### FileValidationResult（文件验证结果）
- Valid、InvalidSize、InvalidType、FileNotFound、AccessDenied、Unknown

---

## ?? 配置示例

```vb
' 获取配置对象
Dim config = MesConfig.GetInstance()

' 设置 API 基础 URL
config.BaseUrl = "http://api.mes.example.com"

' 设置超时
config.ConnectTimeout = 30000  ' 毫秒
config.ReadTimeout = 30000

' 设置上传限制
config.MaxUploadSize = 1024 * 1024 * 500  ' 500MB
config.ChunkSize = 1024 * 1024 * 5        ' 5MB

' 设置重试策略
config.MaxRetryCount = 3
config.RetryDelayMs = 1000

' 添加默认请求头
config.AddDefaultHeader("X-App-Version", "1.0.0")
config.AddDefaultHeader("X-Custom-Header", "value")

' 验证配置
If config.Validate() Then
    Console.WriteLine("配置有效")
End If
```

---

## ?? 响应处理

### 响应结构

```vb
Dim response = Await framework.HttpClient.GetAsync(url)

' 检查成功/失败
If response.IsSuccess Then
    ' 状态码（200 成功）
    Console.WriteLine($"状态码: {response.StatusCode}")
    
    ' 响应数据
    Console.WriteLine($"数据: {response.Data}")
    
    ' 响应耗时
    Console.WriteLine($"耗时: {response.ResponseTimeMs}ms")
Else
    ' 错误代码
    Console.WriteLine($"错误码: {response.ErrorCode}")
    
    ' 错误消息
    Console.WriteLine($"错误: {response.ErrorMessage}")
End If
```

---

## ??? 错误处理

```vb
Try
    Dim response = Await framework.HttpClient.GetAsync(url)
    
    If Not response.IsSuccess Then
        Console.WriteLine($"[{response.ErrorCode}] {response.ErrorMessage}")
    End If
    
Catch ex As MesValidationException
    ' 验证错误
    Console.WriteLine("验证失败: " & String.Join(", ", ex.ValidationErrors.Values))
    
Catch ex As MesHttpException
    ' HTTP 错误
    Console.WriteLine($"HTTP {ex.StatusCode}: {ex.Message}")
    
Catch ex As MesFrameworkException
    ' 框架错误
    Console.WriteLine($"[{ex.ErrorCode}] {ex.Message}")
    
Catch ex As Exception
    ' 其他异常
    Console.WriteLine($"异常: {ex.Message}")
End Try
```

---

## ?? 扩展和自定义

### 添加自定义验证

```vb
' 创建自定义验证方法
Public Function ValidateCustomRule(value As String) As Boolean
    ' 自定义验证逻辑
    Return Not String.IsNullOrEmpty(value)
End Function

' 在验证器中使用
Dim isValid = ValidateCustomRule(someValue)
```

### 添加自定义数据模型

```vb
' 创建自定义业务模型
Public Class CustomOrderModel
    Public Property OrderId As String
    Public Property OrderNo As String
    Public Property Status As String
    
    ' 自定义方法
    Public Function IsValid() As Boolean
        Return Not String.IsNullOrEmpty(OrderId)
    End Function
End Class

' 使用模型
Dim order = New CustomOrderModel()
Dim json = framework.DataConverter.ToJson(order)
```

---

## ?? 使用统计

### 支持的数据类型
- ? 基本类型：6 种（String、Integer、Long、Double、Boolean、DateTime）
- ? 可空类型：无限制
- ? 自定义类型：无限制

### 支持的上传方式
- ? 5 种模式
- ? 最大单文件：500MB（可配置）
- ? 批量上传：无限制
- ? 并行任务：可配置（默认 3 ）

### HTTP 方法支持
- ? 7 种标准方法
- ? 自定义 PATCH 支持
- ? 3 种认证方式

---

## ?? 最佳实践

1. **使用单例模式**：通过 `MesFramework.GetInstance()` 获取框架实例
2. **异步操作**：所有网络操作都支持异步，应该使用 `Await`
3. **错误处理**：始终检查 `response.IsSuccess` 并处理异常
4. **日志记录**：启用日志便于调试和监控
5. **配置集中管理**：使用 `MesConfig` 集中管理所有配置
6. **文件验证**：上传前使用验证器检查文件
7. **使用泛型响应**：提升代码可读性和类型安全
8. **合理使用缓存**：对不经常变化的数据启用缓存
9. **速率限制**：与外部 API 交互时启用速率限制
10. **取消支持**：长时间操作提供取消功能
11. **断点续传**：大文件上传使用断点续传

---

## ?? 常见问题

### Q: 如何支持 HTTPS？
A: 框架默认支持 HTTPS。如需自签名证书，已在 HttpClientHandler 中配置允许。

### Q: 如何设置代理？
A: 在 MesConfig 中设置 ProxyUrl、ProxyUsername、ProxyPassword。

### Q: 大文件上传如何处理？
A: 使用 `UploadMode.Chunked` 模式进行分块上传，支持断点续传。

### Q: 如何监听上传进度？
A: 订阅 `FileUploadService.ProgressChanged` 事件。

### Q: 支持哪些认证方式？
A: 支持 Bearer Token、Basic Auth、API Key、OAuth2（预留）。

---

## ?? 许可证

根据项目需要设置许可证类型。

---

## ? 完成度

- ? HTTP 客户端功能完整
- ? 5 种文件上传模式
- ? 完整的数据验证框架
- ? 日志记录系统
- ? 异常处理体系
- ? 使用示例
- ? 配置管理
- ? VB.NET 完全兼容

---

## ?? 总结

这个 MES 系统交互框架提供了：

1. **完整性**：涵盖 HTTP 通信、文件上传、数据处理的全功能
2. **通用性**：支持多种数据类型、上传方式、认证方法
3. **易扩展性**：模块化设计，易于自定义和扩展
4. **生产就绪**：包含日志、异常处理、重试机制等生产级功能
5. **易使用性**：简洁的 API，丰富的使用示例

可直接在实际项目中使用，无需额外依赖库（除了 .NET 标准库）。

---

**更多示例请查看：`MesFrameworkAdvancedExamples.vb`**
