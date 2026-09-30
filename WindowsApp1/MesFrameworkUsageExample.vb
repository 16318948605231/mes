''' <summary>
''' MES 系统交互框架 - 使用示例
''' 这个类展示了框架的各种使用方式
''' </summary>
Public Class MesFrameworkUsageExample
    
    ''' <summary>
    ''' 示例1：基础初始化和 GET 请求
    ''' </summary>
    Public Shared Async Function Example1_GetRequest() As Task
        ' 获取框架实例
        Dim framework = MesFramework.GetInstance()
        
        ' 初始化框架
        framework.Initialize("http://api.mes.example.com")
        
        ' 执行 GET 请求
        Dim response = Await framework.HttpClient.GetAsync(
            "/api/orders/123",
            New Dictionary(Of String, Object) From {
                {"includeDetails", True},
                {"pageSize", 10}
            })
        
        If response.IsSuccess Then
            Console.WriteLine("请求成功: " & response.Data)
        Else
            Console.WriteLine("请求失败: " & response.ErrorMessage)
        End If
    End Function
    
    ''' <summary>
    ''' 示例2：POST 请求（创建资源）
    ''' </summary>
    Public Shared Async Function Example2_PostRequest() As Task
        Dim framework = MesFramework.GetInstance()
        
        ' 准备数据
        Dim orderData = New With {
            .productCode = "PROD001",
            .quantity = 100,
            .batchNo = "BATCH20240101",
            .dueDate = DateTime.Now.AddDays(7)
        }
        
        ' 执行 POST 请求
        Dim response = Await framework.HttpClient.PostAsync("/api/orders", orderData)
        
        If response.IsSuccess Then
            ' 解析响应
            Dim result = framework.DataConverter.FromJson(Of OrderCreateResult)(response.Data)
            Console.WriteLine($"订单创建成功，ID: {result.OrderId}")
        End If
    End Function
    
    ''' <summary>
    ''' 示例3：使用查询模型（支持可空字段）
    ''' </summary>
    Public Shared Async Function Example3_QueryWithNullableFields() As Task
        Dim framework = MesFramework.GetInstance()
        
        ' 创建查询模型
        Dim query = New MesQueryModel With {
            .ProductCode = "PROD001",
            .StartDate = DateTime.Now.AddDays(-7),
            .StatusCode = 1,
            .PageIndex = 1,
            .PageSize = 50
        }
        ' 注意：.EndDate 和 .OperatorId 不设置，它们保持为 Nothing（可空）
        
        ' 验证查询模型
        Dim validationEx = framework.Validator.ValidateQueryModel(query)
        If validationEx IsNot Nothing Then
            Console.WriteLine("验证失败: " & String.Join(", ", validationEx.ValidationErrors.Values))
            Return
        End If
        
        ' 转换为字典（只包含已设置的字段）
        Dim parameters = query.ToDictionary()
        
        ' 执行查询
        Dim response = Await framework.HttpClient.GetAsync("/api/orders", parameters)
        
        If response.IsSuccess Then
            Console.WriteLine("查询成功: " & response.Data)
        End If
    End Function
    
    ''' <summary>
    ''' 示例4：上传单个文件（各种模式）
    ''' </summary>
    Public Shared Async Function Example4_SingleFileUpload() As Task
        Dim framework = MesFramework.GetInstance()
        framework.Initialize("http://api.mes.example.com")
        
        ' 创建上传模型
        Dim uploadModel = New MesFileUploadModel With {
            .FilePath = "C:\temp\production_report.pdf",
            .UploadUrl = "http://api.mes.example.com/api/files/upload",
            .UploadMode = UploadMode.FormData, ' 可以改为 Binary, Base64, Multipart, Chunked
            .CustomFields = New Dictionary(Of String, Object) From {
                {"documentType", "production_report"},
                {"batchNo", "BATCH20240101"}
            }
        }
        
        ' 监听上传进度
        AddHandler framework.FileUploadService.ProgressChanged, 
            Sub(sender, e)
                Console.WriteLine($"上传进度: {e.Progress}%")
            End Sub
        
        ' 执行上传
        Dim response = Await framework.FileUploadService.UploadAsync(uploadModel)
        
        If response.IsSuccess Then
            Console.WriteLine("文件上传成功")
        Else
            Console.WriteLine("文件上传失败: " & response.ErrorMessage)
        End If
    End Function
    
    ''' <summary>
    ''' 示例5：上传单个文件（Base64 模式）
    ''' </summary>
    Public Shared Async Function Example5_Base64Upload() As Task
        Dim framework = MesFramework.GetInstance()
        
        Dim uploadModel = New MesFileUploadModel With {
            .FilePath = "C:\temp\quality_image.jpg",
            .UploadUrl = "http://api.mes.example.com/api/files/upload-base64",
            .UploadMode = UploadMode.Base64,
            .CustomFields = New Dictionary(Of String, Object) From {
                {"inspectionId", "INS001"},
                {"imageType", "quality_check"}
            }
        }
        
        Dim response = Await framework.FileUploadService.UploadAsync(uploadModel)
        
        If response.IsSuccess Then
            Console.WriteLine("Base64 上传成功")
        End If
    End Function
    
    ''' <summary>
    ''' 示例6：上传单个文件（分块模式 - 适合大文件）
    ''' </summary>
    Public Shared Async Function Example6_ChunkedUpload() As Task
        Dim framework = MesFramework.GetInstance()
        
        Dim uploadModel = New MesFileUploadModel With {
            .FilePath = "C:\temp\large_video.mp4",
            .UploadUrl = "http://api.mes.example.com/api/files/upload-chunked",
            .UploadMode = UploadMode.Chunked,
            .ChunkSize = 1024 * 1024 * 10, ' 10MB 一个分块
            .HashAlgorithm = "SHA256"
        }
        
        ' 监听进度
        AddHandler framework.FileUploadService.ProgressChanged,
            Sub(sender, e)
                Console.WriteLine($"分块 {uploadModel.CurrentChunkIndex + 1}/{uploadModel.TotalChunks} 上传进度: {e.Progress}%")
            End Sub
        
        Dim response = Await framework.FileUploadService.UploadAsync(uploadModel)
        
        If response.IsSuccess Then
            Console.WriteLine($"分块上传完成，耗时: {uploadModel.GetUploadDurationSeconds():F2}秒")
            Console.WriteLine($"上传速率: {uploadModel.GetUploadSpeedMBps():F2} MB/s")
        End If
    End Function
    
    ''' <summary>
    ''' 示例7：批量上传多个文件
    ''' </summary>
    Public Shared Async Function Example7_MultipleFilesUpload() As Task
        Dim framework = MesFramework.GetInstance()
        
        ' 创建批量上传模型
        Dim multiUpload = New MesMultiFileUploadModel With {
            .UploadUrl = "http://api.mes.example.com/api/files/batch-upload",
            .UploadMode = UploadMode.FormData,
            .EnableParallelUpload = True,
            .MaxParallelTasks = 3,
            .SharedCustomFields = New Dictionary(Of String, Object) From {
                {"batchNo", "BATCH20240101"},
                {"projectId", "PROJ001"}
            }
        }
        
        ' 添加文件
        multiUpload.AddFile("C:\temp\report1.pdf")
        multiUpload.AddFile("C:\temp\image1.jpg")
        multiUpload.AddFile("C:\temp\image2.jpg")
        multiUpload.AddFile("C:\temp\data.xlsx")
        
        ' 监听进度
        AddHandler framework.FileUploadService.ProgressChanged,
            Sub(sender, e)
                Console.WriteLine($"整体上传进度: {e.Progress}%")
            End Sub
        
        ' 执行批量上传
        Dim response = Await framework.FileUploadService.UploadMultipleAsync(multiUpload)
        
        Console.WriteLine("批量上传结果: " & response.Data)
        Console.WriteLine($"成功: {multiUpload.SuccessCount}, 失败: {multiUpload.FailureCount}")
    End Function
    
    ''' <summary>
    ''' 示例8：处理不同的响应格式
    ''' </summary>
    Public Shared Async Function Example8_ResponseHandling() As Task
        Dim framework = MesFramework.GetInstance()
        
        ' 执行请求
        Dim response = Await framework.HttpClient.GetAsync("/api/orders/123")
        
        If response.IsSuccess Then
            ' 解析为具体的对象
            Try
                Dim order = framework.DataConverter.FromJson(Of ProductionOrder)(response.Data)
                Console.WriteLine($"订单号: {order.OrderNo}, 数量: {order.Quantity}")
            Catch ex As Exception
                Console.WriteLine("JSON 解析失败: " & ex.Message)
            End Try
        Else
            Console.WriteLine($"[{response.ErrorCode}] {response.ErrorMessage}")
        End If
    End Function
    
    ''' <summary>
    ''' 示例9：带有身份认证的请求
    ''' </summary>
    Public Shared Async Function Example9_AuthenticatedRequest() As Task
        Dim framework = MesFramework.GetInstance()
        
        ' 初始化时设置 Bearer Token
        framework.Initialize(
            "http://api.mes.example.com",
            "eyJhbGciOiJIUzI1NiIs...", ' 你的 Token
            AuthType.BearerToken)
        
        ' 执行请求（会自动添加 Authorization 头）
        Dim response = Await framework.HttpClient.GetAsync("/api/protected/data")
        
        If response.IsSuccess Then
            Console.WriteLine("认证成功")
        Else
            Console.WriteLine("认证失败: " & response.ErrorMessage)
        End If
    End Function
    
    ''' <summary>
    ''' 示例10：自定义请求配置
    ''' </summary>
    Public Shared Async Function Example10_CustomRequest() As Task
        Dim framework = MesFramework.GetInstance()
        
        ' 创建自定义请求
        Dim request = New MesRequest With {
            .Url = "/api/orders",
            .Method = HttpMethod.Post,
            .Body = "{""productCode"": ""PROD001"", ""quantity"": 100}",
            .Timeout = 60000,
            .EnableRetry = True,
            .RetryCount = 5,
            .RetryDelayMs = 2000,
            .DataFormat = DataFormatType.Json,
            .AuthType = AuthType.BearerToken,
            .AuthToken = "your-token-here"
        }
        
        ' 添加自定义请求头
        request.AddHeader("X-Request-Id", Guid.NewGuid().ToString())
        request.AddHeader("X-Custom-Header", "custom-value")
        
        ' 发送请求
        Dim response = Await framework.HttpClient.SendRequestAsync(request)
        
        If response.IsSuccess Then
            Console.WriteLine("自定义请求成功")
        End If
    End Function
    
    ''' <summary>
    ''' 示例11：数据验证
    ''' </summary>
    Public Shared Sub Example11_DataValidation()
        Dim framework = MesFramework.GetInstance()
        
        ' 验证 URL
        If framework.Validator.ValidateUrl("http://api.mes.example.com/api/orders") Then
            Console.WriteLine("URL 格式有效")
        End If
        
        ' 验证电子邮件
        If framework.Validator.ValidateEmail("admin@example.com") Then
            Console.WriteLine("电子邮件格式有效")
        End If
        
        ' 验证文件
        Dim result = framework.Validator.ValidateFile(
            "C:\temp\file.pdf",
            1024 * 1024 * 100, ' 最大 100MB
            ".jpg,.png,.pdf,.doc,.docx")
        
        If result = FileValidationResult.Valid Then
            Console.WriteLine("文件验证通过")
        Else
            Console.WriteLine("文件验证失败: " & result)
        End If
    End Sub
    
    ''' <summary>
    ''' 示例12：日志和调试
    ''' </summary>
    Public Shared Sub Example12_Logging()
        Dim framework = MesFramework.GetInstance()
        
        ' 设置日志级别
        framework.SetLogLevel(LogLevel.Debug)
        
        ' 记录自定义信息
        framework.Logger.Info("这是一条信息日志", "MY_APP")
        framework.Logger.Warning("这是一条警告日志", "MY_APP")
        framework.Logger.Debug("这是一条调试日志", "MY_APP")
        
        ' 获取最近的日志
        Dim logs = framework.GetRecentLogs(50)
        Console.WriteLine("最近的日志：")
        Console.WriteLine(logs)
        
        ' 清空日志
        ' framework.ClearLogs()
    End Sub
End Class

' ===== 数据模型示例 =====

''' <summary>生产订单</summary>
Public Class ProductionOrder
    Public Property OrderId As String
    Public Property OrderNo As String
    Public Property ProductCode As String
    Public Property Quantity As Integer
    Public Property BatchNo As String
    Public Property Status As String
    Public Property CreatedDate As DateTime
    Public Property DueDate As DateTime
End Class

''' <summary>订单创建结果</summary>
Public Class OrderCreateResult
    Public Property OrderId As String
    Public Property OrderNo As String
    Public Property Message As String
End Class
