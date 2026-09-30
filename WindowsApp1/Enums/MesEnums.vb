''' <summary>
''' MES 系统交互框架 - 枚举定义
''' </summary>

''' <summary>
''' HTTP 请求方法
''' </summary>
Public Enum HttpMethod
    [Get]
    Post
    Put
    Delete
    Patch
    Head
    Options
End Enum

''' <summary>
''' 文件上传模式
''' </summary>
Public Enum UploadMode
    ''' <summary>表单上传（Form Data）</summary>
    FormData = 1

    ''' <summary>二进制上传</summary>
    Binary = 2

    ''' <summary>Base64 编码上传</summary>
    Base64 = 3

    ''' <summary>多部分上传（Multipart）</summary>
    Multipart = 4

    ''' <summary>分块上传</summary>
    Chunked = 5
End Enum

''' <summary>
''' 日志级别
''' </summary>
Public Enum LogLevel
    Debug = 0
    Info = 1
    Warning = 2
    [Error] = 3
    Critical = 4
End Enum

''' <summary>
''' 响应状态
''' </summary>
Public Enum ResponseStatus
    Success = 200
    Created = 201
    BadRequest = 400
    Unauthorized = 401
    Forbidden = 403
    NotFound = 404
    Conflict = 409
    ServerError = 500
    ServiceUnavailable = 503
    Timeout = 504
    Unknown = 0
End Enum

''' <summary>
''' 文件验证结果
''' </summary>
Public Enum FileValidationResult
    Valid = 0
    InvalidSize = 1
    InvalidType = 2
    FileNotFound = 3
    AccessDenied = 4
    Unknown = 5
End Enum

''' <summary>
''' 上传状态
''' </summary>
Public Enum UploadStatus
    Pending = 0
    InProgress = 1
    Completed = 2
    Failed = 3
    Cancelled = 4
    Paused = 5
End Enum

''' <summary>
''' 身份认证类型
''' </summary>
Public Enum AuthType
    None = 0
    BasicAuth = 1
    BearerToken = 2
    ApiKey = 3
    OAuth2 = 4
End Enum

''' <summary>
''' 数据格式类型
''' </summary>
Public Enum DataFormatType
    Json = 1
    Xml = 2
    FormUrlEncoded = 3
    FormData = 4
End Enum
