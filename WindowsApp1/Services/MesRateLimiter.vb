Imports System.Threading

''' <summary>
''' MES 系统交互框架 - 速率限制器
''' </summary>
Public Class MesRateLimiter
    ''' <summary>最后请求时间</summary>
    Private _lastRequestTime As DateTime = DateTime.MinValue
    
    ''' <summary>最小请求间隔（毫秒）</summary>
    Private _minRequestInterval As Integer = 100
    
    ''' <summary>线程锁</summary>
    Private ReadOnly _lock As New Object()
    
    ''' <summary>是否启用</summary>
    Private _enabled As Boolean = False
    
    ''' <summary>请求计数</summary>
    Private _requestCount As Long = 0
    
    ''' <summary>时间窗口开始时间</summary>
    Private _windowStartTime As DateTime = DateTime.Now
    
    ''' <summary>时间窗口大小（秒）</summary>
    Private _windowSizeSeconds As Integer = 60
    
    ''' <summary>窗口内最大请求数</summary>
    Private _maxRequestsPerWindow As Integer = 100
    
    ''' <summary>当前窗口内的请求数</summary>
    Private _windowRequestCount As Integer = 0
    
    ''' <summary>日志记录器</summary>
    Private _logger As MesLogger
    
    Public Sub New(Optional logger As MesLogger = Nothing, Optional minIntervalMs As Integer = 100, Optional enabled As Boolean = False)
        _logger = If(logger, MesLogger.GetInstance())
        _minRequestInterval = minIntervalMs
        _enabled = enabled
    End Sub
    
    ''' <summary>
    ''' 启用速率限制
    ''' </summary>
    Public Sub Enable()
        _enabled = True
        _logger.Info("速率限制已启用")
    End Sub
    
    ''' <summary>
    ''' 禁用速率限制
    ''' </summary>
    Public Sub Disable()
        _enabled = False
        _logger.Info("速率限制已禁用")
    End Sub
    
    ''' <summary>
    ''' 设置最小请求间隔
    ''' </summary>
    Public Sub SetMinInterval(intervalMs As Integer)
        _minRequestInterval = Math.Max(0, intervalMs)
        _logger.Info($"最小请求间隔已设置为: {_minRequestInterval}ms")
    End Sub
    
    ''' <summary>
    ''' 配置时间窗口限制
    ''' </summary>
    Public Sub ConfigureWindow(windowSizeSeconds As Integer, maxRequests As Integer)
        _windowSizeSeconds = windowSizeSeconds
        _maxRequestsPerWindow = maxRequests
        _logger.Info($"时间窗口限制: {maxRequests} 次/{windowSizeSeconds}秒")
    End Sub
    
    ''' <summary>
    ''' 等待（如果需要）
    ''' </summary>
    Public Sub WaitIfNeeded()
        If Not _enabled Then Return
        
        SyncLock _lock
            ' 检查时间窗口
            CheckAndResetWindow()
            
            ' 检查窗口内请求数
            If _windowRequestCount >= _maxRequestsPerWindow Then
                Dim waitTime = CInt((_windowStartTime.AddSeconds(_windowSizeSeconds) - DateTime.Now).TotalMilliseconds)
                If waitTime > 0 Then
                    _logger.Warning($"达到速率限制，等待 {waitTime}ms")
                    Thread.Sleep(waitTime)
                    CheckAndResetWindow()
                End If
            End If
            
            ' 检查最小间隔
            Dim elapsed = (DateTime.Now - _lastRequestTime).TotalMilliseconds
            If elapsed < _minRequestInterval Then
                Dim delay = CInt(_minRequestInterval - elapsed)
                Thread.Sleep(delay)
            End If
            
            ' 更新统计
            _lastRequestTime = DateTime.Now
            _requestCount += 1
            _windowRequestCount += 1
        End SyncLock
    End Sub
    
    ''' <summary>
    ''' 检查并重置时间窗口
    ''' </summary>
    Private Sub CheckAndResetWindow()
        Dim now = DateTime.Now
        If (now - _windowStartTime).TotalSeconds >= _windowSizeSeconds Then
            _windowStartTime = now
            _windowRequestCount = 0
        End If
    End Sub
    
    ''' <summary>
    ''' 重置统计
    ''' </summary>
    Public Sub Reset()
        SyncLock _lock
            _lastRequestTime = DateTime.MinValue
            _requestCount = 0
            _windowStartTime = DateTime.Now
            _windowRequestCount = 0
            _logger.Info("速率限制统计已重置")
        End SyncLock
    End Sub
    
    ''' <summary>
    ''' 获取统计信息
    ''' </summary>
    Public Function GetStatistics() As RateLimiterStatistics
        SyncLock _lock
            Return New RateLimiterStatistics With {
                .TotalRequests = _requestCount,
                .WindowRequests = _windowRequestCount,
                .IsEnabled = _enabled,
                .MinIntervalMs = _minRequestInterval,
                .WindowSizeSeconds = _windowSizeSeconds,
                .MaxRequestsPerWindow = _maxRequestsPerWindow
            }
        End SyncLock
    End Function
End Class

''' <summary>
''' 速率限制器统计信息
''' </summary>
Public Class RateLimiterStatistics
    ''' <summary>总请求数</summary>
    Public Property TotalRequests As Long
    
    ''' <summary>当前窗口请求数</summary>
    Public Property WindowRequests As Integer
    
    ''' <summary>是否启用</summary>
    Public Property IsEnabled As Boolean
    
    ''' <summary>最小间隔（毫秒）</summary>
    Public Property MinIntervalMs As Integer
    
    ''' <summary>窗口大小（秒）</summary>
    Public Property WindowSizeSeconds As Integer
    
    ''' <summary>窗口最大请求数</summary>
    Public Property MaxRequestsPerWindow As Integer
    
    Public Overrides Function ToString() As String
        Return $"总请求: {TotalRequests}, 窗口请求: {WindowRequests}/{MaxRequestsPerWindow}, 启用: {IsEnabled}"
    End Function
End Class
