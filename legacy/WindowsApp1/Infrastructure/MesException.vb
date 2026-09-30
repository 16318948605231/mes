''' <summary>
''' MES 系统交互框架 - 异常处理
''' </summary>

''' <summary>
''' MES 框架基础异常
''' </summary>
Public Class MesFrameworkException
    Inherits Exception
    
    ''' <summary>错误代码</summary>
    Public Property ErrorCode As String
    
    ''' <summary>错误详情</summary>
    Public Property Details As String
    
    Public Sub New(message As String)
        MyBase.New(message)
        ErrorCode = "UNKNOWN_ERROR"
    End Sub
    
    Public Sub New(message As String, errorCode As String)
        MyBase.New(message)
        Me.ErrorCode = errorCode
    End Sub
    
    Public Sub New(message As String, errorCode As String, details As String)
        MyBase.New(message)
        Me.ErrorCode = errorCode
        Me.Details = details
    End Sub
    
    Public Sub New(message As String, innerException As Exception)
        MyBase.New(message, innerException)
        ErrorCode = "UNKNOWN_ERROR"
    End Sub
End Class

''' <summary>
''' HTTP 请求异常
''' </summary>
Public Class MesHttpException
    Inherits MesFrameworkException
    
    ''' <summary>HTTP 状态码</summary>
    Public Property StatusCode As Integer
    
    ''' <summary>请求 URL</summary>
    Public Property RequestUrl As String
    
    ''' <summary>响应内容</summary>
    Public Property ResponseContent As String
    
    Public Sub New(message As String, statusCode As Integer)
        MyBase.New(message, "HTTP_ERROR_" & statusCode)
        Me.StatusCode = statusCode
    End Sub
    
    Public Sub New(message As String, statusCode As Integer, responseContent As String)
        MyBase.New(message, "HTTP_ERROR_" & statusCode)
        Me.StatusCode = statusCode
        Me.ResponseContent = responseContent
    End Sub
End Class

''' <summary>
''' 文件上传异常
''' </summary>
Public Class MesUploadException
    Inherits MesFrameworkException
    
    ''' <summary>文件路径</summary>
    Public Property FilePath As String
    
    ''' <summary>文件名</summary>
    Public Property FileName As String
    
    Public Sub New(message As String, filePath As String)
        MyBase.New(message, "UPLOAD_ERROR")
        Me.FilePath = filePath
    End Sub
    
    Public Sub New(message As String, filePath As String, fileName As String)
        MyBase.New(message, "UPLOAD_ERROR")
        Me.FilePath = filePath
        Me.FileName = fileName
    End Sub
End Class

''' <summary>
''' 数据验证异常
''' </summary>
Public Class MesValidationException
    Inherits MesFrameworkException
    
    ''' <summary>验证错误集合</summary>
    Public Property ValidationErrors As New Dictionary(Of String, String)
    
    Public Sub New(message As String)
        MyBase.New(message, "VALIDATION_ERROR")
    End Sub
    
    ''' <summary>
    ''' 添加验证错误
    ''' </summary>
    Public Sub AddError(fieldName As String, errorMessage As String)
        If String.IsNullOrEmpty(fieldName) Then Return
        ValidationErrors(fieldName) = errorMessage
    End Sub
End Class

''' <summary>
''' 配置异常
''' </summary>
Public Class MesConfigurationException
    Inherits MesFrameworkException
    
    ''' <summary>配置项名称</summary>
    Public Property ConfigKey As String
    
    Public Sub New(message As String, configKey As String)
        MyBase.New(message, "CONFIGURATION_ERROR")
        Me.ConfigKey = configKey
    End Sub
End Class

''' <summary>
''' 超时异常
''' </summary>
Public Class MesTimeoutException
    Inherits MesFrameworkException
    
    ''' <summary>超时时间（毫秒）</summary>
    Public Property TimeoutMs As Integer
    
    Public Sub New(message As String, timeoutMs As Integer)
        MyBase.New(message, "TIMEOUT_ERROR")
        Me.TimeoutMs = timeoutMs
    End Sub
End Class

''' <summary>
''' 异常处理工具
''' </summary>
Public NotInheritable Class MesExceptionHandler
    ''' <summary>
    ''' 处理异常并返回友好消息
    ''' </summary>
    Public Shared Function GetFriendlyMessage(ex As Exception) As String
        If TypeOf ex Is MesFrameworkException Then
            Dim mesEx = CType(ex, MesFrameworkException)
            Return $"[{mesEx.ErrorCode}] {ex.Message}"
        End If
        
        If TypeOf ex Is System.Net.Http.HttpRequestException Then
            Return $"网络请求失败：{ex.Message}"
        End If
        
        If TypeOf ex Is TimeoutException Then
            Return "请求超时，请检查网络连接"
        End If
        
        If TypeOf ex Is System.IO.FileNotFoundException Then
            Return $"文件未找到：{ex.Message}"
        End If
        
        If TypeOf ex Is System.IO.IOException Then
            Return $"文件操作失败：{ex.Message}"
        End If
        
        Return $"发生错误：{ex.Message}"
    End Function
    
    ''' <summary>
    ''' 获取完整的异常信息
    ''' </summary>
    Public Shared Function GetFullMessage(ex As Exception) As String
        Dim sb = New System.Text.StringBuilder()
        sb.AppendLine($"异常类型：{ex.GetType().Name}")
        sb.AppendLine($"异常消息：{ex.Message}")
        
        If TypeOf ex Is MesFrameworkException Then
            Dim mesEx = CType(ex, MesFrameworkException)
            sb.AppendLine($"错误代码：{mesEx.ErrorCode}")
            If Not String.IsNullOrEmpty(mesEx.Details) Then
                sb.AppendLine($"错误详情：{mesEx.Details}")
            End If
        End If
        
        If ex.InnerException IsNot Nothing Then
            sb.AppendLine($"内部异常：{ex.InnerException.Message}")
        End If
        
        sb.AppendLine($"堆栈跟踪：{ex.StackTrace}")
        
        Return sb.ToString()
    End Function
End Class
