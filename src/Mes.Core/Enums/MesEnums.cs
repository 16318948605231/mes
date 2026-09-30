namespace Mes.Core.Enums;

/// <summary>
/// 传输层连接状态。
/// </summary>
public enum MesConnectionState
{
    /// <summary>未连接（初始或已主动断开）。</summary>
    Disconnected = 0,

    /// <summary>正在建立连接。</summary>
    Connecting = 1,

    /// <summary>已连接，可以收发数据。</summary>
    Connected = 2,

    /// <summary>连接中断后正在自动重连。</summary>
    Reconnecting = 3,

    /// <summary>发生故障（不可恢复或达到重试上限）。</summary>
    Faulted = 4,

    /// <summary>已关闭并释放资源。</summary>
    Closed = 5
}

/// <summary>
/// 内置协议种类。<see cref="Custom"/> 用于第三方自定义传输实现。
/// </summary>
public enum MesProtocolKind
{
    /// <summary>进程内回环（用于单元测试与界面演示，无需外部服务器）。</summary>
    InMemory = 0,

    /// <summary>REST / HTTP + JSON。</summary>
    Rest = 1,

    /// <summary>MQTT（含 Sparkplug 友好主题约定）。</summary>
    Mqtt = 2,

    /// <summary>OPC UA。</summary>
    OpcUa = 3,

    /// <summary>文件落地（CSV / JSON / XML 交换目录）。</summary>
    FileDrop = 4,

    /// <summary>数据库直连（ADO.NET）。</summary>
    Database = 5,

    /// <summary>SECS/GEM（HSMS）——半导体 / 电子设备。</summary>
    SecsGem = 6,

    /// <summary>用户自定义协议。</summary>
    Custom = 99
}

/// <summary>
/// 传输能力标志。描述某个传输实现支持哪些通信语义。
/// </summary>
[Flags]
public enum MesTransportCapabilities
{
    /// <summary>无。</summary>
    None = 0,

    /// <summary>支持请求/响应（同步语义）。</summary>
    Request = 1,

    /// <summary>支持发布/上报（即发即弃）。</summary>
    Publish = 2,

    /// <summary>支持订阅（服务器主动推送）。</summary>
    Subscribe = 4,

    /// <summary>全部能力。</summary>
    All = Request | Publish | Subscribe
}

/// <summary>
/// 身份认证类型。
/// </summary>
public enum MesAuthType
{
    /// <summary>无认证。</summary>
    None = 0,

    /// <summary>HTTP Basic。</summary>
    Basic = 1,

    /// <summary>持有者令牌（HTTP Authorization 头）。</summary>
    BearerToken = 2,

    /// <summary>API Key（自定义头或查询参数）。</summary>
    ApiKey = 3,

    /// <summary>OAuth2 客户端凭据。</summary>
    OAuth2ClientCredentials = 4,

    /// <summary>用户名/密码（用于 MQTT、数据库、OPC UA 等）。</summary>
    UsernamePassword = 5,

    /// <summary>X.509 证书。</summary>
    Certificate = 6,

    /// <summary>自定义。</summary>
    Custom = 99
}

/// <summary>
/// 负载数据格式。
/// </summary>
public enum MesPayloadFormat
{
    /// <summary>JSON。</summary>
    Json = 0,

    /// <summary>XML。</summary>
    Xml = 1,

    /// <summary>表单 URL 编码。</summary>
    FormUrlEncoded = 2,

    /// <summary>纯文本。</summary>
    Text = 3,

    /// <summary>二进制。</summary>
    Binary = 4
}

/// <summary>
/// 检测结论（机器视觉 / 测试工位通用）。
/// </summary>
public enum InspectionOutcome
{
    /// <summary>未评估。</summary>
    None = 0,

    /// <summary>合格（OK / PASS）。</summary>
    Pass = 1,

    /// <summary>不合格（NG / FAIL）。</summary>
    Fail = 2,

    /// <summary>警告（合格但需关注）。</summary>
    Warning = 3,

    /// <summary>无法判定（图像/数据不足）。</summary>
    Indeterminate = 4,

