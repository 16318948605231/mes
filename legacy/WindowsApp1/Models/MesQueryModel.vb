''' <summary>
''' MES 系统交互框架 - 查询模型示例（包含可空字段）
''' </summary>
Public Class MesQueryModel
    ''' <summary>生产订单 ID（可为空）</summary>
    Public Property ProductionOrderId As String

    ''' <summary>物料批号（可为空）</summary>
    Public Property MaterialBatchNo As String

    ''' <summary>开始日期（可为空）</summary>
    Public Property StartDate As DateTime?

    ''' <summary>结束日期（可为空）</summary>
    Public Property EndDate As DateTime?

    ''' <summary>状态代码（可为空）</summary>
    Public Property StatusCode As Integer?

    ''' <summary>工序代码（可为空）</summary>
    Public Property ProcessCode As String

    ''' <summary>工作中心 ID（可为空）</summary>
    Public Property WorkCenterId As String

    ''' <summary>操作员 ID（可为空）</summary>
    Public Property OperatorId As String

    ''' <summary>设备 ID（可为空）</summary>
    Public Property EquipmentId As String

    ''' <summary>产品代码（可为空）</summary>
    Public Property ProductCode As String

    ''' <summary>类别代码（可为空）</summary>
    Public Property CategoryCode As String

    ''' <summary>是否包含详细信息（可为空）</summary>
    Public Property IncludeDetails As Boolean?

    ''' <summary>是否包含历史记录（可为空）</summary>
    Public Property IncludeHistory As Boolean?

    ''' <summary>页码</summary>
    Public Property PageIndex As Integer = 1

    ''' <summary>每页数量</summary>
    Public Property PageSize As Integer = 50

    ''' <summary>排序字段</summary>
    Public Property OrderBy As String = "Id"

    ''' <summary>排序方向（asc/desc）</summary>
    Public Property SortOrder As String = "desc"

    ''' <summary>搜索关键词（可为空）</summary>
    Public Property SearchKeyword As String

    ''' <summary>
    ''' 检查某个字段是否已设置（不为空）
    ''' </summary>
    Public Function IsPropertySet(propertyName As String) As Boolean
        Dim prop = Me.GetType().GetProperty(propertyName)
        If prop Is Nothing Then Return False

        Dim value = prop.GetValue(Me)

        ' 检查 null
        If value Is Nothing Then Return False

        ' 检查字符串是否为空
        If TypeOf value Is String Then
            Return Not String.IsNullOrWhiteSpace(CStr(value))
        End If

        ' 对于可空类型，检查 HasValue
        Dim valueType = value.GetType()
        If valueType.IsGenericType AndAlso valueType.GetGenericTypeDefinition() = GetType(Nullable(Of)) Then
            Return True ' 可空类型有值
        End If

        Return True
    End Function

    ''' <summary>
    ''' 转换为字典（只包含非空值）
    ''' </summary>
    Public Function ToDictionary() As Dictionary(Of String, Object)
        Dim dict = New Dictionary(Of String, Object)

        ' 添加非空字符串属性
        If Not String.IsNullOrWhiteSpace(ProductionOrderId) Then
            dict.Add(NameOf(ProductionOrderId), ProductionOrderId)
        End If

        If Not String.IsNullOrWhiteSpace(MaterialBatchNo) Then
            dict.Add(NameOf(MaterialBatchNo), MaterialBatchNo)
        End If

        If StartDate.HasValue Then
            dict.Add(NameOf(StartDate), StartDate)
        End If

        If EndDate.HasValue Then
            dict.Add(NameOf(EndDate), EndDate)
        End If

        If StatusCode.HasValue Then
            dict.Add(NameOf(StatusCode), StatusCode)
        End If

        If Not String.IsNullOrWhiteSpace(ProcessCode) Then
            dict.Add(NameOf(ProcessCode), ProcessCode)
        End If

        If Not String.IsNullOrWhiteSpace(WorkCenterId) Then
            dict.Add(NameOf(WorkCenterId), WorkCenterId)
        End If

        If Not String.IsNullOrWhiteSpace(OperatorId) Then
            dict.Add(NameOf(OperatorId), OperatorId)
        End If

        If Not String.IsNullOrWhiteSpace(EquipmentId) Then
            dict.Add(NameOf(EquipmentId), EquipmentId)
        End If

        If Not String.IsNullOrWhiteSpace(ProductCode) Then
            dict.Add(NameOf(ProductCode), ProductCode)
        End If

        If Not String.IsNullOrWhiteSpace(CategoryCode) Then
            dict.Add(NameOf(CategoryCode), CategoryCode)
        End If

        If IncludeDetails.HasValue Then
            dict.Add(NameOf(IncludeDetails), IncludeDetails)
        End If

        If IncludeHistory.HasValue Then
            dict.Add(NameOf(IncludeHistory), IncludeHistory)
        End If

        ' 分页参数总是添加
        dict.Add(NameOf(PageIndex), PageIndex)
        dict.Add(NameOf(PageSize), PageSize)
        dict.Add(NameOf(OrderBy), OrderBy)
        dict.Add(NameOf(SortOrder), SortOrder)

        If Not String.IsNullOrWhiteSpace(SearchKeyword) Then
            dict.Add(NameOf(SearchKeyword), SearchKeyword)
        End If

        Return dict
    End Function
End Class
