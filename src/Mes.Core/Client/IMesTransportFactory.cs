using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mes.Core.Client;

/// <summary>
/// 传输工厂：每个协议 Provider 提供一个实现，负责依据配置创建 <see cref="IMesTransport"/>。
/// 通过 DI 注册后，<see cref="IMesClientFactory"/> 会按 <see cref="MesOptions.Protocol"/> 选取对应工厂。
/// </summary>
public interface IMesTransportFactory
{
    /// <summary>该工厂支持的协议。</summary>
    MesProtocolKind Protocol { get; }

    /// <summary>依据配置创建传输实例。</summary>
    IMesTransport Create(MesOptions options, ILoggerFactory loggerFactory);

    /// <summary>
    /// 为该协议创建默认操作目录。客户端会在其上应用 <see cref="MesOptions.OperationBindings"/> 覆盖。
    /// 默认返回 REST 风格端点映射，协议 Provider 可覆盖为主题/节点/命令映射。
    /// </summary>
    MesOperationCatalog CreateDefaultCatalog(MesOptions options)
        => MesOperationCatalog.CreateRestDefaults();
}
