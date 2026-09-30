''' <summary>
''' MES 系统交互框架 - 基础响应模型
''' </summary>
Public Class MesResponse(Of T)
    ''' <summary>响应请求 ID</summary>
    Public Property RequestId As String

    ''' <summary>HTTP 状态码</summary>
    Public Property StatusCode As Integer

    ''' <summary>是否成功</summary>
    Public Property IsSuccess As Boolean

    ''' <summary>响应数据</summary>
    Public Property Data As T

    ''' <summary>错误消息</summary>
    Public Property ErrorMessage As String

    ''' <summary>错误代码（业务错误码）</summary>
    Public Property ErrorCode As String

    ''' <summary>响应时间戳</summary>
    Public Property Timestamp As DateTime = DateTime.Now

    ''' <summary>响应耗时（毫秒）</summary>
    Public Property ResponseTimeMs As Long

    ''' <summary>响应头集合</summary>
    Public Property Headers As New Dictionary(Of String, String)

    ''' <summary>原始响应内容</summary>
    Public Property RawContent As String

    ''' <summary>
    ''' 创建成功响应
    ''' </summary>
    Public Shared Function CreateSuccess(data As T, Optional statusCode As Integer = 200) As MesResponse(Of T)
        Return New MesResponse(Of T) With {
            .StatusCode = statusCode,
            .IsSuccess = True,
            .Data = data
        }
    End Function

    ''' <summary>
    ''' 创建失败响应
    ''' </summary>
    Public Shared Function CreateError(errorMessage As String, Optional errorCode As String = Nothing, Optional statusCode As Integer = 400) As MesResponse(Of T)
        Return New MesResponse(Of T) With {
            .StatusCode = statusCode,
            .IsSuccess = False,
            .ErrorMessage = errorMessage,
            .ErrorCode = If(errorCode, "UNKNOWN_ERROR")
        }
    End Function
End Class

''' <summary>
''' 非泛型版本的响应类
''' </summary>
Public Class MesResponse
    ''' <summary>响应请求 ID</summary>
    Public Property RequestId As String

    ''' <summary>HTTP 状态码</summary>
    Public Property StatusCode As Integer

    ''' <summary>是否成功</summary>
    Public Property IsSuccess As Boolean

    ''' <summary>响应数据（String 格式）</summary>
    Public Property Data As String

    ''' <summary>错误消息</summary>
    Public Property ErrorMessage As String

    ''' <summary>错误代码</summary>
    Public Property ErrorCode As String

    ''' <summary>响应时间戳</summary>
    Public Property Timestamp As DateTime = DateTime.Now

    ''' <summary>响应耗时（毫秒）</summary>
    Public Property ResponseTimeMs As Long

    ''' <summary>响应头集合</summary>
    Public Property Headers As New Dictionary(Of String, String)

    ''' <summary>原始响应内容</summary>
    Public Property RawContent As String

    ''' <summary>
    ''' 创建成功响应
    ''' </summary>
    Public Shared Function CreateSuccess(data As String, Optional statusCode As Integer = 200) As MesResponse
        Return New MesResponse With {
            .StatusCode = statusCode,
            .IsSuccess = True,
            .Data = data
        }
    End Function

    ''' <summary>
    ''' 创建失败响应
    ''' </summary>
    Public Shared Function CreateError(errorMessage As String, Optional errorCode As String = Nothing, Optional statusCode As Integer = 400) As MesResponse
        Return New MesResponse With {
            .StatusCode = statusCode,
            .IsSuccess = False,
            .ErrorMessage = errorMessage,
            .ErrorCode = If(errorCode, "UNKNOWN_ERROR")
        }
    End Function
End Class
