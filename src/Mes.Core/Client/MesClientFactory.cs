using Mes.Core.Configuration;
using Mes.Core.Operations;
using Mes.Core.Serialization;
using Microsoft.Extensions.Logging;

namespace Mes.Core.Client;

/// <summary>
/// MES 客户端工厂：按名称/配置创建 <see cref="IMesClient"/>。
/// </summary>
public interface IMesClientFactory
{
    /// <summary>使用给定配置创建客户端。</summary>
    IMesClient Create(MesOptions options);

    /// <summary>按已注册名称创建客户端。</summary>
    IMesClient Create(string name);
}

/// <summary>
/// <see cref="IMesClientFactory"/> 的默认实现。
/// </summary>
public sealed class MesClientFactory : IMesClientFactory
{
    private readonly IEnumerable<IMesTransportFactory> _transportFactories;
    private readonly IReadOnlyDictionary<string, MesOptions> _namedOptions;
    private readonly IMesSerializer _serializer;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>构造。</summary>
    public MesClientFactory(
        IEnumerable<IMesTransportFactory> transportFactories,
        IEnumerable<MesOptions>? namedOptions = null,
        IMesSerializer? serializer = null,
        ILoggerFactory? loggerFactory = null)
    {
        _transportFactories = transportFactories ?? throw new ArgumentNullException(nameof(transportFactories));
        _namedOptions = (namedOptions ?? []).ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);
        _serializer = serializer ?? new JsonMesSerializer();
        _loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
    }

    /// <inheritdoc />
    public IMesClient Create(MesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = options.Validate();
        if (errors.Count > 0)
            throw new Exceptions.MesConfigurationException($"配置无效: {string.Join("; ", errors)}");

        var factory = _transportFactories.FirstOrDefault(f => f.Protocol == options.Protocol)
            ?? throw new Exceptions.MesConfigurationException(
                $"未找到协议 {options.Protocol} 的传输工厂，请确认已注册对应的 Provider（例如 AddRestProtocol/AddMqttProtocol）。");

        var transport = factory.Create(options, _loggerFactory);
        var catalog = BuildCatalog(factory, options);
        var logger = _loggerFactory.CreateLogger<MesClient>();
        return new MesClient(transport, catalog, _serializer, options, logger);
    }

    /// <inheritdoc />
    public IMesClient Create(string name)
    {
        if (!_namedOptions.TryGetValue(name, out var options))
            throw new Exceptions.MesConfigurationException($"未找到名为 '{name}' 的 MES 客户端配置。");
        return Create(options);
    }

    private static MesOperationCatalog BuildCatalog(IMesTransportFactory factory, MesOptions options)
    {
        var catalog = factory.CreateDefaultCatalog(options);
        foreach (var binding in options.OperationBindings)
            catalog.Map(binding);
        return catalog;
    }
}
