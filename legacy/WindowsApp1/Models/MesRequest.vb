Imports System.Threading

''' <summary>
''' MES 系统交互框架 - 基础请求模型
''' </summary>
Public Class MesRequest
    ''' <summary>请求唯一标识</summary>
    Public Property RequestId As String = Guid.NewGuid().ToString()

    ''' <summary>请求时间戳</summary>
    Public Property Timestamp As DateTime = DateTime.Now

    ''' <summary>HTTP 方法</summary>
    Public Property Method As HttpMethod = HttpMethod.Get

    ''' <summary>请求 URL</summary>
    Public Property Url As String

    ''' <summary>请求头集合</summary>
    Public Property Headers As New Dictionary(Of String, String)

    ''' <summary>请求参数（查询字符串）</summary>
    Public Property Parameters As New Dictionary(Of String, Object)

    ''' <summary>请求体（JSON/XML/Form Data）</summary>
    Public Property Body As String

    ''' <summary>请求超时时间（毫秒）</summary>
    Public Property Timeout As Integer = 30000

    ''' <summary>身份认证类型</summary>
    Public Property AuthType As AuthType = AuthType.None

    ''' <summary>身份认证信息</summary>
    Public Property AuthToken As String

    ''' <summary>数据格式类型</summary>
    Public Property DataFormat As DataFormatType = DataFormatType.Json

    ''' <summary>是否启用重试</summary>
    Public Property EnableRetry As Boolean = True

    ''' <summary>重试次数</summary>
    Public Property RetryCount As Integer = 3

    ''' <summary>重试延迟（毫秒）</summary>
    Public Property RetryDelayMs As Integer = 1000

    ''' <summary>自定义标签（用于日志跟踪）</summary>
    Public Property Tag As String

    ''' <summary>取消令牌（用于取消请求）</summary>
    Public Property CancellationToken As CancellationToken = CancellationToken.None

    ''' <summary>
    ''' 添加请求头
    ''' </summary>
    Public Sub AddHeader(key As String, value As String)
        If String.IsNullOrEmpty(key) Then Return
        Headers(key) = value
    End Sub

    ''' <summary>
    ''' 添加查询参数
    ''' </summary>
    Public Sub AddParameter(key As String, value As Object)
        If String.IsNullOrEmpty(key) Then Return
        Parameters(key) = value
    End Sub

    ''' <summary>
    ''' 清空所有参数
    ''' </summary>
    Public Sub ClearParameters()
        Parameters.Clear()
    End Sub

    ''' <summary>
    ''' 清空所有请求头
    ''' </summary>
    Public Sub ClearHeaders()
        Headers.Clear()
    End Sub

    ''' <summary>
    ''' 构建完整 URL（包含查询参数）
    ''' </summary>
    Public Function BuildUrl() As String
        If String.IsNullOrEmpty(Url) Then Return ""

        If Parameters.Count = 0 Then Return Url

        Dim queryString As String = ""
        For Each kvp In Parameters
            If queryString.Length > 0 Then queryString &= "&"
            Dim escapedKey = Uri.EscapeDataString(kvp.Key)
            Dim escapedValue = Uri.EscapeDataString(If(kvp.Value IsNot Nothing, kvp.Value.ToString(), ""))
            queryString &= escapedKey & "=" & escapedValue
        Next

        Return If(Url.Contains("?"), Url & "&" & queryString, Url & "?" & queryString)
    End Function
End Class
