using System.Net;
using System.Text;
using System.Text.Json;
using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Enums;
using Mes.Core.Models;
using Mes.Protocols.Rest;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 使用可编程的 <see cref="HttpMessageHandler"/> 桩验证 REST Provider 的请求映射与响应解析，
/// 无需真实 HTTP 服务器。
/// </summary>
public sealed class RestProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responder(request));
        }
    }

    private static IMesClient CreateClient(StubHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://mes.example.com") };
        return MesClientBuilder.Create()
            .Configure(o =>
            {
                o.Protocol = MesProtocolKind.Rest;
                o.Endpoint.BaseAddress = "https://mes.example.com";
            })
            .AddTransportFactory(new RestTransportFactory(_ => httpClient))
            .Build();
    }

    [Fact]
    public async Task GetWorkOrder_MapsToGet_AndParsesJson()
    {
        var handler = new StubHandler(_ =>
        {
            var wo = new WorkOrder { Id = "WO-REST", ProductCode = "PR" };
            var json = JsonSerializer.Serialize(wo);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var client = CreateClient(handler);

        var result = await client.GetWorkOrderAsync("WO-REST");

        Assert.True(result.Success);
        Assert.Equal("WO-REST", result.Value!.Id);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Contains("/api/mes/workorders/WO-REST", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task ReportInspection_MapsToPost()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"accepted\":true}", Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var result = await client.ReportInspectionResultAsync(new InspectionResult
        {
            SerialNumber = "SN-REST",
            Outcome = InspectionOutcome.Pass
        });

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/api/mes/inspections", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetWorkOrder_NotFound_ReturnsFailure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var result = await client.GetWorkOrderAsync("missing");

        Assert.False(result.Success);
    }
}
