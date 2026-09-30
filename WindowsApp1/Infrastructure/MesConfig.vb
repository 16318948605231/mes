Imports System.IO

''' <summary>
''' MES 系统交互框架 - 配置管理
''' </summary>
Public Class MesConfig
    ''' <summary>单例实例</summary>
    Private Shared _instance As MesConfig
    Private Shared ReadOnly _lock As New Object()

    ''' <summary>API 基础 URL</summary>
    Public Property BaseUrl As String

    ''' <summary>连接超时（毫秒）</summary>
    Public Property ConnectTimeout As Integer = 30000

    ''' <summary>读取超时（毫秒）</summary>
    Public Property ReadTimeout As Integer = 30000

    ''' <summary>日志级别</summary>
    Public Property LogLevel As LogLevel = LogLevel.Info

    ''' <summary>日志文件路径</summary>
    Public Property LogFilePath As String

    ''' <summary>是否启用日志</summary>
    Public Property EnableLogging As Boolean = True

    ''' <summary>最大重试次数</summary>
    Public Property MaxRetryCount As Integer = 3

    ''' <summary>重试延迟（毫秒）</summary>
    Public Property RetryDelayMs As Integer = 1000

    ''' <summary>文件上传最大大小（字节）</summary>
    Public Property MaxUploadSize As Long = 1024 * 1024 * 500 ' 默认 500MB

    ''' <summary>允许的文件扩展名（逗号分隔）</summary>
    Public Property AllowedFileExtensions As String = ".jpg,.jpeg,.png,.gif,.pdf,.doc,.docx,.xls,.xlsx,.zip,.txt"

    ''' <summary>分块上传的分块大小（字节）</summary>
    Public Property ChunkSize As Long = 1024 * 1024 * 5 ' 默认 5MB

    ''' <summary>身份认证类型</summary>
    Public Property AuthType As AuthType = AuthType.None

    ''' <summary>认证 Token</summary>
    Public Property AuthToken As String

    ''' <summary>API 密钥</summary>
    Public Property ApiKey As String

    ''' <summary>是否使用 HTTPS</summary>
    Public Property UseHttps As Boolean = True

    ''' <summary>代理地址（可选）</summary>
    Public Property ProxyUrl As String

    ''' <summary>代理用户名（可选）</summary>
    Public Property ProxyUsername As String

    ''' <summary>代理密码（可选）</summary>
    Public Property ProxyPassword As String

    ''' <summary>默认请求头</summary>
    Public Property DefaultHeaders As New Dictionary(Of String, String)

    ''' <summary>默认查询参数</summary>
    Public Property DefaultParameters As New Dictionary(Of String, Object)

    ''' <summary>缓存启用</summary>
    Public Property EnableCache As Boolean = False

    ''' <summary>缓存过期时间（秒）</summary>
    Public Property CacheExpireSeconds As Integer = 300

    ''' <summary>启用速率限制</summary>
    Public Property EnableRateLimit As Boolean = False

    ''' <summary>最小请求间隔（毫秒）</summary>
    Public Property RateLimitMinIntervalMs As Integer = 100

    ''' <summary>速率限制时间窗口（秒）</summary>
    Public Property RateLimitWindowSeconds As Integer = 60

    ''' <summary>时间窗口内最大请求数</summary>
    Public Property RateLimitMaxRequests As Integer = 100

    ''' <summary>获取单例实例</summary>
    Public Shared Function GetInstance() As MesConfig
        If _instance Is Nothing Then
            SyncLock _lock
                If _instance Is Nothing Then
                    _instance = New MesConfig()
                End If
            End SyncLock
        End If
        Return _instance
    End Function

    ''' <summary>
    ''' 从配置文件初始化（App.config）
    ''' </summary>
    Public Sub InitializeFromConfig()
        Try
            ' 从 App.config 读取配置（简化版，可根据需要扩展）
            ' BaseUrl = ConfigurationManager.AppSettings("MES_BaseUrl")
            ' 因为此项目可能没有 app.config，所以使用默认值
            If String.IsNullOrEmpty(BaseUrl) Then
                BaseUrl = "http://localhost"
            End If

        Catch ex As Exception
            ' 使用默认配置
            Debug.WriteLine("配置文件读取失败: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 添加默认请求头
    ''' </summary>
    Public Sub AddDefaultHeader(key As String, value As String)
        If String.IsNullOrEmpty(key) Then Return
        DefaultHeaders(key) = value
    End Sub

    ''' <summary>
    ''' 添加默认查询参数
    ''' </summary>
    Public Sub AddDefaultParameter(key As String, value As Object)
        If String.IsNullOrEmpty(key) Then Return
        DefaultParameters(key) = value
    End Sub

    ''' <summary>
    ''' 清空默认请求头
    ''' </summary>
    Public Sub ClearDefaultHeaders()
        DefaultHeaders.Clear()
    End Sub

    ''' <summary>
    ''' 清空默认查询参数
    ''' </summary>
    Public Sub ClearDefaultParameters()
        DefaultParameters.Clear()
    End Sub

    ''' <summary>
    ''' 验证配置是否有效
    ''' </summary>
    Public Function Validate() As Boolean
        If String.IsNullOrEmpty(BaseUrl) Then Return False
        If ConnectTimeout <= 0 Then Return False
        If ReadTimeout <= 0 Then Return False
        If MaxUploadSize <= 0 Then Return False
        Return True
    End Function

    ''' <summary>
    ''' 重置为默认配置
    ''' </summary>
    Public Sub Reset()
        BaseUrl = ""
        ConnectTimeout = 30000
        ReadTimeout = 30000
        LogLevel = LogLevel.Info
        EnableLogging = True
        MaxRetryCount = 3
        RetryDelayMs = 1000
        MaxUploadSize = 1024 * 1024 * 500
        ChunkSize = 1024 * 1024 * 5
        AuthType = AuthType.None
        AuthToken = ""
        ApiKey = ""
        UseHttps = True
        ProxyUrl = ""
        ProxyUsername = ""
        ProxyPassword = ""
        DefaultHeaders.Clear()
        DefaultParameters.Clear()
        EnableCache = False
        CacheExpireSeconds = 300
        EnableRateLimit = False
        RateLimitMinIntervalMs = 100
        RateLimitWindowSeconds = 60
        RateLimitMaxRequests = 100
    End Sub
End Class
