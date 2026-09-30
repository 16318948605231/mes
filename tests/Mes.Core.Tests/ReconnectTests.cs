using System.Collections.Concurrent;
using Mes.Core.Client;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Models;
using Mes.Core.Operations;
using Mes.Core.Transport;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 自动重连 + 自动重订阅测试：使用可控的伪传输强制进入 Faulted，
/// 验证客户端会自动重连并恢复此前的订阅。
/// </summary>
public sealed class ReconnectTests
{
    [Fact]
    public async Task Fault_TriggersReconnect_AndResubscribes()
    {
        var transport = new ControllableTransport();
        var options = new MesOptions
        {
            AutoConnect = true,
            Reconnect = new MesReconnectOptions { Enabled = true, InitialDelayMs = 10, MaxDelayMs = 50 }
        };
        await using var client = new MesClient(transport, MesOperationCatalog.CreateRestDefaults(), options: options);

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sub = await client.SubscribeAsync<Alarm>("alarms", (a, _) =>
        {
            received.TrySetResult(a.Code);
            return Task.CompletedTask;
        });

        Assert.Equal(1, transport.SubscribeCount);

        // 强制故障：应触发自动重连 + 重新订阅。
        transport.Fault();

        await WaitForAsync(() => transport.SubscribeCount >= 2, TimeSpan.FromSeconds(5));
        Assert.True(transport.SubscribeCount >= 2, "重连后应重新建立订阅。");
        Assert.Equal(MesConnectionState.Connected, transport.State);

        // 重新订阅后推送应可被接收。
        transport.Push("alarms", "{\"Code\":\"E-900\"}");
        var code = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("E-900", code);
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;
            await Task.Delay(20);
        }
    }

    /// <summary>可控伪传输：支持强制故障、统计订阅次数、手工推送消息。</summary>
    private sealed class ControllableTransport : MesTransportBase
    {
        private readonly ConcurrentDictionary<string, Func<TransportMessage, CancellationToken, Task>> _handlers = new(StringComparer.Ordinal);

        public ControllableTransport() : base("controllable") { }

        public override MesProtocolKind Protocol => MesProtocolKind.InMemory;
        public override MesTransportCapabilities Capabilities => MesTransportCapabilities.All;

        public int SubscribeCount;

        protected override Task DoConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task DoDisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override Task<IAsyncDisposable> DoSubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref SubscribeCount);
            _handlers[channel] = handler;
            IAsyncDisposable handle = new Unsub(this, channel);
            return Task.FromResult(handle);
        }

        public void Fault() => SetState(MesConnectionState.Faulted, "test-forced");

        public void Push(string channel, string json)
        {
            if (_handlers.TryGetValue(channel, out var h))
                _ = h(new TransportMessage { Channel = channel, Body = System.Text.Encoding.UTF8.GetBytes(json) }, CancellationToken.None);
        }

        private sealed class Unsub : IAsyncDisposable
        {
            private readonly ControllableTransport _owner;
            private readonly string _channel;
            public Unsub(ControllableTransport owner, string channel) { _owner = owner; _channel = channel; }
            public ValueTask DisposeAsync()
            {
                _owner._handlers.TryRemove(_channel, out _);
                return ValueTask.CompletedTask;
            }
        }
    }
}
