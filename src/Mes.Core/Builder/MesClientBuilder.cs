using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Mes.Core.Serialization;
using Microsoft.Extensions.Logging;

namespace Mes.Core.Builder;

/// <summary>
/// Fluent 客户端构建器（无需 DI 容器即可创建 <see cref="IMesClient"/>）。
/// 协议 Provider 通过扩展方法（如 <c>UseRest</c>、<c>UseMqtt</c>）挂接。
/// </summary>
public sealed class MesClientBuilder
{
    private readonly List<IMesTransportFactory> _factories = new();
    private IMesSerializer? _serializer;
    private ILoggerFactory? _loggerFactory;

    /// <summary>可直接编辑的配置对象。</summary>
    public MesOptions Options { get; } = new();

    /// <summary>创建构建器。</summary>
    public static MesClientBuilder Create() => new();

    /// <summary>设置客户端名称。</summary>
    public MesClientBuilder WithName(string name)
    {
        Options.Name = name;
        return this;
    }

    /// <summary>自定义配置。</summary>
    public MesClientBuilder Configure(Action<MesOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Options);
        return this;
    }

    /// <summary>设置序列化器。</summary>
    public MesClientBuilder WithSerializer(IMesSerializer serializer)
    {
        _serializer = serializer;
        return this;
    }

    /// <summary>设置日志工厂。</summary>
    public MesClientBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>配置认证。</summary>
    public MesClientBuilder WithAuth(Action<MesAuthOptions> configure)
    {
        configure(Options.Auth);
        return this;
    }

    /// <summary>配置重试。</summary>
    public MesClientBuilder WithRetry(Action<MesRetryOptions> configure)
    {
        configure(Options.Retry);
        return this;
    }

    /// <summary>配置图像处理。</summary>
    public MesClientBuilder WithImageOptions(Action<MesImageOptions> configure)
    {
        configure(Options.Image);
        return this;
    }

    /// <summary>覆盖/新增一个操作映射。</summary>
    public MesClientBuilder MapOperation(MesOperationBinding binding)
    {
        Options.OperationBindings.Add(binding);
        return this;
    }

    /// <summary>覆盖/新增一个操作映射（简化参数）。</summary>
    public MesClientBuilder MapOperation(string operationKey, string channelTemplate, string? verb = null, bool requestResponse = true)
        => MapOperation(new MesOperationBinding
        {
            OperationKey = operationKey,
            ChannelTemplate = channelTemplate,
            Verb = verb,
            RequestResponse = requestResponse
        });

    /// <summary>注册传输工厂（通常由协议 Provider 的扩展方法调用）。</summary>
    public MesClientBuilder AddTransportFactory(IMesTransportFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories.RemoveAll(f => f.Protocol == factory.Protocol);
        _factories.Add(factory);
        Options.Protocol = factory.Protocol;
        return this;
    }

    /// <summary>构建客户端。</summary>
    public IMesClient Build()
    {
        var factory = new MesClientFactory(_factories, null, _serializer, _loggerFactory);
        return factory.Create(Options);
    }
}
