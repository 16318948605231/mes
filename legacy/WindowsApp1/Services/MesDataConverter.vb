Imports System.Globalization
Imports System.Text

''' <summary>
''' MES 系统交互框架 - 数据转换器
''' </summary>
Public Class MesDataConverter
    ''' <summary>日志记录器</summary>
    Private _logger As MesLogger
    
    Public Sub New(Optional logger As MesLogger = Nothing)
        _logger = If(logger, MesLogger.GetInstance())
    End Sub
    
    ''' <summary>
    ''' 将对象序列化为 JSON 字符串（简化实现）
    ''' </summary>
    Public Function ToJson(obj As Object) As String
        Try
            If obj Is Nothing Then Return "{}"
            
            ' 使用反射生成简单 JSON
            Return SerializeToJson(obj)
        Catch ex As Exception
            _logger.Error($"JSON 序列化失败: {ex.Message}")
            Throw New MesFrameworkException("JSON 序列化失败", "SERIALIZATION_ERROR", ex.Message)
        End Try
    End Function
    
    ''' <summary>
    ''' 将 JSON 字符串反序列化为对象（简化实现）
    ''' </summary>
    Public Function FromJson(Of T)(jsonString As String) As T
        Try
            If String.IsNullOrEmpty(jsonString) Then Return Nothing
            
            ' 注意：这是简化实现，生产环境应使用 Newtonsoft.Json 或 System.Text.Json
            ' 当前仅支持基本类型
            If GetType(T) Is GetType(String) Then
                Return CType(CObj(jsonString), T)
            End If
            
            ' 对于复杂类型，返回默认值
            _logger.Warning("FromJson: 简化实现，建议安装 Newtonsoft.Json 获得完整支持")
            Return Nothing
        Catch ex As Exception
            _logger.Error($"JSON 反序列化失败: {ex.Message}")
            Throw New MesFrameworkException("JSON 反序列化失败", "DESERIALIZATION_ERROR", ex.Message)
        End Try
    End Function
    
    ''' <summary>
    ''' 尝试解析 JSON（不抛出异常）
    ''' </summary>
    Public Function TryFromJson(Of T)(jsonString As String, ByRef result As T) As Boolean
        Try
            If String.IsNullOrEmpty(jsonString) Then 
                result = Nothing
                Return False
            End If
            
            result = FromJson(Of T)(jsonString)
            Return result IsNot Nothing
        Catch ex As Exception
            _logger.Warning($"JSON 解析失败: {ex.Message}")
            result = Nothing
            Return False
        End Try
    End Function
    
    ''' <summary>
    ''' 验证 JSON 字符串格式是否有效
    ''' </summary>
    Public Function IsValidJson(jsonString As String) As Boolean
        If String.IsNullOrWhiteSpace(jsonString) Then Return False
        
        Try
            jsonString = jsonString.Trim()
            If (jsonString.StartsWith("{") AndAlso jsonString.EndsWith("}")) OrElse
               (jsonString.StartsWith("[") AndAlso jsonString.EndsWith("]")) Then
                Return True
            End If
        Catch
        End Try
        Return False
    End Function
    
    ''' <summary>
    ''' 序列化对象为 JSON（基础实现）
    ''' </summary>
    Private Function SerializeToJson(obj As Object) As String
        If obj Is Nothing Then Return ""
        
        Dim objType = obj.GetType()
        
        ' 处理简单类型
        If objType = GetType(String) Then
            Return $"""{EscapeJsonString(CStr(obj))}"""
        End If
        
        If objType = GetType(Integer) OrElse objType = GetType(Long) OrElse 
           objType = GetType(Double) OrElse objType = GetType(Decimal) Then
            Return obj.ToString()
        End If
        
        If objType = GetType(Boolean) Then
            Return obj.ToString().ToLower()
        End If
        
        If objType = GetType(DateTime) Then
            Return $"""{CDate(obj):yyyy-MM-dd HH:mm:ss}"""
        End If
        
        ' 处理字典
        If TypeOf obj Is Dictionary(Of String, Object) Then
            Dim dict = CType(obj, Dictionary(Of String, Object))
            Dim items = New List(Of String)
            For Each kvp In dict
                Dim key = $"""{EscapeJsonString(kvp.Key)}"""
                Dim value = If(kvp.Value Is Nothing, "null", SerializeToJson(kvp.Value))
                items.Add($"{key}:{value}")
            Next
            Return "{" & String.Join(",", items) & "}"
        End If
        
        ' 处理对象属性
        Dim sb = New StringBuilder()
        sb.Append("{")
        
        Dim props = objType.GetProperties()
        Dim propStrs = New List(Of String)
        
        For Each prop In props
            If prop.CanRead Then
                Try
                    Dim value = prop.GetValue(obj)
                    If value IsNot Nothing Then
                        Dim key = $"""{EscapeJsonString(prop.Name)}"""
                        Dim serializedValue = SerializeToJson(value)
                        propStrs.Add($"{key}:{serializedValue}")
                    End If
                Catch
                    ' 跳过无法读取的属性
                End Try
            End If
        Next
        
        sb.Append(String.Join(",", propStrs))
        sb.Append("}")
        
        Return sb.ToString()
    End Function
    
    ''' <summary>
    ''' 转义 JSON 字符串
    ''' </summary>
    Private Function EscapeJsonString(str As String) As String
        If String.IsNullOrEmpty(str) Then Return ""
        
        Return str.Replace("\", "\\") _
                  .Replace("""", "\""") _
                  .Replace(vbCr, "\r") _
                  .Replace(vbLf, "\n") _
                  .Replace(vbTab, "\t")
    End Function
    
    ''' <summary>
    ''' 将对象转换为字典
    ''' </summary>
    Public Function ObjectToDictionary(obj As Object) As Dictionary(Of String, Object)
        Try
            If obj Is Nothing Then Return New Dictionary(Of String, Object)
            
            Dim dict = New Dictionary(Of String, Object)
            Dim properties = obj.GetType().GetProperties()
            
            For Each prop In properties
                If prop.CanRead Then
                    Dim value = prop.GetValue(obj)
                    ' 跳过空值
                    If value IsNot Nothing Then
                        dict(prop.Name) = value
                    End If
                End If
            Next
            
            Return dict
        Catch ex As Exception
            _logger.Error($"对象转字典失败: {ex.Message}")
            Throw New MesFrameworkException("对象转字典失败", "CONVERSION_ERROR", ex.Message)
        End Try
    End Function
    
    ''' <summary>
    ''' 将字典转换为对象
    ''' </summary>
    Public Function DictionaryToObject(Of T)(dict As Dictionary(Of String, Object)) As T
        Try
            If dict Is Nothing OrElse dict.Count = 0 Then Return Nothing
            
            Dim json = ToJson(dict)
            Return FromJson(Of T)(json)
        Catch ex As Exception
            _logger.Error($"字典转对象失败: {ex.Message}")
            Throw New MesFrameworkException("字典转对象失败", "CONVERSION_ERROR", ex.Message)
        End Try
    End Function
    
    ''' <summary>
    ''' 构建 URL 查询字符串
    ''' </summary>
    Public Function BuildQueryString(parameters As Dictionary(Of String, Object)) As String
        If parameters Is Nothing OrElse parameters.Count = 0 Then Return ""
        
        Dim pairs = New List(Of String)
        For Each kvp In parameters
            If kvp.Value IsNot Nothing Then
                Dim key = Uri.EscapeDataString(kvp.Key)
                Dim value = Uri.EscapeDataString(kvp.Value.ToString())
                pairs.Add($"{key}={value}")
            End If
        Next
        
        Return String.Join("&", pairs)
    End Function
    
    ''' <summary>
    ''' 将字典转换为 Form URL Encoded 格式
    ''' </summary>
    Public Function DictionaryToFormUrlEncoded(dict As Dictionary(Of String, Object)) As String
        Return BuildQueryString(dict)
    End Function
    
    ''' <summary>
    ''' 解析查询字符串为字典
    ''' </summary>
    Public Function ParseQueryString(queryString As String) As Dictionary(Of String, String)
        Dim dict = New Dictionary(Of String, String)
        
        If String.IsNullOrEmpty(queryString) Then Return dict
        
        ' 移除开头的 ?
        If queryString.StartsWith("?") Then
            queryString = queryString.Substring(1)
        End If
        
        Dim pairs = queryString.Split("&"c)
        For Each pair In pairs
            Dim keyValue = pair.Split("="c)
            If keyValue.Length = 2 Then
                Dim key = Uri.UnescapeDataString(keyValue(0))
                Dim value = Uri.UnescapeDataString(keyValue(1))
                dict(key) = value
            End If
        Next
        
        Return dict
    End Function
    
    ''' <summary>
    ''' 将对象转换为 JSON 字节数组
    ''' </summary>
    Public Function ToJsonBytes(obj As Object) As Byte()
        Try
            Dim json = ToJson(obj)
            Return Encoding.UTF8.GetBytes(json)
        Catch ex As Exception
            _logger.Error($"转换为 JSON 字节失败: {ex.Message}")
            Throw New MesFrameworkException("转换为 JSON 字节失败", "ENCODING_ERROR", ex.Message)
        End Try
    End Function
    
    ''' <summary>
    ''' 将字节数组转换为 JSON 字符串
    ''' </summary>
    Public Function FromJsonBytes(bytes As Byte()) As String
        Try
            If bytes Is Nothing OrElse bytes.Length = 0 Then Return ""
            Return Encoding.UTF8.GetString(bytes)
        Catch ex As Exception
            _logger.Error($"从 JSON 字节转换失败: {ex.Message}")
            Throw New MesFrameworkException("从 JSON 字节转换失败", "ENCODING_ERROR", ex.Message)
        End Try
    End Function
    
    ''' <summary>
    ''' 合并多个字典
    ''' </summary>
    Public Function MergeDictionaries(ParamArray dicts As Dictionary(Of String, Object)()) As Dictionary(Of String, Object)
        Dim result = New Dictionary(Of String, Object)
        
        For Each dict In dicts
            If dict IsNot Nothing Then
                For Each kvp In dict
                    result(kvp.Key) = kvp.Value
                Next
            End If
        Next
        
        Return result
    End Function
    
    ''' <summary>
    ''' 类型转换（支持可空类型）
    ''' </summary>
    Public Function ConvertValue(value As Object, targetType As Type) As Object
        Try
            If value Is Nothing Then
                If targetType.IsValueType AndAlso Nullable.GetUnderlyingType(targetType) Is Nothing Then
                    Return Activator.CreateInstance(targetType)
                End If
                Return Nothing
            End If
            
            If value.GetType() = targetType Then Return value
            
            ' 处理可空类型
            Dim underlyingType = Nullable.GetUnderlyingType(targetType)
            If underlyingType IsNot Nothing Then
                If String.IsNullOrEmpty(value.ToString()) Then
                    Return Nothing
                End If
                targetType = underlyingType
            End If
            
            ' 基本类型转换
            If targetType = GetType(String) Then Return value.ToString()
            If targetType = GetType(Integer) Then Return Convert.ToInt32(value)
            If targetType = GetType(Long) Then Return Convert.ToInt64(value)
            If targetType = GetType(Double) Then Return Convert.ToDouble(value)
            If targetType = GetType(Boolean) Then Return Convert.ToBoolean(value)
            If targetType = GetType(DateTime) Then Return Convert.ToDateTime(value)
            
            Return Convert.ChangeType(value, targetType)
        Catch ex As Exception
            _logger.Warning($"类型转换失败: {value} -> {targetType.Name}: {ex.Message}")
            Return Nothing
        End Try
    End Function
End Class
