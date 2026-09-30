Imports System.Threading

''' <summary>
''' MES 系统交互框架 - 文件上传模型
''' </summary>
Public Class MesFileUploadModel
    ''' <summary>文件完整路径</summary>
    Public Property FilePath As String

    ''' <summary>文件名</summary>
    Public Property FileName As String

    ''' <summary>文件 MIME 类型</summary>
    Public Property FileType As String

    ''' <summary>文件大小（字节）</summary>
    Public Property FileSize As Long

    ''' <summary>文件哈希值（MD5/SHA256）</summary>
    Public Property FileHash As String

    ''' <summary>哈希算法类型</summary>
    Public Property HashAlgorithm As String = "MD5"

    ''' <summary>上传模式</summary>
    Public Property UploadMode As UploadMode = UploadMode.FormData

    ''' <summary>目标上传 URL</summary>
    Public Property UploadUrl As String

    ''' <summary>自定义字段（附加参数）</summary>
    Public Property CustomFields As New Dictionary(Of String, Object)

    ''' <summary>上传进度（0-100）</summary>
    Public Property Progress As Integer

    ''' <summary>上传状态</summary>
    Public Property Status As UploadStatus = UploadStatus.Pending

    ''' <summary>错误信息</summary>
    Public Property ErrorMessage As String

    ''' <summary>分块大小（用于分块上传，单位：字节）</summary>
    Public Property ChunkSize As Long = 1024 * 1024 * 5 ' 默认 5MB

    ''' <summary>当前分块索引</summary>
    Public Property CurrentChunkIndex As Integer

    ''' <summary>总分块数</summary>
    Public Property TotalChunks As Integer

    ''' <summary>上传开始时间</summary>
    Public Property StartTime As DateTime?

    ''' <summary>上传结束时间</summary>
    Public Property EndTime As DateTime?

    ''' <summary>响应数据</summary>
    Public Property ResponseData As String

    ''' <summary>取消令牌（用于取消上传）</summary>
    Public Property CancellationToken As CancellationToken = CancellationToken.None

    ''' <summary>
    ''' 添加自定义字段
    ''' </summary>
    Public Sub AddCustomField(key As String, value As Object)
        If String.IsNullOrEmpty(key) Then Return
        CustomFields(key) = value
    End Sub

    ''' <summary>
    ''' 计算上传耗时（秒）
    ''' </summary>
    Public Function GetUploadDurationSeconds() As Double
        If StartTime Is Nothing OrElse EndTime Is Nothing Then Return 0
        Return (EndTime.Value - StartTime.Value).TotalSeconds
    End Function

    ''' <summary>
    ''' 计算上传速率（MB/s）
    ''' </summary>
    Public Function GetUploadSpeedMBps() As Double
        Dim duration = GetUploadDurationSeconds()
        If duration <= 0 Then Return 0
        Return (FileSize / (1024 * 1024)) / duration
    End Function

    ''' <summary>
    ''' 计算剩余上传时间（秒）
    ''' </summary>
    Public Function GetEstimatedRemainingSeconds() As Double
        If Progress <= 0 Then Return 0
        Dim speed = GetUploadSpeedMBps()
        If speed <= 0 Then Return 0
        Dim remainingMB = (FileSize * (100 - Progress) / 100) / (1024 * 1024)
        Return remainingMB / speed
    End Function

    ''' <summary>
    ''' 保存上传状态到文件（用于断点续传）
    ''' </summary>
    Public Sub SaveState(stateFilePath As String)
        Try
            ' 使用简单的 JSON 构建
            Dim json = "{" & vbCrLf &
                $"""FileName"":""{EscapeJson(Me.FileName)}""," & vbCrLf &
                $"""FilePath"":""{EscapeJson(Me.FilePath)}""," & vbCrLf &
                $"""FileHash"":""{EscapeJson(Me.FileHash)}""," & vbCrLf &
                $"""FileSize"":{Me.FileSize}," & vbCrLf &
                $"""UploadUrl"":""{EscapeJson(Me.UploadUrl)}""," & vbCrLf &
                $"""CurrentChunkIndex"":{Me.CurrentChunkIndex}," & vbCrLf &
                $"""TotalChunks"":{Me.TotalChunks}," & vbCrLf &
                $"""ChunkSize"":{Me.ChunkSize}," & vbCrLf &
                $"""Progress"":{Me.Progress}" & vbCrLf &
                "}"
            
            System.IO.File.WriteAllText(stateFilePath, json)
        Catch ex As Exception
            Debug.WriteLine($"保存上传状态失败: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' 从文件恢复上传状态（用于断点续传）
    ''' </summary>
    Public Shared Function LoadState(stateFilePath As String) As MesFileUploadModel
        Try
            If Not System.IO.File.Exists(stateFilePath) Then Return Nothing
            
            Dim json = System.IO.File.ReadAllText(stateFilePath)
            Dim model = New MesFileUploadModel()
            
            ' 简单的 JSON 解析
            model.FileName = ExtractJsonValue(json, "FileName")
            model.FilePath = ExtractJsonValue(json, "FilePath")
            model.FileHash = ExtractJsonValue(json, "FileHash")
            model.UploadUrl = ExtractJsonValue(json, "UploadUrl")
            
            Dim temp As String
            temp = ExtractJsonValue(json, "FileSize")
            If Not String.IsNullOrEmpty(temp) Then Long.TryParse(temp, model.FileSize)
            
            temp = ExtractJsonValue(json, "CurrentChunkIndex")
            If Not String.IsNullOrEmpty(temp) Then Integer.TryParse(temp, model.CurrentChunkIndex)
            
            temp = ExtractJsonValue(json, "TotalChunks")
            If Not String.IsNullOrEmpty(temp) Then Integer.TryParse(temp, model.TotalChunks)
            
            temp = ExtractJsonValue(json, "ChunkSize")
            If Not String.IsNullOrEmpty(temp) Then Long.TryParse(temp, model.ChunkSize)
            
            temp = ExtractJsonValue(json, "Progress")
            If Not String.IsNullOrEmpty(temp) Then Integer.TryParse(temp, model.Progress)
            
            model.Status = UploadStatus.Paused
            
            Return model
        Catch ex As Exception
            Debug.WriteLine($"加载上传状态失败: {ex.Message}")
            Return Nothing
        End Try
    End Function
    
    ''' <summary>
    ''' 删除状态文件
    ''' </summary>
    Public Shared Sub DeleteState(stateFilePath As String)
        Try
            If System.IO.File.Exists(stateFilePath) Then
                System.IO.File.Delete(stateFilePath)
            End If
        Catch ex As Exception
            Debug.WriteLine($"删除状态文件失败: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' 转义 JSON 字符串
    ''' </summary>
    Private Shared Function EscapeJson(value As String) As String
        If String.IsNullOrEmpty(value) Then Return ""
        Return value.Replace("\", "\\").Replace("""", "\""").Replace(vbCr, "").Replace(vbLf, "")
    End Function
    
    ''' <summary>
    ''' 从 JSON 中提取值
    ''' </summary>
    Private Shared Function ExtractJsonValue(json As String, key As String) As String
        Try
            Dim pattern = $"""{key}""\s*:\s*""([^""]*)""|""{key}""\s*:\s*([0-9]+)"
            Dim match = System.Text.RegularExpressions.Regex.Match(json, pattern)
            If match.Success Then
                Return If(match.Groups(1).Success, match.Groups(1).Value, match.Groups(2).Value)
            End If
        Catch
        End Try
        Return ""
    End Function
End Class

''' <summary>
''' 多文件上传模型
''' </summary>
Public Class MesMultiFileUploadModel
    ''' <summary>要上传的文件列表</summary>
    Public Property Files As New List(Of MesFileUploadModel)

    ''' <summary>目标上传 URL</summary>
    Public Property UploadUrl As String

    ''' <summary>共享的自定义字段</summary>
    Public Property SharedCustomFields As New Dictionary(Of String, Object)

    ''' <summary>上传模式</summary>
    Public Property UploadMode As UploadMode = UploadMode.FormData

    ''' <summary>是否并行上传</summary>
    Public Property EnableParallelUpload As Boolean = False

    ''' <summary>最大并行任务数</summary>
    Public Property MaxParallelTasks As Integer = 3

    ''' <summary>总进度（0-100）</summary>
    Public Property TotalProgress As Integer

    ''' <summary>成功的文件数</summary>
    Public Property SuccessCount As Integer

    ''' <summary>失败的文件数</summary>
    Public Property FailureCount As Integer

    ''' <summary>
    ''' 添加文件
    ''' </summary>
    Public Sub AddFile(filePath As String)
        If String.IsNullOrEmpty(filePath) OrElse Not System.IO.File.Exists(filePath) Then Return

        Dim fileInfo = New System.IO.FileInfo(filePath)
        Dim model = New MesFileUploadModel With {
            .FilePath = filePath,
            .FileName = fileInfo.Name,
            .FileSize = fileInfo.Length,
            .FileType = GetMimeType(fileInfo.Extension),
            .UploadUrl = UploadUrl
        }

        Files.Add(model)
    End Sub

    ''' <summary>
    ''' 获取文件 MIME 类型
    ''' </summary>
    Private Shared Function GetMimeType(extension As String) As String
        Select Case extension?.ToLower()
            Case ".jpg", ".jpeg"
                Return "image/jpeg"
            Case ".png"
                Return "image/png"
            Case ".gif"
                Return "image/gif"
            Case ".bmp"
                Return "image/bmp"
            Case ".pdf"
                Return "application/pdf"
            Case ".doc"
                Return "application/msword"
            Case ".docx"
                Return "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            Case ".xls"
                Return "application/vnd.ms-excel"
            Case ".xlsx"
                Return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            Case ".zip"
                Return "application/zip"
            Case ".txt"
                Return "text/plain"
            Case Else
                Return "application/octet-stream"
        End Select
    End Function
End Class
