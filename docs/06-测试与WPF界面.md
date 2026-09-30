# 测试与 WPF 界面

> 本库提供两条“无需真实 MES 服务器”即可验证的路径：**xUnit 单元测试**（CI 友好，跨平台）
> 与 **WPF 测试界面**（`samples/Mes.TestApp`，仅 Windows）。两者默认都基于进程内回环 Provider
> （`Mes.Protocols.InMemory`）。总纲见 [开发指导文档](./01-开发指导文档.md)。

---

## 1. 进程内回环服务器（`InMemoryMesServer`）

`InMemoryMesServer` 以内存字典/队列模拟 MES 后端，是测试与演示的核心：

- **预置查询数据**：`WorkOrders`、`Units`、`Recipes`、`Traceability`、`GateDecisions`。
- **捕获上报报文**：`ReportedInspections`、`ReportedAlarms`、`ReportedDeviceStatuses`、
  `UploadedImages` 等队列，供断言。
- **自定义处理器**：`OnRequest(operationKey, handler)` 注册任意操作的响应。
- **服务器推送**：`PushAsync(channel, payload)` 向订阅者推送消息（用于验证订阅链路）。

```csharp
var server = new InMemoryMesServer();
server.WorkOrders["WO-1"] = new WorkOrder { Id = "WO-1", ProductCode = "P1", Quantity = 10 };

var client = MesClientBuilder.Create().UseInMemory(server).Build();
var wo = await client.GetWorkOrderAsync("WO-1");
Assert.True(wo.Success);
```

---

## 2. 单元测试（`tests/Mes.Core.Tests`）

基于 xUnit，覆盖核心库与多个 Provider：

| 测试文件 | 覆盖内容 |
| --- | --- |
| `InMemoryClientTests` | 端到端查询/上报/事件/通用出口/连接状态。 |
| `ImageTransferTests` | 图像内嵌、单独上传、上传回执、超阈值自动切换。 |
| `OperationCatalogTests` | 模板渲染、参数外溢、配置校验、目录克隆。 |
| `RestProviderTests` | REST 请求/响应映射与行为。 |
| `DatabaseProviderTests` | 数据库直连（SQLite）查询/上报。 |
| `FileDropProviderTests` | 文件落地上报（outbox 生成 JSON）。 |
| `MqttProviderTests` | MQTT 请求/响应（RPC）、发布、订阅、通配符、超时（进程内 MQTT Broker）。 |
| `DependencyInjectionTests` | DI 注册与解析。 |

### 2.1 运行

```bash
dotnet test tests/Mes.Core.Tests/Mes.Core.Tests.csproj
```

> ⚠️ 在 Linux/CI 上请**逐项目**构建，不要对整个解决方案 `dotnet build`——WPF 示例目标
> `net10.0-windows` 无法在非 Windows 平台编译。测试项目本身是 `net10.0`，跨平台可运行。

### 2.2 典型模式

- **查询/上报断言**：预置数据 → 调用客户端 → 断言 `MesResult` 与服务器捕获队列。
- **事件断言**：订阅 `InspectionReported`/`AlarmReceived` 等事件，验证被触发。
- **订阅断言**：`SubscribeAsync`/`SubscribeAlarmsAsync` + 服务器推送 + `TaskCompletionSource`
  配合 `WaitAsync(timeout)` 等待异步回调。

```csharp
var tcs = new TaskCompletionSource<Alarm>(TaskCreationOptions.RunContinuationsAsynchronously);
await using var sub = await client.SubscribeAlarmsAsync("alarms", (a, _) => { tcs.TrySetResult(a); return Task.CompletedTask; });
await server.PushAsync("alarms", new Alarm { Code = "E-200" });
var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
```

### 2.3 MQTT 测试：进程内 Broker

`MqttProviderTests` 无需外部 Broker：用 `MQTTnet.Server` 在随机空闲端口启动一个进程内
Broker，并连接一个“响应端”客户端模拟 MES/网关——它读取请求的 `ResponseTopic` 与
`CorrelationData`，把结果回发到该响应主题，从而验证 MQTT 5 的请求/响应（RPC）链路；
同时覆盖单向上报发布、服务器推送订阅、通配符订阅与请求超时（无响应端时返回失败）。
测试通过 `IAsyncLifetime` 管理 Broker/客户端的建立与释放。

---

## 3. WPF 测试界面（`samples/Mes.TestApp`）

一个 `net10.0-windows` 的 WPF 应用（**仅 Windows** 可编译运行），默认使用 InMemory 协议并预置
样例数据，**无需任何真实服务器**即可完整体验：

- 选择协议（InMemory / REST / MQTT / FileDrop / Database）与端点；
- 连接 / 断开，实时显示连接状态；
- 查询工单、过站校验；
- 上报检测结果（可挑选图像并选择传输模式）；
- 触发 / 解除报警；
- 实时事件日志（连接、消息、报警、上报回执、错误）。

### 3.1 运行

```powershell
# 在 Windows 上
dotnet run --project samples/Mes.TestApp
```

启动后默认即为 InMemory 协议，直接点击“连接”即可开始联调。切换到其它协议时，只需在界面选择协议
并填入端点——这正是本库“换协议不换业务代码”的直观演示。

---

## 4. 小结

| 目标 | 推荐路径 |
| --- | --- |
| CI / 回归验证 | `dotnet test`（跨平台，InMemory + 进程内 MQTT Broker）。 |
| 手动体验 / 演示 | WPF 测试界面（Windows）。 |
| 对接真实工厂前的联调 | 先用 InMemory 跑通数据流，再用 `MapOperation` 适配工厂接口。 |
