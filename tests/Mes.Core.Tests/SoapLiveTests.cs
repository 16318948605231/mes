using System.Net;
using System.Net.Sockets;
using System.Text;
using Mes.Core.Builder;
using Mes.Protocols.Soap;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// SOAP Provider 端到端测试：用本地 <see cref="HttpListener"/> 充当 SOAP 网关，
/// 验证客户端能正确封装 SOAP 请求并解析 <c>soap:Body</c> 中的内层 JSON。
/// </summary>
public sealed class SoapLiveTests
{
    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task GetWorkOrder_OverSoap_ReturnsDeserializedValue()
    {
        if (!HttpListener.IsSupported)
            return; // 平台不支持则跳过。

        var port = GetFreePort();
        var prefix = $"http://localhost:{port}/soap/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var serverTask = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync().ConfigureAwait(false);
            // 返回一个 SOAP 1.1 信封，Body 首元素的内层文本为 WorkOrder JSON。
            const string workOrderJson = "{\"id\":\"WO-1\",\"productCode\":\"P1\",\"quantity\":10}";
            var responseEnvelope =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<soap:Body><GetWorkOrderResponse xmlns=\"http://mes.local/\">" +
                "<![CDATA[" + workOrderJson + "]]>" +
                "</GetWorkOrderResponse></soap:Body></soap:Envelope>";
            var bytes = Encoding.UTF8.GetBytes(responseEnvelope);
            ctx.Response.ContentType = "text/xml; charset=utf-8";
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            ctx.Response.Close();
        }, serverCts.Token);

        await using var client = MesClientBuilder.Create()
            .UseSoap(prefix, serviceNamespace: "http://mes.local/")
            .Build();

        var result = await client.GetWorkOrderAsync("WO-1");

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Value);
        Assert.Equal("WO-1", result.Value!.Id);
        Assert.Equal("P1", result.Value.ProductCode);

        await serverTask;
        listener.Stop();
    }
}
