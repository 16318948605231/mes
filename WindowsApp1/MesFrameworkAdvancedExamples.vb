''' <summary>
''' MES 系统交互框架 - 高级功能使用示例
''' </summary>
Public Class MesFrameworkAdvancedExamples

    ''' <summary>
    ''' 示例 1: 使用泛型响应（自动反序列化）
    ''' </summary>
    Public Async Function Example1_GenericResponse() As Task
        Dim framework = MesFramework.GetInstance()
        framework.Initialize("http://api.mes.example.com")

        ' 定义响应数据模型
        Dim response = Await framework.HttpClient.GetAsync(Of Order)("/api/orders/12345")

        If response.IsSuccess Then
            Console.WriteLine($"订单号: {response.Data.OrderNo}")
            Console.WriteLine($"状态: {response.Data.Status}")
        Else
            Console.WriteLine($"错误: {response.ErrorMessage}")
        End If
    End Function

    ''' <summary>
    ''' 示例 2: 使用取消令牌取消请求
    ''' </summary>
    Public Async Function Example2_CancellationToken() As Task
        Dim framework = MesFramework.GetInstance()
        Dim cts = New Threading.CancellationTokenSource()

        ' 5 秒后自动取消
        cts.CancelAfter(5000)

        Dim request = New MesRequest With {
            .Url = "/api/large-data",
            .Method = HttpMethod.Get,
            .CancellationToken = cts.Token
        }

        Try
            Dim response = Await framework.HttpClient.SendRequestAsync(request)
            Console.WriteLine("请求完成")
        Catch ex As OperationCanceledException
            Console.WriteLine("请求已取消")
        End Try
    End Function

    ''' <summary>
    ''' 示例 3: 使用取消令牌取消文件上传
    ''' </summary>
    Public Async Function Example3_CancelFileUpload() As Task
        Dim framework = MesFramework.GetInstance()
        Dim cts = New Threading.CancellationTokenSource()

        Dim uploadModel = New MesFileUploadModel With {
            .FilePath = "C:\temp\large_file.zip",
            .UploadUrl = "http://api.mes.example.com/api/files/upload",
            .UploadMode = UploadMode.Chunked,
            .CancellationToken = cts.Token
        }

        ' 启动上传任务
        Dim uploadTask = framework.FileUploadService.UploadAsync(uploadModel)

        ' 模拟：5 秒后用户点击取消按钮
        Await Task.Delay(5000)
        cts.Cancel()

        Try
            Dim response = Await uploadTask
            Console.WriteLine($"上传状态: {uploadModel.Status}")
        Catch ex As OperationCanceledException
            Console.WriteLine("上传已取消")
        End Try
    End Function

    ''' <summary>
    ''' 示例 4: 启用和使用缓存
    ''' </summary>
    Public Async Function Example4_UseCache() As Task
        Dim framework = MesFramework.GetInstance()

        ' 启用缓存（5 分钟过期）
        framework.EnableCache(300)

        ' 第一次请求（从服务器获取）
        Dim response1 = Await framework.HttpClient.GetAsync("/api/products")
        Console.WriteLine($"第一次请求耗时: {response1.ResponseTimeMs}ms")

        ' 第二次请求（从缓存获取，速度更快）
        Dim response2 = Await framework.HttpClient.GetAsync("/api/products")
        Console.WriteLine($"第二次请求耗时: {response2.ResponseTimeMs}ms")

        ' 查看缓存统计
        Dim stats = framework.GetCacheStatistics()
        Console.WriteLine($"缓存统计: {stats}")

        ' 清空缓存
        framework.ClearCache()
    End Function

    ''' <summary>
    ''' 示例 5: 启用速率限制
    ''' </summary>
    Public Async Function Example5_RateLimit() As Task
        Dim framework = MesFramework.GetInstance()

        ' 启用速率限制：最小间隔 200ms，每分钟最多 50 次请求
        framework.EnableRateLimit(200, 60, 50)

        ' 发送多个请求
        For i = 1 To 10
            Dim response = Await framework.HttpClient.GetAsync($"/api/items/{i}")
            Console.WriteLine($"请求 {i} 完成")
        Next

        ' 查看速率限制统计
        Dim stats = framework.GetRateLimiterStatistics()
        Console.WriteLine($"速率统计: {stats}")

        ' 禁用速率限制
        framework.DisableRateLimit()
    End Function

    ''' <summary>
    ''' 示例 6: 断点续传（分块上传）
    ''' </summary>
    Public Async Function Example6_ResumeUpload() As Task
        Dim framework = MesFramework.GetInstance()
        Dim stateFile = "C:\temp\upload_state.json"

        ' 尝试从状态文件恢复
        Dim uploadModel = MesFileUploadModel.LoadState(stateFile)

        If uploadModel Is Nothing Then
            ' 首次上传
            uploadModel = New MesFileUploadModel With {
                .FilePath = "C:\temp\large_video.mp4",
                .UploadUrl = "http://api.mes.example.com/api/files/chunked",
                .UploadMode = UploadMode.Chunked
            }
        Else
            Console.WriteLine($"从断点 {uploadModel.CurrentChunkIndex}/{uploadModel.TotalChunks} 继续上传")
        End If

        ' 监听上传进度并保存状态
        AddHandler framework.FileUploadService.ProgressChanged,
            Sub(sender, e)
                Console.WriteLine($"上传进度: {e.Progress}%")
                uploadModel.SaveState(stateFile)
            End Sub

        Try
            Dim response = Await framework.FileUploadService.UploadAsync(uploadModel)

            If response.IsSuccess Then
                Console.WriteLine("上传完成！")
                ' 删除状态文件
                MesFileUploadModel.DeleteState(stateFile)
            End If
        Catch ex As Exception
            Console.WriteLine($"上传失败: {ex.Message}")
            ' 保存状态以便下次恢复
            uploadModel.SaveState(stateFile)
        End Try
    End Function

    ''' <summary>
    ''' 示例 7: 泛型 POST 请求
    ''' </summary>
    Public Async Function Example7_GenericPost() As Task
        Dim framework = MesFramework.GetInstance()

        ' 准备请求数据
        Dim orderData = New With {
            .productCode = "PROD001",
            .quantity = 100,
            .priority = "high"
        }

        ' 发送 POST 请求并自动反序列化响应
        Dim response = Await framework.HttpClient.PostAsync(Of OrderResponse)(
            "/api/orders",
            orderData
        )

        If response.IsSuccess Then
            Console.WriteLine($"创建成功，订单ID: {response.Data.OrderId}")
            Console.WriteLine($"订单号: {response.Data.OrderNo}")
        Else
            Console.WriteLine($"创建失败: {response.ErrorMessage}")
        End If
    End Function

    ''' <summary>
    ''' 示例 8: 验证 JSON 和安全解析
    ''' </summary>
    Public Async Function Example8_SafeJsonParsing() As Task
        Dim framework = MesFramework.GetInstance()

        Dim response = Await framework.HttpClient.GetAsync("/api/data")

        If response.IsSuccess Then
            ' 验证 JSON 格式
            If framework.DataConverter.IsValidJson(response.Data) Then
                Console.WriteLine("JSON 格式有效")

                ' 安全解析（不抛出异常）
                Dim data As Dictionary(Of String, Object) = Nothing
                If framework.DataConverter.TryFromJson(response.Data, data) Then
                    Console.WriteLine($"解析成功，包含 {data.Count} 个字段")
                Else
                    Console.WriteLine("解析失败")
                End If
            Else
                Console.WriteLine("JSON 格式无效")
            End If
        End If
    End Function

    ''' <summary>
    ''' 示例 9: 综合使用（缓存 + 速率限制 + 泛型响应）
    ''' </summary>
    Public Async Function Example9_ComprehensiveUsage() As Task
        Dim framework = MesFramework.GetInstance()
        framework.Initialize("http://api.mes.example.com", "your-token", AuthType.BearerToken)

        ' 启用缓存
        framework.EnableCache(600)

        ' 启用速率限制
        framework.EnableRateLimit(100, 60, 100)

        ' 使用泛型响应
        Dim response = Await framework.HttpClient.GetAsync(Of List(Of Product))("/api/products")

        If response.IsSuccess Then
            Console.WriteLine($"获取到 {response.Data.Count} 个产品")
            Console.WriteLine($"响应耗时: {response.ResponseTimeMs}ms")

            For Each product In response.Data
                Console.WriteLine($"- {product.Name}: ¥{product.Price}")
            Next
        End If

        ' 查看统计信息
        Console.WriteLine($"缓存统计: {framework.GetCacheStatistics()}")
        Console.WriteLine($"速率统计: {framework.GetRateLimiterStatistics()}")
    End Function

    ''' <summary>
    ''' 示例 10: 批量请求与错误处理
    ''' </summary>
    Public Async Function Example10_BatchRequests() As Task
        Dim framework = MesFramework.GetInstance()

        Dim productIds = {"PROD001", "PROD002", "PROD003", "PROD004", "PROD005"}
        Dim tasks = New List(Of Task(Of MesResponse(Of Product)))

        ' 并行发送多个请求
        For Each productId In productIds
            Dim task = framework.HttpClient.GetAsync(Of Product)($"/api/products/{productId}")
            tasks.Add(task)
        Next

        ' 等待所有请求完成
        Dim responses = Await Task.WhenAll(tasks)

        ' 处理结果
        Dim successCount = 0
        Dim failureCount = 0

        For Each response In responses
            If response.IsSuccess Then
                successCount += 1
                Console.WriteLine($"✓ {response.Data.Name}")
            Else
                failureCount += 1
                Console.WriteLine($"✗ 错误: {response.ErrorMessage}")
            End If
        Next

        Console.WriteLine($"成功: {successCount}, 失败: {failureCount}")
    End Function
End Class

' ===== 示例数据模型 =====

Public Class Order
    Public Property OrderId As String
    Public Property OrderNo As String
    Public Property Status As String
    Public Property ProductCode As String
    Public Property Quantity As Integer
    Public Property CreateTime As DateTime
End Class

Public Class OrderResponse
    Public Property OrderId As String
    Public Property OrderNo As String
    Public Property Message As String
End Class

Public Class Product
    Public Property Id As String
    Public Property Name As String
    Public Property Price As Decimal
    Public Property Stock As Integer
    Public Property Category As String
End Class
