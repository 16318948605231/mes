Imports System.IO
Imports System.Text

''' <summary>
''' MES 系统交互框架 - 日志记录
''' </summary>
Public Class MesLogger
    ''' <summary>日志文件路径</summary>
    Private _logFilePath As String
    
    ''' <summary>日志级别</summary>
    Public Property CurrentLogLevel As LogLevel = LogLevel.Info
    
    ''' <summary>日志锁</summary>
    Private ReadOnly _logLock As New Object()
    
    ''' <summary>日志实例</summary>
    Private Shared _instance As MesLogger
    Private Shared ReadOnly _lock As New Object()
    
    Public Sub New(Optional logFilePath As String = Nothing, Optional logLevel As LogLevel = LogLevel.Info)
        CurrentLogLevel = logLevel
        
        ' 如果未指定日志路径，使用默认路径
        If String.IsNullOrEmpty(logFilePath) Then
            _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "mes_framework.log")
        Else
            _logFilePath = logFilePath
        End If
        
        ' 确保目录存在
        Dim logDir = Path.GetDirectoryName(_logFilePath)
        If Not Directory.Exists(logDir) Then
            Directory.CreateDirectory(logDir)
        End If
    End Sub
    
    ''' <summary>获取单例实例</summary>
    Public Shared Function GetInstance() As MesLogger
        If _instance Is Nothing Then
            SyncLock _lock
                If _instance Is Nothing Then
                    _instance = New MesLogger()
                End If
            End SyncLock
        End If
        Return _instance
    End Function
    
    ''' <summary>设置日志级别</summary>
    Public Sub SetLogLevel(logLevel As LogLevel)
        CurrentLogLevel = logLevel
    End Sub
    
    ''' <summary>记录 Debug 级别日志</summary>
    Public Sub Debug(message As String, Optional tag As String = Nothing)
        WriteLog(LogLevel.Debug, message, tag)
    End Sub
    
    ''' <summary>记录 Info 级别日志</summary>
    Public Sub Info(message As String, Optional tag As String = Nothing)
        WriteLog(LogLevel.Info, message, tag)
    End Sub
    
    ''' <summary>记录 Warning 级别日志</summary>
    Public Sub Warning(message As String, Optional tag As String = Nothing)
        WriteLog(LogLevel.Warning, message, tag)
    End Sub
    
    ''' <summary>记录 Error 级别日志</summary>
    Public Sub [Error](message As String, Optional tag As String = Nothing)
        WriteLog(LogLevel.Error, message, tag)
    End Sub
    
    ''' <summary>记录异常</summary>
    Public Sub [Error](ex As Exception, Optional tag As String = Nothing)
        Dim message = MesExceptionHandler.GetFullMessage(ex)
        WriteLog(LogLevel.Error, message, tag)
    End Sub
    
    ''' <summary>记录 Critical 级别日志</summary>
    Public Sub Critical(message As String, Optional tag As String = Nothing)
        WriteLog(LogLevel.Critical, message, tag)
    End Sub
    
    ''' <summary>
    ''' 记录 HTTP 请求
    ''' </summary>
    Public Sub LogRequest(request As MesRequest)
        Try
            Dim message = New System.Text.StringBuilder()
            message.AppendLine($"===== HTTP Request =====")
            message.AppendLine($"RequestId: {request.RequestId}")
            message.AppendLine($"Method: {request.Method}")
            message.AppendLine($"Url: {request.BuildUrl()}")
            message.AppendLine($"Timeout: {request.Timeout}ms")
            message.AppendLine($"Auth: {request.AuthType}")
            
            If request.Headers.Count > 0 Then
                message.AppendLine("Headers:")
                For Each header In request.Headers
                    message.AppendLine($"  {header.Key}: {header.Value}")
                Next
            End If
            
            If Not String.IsNullOrEmpty(request.Body) Then
                Dim bodyPreview = If(request.Body.Length > 500, 
                    request.Body.Substring(0, 500) & "...", 
                    request.Body)
                message.AppendLine($"Body: {bodyPreview}")
            End If
            
            Info(message.ToString(), "HTTP_REQUEST")
        Catch ex As Exception
            ' 忽略日志本身的错误
        End Try
    End Sub
    
    ''' <summary>
    ''' 记录 HTTP 响应
    ''' </summary>
    Public Sub LogResponse(response As MesResponse)
        Try
            Dim message = New System.Text.StringBuilder()
            message.AppendLine($"===== HTTP Response =====")
            message.AppendLine($"RequestId: {response.RequestId}")
            message.AppendLine($"StatusCode: {response.StatusCode}")
            message.AppendLine($"IsSuccess: {response.IsSuccess}")
            message.AppendLine($"ResponseTime: {response.ResponseTimeMs}ms")
            
            If Not response.IsSuccess Then
                message.AppendLine($"ErrorCode: {response.ErrorCode}")
                message.AppendLine($"ErrorMessage: {response.ErrorMessage}")
            End If
            
            If Not String.IsNullOrEmpty(response.Data) Then
                Dim dataPreview = If(response.Data.Length > 500, 
                    response.Data.Substring(0, 500) & "...", 
                    response.Data)
                message.AppendLine($"Data: {dataPreview}")
            End If
            
            If response.IsSuccess Then
                Info(message.ToString(), "HTTP_RESPONSE")
            Else
                [Error](message.ToString(), "HTTP_RESPONSE")
            End If
        Catch ex As Exception
            ' 忽略日志本身的错误
        End Try
    End Sub
    
    ''' <summary>
    ''' 记录文件上传
    ''' </summary>
    Public Sub LogFileUpload(uploadModel As MesFileUploadModel)
        Try
            Dim message = New System.Text.StringBuilder()
            message.AppendLine($"===== File Upload =====")
            message.AppendLine($"FileName: {uploadModel.FileName}")
            message.AppendLine($"FileSize: {uploadModel.FileSize} bytes")
            message.AppendLine($"FileType: {uploadModel.FileType}")
            message.AppendLine($"UploadMode: {uploadModel.UploadMode}")
            message.AppendLine($"Status: {uploadModel.Status}")
            message.AppendLine($"Progress: {uploadModel.Progress}%")
            
            If uploadModel.Status = UploadStatus.Completed Then
                message.AppendLine($"Duration: {uploadModel.GetUploadDurationSeconds():F2}s")
                message.AppendLine($"Speed: {uploadModel.GetUploadSpeedMBps():F2} MB/s")
            End If
            
            If Not String.IsNullOrEmpty(uploadModel.ErrorMessage) Then
                message.AppendLine($"Error: {uploadModel.ErrorMessage}")
                [Error](message.ToString(), "FILE_UPLOAD")
            Else
                Info(message.ToString(), "FILE_UPLOAD")
            End If
        Catch ex As Exception
            ' 忽略日志本身的错误
        End Try
    End Sub
    
    ''' <summary>
    ''' 写入日志
    ''' </summary>
    Private Sub WriteLog(level As LogLevel, message As String, Optional tag As String = Nothing)
        Try
            ' 检查日志级别
            If level < CurrentLogLevel Then Return
            
            Dim timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            Dim levelStr = level.ToString().ToUpper()
            Dim tagStr = If(String.IsNullOrEmpty(tag), "", $"[{tag}]")
            Dim logMessage = $"{timestamp} | {levelStr} {tagStr} | {message}" & vbCrLf
            
            SyncLock _logLock
                ' 按日期分割日志文件
                Dim dateStr = DateTime.Now.ToString("yyyy-MM-dd")
                Dim baseName = Path.GetFileNameWithoutExtension(_logFilePath)
                Dim ext = Path.GetExtension(_logFilePath)
                Dim dir = Path.GetDirectoryName(_logFilePath)
                Dim dailyLogFile = Path.Combine(dir, $"{baseName}_{dateStr}{ext}")
                
                ' 写入日志文件
                File.AppendAllText(dailyLogFile, logMessage)
                
                ' 控制日志文件大小（超过 10MB 则归档）
                Dim fileInfo = New System.IO.FileInfo(dailyLogFile)
                If fileInfo.Length > 10 * 1024 * 1024 Then
                    Dim archiveFile = Path.Combine(dir, $"{baseName}_{dateStr}_{DateTime.Now:HHmmss}.log.bak")
                    System.IO.File.Move(dailyLogFile, archiveFile)
                End If
            End SyncLock
            
            ' 同时输出到调试输出
            System.Diagnostics.Debug.WriteLine(logMessage)
            
        Catch ex As Exception
            ' 日志记录失败，忽略
            System.Diagnostics.Debug.WriteLine($"日志写入失败: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' 清空日志文件
    ''' </summary>
    Public Sub ClearLogs()
        Try
            SyncLock _logLock
                Dim logDir = Path.GetDirectoryName(_logFilePath)
                If Directory.Exists(logDir) Then
                    For Each file In Directory.GetFiles(logDir, "*.log*")
                        System.IO.File.Delete(file)
                    Next
                End If
            End SyncLock
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine($"清空日志失败: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' 获取最近的日志内容
    ''' </summary>
    Public Function GetRecentLogs(Optional maxLines As Integer = 100) As String
        Try
            Dim dateStr = DateTime.Now.ToString("yyyy-MM-dd")
            Dim baseName = Path.GetFileNameWithoutExtension(_logFilePath)
            Dim ext = Path.GetExtension(_logFilePath)
            Dim dir = Path.GetDirectoryName(_logFilePath)
            Dim dailyLogFile = Path.Combine(dir, $"{baseName}_{dateStr}{ext}")
            
            If Not File.Exists(dailyLogFile) Then Return ""
            
            ' 读取最后 N 行
            Dim lines = File.ReadAllLines(dailyLogFile)
            Dim startIndex = Math.Max(0, lines.Length - maxLines)
            
            Return String.Join(vbCrLf, lines.Skip(startIndex))
        Catch ex As Exception
            Return $"获取日志失败: {ex.Message}"
        End Try
    End Function
End Class
