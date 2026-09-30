Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Threading
Imports System.Reflection

''' <summary>
''' MES 系统交互框架 - HTTP 客户端（核心）
''' </summary>
Public Class MesHttpClient
    Implements IDisposable
    
    ''' <summary>HTTP 客户端</summary>
    Private _httpClient As HttpClient
    
    ''' <summary>配置</summary>
    Private _config As MesConfig
    
    ''' <summary>日志记录器</summary>
    Private _logger As MesLogger
    
    ''' <summary>数据转换器</summary>
    Private _converter As MesDataConverter
    
    ''' <summary>数据验证器</summary>
    Private _validator As MesValidator
    
    ''' <summary>缓存服务</summary>
    Private _cache As MesCacheService
    
    ''' <summary>速率限制器</summary>
    Private _rateLimiter As MesRateLimiter
    
    ''' <summary>是否已释放</summary>
    Private _disposed As Boolean = False
    
    ''' <summary>PATCH 方法</summary>
    Private Shared _patchMethod As HttpMethod = Nothing
    Private Shared _patchMethodInitialized As Boolean = False
    
    Public Sub New(Optional config As MesConfig = Nothing, Optional logger As MesLogger = Nothing)
        _config = If(config, MesConfig.GetInstance())
        _logger = If(logger, MesLogger.GetInstance())
        _converter = New MesDataConverter(_logger)
        _validator = New MesValidator(_logger)
        _cache = New MesCacheService(_logger, _config.CacheExpireSeconds)
        _rateLimiter = New MesRateLimiter(_logger, 100, False)
        
        ' 初始化 HTTP 客户端
        Dim handler = New HttpClientHandler()
        
        ' 配置代理
        If Not String.IsNullOrEmpty(_config.ProxyUrl) Then
            handler.Proxy = New WebProxy(_config.ProxyUrl)
            If Not String.IsNullOrEmpty(_config.ProxyUsername) Then
                handler.Proxy.Credentials = New NetworkCredential(_config.ProxyUsername, _config.ProxyPassword)
            End If
        End If
        
        ' 允许自签名证书（仅用于开发测试）
        handler.ServerCertificateCustomValidationCallback = Function(msg, cert, chain, errors) True
        
        ' 启用自动重定向
        handler.AllowAutoRedirect = True
        handler.MaxAutomaticRedirections = 5
        
        _httpClient = New HttpClient(handler) With {
            .Timeout = TimeSpan.FromMilliseconds(_config.ConnectTimeout)
        }
        
        ' 设置默认请求头
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MES-Framework/2.0")
        For Each header In _config.DefaultHeaders
            _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value)
        Next
        
        ' 初始化 PATCH 方法
        If Not _patchMethodInitialized Then
            Try
                Dim methodType = GetType(HttpMethod)
                Dim patchProperty = methodType.GetProperty("Patch", BindingFlags.Public Or BindingFlags.Static)
                If patchProperty IsNot Nothing Then
                    _patchMethod = CType(patchProperty.GetValue(Nothing), HttpMethod)
                End If
            Catch
                ' 如果获取失败，将使用 PUT 作为备选
            End Try
            _patchMethodInitialized = True
        End If
    End Sub

    ''' <summary>
    ''' 执行 GET 请求
    ''' </summary>
    Public Async Function GetAsync(url As String, Optional parameters As Dictionary(Of String, Object) = Nothing, Optional headers As Dictionary(Of String, String) = Nothing) As Task(Of MesResponse)
        Try
            Dim request = New MesRequest With {
                .Url = url,
                .Method = HttpMethod.Get,
                .Parameters = If(parameters, New Dictionary(Of String, Object)),
                .Headers = If(headers, New Dictionary(Of String, String))
            }
            
            ' 如果启用缓存，尝试从缓存获取
            If _config.EnableCache Then
                Dim cacheKey = GenerateCacheKey(request)
                Dim cachedResponse As MesResponse = Nothing
                
                If _cache.TryGet(cacheKey, cachedResponse) Then
                    _logger.Debug($"从缓存返回: {url}")
                    Return cachedResponse
                End If
                
                ' 发送请求
                Dim response = Await SendRequestAsync(request)
                
                ' 成功响应则缓存
                If response.IsSuccess Then
                    _cache.Set(cacheKey, response, _config.CacheExpireSeconds)
                End If
                
                Return response
            Else
                Return Await SendRequestAsync(request)
            End If
        Catch ex As Exception
            _logger.Error(ex, "GET请求异常")
            Return MesResponse.CreateError($"GET 请求失败: {ex.Message}", "GET_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 执行 POST 请求
    ''' </summary>
    Public Async Function PostAsync(url As String, body As Object, Optional headers As Dictionary(Of String, String) = Nothing) As Task(Of MesResponse)
        Try
            Dim request = New MesRequest With {
                .Url = url,
                .Method = HttpMethod.Post,
                .Body = _converter.ToJson(body),
                .Headers = If(headers, New Dictionary(Of String, String))
            }
            
            Return Await SendRequestAsync(request)
        Catch ex As Exception
            _logger.Error(ex, "POST请求异常")
            Return MesResponse.CreateError($"POST 请求失败: {ex.Message}", "POST_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 执行 PUT 请求
    ''' </summary>
    Public Async Function PutAsync(url As String, body As Object, Optional headers As Dictionary(Of String, String) = Nothing) As Task(Of MesResponse)
        Try
            Dim request = New MesRequest With {
                .Url = url,
                .Method = HttpMethod.Put,
                .Body = _converter.ToJson(body),
                .Headers = If(headers, New Dictionary(Of String, String))
            }
            
            Return Await SendRequestAsync(request)
        Catch ex As Exception
            _logger.Error(ex, "PUT请求异常")
            Return MesResponse.CreateError($"PUT 请求失败: {ex.Message}", "PUT_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 执行 DELETE 请求
    ''' </summary>
    Public Async Function DeleteAsync(url As String, Optional parameters As Dictionary(Of String, Object) = Nothing, Optional headers As Dictionary(Of String, String) = Nothing) As Task(Of MesResponse)
        Try
            Dim request = New MesRequest With {
                .Url = url,
                .Method = HttpMethod.Delete,
                .Parameters = If(parameters, New Dictionary(Of String, Object)),
                .Headers = If(headers, New Dictionary(Of String, String))
            }
            
            Return Await SendRequestAsync(request)
        Catch ex As Exception
            _logger.Error(ex, "DELETE请求异常")
            Return MesResponse.CreateError($"DELETE 请求失败: {ex.Message}", "DELETE_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 执行 PATCH 请求
    ''' </summary>
    Public Async Function PatchAsync(url As String, body As Object, Optional headers As Dictionary(Of String, String) = Nothing) As Task(Of MesResponse)
        Try
            Dim request = New MesRequest With {
                .Url = url,
                .Method = HttpMethod.Patch,
                .Body = _converter.ToJson(body),
                .Headers = If(headers, New Dictionary(Of String, String))
            }
            
            Return Await SendRequestAsync(request)
        Catch ex As Exception
            _logger.Error(ex, "PATCH请求异常")
            Return MesResponse.CreateError($"PATCH 请求失败: {ex.Message}", "PATCH_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 执行 GET 请求（泛型版本，自动反序列化）
    ''' </summary>
    Public Async Function GetAsync(Of T)(url As String, Optional parameters As Dictionary(Of String, Object) = Nothing, Optional headers As Dictionary(Of String, String) = Nothing) As Task(Of MesResponse(Of T))
        Try
            Dim response = Await GetAsync(url, parameters, headers)
            Return ConvertToGenericResponse(Of T)(response)
        Catch ex As Exception
            _logger.Error(ex, "泛型GET请求异常")
            Return MesResponse(Of T).CreateError($"GET 请求失败: {ex.Message}", "GET_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 执行 POST 请求（泛型版本，自动反序列化）
    ''' </summary>
    Public Async Function PostAsync(Of T)(url As String, body As Object, Optional headers As Dictionary(Of String, String) = Nothing) As Task(Of MesResponse(Of T))
        Try
            Dim response = Await PostAsync(url, body, headers)
            Return ConvertToGenericResponse(Of T)(response)
        Catch ex As Exception
            _logger.Error(ex, "泛型POST请求异常")
            Return MesResponse(Of T).CreateError($"POST 请求失败: {ex.Message}", "POST_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 执行 PUT 请求（泛型版本，自动反序列化）
    ''' </summary>
    Public Async Function PutAsync(Of T)(url As String, body As Object, Optional headers As Dictionary(Of String, String) = Nothing) As Task(Of MesResponse(Of T))
        Try
            Dim response = Await PutAsync(url, body, headers)
            Return ConvertToGenericResponse(Of T)(response)
        Catch ex As Exception
            _logger.Error(ex, "泛型PUT请求异常")
            Return MesResponse(Of T).CreateError($"PUT 请求失败: {ex.Message}", "PUT_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 执行 DELETE 请求（泛型版本，自动反序列化）
    ''' </summary>
    Public Async Function DeleteAsync(Of T)(url As String, Optional parameters As Dictionary(Of String, Object) = Nothing, Optional headers As Dictionary(Of String, String) = Nothing) As Task(Of MesResponse(Of T))
        Try
            Dim response = Await DeleteAsync(url, parameters, headers)
            Return ConvertToGenericResponse(Of T)(response)
        Catch ex As Exception
            _logger.Error(ex, "泛型DELETE请求异常")
            Return MesResponse(Of T).CreateError($"DELETE 请求失败: {ex.Message}", "DELETE_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 执行 PATCH 请求（泛型版本，自动反序列化）
    ''' </summary>
    Public Async Function PatchAsync(Of T)(url As String, body As Object, Optional headers As Dictionary(Of String, String) = Nothing) As Task(Of MesResponse(Of T))
        Try
            Dim response = Await PatchAsync(url, body, headers)
            Return ConvertToGenericResponse(Of T)(response)
        Catch ex As Exception
            _logger.Error(ex, "泛型PATCH请求异常")
            Return MesResponse(Of T).CreateError($"PATCH 请求失败: {ex.Message}", "PATCH_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 发送自定义请求
    ''' </summary>
    Public Async Function SendRequestAsync(request As MesRequest) As Task(Of MesResponse)
        ' 验证请求
        Dim validationEx = _validator.ValidateRequest(request)
        If validationEx IsNot Nothing Then
            Throw validationEx
        End If
        
        Dim response = New MesResponse With {
            .RequestId = request.RequestId,
            .Timestamp = DateTime.Now
        }
        
        Dim startTime = DateTime.Now
        
        Try
            ' 构建 URL
            Dim fullUrl = request.BuildUrl()
            If Not fullUrl.StartsWith("http") Then
                fullUrl = _config.BaseUrl.TrimEnd("/"c) & "/" & fullUrl.TrimStart("/"c)
            End If
            
            ' 记录请求
            _logger.LogRequest(request)
            
            ' 执行请求（带重试）
            Dim result = Await ExecuteWithRetryAsync(fullUrl, request)
            
            response = result
            response.ResponseTimeMs = CLng((DateTime.Now - startTime).TotalMilliseconds)
            response.RequestId = request.RequestId
            
            ' 记录响应
            _logger.LogResponse(response)
            
            Return response
            
        Catch ex As TimeoutException
            _logger.Error(ex, "请求超时")
            response.StatusCode = 504
            response.IsSuccess = False
            response.ErrorMessage = "请求超时"
            response.ErrorCode = "TIMEOUT_ERROR"
            response.ResponseTimeMs = CLng((DateTime.Now - startTime).TotalMilliseconds)
            Return response
            
        Catch ex As HttpRequestException
            _logger.Error(ex, "HTTP请求异常")
            response.StatusCode = 500
            response.IsSuccess = False
            response.ErrorMessage = ex.Message
            response.ErrorCode = "HTTP_REQUEST_ERROR"
            response.ResponseTimeMs = CLng((DateTime.Now - startTime).TotalMilliseconds)
            Return response
            
        Catch ex As Exception
            _logger.Error(ex, "发送请求异常")
            response.StatusCode = 500
            response.IsSuccess = False
            response.ErrorMessage = ex.Message
            response.ErrorCode = "UNKNOWN_ERROR"
            response.ResponseTimeMs = CLng((DateTime.Now - startTime).TotalMilliseconds)
            Return response
        End Try
    End Function
    
    ''' <summary>
    ''' 执行请求（带重试机制）
    ''' </summary>
    Private Async Function ExecuteWithRetryAsync(url As String, request As MesRequest) As Task(Of MesResponse)
        Dim retryCount = If(request.EnableRetry, Math.Min(request.RetryCount, _config.MaxRetryCount), 0)
        Dim lastException As Exception = Nothing
        
        For i = 0 To retryCount
            Try
                Return Await DoSendRequestAsync(url, request)
            Catch ex As Exception
                lastException = ex
                
                If i < retryCount Then
                    ' 重试之前等待（不使用 Await）
                    Task.Delay(request.RetryDelayMs).Wait()
                    _logger.Warning($"请求失败，准备重试 ({i + 1}/{retryCount}): {ex.Message}")
                End If
            End Try
        Next
        
        ' 所有重试都失败
        If lastException IsNot Nothing Then
            Throw lastException
        End If
        
        Throw New Exception("请求失败")
    End Function
    
    ''' <summary>
    ''' 实际发送请求
    ''' </summary>
    Private Async Function DoSendRequestAsync(url As String, request As MesRequest) As Task(Of MesResponse)
        ' 应用速率限制
        _rateLimiter.WaitIfNeeded()
        
        Using httpRequest = New HttpRequestMessage()
            httpRequest.RequestUri = New Uri(url)
            
            ' 设置方法
            Select Case request.Method
                Case HttpMethod.Get
                    httpRequest.Method = System.Net.Http.HttpMethod.Get
                Case HttpMethod.Post
                    httpRequest.Method = System.Net.Http.HttpMethod.Post
                Case HttpMethod.Put
                    httpRequest.Method = System.Net.Http.HttpMethod.Put
                Case HttpMethod.Delete
                    httpRequest.Method = System.Net.Http.HttpMethod.Delete
                Case HttpMethod.Patch
                    httpRequest.Method = System.Net.Http.HttpMethod.Put
                    httpRequest.Headers.Add("X-HTTP-Method-Override", "PATCH")
                Case HttpMethod.Head
                    httpRequest.Method = System.Net.Http.HttpMethod.Head
                Case HttpMethod.Options
                    httpRequest.Method = System.Net.Http.HttpMethod.Options
            End Select
            
            ' 设置请求头
            For Each header In request.Headers
                httpRequest.Headers.Add(header.Key, header.Value)
            Next
            
            ' 添加认证
            If request.AuthType <> AuthType.None Then
                Select Case request.AuthType
                    Case AuthType.BearerToken
                        httpRequest.Headers.Authorization = New System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.AuthToken)
                    Case AuthType.BasicAuth
                        Dim encoded = Convert.ToBase64String(Text.Encoding.ASCII.GetBytes(request.AuthToken))
                        httpRequest.Headers.Authorization = New System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded)
                    Case AuthType.ApiKey
                        httpRequest.Headers.Add("X-API-Key", request.AuthToken)
                End Select
            End If
            
            ' 设置请求体
            If Not String.IsNullOrEmpty(request.Body) AndAlso 
               (request.Method = HttpMethod.Post OrElse request.Method = HttpMethod.Put OrElse request.Method = HttpMethod.Patch) Then
                Select Case request.DataFormat
                    Case DataFormatType.Json
                        httpRequest.Content = New StringContent(request.Body, Text.Encoding.UTF8, "application/json")
                    Case Else
                        httpRequest.Content = New StringContent(request.Body, Text.Encoding.UTF8, "application/octet-stream")
                End Select
            End If
            
            ' 合并超时和取消令牌
            Dim timeoutCts = New CancellationTokenSource(TimeSpan.FromMilliseconds(request.Timeout))
            Dim linkedCts = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken, timeoutCts.Token)
            
            Try
                ' 发送请求
                Dim httpResponse = Await _httpClient.SendAsync(httpRequest, linkedCts.Token)
                
                ' 读取响应
                Dim responseContent = Await httpResponse.Content.ReadAsStringAsync()
                
                ' 构建响应对象
                Dim response = New MesResponse With {
                    .StatusCode = CInt(httpResponse.StatusCode),
                    .IsSuccess = httpResponse.IsSuccessStatusCode,
                    .Data = responseContent,
                    .RawContent = responseContent,
                    .Timestamp = DateTime.Now
                }
                
                ' 复制响应头
                For Each header In httpResponse.Headers
                    response.Headers(header.Key) = String.Join(",", header.Value)
                Next
                
                ' 如果是失败响应，尝试解析错误信息
                If Not response.IsSuccess Then
                    Try
                        If responseContent.Contains("""code""") Then
                            Dim codeMatch = System.Text.RegularExpressions.Regex.Match(responseContent, """code""\s*:\s*""([^""]+)""")
                            If codeMatch.Success Then
                                response.ErrorCode = codeMatch.Groups(1).Value
                            End If
                        End If
                        
                        If responseContent.Contains("""message""") Then
                            Dim msgMatch = System.Text.RegularExpressions.Regex.Match(responseContent, """message""\s*:\s*""([^""]+)""")
                            If msgMatch.Success Then
                                response.ErrorMessage = msgMatch.Groups(1).Value
                            Else
                                response.ErrorMessage = responseContent
                            End If
                        Else
                            response.ErrorMessage = responseContent
                        End If
                    Catch
                        response.ErrorMessage = responseContent
                    End Try
                End If
                
                Return response
            Finally
                timeoutCts?.Dispose()
                linkedCts?.Dispose()
            End Try
        End Using
    End Function
    
    ''' <summary>
    ''' 生成缓存键
    ''' </summary>
    Private Function GenerateCacheKey(request As MesRequest) As String
        Dim fullUrl = request.BuildUrl()
        If Not fullUrl.StartsWith("http") Then
            fullUrl = _config.BaseUrl.TrimEnd("/"c) & "/" & fullUrl.TrimStart("/"c)
        End If
        Return $"{request.Method}:{fullUrl}"
    End Function
    
    ''' <summary>
    ''' 将非泛型响应转换为泛型响应
    ''' </summary>
    Private Function ConvertToGenericResponse(Of T)(response As MesResponse) As MesResponse(Of T)
        If response.IsSuccess Then
            Try
                Dim data As T = Nothing
                
                ' 如果 T 是 String 类型，直接使用原始数据
                If GetType(T) Is GetType(String) Then
                    data = CType(CObj(response.Data), T)
                Else
                    ' 否则进行 JSON 反序列化
                    data = _converter.FromJson(Of T)(response.Data)
                End If
                
                Return New MesResponse(Of T) With {
                    .RequestId = response.RequestId,
                    .StatusCode = response.StatusCode,
                    .IsSuccess = True,
                    .Data = data,
                    .Timestamp = response.Timestamp,
                    .ResponseTimeMs = response.ResponseTimeMs,
                    .Headers = response.Headers,
                    .RawContent = response.RawContent
                }
            Catch ex As Exception
                _logger.Error(ex, "响应数据解析失败")
                Return MesResponse(Of T).CreateError($"数据解析失败: {ex.Message}", "PARSE_ERROR", 500)
            End Try
        Else
            Return New MesResponse(Of T) With {
                .RequestId = response.RequestId,
                .StatusCode = response.StatusCode,
                .IsSuccess = False,
                .ErrorMessage = response.ErrorMessage,
                .ErrorCode = response.ErrorCode,
                .Timestamp = response.Timestamp,
                .ResponseTimeMs = response.ResponseTimeMs,
                .Headers = response.Headers,
                .RawContent = response.RawContent
            }
        End If
    End Function
    
    ''' <summary>
    ''' 清空缓存
    ''' </summary>
    Public Sub ClearCache()
        _cache.Clear()
        _logger.Info("HTTP 缓存已清空")
    End Sub
    
    ''' <summary>
    ''' 获取缓存统计信息
    ''' </summary>
    Public Function GetCacheStatistics() As CacheStatistics
        Return _cache.GetStatistics()
    End Function
    
    ''' <summary>
    ''' 启用速率限制
    ''' </summary>
    Public Sub EnableRateLimit(Optional minIntervalMs As Integer = 100, Optional windowSeconds As Integer = 60, Optional maxRequestsPerWindow As Integer = 100)
        _rateLimiter.SetMinInterval(minIntervalMs)
        _rateLimiter.ConfigureWindow(windowSeconds, maxRequestsPerWindow)
        _rateLimiter.Enable()
        _logger.Info($"速率限制已启用: {maxRequestsPerWindow}次/{windowSeconds}秒, 最小间隔{minIntervalMs}ms")
    End Sub
    
    ''' <summary>
    ''' 禁用速率限制
    ''' </summary>
    Public Sub DisableRateLimit()
        _rateLimiter.Disable()
        _logger.Info("速率限制已禁用")
    End Sub
    
    ''' <summary>
    ''' 获取速率限制统计信息
    ''' </summary>
    Public Function GetRateLimiterStatistics() As RateLimiterStatistics
        Return _rateLimiter.GetStatistics()
    End Function
    
    ''' <summary>
    ''' 下载文件
    ''' </summary>
    Public Async Function DownloadFileAsync(url As String, savePath As String) As Task(Of MesResponse)
        Try
            Using response = Await _httpClient.GetAsync(url)
                If Not response.IsSuccessStatusCode Then
                    Return MesResponse.CreateError($"下载失败: {response.StatusCode}", "DOWNLOAD_ERROR", CInt(response.StatusCode))
                End If
                
                Dim dir = Path.GetDirectoryName(savePath)
                If Not Directory.Exists(dir) Then
                    Directory.CreateDirectory(dir)
                End If
                
                Dim bytes = Await response.Content.ReadAsByteArrayAsync()
                File.WriteAllBytes(savePath, bytes)
                
                _logger.Info($"文件下载成功: {savePath}")
                Return MesResponse.CreateSuccess($"文件已下载到: {savePath}")
            End Using
        Catch ex As Exception
            _logger.Error(ex, "文件下载失败")
            Return MesResponse.CreateError($"文件下载失败: {ex.Message}", "DOWNLOAD_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 释放资源
    ''' </summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        
        _httpClient?.Dispose()
        _disposed = True
        GC.SuppressFinalize(Me)
    End Sub
End Class
