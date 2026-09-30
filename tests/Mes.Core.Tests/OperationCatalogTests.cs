using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Operations;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 验证操作目录的模板渲染、参数外溢，以及配置校验。
/// </summary>
public sealed class OperationCatalogTests
{
    [Fact]
    public void BuildRequest_RendersPlaceholders()
    {
        var catalog = MesOperationCatalog.CreateRestDefaults();

        var request = catalog.BuildRequest(
            MesOperationKeys.GetWorkOrder,
            payload: null,
            args: new Dictionary<string, object?> { ["workOrderId"] = "WO 1/9" });

        Assert.Equal("GET", request.Verb);
        // {workOrderId:url} 应进行 URL 编码
        Assert.Contains("WO%201%2F9", request.Channel);
    }

    [Fact]
    public void BuildRequest_UnusedArgs_BecomeParameters()
    {
        var catalog = new MesOperationCatalog();
        catalog.Map("Q", "query/endpoint", "GET");

        var request = catalog.BuildRequest(
            "Q",
            payload: null,
            args: new Dictionary<string, object?> { ["a"] = "1", ["b"] = 2 });

        Assert.Equal("1", request.Parameters["a"]);
        Assert.Equal(2, request.Parameters["b"]);
    }

    [Fact]
    public void BuildRequest_UnknownOperation_Throws()
    {
        var catalog = new MesOperationCatalog();
        Assert.Throws<KeyNotFoundException>(() =>
            catalog.BuildRequest("missing", null, new Dictionary<string, object?>()));
    }

    [Fact]
    public void Clone_ProducesIndependentCatalog()
    {
        var original = MesOperationCatalog.CreateRestDefaults();
        var clone = original.Clone();
        clone.Map("Extra", "/extra", "GET");

        Assert.True(clone.Contains("Extra"));
        Assert.False(original.Contains("Extra"));
    }

    [Fact]
    public void Validate_Rest_RequiresBaseAddress()
    {
        var options = new MesOptions { Protocol = MesProtocolKind.Rest };

        var errors = options.Validate();

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validate_Rest_WithBaseAddress_Passes()
    {
        var options = new MesOptions { Protocol = MesProtocolKind.Rest };
        options.Endpoint.BaseAddress = "https://mes.example.com";

        var errors = options.Validate();

        Assert.Empty(errors);
    }

    [Fact]
    public void GetSetProperty_RoundTrips()
    {
        var options = new MesOptions();
        options.SetProperty("Provider", "Sqlite");

        Assert.Equal("Sqlite", options.GetProperty("Provider"));
        Assert.Equal("fallback", options.GetProperty("Absent", "fallback"));
    }
}
