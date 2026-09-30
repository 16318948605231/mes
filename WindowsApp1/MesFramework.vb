''' <summary>
''' MES 系统交互框架 - 主入口（简化使用）
''' </summary>
Public NotInheritable Class MesFramework
    ''' <summary>框架实例</summary>
    Private Shared _instance As MesFramework
    Private Shared ReadOnly _lock As New Object()
    
    ''' <summary>配置管理器</summary>
    Public Property Config As MesConfig
    
    ''' <summary>日志记录器</summary>
    Public Property Logger As MesLogger
    
    ''' <summary>HTTP 客户端</summary>
    Public Property HttpClient As MesHttpClient
    
    ''' <summary>文件上传服务</summary>
    Public Property FileUploadService As MesFileUploadService
    
    ''' <summary>数据转换器</summary>
    Public Property DataConverter As MesDataConverter
    
    ''' <summary>数据验证器</summary>
    Public Property Validator As MesValidator
    
    ''' <summary>获取框架单例</summary>
    Public Shared Function GetInstance() As MesFramework
        If _instance Is Nothing Then
            SyncLock _lock
                If _instance Is Nothing Then
                    _instance = New MesFramework()
                End If
            End SyncLock
        End If
        Return _instance
    End Function
    
    ''' <summary>
    ''' 私有构造函数
    ''' </summary>
    Private Sub New()
        ' 初始化各个组件
        Config = MesConfig.GetInstance()
        Logger = MesLogger.GetInstance()
        DataConverter = New MesDataConverter(Logger)
        Validator = New MesValidator(Logger)
        HttpClient = New MesHttpClient(Config, Logger)
        FileUploadService = New MesFileUploadService(HttpClient, Config, Logger)
        
        Logger.Info("MES 框架已初始化")
    End Sub
    
    ''' <summary>
    ''' 初始化框架配置
    ''' </summary>
    Public Sub Initialize(baseUrl As String, Optional authToken As String = Nothing, Optional authType As AuthType = AuthType.None)
        Config.BaseUrl = baseUrl
        Config.AuthToken = authToken
        Config.AuthType = authType
        
        ' 重新创建 HTTP 客户端以应用新配置
        HttpClient?.Dispose()
        HttpClient = New MesHttpClient(Config, Logger)
        
        ' 更新文件上传服务
        FileUploadService?.Dispose()
        FileUploadService = New MesFileUploadService(HttpClient, Config, Logger)
        
        Logger.Info($"框架已初始化: {baseUrl}")
    End Sub
    
    ''' <summary>
    ''' 获取最近的日志
    ''' </summary>
    Public Function GetRecentLogs(Optional maxLines As Integer = 100) As String
        Return Logger.GetRecentLogs(maxLines)
    End Function
    
    ''' <summary>
    ''' 清空日志
    ''' </summary>
    Public Sub ClearLogs()
        Logger.ClearLogs()
    End Sub
    
    ''' <summary>
    ''' 验证框架配置
    ''' </summary>
    Public Function Validate() As Boolean
        Return Config.Validate()
    End Function
    
    ''' <summary>
    ''' 重置框架
    ''' </summary>
    Public Sub Reset()
        Config.Reset()
        Logger.ClearLogs()
        HttpClient?.Dispose()
        FileUploadService?.Dispose()
        
        HttpClient = New MesHttpClient(Config, Logger)
        FileUploadService = New MesFileUploadService(HttpClient, Config, Logger)
        
        Logger.Info("框架已重置")
    End Sub
    
    ''' <summary>
    ''' 设置日志级别
    ''' </summary>
    Public Sub SetLogLevel(logLevel As LogLevel)
        Logger.SetLogLevel(logLevel)
    End Sub
    
    ''' <summary>
    ''' 启用缓存
    ''' </summary>
    Public Sub EnableCache(Optional expireSeconds As Integer = 300)
        Config.EnableCache = True
        Config.CacheExpireSeconds = expireSeconds
        Logger.Info($"缓存已启用，过期时间: {expireSeconds}秒")
    End Sub
    
    ''' <summary>
    ''' 禁用缓存
    ''' </summary>
    Public Sub DisableCache()
        Config.EnableCache = False
        HttpClient.ClearCache()
        Logger.Info("缓存已禁用")
    End Sub
    
    ''' <summary>
    ''' 清空缓存
    ''' </summary>
    Public Sub ClearCache()
        HttpClient.ClearCache()
        Logger.Info("缓存已清空")
    End Sub
    
    ''' <summary>
    ''' 获取缓存统计信息
    ''' </summary>
    Public Function GetCacheStatistics() As CacheStatistics
        Return HttpClient.GetCacheStatistics()
    End Function
    
    ''' <summary>
    ''' 启用速率限制
    ''' </summary>
    Public Sub EnableRateLimit(Optional minIntervalMs As Integer = 100, Optional windowSeconds As Integer = 60, Optional maxRequests As Integer = 100)
        Config.EnableRateLimit = True
        Config.RateLimitMinIntervalMs = minIntervalMs
        Config.RateLimitWindowSeconds = windowSeconds
        Config.RateLimitMaxRequests = maxRequests
        HttpClient.EnableRateLimit(minIntervalMs, windowSeconds, maxRequests)
        Logger.Info($"速率限制已启用: {maxRequests}次/{windowSeconds}秒")
    End Sub
    
    ''' <summary>
    ''' 禁用速率限制
    ''' </summary>
    Public Sub DisableRateLimit()
        Config.EnableRateLimit = False
        HttpClient.DisableRateLimit()
        Logger.Info("速率限制已禁用")
    End Sub
    
    ''' <summary>
    ''' 获取速率限制统计信息
    ''' </summary>
    Public Function GetRateLimiterStatistics() As RateLimiterStatistics
        Return HttpClient.GetRateLimiterStatistics()
    End Function
    
    ''' <summary>
    ''' 关闭框架
    ''' </summary>
    Public Sub Shutdown()
        Logger.Info("框架正在关闭...")
        HttpClient?.Dispose()
        FileUploadService?.Dispose()
        Logger.Info("框架已关闭")
    End Sub
End Class