    /// <summary>报废。</summary>
    Scrap = 5,

    /// <summary>返修。</summary>
    Rework = 6
}

/// <summary>
/// 单项测量值的判定状态。
/// </summary>
public enum MeasurementStatus
{
    /// <summary>未评估。</summary>
    NotEvaluated = 0,

    /// <summary>合格。</summary>
    Ok = 1,

    /// <summary>不合格（超规格）。</summary>
    Ng = 2,

    /// <summary>超出统计控制限（SPC）。</summary>
    OutOfControl = 3
}

/// <summary>
/// 缺陷严重度。
/// </summary>
public enum DefectSeverity
{
    /// <summary>提示。</summary>
    Info = 0,

    /// <summary>轻微。</summary>
    Minor = 1,

    /// <summary>主要。</summary>
    Major = 2,

    /// <summary>严重（致命）。</summary>
    Critical = 3
}

/// <summary>
/// 设备状态（参考 SEMI E10 设备状态模型）。
/// </summary>
public enum DeviceState
{
    /// <summary>未知。</summary>
    Unknown = 0,

    /// <summary>空闲待机。</summary>
    Idle = 1,

    /// <summary>生产运行中。</summary>
    Running = 2,

    /// <summary>暂停。</summary>
    Paused = 3,

    /// <summary>停机（非计划宕机）。</summary>
    Down = 4,

    /// <summary>维护保养。</summary>
    Maintenance = 5,

    /// <summary>调机 / 换型。</summary>
    Setup = 6,

    /// <summary>工程调试。</summary>
    Engineering = 7,

    /// <summary>离线。</summary>
    Offline = 8
}

/// <summary>
/// 报警严重度。
/// </summary>
public enum AlarmSeverity
{
    /// <summary>提示。</summary>
    Info = 0,

    /// <summary>警告。</summary>
    Warning = 1,

    /// <summary>轻微。</summary>
    Minor = 2,

    /// <summary>主要。</summary>
    Major = 3,

    /// <summary>严重。</summary>
    Critical = 4
}

/// <summary>
/// 报警状态。
/// </summary>
public enum AlarmState
{
    /// <summary>触发（Set / Active）。</summary>
    Set = 0,

    /// <summary>已解除（Cleared）。</summary>
    Cleared = 1,

    /// <summary>已确认（Acknowledged）。</summary>
    Acknowledged = 2
}

/// <summary>
/// 图像传输模式。允许调用方针对每张图像选择内嵌或单独上传。
/// </summary>
public enum ImageTransferMode
{
    /// <summary>内嵌：图像字节随业务报文一起发送（Base64 / 二进制）。</summary>
    Embedded = 0,

    /// <summary>单独上传：先上传图像获取引用（URI/Key），业务报文只携带引用。</summary>
    SeparateUpload = 1,

    /// <summary>仅引用：图像已存在于外部存储，报文只携带其引用。</summary>
    ReferenceOnly = 2
}

/// <summary>
/// 图像格式。
/// </summary>
public enum ImageFormat
{
    /// <summary>未知。</summary>
    Unknown = 0,

    /// <summary>PNG。</summary>
    Png = 1,

    /// <summary>JPEG。</summary>
    Jpeg = 2,

    /// <summary>BMP。</summary>
    Bmp = 3,

    /// <summary>TIFF。</summary>
    Tiff = 4,

    /// <summary>WebP。</summary>
    Webp = 5,

    /// <summary>原始像素数据。</summary>
    Raw = 6
}

/// <summary>
/// 工单状态。
/// </summary>
public enum WorkOrderStatus
{
    /// <summary>未知。</summary>
    Unknown = 0,

    /// <summary>已创建。</summary>
    Created = 1,

    /// <summary>已下达。</summary>
    Released = 2,

    /// <summary>进行中。</summary>
    InProgress = 3,

    /// <summary>已暂停。</summary>
    OnHold = 4,

    /// <summary>已完成。</summary>
    Completed = 5,

    /// <summary>已关闭。</summary>
    Closed = 6,

    /// <summary>已取消。</summary>
    Cancelled = 7
}
