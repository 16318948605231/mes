Imports System.Collections.Concurrent

''' <summary>
''' MES 系统交互框架 - 缓存服务
''' </summary>
Public Class MesCacheService
    ''' <summary>缓存字典（线程安全）</summary>
    Private Shared _cache As New ConcurrentDictionary(Of String, CacheEntry)
    
    ''' <summary>日志记录器</summary>
    Private _logger As MesLogger
    
    ''' <summary>默认过期时间（秒）</summary>
    Private _defaultExpireSeconds As Integer = 300
    
    Public Sub New(Optional logger As MesLogger = Nothing, Optional defaultExpireSeconds As Integer = 300)
        _logger = If(logger, MesLogger.GetInstance())
        _defaultExpireSeconds = defaultExpireSeconds
    End Sub
    
    ''' <summary>
    ''' 设置缓存
    ''' </summary>
    Public Sub [Set](key As String, value As Object, Optional expireSeconds As Integer? = Nothing)
        Try
            If String.IsNullOrEmpty(key) Then
                Throw New ArgumentNullException(NameOf(key), "缓存键不能为空")
            End If
            
            Dim expire = If(expireSeconds, _defaultExpireSeconds)
            Dim entry = New CacheEntry With {
                .Value = value,
                .ExpireTime = DateTime.Now.AddSeconds(expire),
                .CreateTime = DateTime.Now
            }
            
            _cache(key) = entry
            _logger.Debug($"缓存已设置: {key}, 过期时间: {expire}秒")
        Catch ex As Exception
            _logger.Error($"设置缓存失败: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' 获取缓存
    ''' </summary>
    Public Function [Get](Of T)(key As String) As T
        Dim value As T = Nothing
        If TryGet(key, value) Then
            Return value
        End If
        Return Nothing
    End Function
    
    ''' <summary>
    ''' 尝试获取缓存
    ''' </summary>
    Public Function TryGet(Of T)(key As String, ByRef value As T) As Boolean
        Try
            If String.IsNullOrEmpty(key) Then Return False
            
            Dim entry As CacheEntry = Nothing
            If _cache.TryGetValue(key, entry) Then
                ' 检查是否过期
                If entry.ExpireTime > DateTime.Now Then
                    value = CType(entry.Value, T)
                    _logger.Debug($"缓存命中: {key}")
                    Return True
                Else
                    ' 已过期，移除
                    Remove(key)
                    _logger.Debug($"缓存已过期: {key}")
                End If
            End If
            
            Return False
        Catch ex As Exception
            _logger.Error($"获取缓存失败: {ex.Message}")
            value = Nothing
            Return False
        End Try
    End Function
    
    ''' <summary>
    ''' 移除缓存
    ''' </summary>
    Public Sub Remove(key As String)
        Try
            If String.IsNullOrEmpty(key) Then Return
            
            Dim entry As CacheEntry = Nothing
            _cache.TryRemove(key, entry)
            _logger.Debug($"缓存已移除: {key}")
        Catch ex As Exception
            _logger.Error($"移除缓存失败: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' 清空所有缓存
    ''' </summary>
    Public Sub Clear()
        Try
            _cache.Clear()
            _logger.Info("所有缓存已清空")
        Catch ex As Exception
            _logger.Error($"清空缓存失败: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' 检查缓存是否存在且有效
    ''' </summary>
    Public Function Exists(key As String) As Boolean
        If String.IsNullOrEmpty(key) Then Return False
        
        Dim entry As CacheEntry = Nothing
        If _cache.TryGetValue(key, entry) Then
            If entry.ExpireTime > DateTime.Now Then
                Return True
            Else
                Remove(key)
            End If
        End If
        
        Return False
    End Function
    
    ''' <summary>
    ''' 获取缓存数量
    ''' </summary>
    Public Function Count() As Integer
        Return _cache.Count
    End Function
    
    ''' <summary>
    ''' 清理过期缓存
    ''' </summary>
    Public Sub CleanExpired()
        Try
            Dim expiredKeys = New List(Of String)
            
            For Each kvp In _cache
                If kvp.Value.ExpireTime <= DateTime.Now Then
                    expiredKeys.Add(kvp.Key)
                End If
            Next
            
            For Each key In expiredKeys
                Remove(key)
            Next
            
            _logger.Info($"已清理 {expiredKeys.Count} 个过期缓存")
        Catch ex As Exception
            _logger.Error($"清理过期缓存失败: {ex.Message}")
        End Try
    End Sub
    
    ''' <summary>
    ''' 获取或设置缓存（如果不存在则通过工厂方法创建）
    ''' </summary>
    Public Function GetOrSet(Of T)(key As String, factory As Func(Of T), Optional expireSeconds As Integer? = Nothing) As T
        Dim value As T = Nothing
        
        If TryGet(key, value) Then
            Return value
        End If
        
        ' 缓存不存在，通过工厂方法创建
        value = factory()
        [Set](key, value, expireSeconds)
        
        Return value
    End Function
    
    ''' <summary>
    ''' 获取缓存统计信息
    ''' </summary>
    Public Function GetStatistics() As CacheStatistics
        Dim stats = New CacheStatistics With {
            .TotalCount = _cache.Count,
            .ExpiredCount = 0,
            .ValidCount = 0
        }
        
        For Each entry In _cache.Values
            If entry.ExpireTime <= DateTime.Now Then
                stats.ExpiredCount += 1
            Else
                stats.ValidCount += 1
            End If
        Next
        
        Return stats
    End Function
End Class

''' <summary>
''' 缓存条目
''' </summary>
Friend Class CacheEntry
    ''' <summary>缓存值</summary>
    Public Property Value As Object
    
    ''' <summary>过期时间</summary>
    Public Property ExpireTime As DateTime
    
    ''' <summary>创建时间</summary>
    Public Property CreateTime As DateTime
End Class

''' <summary>
''' 缓存统计信息
''' </summary>
Public Class CacheStatistics
    ''' <summary>总数量</summary>
    Public Property TotalCount As Integer
    
    ''' <summary>有效数量</summary>
    Public Property ValidCount As Integer
    
    ''' <summary>过期数量</summary>
    Public Property ExpiredCount As Integer
    
    Public Overrides Function ToString() As String
        Return $"总计: {TotalCount}, 有效: {ValidCount}, 过期: {ExpiredCount}"
    End Function
End Class
