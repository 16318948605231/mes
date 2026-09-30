using Mes;
using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Models;
using Mes.Protocols.InMemory;
using Microsoft.Extensions.DependencyInjection;

// ---------------------------------------------------------------------------
// MES 连接库 · 30 秒快速上手（跨平台控制台，使用进程内 InMemory 协议，零外部依赖）
//
// 本示例演示“被别的软件调用有多简单”：
//   1) 一行连接字符串创建客户端 + 连通性自检
//   2) 统一业务 API（查询工单）——切协议只改连接字符串，业务代码不变
//   3) 依赖注入方式（AddMes）
//   4) 发布/订阅
// ---------------------------------------------------------------------------

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== MES 连接库快速上手 ===\n");

// (1) 最简用法：一行连接字符串即可创建客户端。
//     切换协议时，只需把连接字符串换成 mqtt://、rest://、kafka://、amqp://、grpc://… 业务代码完全不变。
await using (var quick = MesClients.Connect("mem://demo"))
{
    var probe = await quick.TestConnectionAsync();
    Console.WriteLine($"[1] 连接字符串创建 + 连通性自检：success={probe.Success}, code={probe.Code}, {probe.ElapsedMilliseconds}ms");
    Console.WriteLine($"    Ping = {await quick.PingAsync()}\n");
}

// (2) 统一业务 API 演示：用一个已预置数据的 InMemory 服务端，查询工单。
var server = new InMemoryMesServer();
server.WorkOrders["WO-1001"] = new WorkOrder
{
    Id = "WO-1001",
    Number = "WO-1001",
    ProductCode = "P-VISION-01",
    ProductName = "视觉检测样件",
    Quantity = 500,
    CompletedQuantity = 120
};

await using (IMesClient client = MesClientBuilder.Create().UseInMemory(server).Build())
{
    var result = await client.GetWorkOrderAsync("WO-1001");
    if (result.Success && result.Value is { } wo)
        Console.WriteLine($"[2] 查询工单成功：{wo.Number} / {wo.ProductName}，进度 {wo.CompletedQuantity}/{wo.Quantity}\n");
    else
        Console.WriteLine($"[2] 查询失败：{result.Code} {result.Message}\n");
}

// (3) 依赖注入：AddMes(连接字符串) 自动注册核心服务 + 对应 Provider + 默认 IMesClient。
var services = new ServiceCollection();
services.AddMes("mem://di-sample");
await using (var provider = services.BuildServiceProvider())
{
    var client = provider.GetRequiredService<IMesClient>();
    Console.WriteLine($"[3] DI 解析 IMesClient：name={client.Name}, ping={await client.PingAsync()}\n");
}

// (4) 发布/订阅：订阅频道并接收一条服务端推送的消息。
var busServer = new InMemoryMesServer();
await using (var client = MesClientBuilder.Create().UseInMemory(busServer).Build())
{
    var received = new TaskCompletionSource<string>();
    await using var sub = await client.SubscribeAsync<DeviceStatus>("mes/device/status",
        (status, _) =>
        {
            received.TrySetResult(status.DeviceId);
            return Task.CompletedTask;
        });

    await busServer.PushAsync("mes/device/status",
        new DeviceStatus { DeviceId = "CAM-01", State = Mes.Core.Enums.DeviceState.Running });
    var deviceId = await WaitOrTimeout(received.Task, TimeSpan.FromSeconds(2));
    Console.WriteLine($"[4] 发布/订阅：收到设备状态 deviceId={deviceId ?? "(超时)"}\n");
}

Console.WriteLine("完成。切换到真实协议只需替换连接字符串，例如：");
Console.WriteLine("  mqtt://broker:1883?prefix=mes");
Console.WriteLine("  rest://mes.example.com/api?token=xxxxx");
Console.WriteLine("  kafka://broker:9092?prefix=mes");
Console.WriteLine("  ******rabbit:5672/vhost");
Console.WriteLine("  grpc://mes.example.com:5001");

static async Task<string?> WaitOrTimeout(Task<string> task, TimeSpan timeout)
{
    var completed = await Task.WhenAny(task, Task.Delay(timeout));
    return completed == task ? await task : null;
}
