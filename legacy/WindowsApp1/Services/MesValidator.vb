Imports System.Text.RegularExpressions

''' <summary>
''' MES 系统交互框架 - 数据验证器
''' </summary>
Public Class MesValidator
    ''' <summary>日志记录器</summary>
    Private _logger As MesLogger
    
    Public Sub New(Optional logger As MesLogger = Nothing)
        _logger = If(logger, MesLogger.GetInstance())
    End Sub
    
    ''' <summary>
    ''' 验证字符串不为空
    ''' </summary>
    Public Function ValidateNotEmpty(value As String, fieldName As String) As Boolean
        If String.IsNullOrWhiteSpace(value) Then
            _logger.Warning($"验证失败: {fieldName} 不能为空")
            Return False
        End If
        Return True
    End Function
    
    ''' <summary>
    ''' 验证字符串长度
    ''' </summary>
    Public Function ValidateLength(value As String, fieldName As String, minLength As Integer, maxLength As Integer) As Boolean
        If String.IsNullOrEmpty(value) Then Return False
        If value.Length < minLength OrElse value.Length > maxLength Then
            _logger.Warning($"验证失败: {fieldName} 长度必须在 {minLength} 到 {maxLength} 之间")
            Return False
        End If
        Return True
    End Function
    
    ''' <summary>
    ''' 验证电子邮件格式
    ''' </summary>
    Public Function ValidateEmail(email As String) As Boolean
        Try
            Dim pattern = "^[^@\s]+@[^@\s]+\.[^@\s]+$"
            Return Regex.IsMatch(email, pattern)
        Catch
            Return False
        End Try
    End Function
    
    ''' <summary>
    ''' 验证 URL 格式
    ''' </summary>
    Public Function ValidateUrl(url As String) As Boolean
        Dim result As Uri = Nothing
        Return Uri.TryCreate(url, UriKind.Absolute, result)
    End Function
    
    ''' <summary>
    ''' 验证数字
    ''' </summary>
    Public Function ValidateNumeric(value As String) As Boolean
        Return Double.TryParse(value, Nothing)
    End Function
    
    ''' <summary>
    ''' 验证整数
    ''' </summary>
    Public Function ValidateInteger(value As String) As Boolean
        Return Integer.TryParse(value, Nothing)
    End Function
    
    ''' <summary>
    ''' 验证日期格式
    ''' </summary>
    Public Function ValidateDate(value As String, Optional format As String = "yyyy-MM-dd") As Boolean
        Return DateTime.TryParseExact(value, format, System.Globalization.CultureInfo.InvariantCulture, 
                                     System.Globalization.DateTimeStyles.None, Nothing)
    End Function
    
    ''' <summary>
    ''' 验证 HTTP 方法是否有效
    ''' </summary>
    Public Function ValidateHttpMethod(method As String) As Boolean
        Select Case method?.ToUpper()
            Case "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"
                Return True
            Case Else
                Return False
        End Select
    End Function
    
    ''' <summary>
    ''' 验证 MES 请求
    ''' </summary>
    Public Function ValidateRequest(request As MesRequest) As MesValidationException
        Dim ex = New MesValidationException("请求验证失败")
        
        If String.IsNullOrEmpty(request.Url) Then
            ex.AddError(NameOf(MesRequest.Url), "URL 不能为空")
        ElseIf Not ValidateUrl(request.Url) Then
            ex.AddError(NameOf(MesRequest.Url), "URL 格式无效")
        End If
        
        If request.Timeout <= 0 Then
            ex.AddError(NameOf(MesRequest.Timeout), "超时时间必须大于 0")
        End If
        
        If request.RetryCount < 0 Then
            ex.AddError(NameOf(MesRequest.RetryCount), "重试次数不能为负数")
        End If
        
        If ex.ValidationErrors.Count = 0 Then Return Nothing
        
        _logger.Warning($"请求验证失败: {String.Join(", ", ex.ValidationErrors.Values)}")
        Return ex
    End Function
    
    ''' <summary>
    ''' 验证文件
    ''' </summary>
    Public Function ValidateFile(filePath As String, maxSizeBytes As Long, allowedExtensions As String) As FileValidationResult
        ' 检查文件是否存在
        If Not System.IO.File.Exists(filePath) Then
            _logger.Warning($"验证失败: 文件不存在 {filePath}")
            Return FileValidationResult.FileNotFound
        End If
        
        Try
            ' 检查文件大小
            Dim fileInfo = New System.IO.FileInfo(filePath)
            If fileInfo.Length > maxSizeBytes Then
                _logger.Warning($"验证失败: 文件 {filePath} 超过最大大小")
                Return FileValidationResult.InvalidSize
            End If
            
            ' 检查文件扩展名
            If Not String.IsNullOrEmpty(allowedExtensions) Then
                Dim extensions = allowedExtensions.Split(","c)
                Dim ext = System.IO.Path.GetExtension(filePath).ToLower()
                Dim isAllowed = extensions.Any(Function(e) e.Trim().Equals(ext, StringComparison.OrdinalIgnoreCase))
                
                If Not isAllowed Then
                    _logger.Warning($"验证失败: 文件扩展名 {ext} 不允许")
                    Return FileValidationResult.InvalidType
                End If
            End If
            
            Return FileValidationResult.Valid
        Catch ex As Exception
            _logger.Error($"文件验证异常: {ex.Message}")
            Return FileValidationResult.Unknown
        End Try
    End Function
    
    ''' <summary>
    ''' 验证文件上传模型
    ''' </summary>
    Public Function ValidateFileUploadModel(uploadModel As MesFileUploadModel, maxSize As Long, allowedExtensions As String) As MesValidationException
        Dim ex = New MesValidationException("文件上传模型验证失败")
        
        If String.IsNullOrEmpty(uploadModel.FilePath) Then
            ex.AddError(NameOf(MesFileUploadModel.FilePath), "文件路径不能为空")
        Else
            Dim result = ValidateFile(uploadModel.FilePath, maxSize, allowedExtensions)
            If result <> FileValidationResult.Valid Then
                ex.AddError(NameOf(MesFileUploadModel.FilePath), $"文件验证失败: {result}")
            End If
        End If
        
        If String.IsNullOrEmpty(uploadModel.UploadUrl) Then
            ex.AddError(NameOf(MesFileUploadModel.UploadUrl), "上传 URL 不能为空")
        ElseIf Not ValidateUrl(uploadModel.UploadUrl) Then
            ex.AddError(NameOf(MesFileUploadModel.UploadUrl), "上传 URL 格式无效")
        End If
        
        If uploadModel.ChunkSize <= 0 Then
            ex.AddError(NameOf(MesFileUploadModel.ChunkSize), "分块大小必须大于 0")
        End If
        
        If ex.ValidationErrors.Count = 0 Then Return Nothing
        
        _logger.Warning($"文件上传模型验证失败: {String.Join(", ", ex.ValidationErrors.Values)}")
        Return ex
    End Function
    
    ''' <summary>
    ''' 验证查询模型
    ''' </summary>
    Public Function ValidateQueryModel(queryModel As MesQueryModel) As MesValidationException
        Dim ex = New MesValidationException("查询模型验证失败")
        
        ' 验证日期范围
        If queryModel.StartDate.HasValue AndAlso queryModel.EndDate.HasValue Then
            If queryModel.StartDate > queryModel.EndDate Then
                ex.AddError("DateRange", "开始日期不能晚于结束日期")
            End If
        End If
        
        ' 验证分页参数
        If queryModel.PageIndex < 1 Then
            ex.AddError(NameOf(MesQueryModel.PageIndex), "页码必须大于 0")
        End If
        
        If queryModel.PageSize < 1 Then
            ex.AddError(NameOf(MesQueryModel.PageSize), "每页数量必须大于 0")
        End If
        
        If queryModel.PageSize > 1000 Then
            ex.AddError(NameOf(MesQueryModel.PageSize), "每页数量不能超过 1000")
        End If
        
        If ex.ValidationErrors.Count = 0 Then Return Nothing
        
        _logger.Warning($"查询模型验证失败: {String.Join(", ", ex.ValidationErrors.Values)}")
        Return ex
    End Function
    
    ''' <summary>
    ''' 验证对象为空
    ''' </summary>
    Public Function ValidateNotNull(obj As Object, fieldName As String) As Boolean
        If obj Is Nothing Then
            _logger.Warning($"验证失败: {fieldName} 不能为空")
            Return False
        End If
        Return True
    End Function
    
    ''' <summary>
    ''' 验证集合不为空
    ''' </summary>
    Public Function ValidateCollectionNotEmpty(Of T)(collection As IEnumerable(Of T), fieldName As String) As Boolean
        If collection Is Nothing OrElse collection.Count() = 0 Then
            _logger.Warning($"验证失败: {fieldName} 集合不能为空")
            Return False
        End If
        Return True
    End Function
    
    ''' <summary>
    ''' 验证范围值
    ''' </summary>
    Public Function ValidateRange(value As Integer, fieldName As String, minValue As Integer, maxValue As Integer) As Boolean
        If value < minValue OrElse value > maxValue Then
            _logger.Warning($"验证失败: {fieldName} 必须在 {minValue} 到 {maxValue} 之间")
            Return False
        End If
        Return True
    End Function
End Class
