Imports System.IO
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Threading.Tasks

''' <summary>
''' MES 系统交互框架 - 文件上传服务（核心）
''' </summary>
Public Class MesFileUploadService
    Implements IDisposable
    
    ''' <summary>HTTP 客户端</summary>
    Private _httpClient As MesHttpClient
    
    ''' <summary>配置</summary>
    Private _config As MesConfig
    
    ''' <summary>日志记录器</summary>
    Private _logger As MesLogger
    
    ''' <summary>数据验证器</summary>
    Private _validator As MesValidator
    
    ''' <summary>是否已释放</summary>
    Private _disposed As Boolean = False
    
    ''' <summary>上传进度事件</summary>
    Public Event ProgressChanged(sender As Object, e As UploadProgressEventArgs)
    
    Public Sub New(Optional httpClient As MesHttpClient = Nothing, Optional config As MesConfig = Nothing, Optional logger As MesLogger = Nothing)
        _httpClient = If(httpClient, New MesHttpClient(config, logger))
        _config = If(config, MesConfig.GetInstance())
        _logger = If(logger, MesLogger.GetInstance())
        _validator = New MesValidator(_logger)
    End Sub
    
    ''' <summary>
    ''' 上传单个文件
    ''' </summary>
    Public Async Function UploadAsync(uploadModel As MesFileUploadModel) As Task(Of MesResponse)
        ' 验证模型
        Dim validationEx = _validator.ValidateFileUploadModel(uploadModel, _config.MaxUploadSize, _config.AllowedFileExtensions)
        If validationEx IsNot Nothing Then
            _logger.Error(validationEx)
            Throw validationEx
        End If
        
        uploadModel.StartTime = DateTime.Now
        uploadModel.Status = UploadStatus.InProgress
        
        Try
            Select Case uploadModel.UploadMode
                Case UploadMode.FormData
                    Return Await UploadFormDataAsync(uploadModel)
                Case UploadMode.Binary
                    Return Await UploadBinaryAsync(uploadModel)
                Case UploadMode.Base64
                    Return Await UploadBase64Async(uploadModel)
                Case UploadMode.Multipart
                    Return Await UploadMultipartAsync(uploadModel)
                Case UploadMode.Chunked
                    Return Await UploadChunkedAsync(uploadModel)
                Case Else
                    Return MesResponse.CreateError("不支持的上传模式", "UNSUPPORTED_MODE", 400)
            End Select
        Catch ex As Exception
            uploadModel.Status = UploadStatus.Failed
            uploadModel.ErrorMessage = ex.Message
            _logger.Error(ex, "文件上传异常")
            _logger.LogFileUpload(uploadModel)
            Return MesResponse.CreateError($"文件上传失败: {ex.Message}", "UPLOAD_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 表单上传（Form Data）
    ''' </summary>
    Private Async Function UploadFormDataAsync(uploadModel As MesFileUploadModel) As Task(Of MesResponse)
        Using formContent = New MultipartFormDataContent()
            ' 添加文件
            Using fileStream = File.OpenRead(uploadModel.FilePath)
                formContent.Add(
                    New StreamContent(fileStream),
                    "file",
                    uploadModel.FileName)
            End Using
            
            ' 添加自定义字段
            For Each field In uploadModel.CustomFields
                Dim fieldValue = If(field.Value IsNot Nothing, field.Value.ToString(), "")
                formContent.Add(New StringContent(fieldValue), field.Key)
            Next
            
            ' 计算进度
            Dim fileInfo = New FileInfo(uploadModel.FilePath)
            uploadModel.Progress = 50
            RaiseEvent ProgressChanged(Me, New UploadProgressEventArgs(uploadModel.FileName, 50))
            
            ' 执行上传
            Using httpClient = New Net.Http.HttpClient()
                Dim response = Await httpClient.PostAsync(uploadModel.UploadUrl, formContent)
                Dim content = Await response.Content.ReadAsStringAsync()
                
                uploadModel.ResponseData = content
                
                If response.IsSuccessStatusCode Then
                    uploadModel.Status = UploadStatus.Completed
                    uploadModel.Progress = 100
                    RaiseEvent ProgressChanged(Me, New UploadProgressEventArgs(uploadModel.FileName, 100))
                    _logger.LogFileUpload(uploadModel)
                    Return MesResponse.CreateSuccess(content, CInt(response.StatusCode))
                Else
                    uploadModel.Status = UploadStatus.Failed
                    uploadModel.ErrorMessage = $"服务器返回错误: {response.StatusCode}"
                    _logger.LogFileUpload(uploadModel)
                    Return MesResponse.CreateError(content, "UPLOAD_ERROR", CInt(response.StatusCode))
                End If
            End Using
        End Using
    End Function
    
    ''' <summary>
    ''' 二进制上传
    ''' </summary>
    Private Async Function UploadBinaryAsync(uploadModel As MesFileUploadModel) As Task(Of MesResponse)
        Dim fileBytes = File.ReadAllBytes(uploadModel.FilePath)
        
        ' 计算文件哈希
        uploadModel.FileHash = CalculateFileHash(uploadModel.FilePath, uploadModel.HashAlgorithm)
        
        Using httpClient = New Net.Http.HttpClient()
            ' 设置请求头
            httpClient.DefaultRequestHeaders.Add("Content-Disposition", $"attachment; filename=""{uploadModel.FileName}""")
            httpClient.DefaultRequestHeaders.Add("X-File-Hash", uploadModel.FileHash)
            httpClient.DefaultRequestHeaders.Add("X-File-Size", uploadModel.FileSize.ToString())
            
            ' 添加自定义字段作为查询参数
            Dim url = uploadModel.UploadUrl
            If uploadModel.CustomFields.Count > 0 Then
                Dim paramList = New List(Of String)
                For Each field In uploadModel.CustomFields
                    Dim fieldValue = If(field.Value IsNot Nothing, field.Value.ToString(), "")
                    paramList.Add($"{Uri.EscapeDataString(field.Key)}={Uri.EscapeDataString(fieldValue)}")
                Next
                url = $"{url}?{String.Join("&", paramList)}"
            End If
            
            Dim content = New ByteArrayContent(fileBytes)
            content.Headers.ContentType = New System.Net.Http.Headers.MediaTypeHeaderValue(uploadModel.FileType)
            
            uploadModel.Progress = 50
            RaiseEvent ProgressChanged(Me, New UploadProgressEventArgs(uploadModel.FileName, 50))
            
            Dim response = Await httpClient.PutAsync(url, content)
            Dim responseContent = Await response.Content.ReadAsStringAsync()
            
            uploadModel.ResponseData = responseContent
            
            If response.IsSuccessStatusCode Then
                uploadModel.Status = UploadStatus.Completed
                uploadModel.Progress = 100
                RaiseEvent ProgressChanged(Me, New UploadProgressEventArgs(uploadModel.FileName, 100))
                _logger.LogFileUpload(uploadModel)
                Return MesResponse.CreateSuccess(responseContent, CInt(response.StatusCode))
            Else
                uploadModel.Status = UploadStatus.Failed
                uploadModel.ErrorMessage = $"服务器返回错误: {response.StatusCode}"
                _logger.LogFileUpload(uploadModel)
                Return MesResponse.CreateError(responseContent, "UPLOAD_ERROR", CInt(response.StatusCode))
            End If
        End Using
    End Function
    
    ''' <summary>
    ''' Base64 编码上传
    ''' </summary>
    Private Async Function UploadBase64Async(uploadModel As MesFileUploadModel) As Task(Of MesResponse)
        Dim fileBytes = File.ReadAllBytes(uploadModel.FilePath)
        Dim base64String = Convert.ToBase64String(fileBytes)
        
        uploadModel.FileHash = CalculateFileHash(uploadModel.FilePath, uploadModel.HashAlgorithm)
        
        ' 构建上传数据
        Dim uploadData = New With {
            .filename = uploadModel.FileName,
            .fileContent = base64String,
            .fileType = uploadModel.FileType,
            .fileSize = uploadModel.FileSize,
            .fileHash = uploadModel.FileHash,
            .customFields = uploadModel.CustomFields
        }
        
        uploadModel.Progress = 50
        RaiseEvent ProgressChanged(Me, New UploadProgressEventArgs(uploadModel.FileName, 50))
        
        ' 使用 HTTP 客户端发送
        Dim response = Await _httpClient.PostAsync(uploadModel.UploadUrl, uploadData)
        
        uploadModel.ResponseData = response.Data
        
        If response.IsSuccess Then
            uploadModel.Status = UploadStatus.Completed
            uploadModel.Progress = 100
            RaiseEvent ProgressChanged(Me, New UploadProgressEventArgs(uploadModel.FileName, 100))
            _logger.LogFileUpload(uploadModel)
            Return response
        Else
            uploadModel.Status = UploadStatus.Failed
            uploadModel.ErrorMessage = response.ErrorMessage
            _logger.LogFileUpload(uploadModel)
            Return response
        End If
    End Function
    
    ''' <summary>
    ''' 多部分上传（Multipart）
    ''' </summary>
    Private Async Function UploadMultipartAsync(uploadModel As MesFileUploadModel) As Task(Of MesResponse)
        Using formContent = New MultipartFormDataContent()
            ' 添加文件
            Using fileStream = File.OpenRead(uploadModel.FilePath)
                formContent.Add(
                    New StreamContent(fileStream),
                    "file",
                    uploadModel.FileName)
            End Using
            
            ' 添加元数据
            formContent.Add(New StringContent(uploadModel.FileName), "fileName")
            formContent.Add(New StringContent(uploadModel.FileType), "fileType")
            formContent.Add(New StringContent(uploadModel.FileSize.ToString()), "fileSize")
            
            ' 添加自定义字段
            For Each field In uploadModel.CustomFields
                Dim fieldValue = If(field.Value IsNot Nothing, field.Value.ToString(), "")
                formContent.Add(New StringContent(fieldValue), field.Key)
            Next
            
            uploadModel.Progress = 50
            RaiseEvent ProgressChanged(Me, New UploadProgressEventArgs(uploadModel.FileName, 50))
            
            Using httpClient = New Net.Http.HttpClient()
                Dim response = Await httpClient.PostAsync(uploadModel.UploadUrl, formContent)
                Dim content = Await response.Content.ReadAsStringAsync()
                
                uploadModel.ResponseData = content
                
                If response.IsSuccessStatusCode Then
                    uploadModel.Status = UploadStatus.Completed
                    uploadModel.Progress = 100
                    RaiseEvent ProgressChanged(Me, New UploadProgressEventArgs(uploadModel.FileName, 100))
                    _logger.LogFileUpload(uploadModel)
                    Return MesResponse.CreateSuccess(content, CInt(response.StatusCode))
                Else
                    uploadModel.Status = UploadStatus.Failed
                    uploadModel.ErrorMessage = $"服务器返回错误: {response.StatusCode}"
                    _logger.LogFileUpload(uploadModel)
                    Return MesResponse.CreateError(content, "UPLOAD_ERROR", CInt(response.StatusCode))
                End If
            End Using
        End Using
    End Function
    
    ''' <summary>
    ''' 分块上传（支持断点续传）
    ''' </summary>
    Private Async Function UploadChunkedAsync(uploadModel As MesFileUploadModel) As Task(Of MesResponse)
        Try
            ' 检查是否已取消
            uploadModel.CancellationToken.ThrowIfCancellationRequested()
            
            Dim fileSize = New FileInfo(uploadModel.FilePath).Length
            Dim chunkSize = uploadModel.ChunkSize
            uploadModel.TotalChunks = CInt(Math.Ceiling(CDbl(fileSize) / chunkSize))
            
            ' 计算文件哈希
            uploadModel.FileHash = CalculateFileHash(uploadModel.FilePath, uploadModel.HashAlgorithm)
            
            Using fileStream = File.OpenRead(uploadModel.FilePath)
                For chunkIndex = 0 To uploadModel.TotalChunks - 1
                    ' 检查是否已取消
                    uploadModel.CancellationToken.ThrowIfCancellationRequested()
                    
                    uploadModel.CurrentChunkIndex = chunkIndex
                    
                    ' 读取分块
                    Dim buffer = New Byte(Math.Min(CInt(chunkSize - 1), CInt(fileSize - fileStream.Position - 1))) {}
                    Dim bytesRead = Await fileStream.ReadAsync(buffer, 0, buffer.Length, uploadModel.CancellationToken)
                    
                    If bytesRead = 0 Then Exit For
                    
                    ' 上传分块
                    Dim result = Await UploadChunkAsync(uploadModel, buffer, bytesRead, chunkIndex)
                    
                    If Not result.IsSuccess Then
                        uploadModel.Status = UploadStatus.Failed
                        uploadModel.ErrorMessage = result.ErrorMessage
                        _logger.LogFileUpload(uploadModel)
                        Return result
                    End If
                    
                    ' 更新进度
                    uploadModel.Progress = CInt((chunkIndex + 1) * 100 / uploadModel.TotalChunks)
                    RaiseEvent ProgressChanged(Me, New UploadProgressEventArgs(uploadModel.FileName, uploadModel.Progress))
                Next
            End Using
            
            uploadModel.Status = UploadStatus.Completed
            uploadModel.Progress = 100
            uploadModel.EndTime = DateTime.Now
            RaiseEvent ProgressChanged(Me, New UploadProgressEventArgs(uploadModel.FileName, 100))
            _logger.LogFileUpload(uploadModel)
            
            Return MesResponse.CreateSuccess($"分块上传完成: {uploadModel.TotalChunks} 个分块")
            
        Catch ex As OperationCanceledException
            uploadModel.Status = UploadStatus.Cancelled
            uploadModel.ErrorMessage = "上传已取消"
            _logger.Warning($"上传已取消: {uploadModel.FileName}")
            _logger.LogFileUpload(uploadModel)
            Return MesResponse.CreateError("上传已取消", "UPLOAD_CANCELLED", 499)
        Catch ex As Exception
            uploadModel.Status = UploadStatus.Failed
            uploadModel.ErrorMessage = ex.Message
            _logger.Error(ex)
            _logger.LogFileUpload(uploadModel)
            Return MesResponse.CreateError($"分块上传失败: {ex.Message}", "CHUNKED_UPLOAD_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 上传单个分块
    ''' </summary>
    Private Async Function UploadChunkAsync(uploadModel As MesFileUploadModel, buffer As Byte(), bytesRead As Integer, chunkIndex As Integer) As Task(Of MesResponse)
        Try
            Dim chunkData = New With {
                .fileName = uploadModel.FileName,
                .fileSize = uploadModel.FileSize,
                .fileHash = uploadModel.FileHash,
                .chunkIndex = chunkIndex,
                .totalChunks = uploadModel.TotalChunks,
                .chunkContent = Convert.ToBase64String(buffer, 0, bytesRead),
                .customFields = uploadModel.CustomFields
            }
            
            Return Await _httpClient.PostAsync(uploadModel.UploadUrl, chunkData)
        Catch ex As Exception
            Return MesResponse.CreateError($"分块 {chunkIndex} 上传失败: {ex.Message}", "CHUNK_UPLOAD_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 计算文件哈希
    ''' </summary>
    Private Function CalculateFileHash(filePath As String, algorithm As String) As String
        Try
            Using stream = File.OpenRead(filePath)
                Dim hash As Byte()
                
                Select Case algorithm?.ToUpper()
                    Case "SHA256"
                        Using hashAlgorithm = System.Security.Cryptography.SHA256.Create()
                            hash = hashAlgorithm.ComputeHash(stream)
                        End Using
                    Case Else ' MD5
                        Using hashAlgorithm = System.Security.Cryptography.MD5.Create()
                            hash = hashAlgorithm.ComputeHash(stream)
                        End Using
                End Select
                
                Return BitConverter.ToString(hash).Replace("-", "").ToLower()
            End Using
        Catch ex As Exception
            _logger.Warning($"计算文件哈希失败: {ex.Message}")
            Return ""
        End Try
    End Function
    
    ''' <summary>
    ''' 批量上传文件
    ''' </summary>
    Public Async Function UploadMultipleAsync(multiUploadModel As MesMultiFileUploadModel) As Task(Of MesResponse)
        Try
            If multiUploadModel.Files.Count = 0 Then
                Return MesResponse.CreateError("没有文件要上传", "NO_FILES", 400)
            End If
            
            If multiUploadModel.EnableParallelUpload Then
                ' 并行上传
                Return Await UploadParallelAsync(multiUploadModel)
            Else
                ' 顺序上传
                Return Await UploadSequentialAsync(multiUploadModel)
            End If
        Catch ex As Exception
            _logger.Error(ex)
            Return MesResponse.CreateError($"批量上传失败: {ex.Message}", "BATCH_UPLOAD_ERROR", 500)
        End Try
    End Function
    
    ''' <summary>
    ''' 顺序上传文件
    ''' </summary>
    Private Async Function UploadSequentialAsync(multiUploadModel As MesMultiFileUploadModel) As Task(Of MesResponse)
        Dim results = New List(Of String)
        
        For Each file In multiUploadModel.Files
            ' 合并共享的自定义字段
            For Each field In multiUploadModel.SharedCustomFields
                file.CustomFields(field.Key) = field.Value
            Next
            
            file.UploadUrl = multiUploadModel.UploadUrl
            file.UploadMode = multiUploadModel.UploadMode
            
            Dim result = Await UploadAsync(file)
            
            If result.IsSuccess Then
                multiUploadModel.SuccessCount += 1
                results.Add($"? {file.FileName}")
            Else
                multiUploadModel.FailureCount += 1
                results.Add($"? {file.FileName}: {result.ErrorMessage}")
            End If
            
            ' 更新总进度
            multiUploadModel.TotalProgress = CInt((multiUploadModel.SuccessCount + multiUploadModel.FailureCount) * 100 / multiUploadModel.Files.Count)
            RaiseEvent ProgressChanged(Me, New UploadProgressEventArgs("", multiUploadModel.TotalProgress))
        Next
        
        Dim message = $"上传完成: 成功 {multiUploadModel.SuccessCount}, 失败 {multiUploadModel.FailureCount}" & vbCrLf & 
                     String.Join(vbCrLf, results)
        
        Return MesResponse.CreateSuccess(message)
    End Function
    
    ''' <summary>
    ''' 并行上传文件
    ''' </summary>
    Private Async Function UploadParallelAsync(multiUploadModel As MesMultiFileUploadModel) As Task(Of MesResponse)
        Dim tasks = New List(Of Task(Of MesResponse))
        
        For i = 0 To multiUploadModel.Files.Count - 1
            If tasks.Count >= multiUploadModel.MaxParallelTasks Then
                Dim completedTask = Await Task.WhenAny(tasks)
                tasks.Remove(completedTask)
            End If
            
            Dim file = multiUploadModel.Files(i)
            
            ' 合并共享的自定义字段
            For Each field In multiUploadModel.SharedCustomFields
                file.CustomFields(field.Key) = field.Value
            Next
            
            file.UploadUrl = multiUploadModel.UploadUrl
            file.UploadMode = multiUploadModel.UploadMode
            
            Dim uploadTask = UploadAsync(file)
            tasks.Add(uploadTask)
        Next
        
        Await Task.WhenAll(tasks)
        
        ' 统计结果
        For Each result In tasks.Select(Function(t) t.Result)
            If result.IsSuccess Then
                multiUploadModel.SuccessCount += 1
            Else
                multiUploadModel.FailureCount += 1
            End If
        Next
        
        multiUploadModel.TotalProgress = 100
        
        Dim message = $"批量上传完成: 成功 {multiUploadModel.SuccessCount}, 失败 {multiUploadModel.FailureCount}"
        
        Return MesResponse.CreateSuccess(message)
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

''' <summary>
''' 上传进度事件参数
''' </summary>
Public Class UploadProgressEventArgs
    Inherits EventArgs
    
    ''' <summary>文件名</summary>
    Public Property FileName As String
    
    ''' <summary>进度百分比（0-100）</summary>
    Public Property Progress As Integer
    
    Public Sub New(fileName As String, progress As Integer)
        Me.FileName = fileName
        Me.Progress = progress
    End Sub
End Class
