# MES 连接库（MES Connectivity Toolkit）

[![CI](https://github.com/16318948605231/mes/actions/workflows/ci.yml/badge.svg)](https://github.com/16318948605231/mes/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](./LICENSE)

面向**机器视觉检测软件**与工厂 **MES 系统**对接的 C# / .NET 类库（多目标 **net8.0 / net10.0**）。
一套协议无关的统一 API，让你的检测软件**换工厂时只改配置（甚至一行连接字符串）、不改业务代码**即可切换底层通信协议。

> 由仓库中原有的 VB.NET 通用框架全新重写为 **C# + WPF**。旧代码已归档于 [`legacy/`](./legacy/)。

## 特性

- **协议无关的统一客户端** `IMesClient`：查询工单/配方/过站、上报检测结果/测量/缺陷、上传图像、设备状态与报警、订阅下发。
- **广泛的协议覆盖**：InMemory、REST、MQTT、FileDrop、Database(SQLite/SqlServer)、OPC UA、SECS/GEM，以及 **SOAP/WCF、Kafka、AMQP(RabbitMQ)、WebSocket/SignalR、gRPC**，按需引用。
- **极简接入**：一行连接字符串 `MesClients.Connect("mqtt://host:1883")` 直接创建客户端；或 `AddMes(configuration)` 从 `appsettings.json` 自动选协议并注册 Provider。
- **连通性自检**：`TestConnectionAsync()` / `PingAsync()` 一行验证配置是否正确。
- **配置驱动 + 操作目录**：业务操作到协议通道（URL/主题/SQL/NodeId/SxFy）的映射可覆盖，适配任意工厂接口。
- **配置驱动的检测流程（场景剧本）**：检测软件只触发“阶段”（检测前/中/后……），**每阶段做哪些 MES 交互全写在配置里**；换厂只换配置、代码零改动，`Enabled=false` 即全部空操作。见 [docs/08](./docs/08-配置驱动的检测流程(场景剧本).md)。
- **图像双模**：逐张图像可选**内嵌**或**单独上传**，超阈值自动切换。
- **稳健性与可观测性**：自动重连 + 自动重订阅（`MesReconnectOptions`）、`MesDiagnostics`（ActivitySource + Meter，可接入 OpenTelemetry）、完善的强类型事件、结果封装与重试。
- **WPF 测试界面** + **跨平台控制台样例** + **xUnit 单元/端到端测试**。

## 项目结构

```
src/Mes.Core                核心库（模型/客户端/构建器/操作目录/事件/诊断，零协议依赖）
src/Mes.Protocols.*         各协议 Provider（InMemory/Rest/Mqtt/FileDrop/Database/OpcUa/SecsGem/
                            Soap/Kafka/Amqp/WebSocket/Grpc）
src/Mes                     聚合包：连接字符串 + AddMes 自动注册（一次引入常用 Provider）
tests/Mes.Core.Tests        单元 + 端到端测试（xUnit）
samples/Mes.QuickStart      跨平台控制台快速上手样例（net8.0）
samples/Mes.ConfigDriven    配置驱动的检测流程样例：同一份代码 + 多份工厂配置（net8.0）
samples/Mes.TestApp         WPF 测试界面（net10.0-windows，仅 Windows）
docs/                       开发文档
legacy/                     归档的旧 VB.NET 代码
```

## 快速开始

### 方式一：一行连接字符串（最简）

```csharp
using Mes; // 聚合包

await using var client = MesClients.Connect("mqtt://mes-broker:1883?prefix=mes");
if (await client.PingAsync())           // 连通性自检
    await client.ReportDeviceStatusAsync(new() { DeviceId = "CAM-1", State = DeviceState.Running });
```

支持的 scheme：`mem/inmemory`、`rest/http/https`、`mqtt/mqtts`、`opc.tcp/opcua`、`file/filedrop`、
`db/sqlite/mssql`、`secs/hsms/gem`、`soap/soaps/wcf`、`kafka`、`amqp/amqps/rabbitmq`、`ws/wss/signalr`、`grpc/grpcs`。

### 方式二：依赖注入 + 配置文件（零代码切协议）

```csharp
// appsettings.json: { "Mes": { "ConnectionString": "rest://mes.factory.local?token=..." } }
services.AddMes(configuration);         // 自动解析协议并注册对应 Provider
```

### 方式三：Fluent 构建器（精细控制）

```csharp
using Mes.Core.Builder;
using Mes.Protocols.InMemory;

await using var client = MesClientBuilder.Create()
    .UseInMemory()                      // 换工厂时改这一行，如 .UseRest(...) / .UseKafka(...) / .UseGrpc(...)
    .Build();

await client.ConnectAsync();
await client.ReportInspectionResultAsync(new InspectionResult
{
    SerialNumber = "SN-0001",
    Outcome = InspectionOutcome.Pass
});
```

### 方式四：配置驱动的检测流程（换厂只换配置，代码零改动）

检测软件只触发“阶段”，**每个阶段做哪些 MES 交互全写在 `appsettings.json` 里**：

```csharp
using Mes; // 聚合包

// 启动读配置即得到一个“配置驱动”的 MES 宿主（Enabled=false 时全部空操作）
var mes = MesWorkflows.Create(configuration);   // 或 DI：注入 IMesWorkflowHost

var ctx = mes.CreateContext()
    .Set("serialNumber", "SN-0001")
    .Set("workOrderId", "WO-1001");
await mes.RunAsync("BeforeInspection", ctx);    // 检测前：取工单/配方/过站，全由配置决定

// …视觉算法…
ctx.Set("outcome", "Pass").Set("inspectionResult", result);
await mes.RunAsync("AfterInspection", ctx);     // 检测后：上报结果/测量/报警，全由配置决定
```

换工厂只需换一份 `appsettings.json`。详见 [docs/08 · 配置驱动的检测流程](./docs/08-配置驱动的检测流程(场景剧本).md) 与可运行样例 [`samples/Mes.ConfigDriven`](./samples/Mes.ConfigDriven)。

完整用法见 [`docs/`](./docs/README.md)，可运行样例见 [`samples/Mes.QuickStart`](./samples/Mes.QuickStart)。

## 构建与测试

```bash
dotnet restore Mes.slnx
dotnet build src/Mes.Core/Mes.Core.csproj      # 逐项目构建（Linux/CI 请勿整体构建，避免 WPF 目标）
dotnet test  tests/Mes.Core.Tests/Mes.Core.Tests.csproj
```

Windows 上运行测试界面：

```powershell
dotnet run --project samples/Mes.TestApp
```

> WPF 界面（`net10.0-windows`）仅能在 Windows 上编译运行；其余库均为多目标 `net8.0;net10.0`（跨平台）。
> 依赖版本集中于 `Directory.Packages.props`（中央包管理）。

## 持续集成与发布

- **CI（自动验证）**：[`.github/workflows/ci.yml`](./.github/workflows/ci.yml) 在每次推送 / PR 上自动执行：
  Linux 逐项目构建全部库（`net8.0;net10.0`）+ 运行全部单元测试 + 验证可打包；Windows 额外整体构建解决方案（含 WPF 界面）。
- **发布**：[`.github/workflows/release.yml`](./.github/workflows/release.yml) 在推送 `v*` 标签时 `dotnet pack` 全部库，
  并在配置了仓库密钥 `NUGET_API_KEY` 时推送到 NuGet（未配置则仅产出 `.nupkg`/`.snupkg` 工件）。
- 面向真实 Broker 的集成测试默认跳过，设置 `MES_KAFKA_BOOTSTRAP` / `MES_AMQP_URI` / `MES_GRPC_URL` 环境变量后启用。

详见 [打包发布与持续集成](./docs/07-打包发布与持续集成.md)。

## 许可

本项目采用 [MIT 许可证](./LICENSE)。
