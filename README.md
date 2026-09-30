# MES 连接库（MES Connectivity Toolkit）

面向**机器视觉检测软件**与工厂 **MES 系统**对接的 C# / .NET 10 类库。
一套协议无关的统一 API，让你的检测软件**换工厂时只改配置、不改业务代码**即可切换底层通信协议。

> 由仓库中原有的 VB.NET 通用框架全新重写为 **C# + .NET 10 + WPF**。旧代码已归档于 [`legacy/`](./legacy/)。

## 特性

- **协议无关的统一客户端** `IMesClient`：查询工单/配方/过站、上报检测结果/测量/缺陷、上传图像、设备状态与报警、订阅下发。
- **可插拔协议 Provider**：InMemory、REST、MQTT、FileDrop、Database(SQLite/SqlServer)、OPC UA、SECS/GEM，按需引用。
- **配置驱动 + 操作目录**：业务操作到协议通道（URL/主题/SQL/NodeId/SxFy）的映射可覆盖，适配任意工厂接口。
- **图像双模**：逐张图像可选**内嵌**或**单独上传**，超阈值自动切换。
- **完善的强类型事件、结果封装与重试**。
- **WPF 测试界面** + **xUnit 单元测试**。

## 项目结构

```
src/Mes.Core                核心库（模型/客户端/构建器/操作目录/事件，零协议依赖）
src/Mes.Protocols.*         各协议 Provider（InMemory/Rest/Mqtt/FileDrop/Database/OpcUa/SecsGem）
tests/Mes.Core.Tests        单元测试（xUnit）
samples/Mes.TestApp         WPF 测试界面（net10.0-windows，仅 Windows）
docs/                       开发文档
legacy/                     归档的旧 VB.NET 代码
```

## 快速开始

```csharp
using Mes.Core.Builder;
using Mes.Core.Models;
using Mes.Protocols.InMemory;

await using var client = MesClientBuilder.Create()
    .UseInMemory()                 // 换工厂时改这一行，如 .UseRest("https://mes...")
    .Build();

await client.ConnectAsync();
await client.ReportInspectionResultAsync(new InspectionResult
{
    SerialNumber = "SN-0001",
    Outcome = InspectionOutcome.Pass
});
```

完整用法见 [`docs/`](./docs/README.md)。

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

> WPF 界面（`net10.0-windows`）仅能在 Windows 上编译运行；其余库均为跨平台 `net10.0`。
> 依赖版本集中于 `Directory.Packages.props`（中央包管理）。

## 许可

MIT
