# API 参考

> 本文件汇总 `Mes.Core` 对外的核心类型与方法签名。业务代码只需依赖 `IMesClient` 与
> `Mes.Core.Models` 中的强类型模型；协议差异由各 `Mes.Protocols.*` Provider 屏蔽。
> 快速上手见 [开发指导文档](./01-开发指导文档.md)，各协议细节见 [协议矩阵与配置](./03-协议矩阵与配置.md)。

命名空间速览：

| 命名空间 | 内容 |
| --- | --- |
| `Mes.Core.Client` | `IMesClient`（唯一业务入口）、`MesClient` |
| `Mes.Core.Builder` | `MesClientBuilder`（流式构建器） |
| `Mes.Core.Configuration` | `MesOptions` 及子配置（端点、鉴权、重试、TLS、图像） |
| `Mes.Core.Common` | `MesResult` / `MesResult<T>` / `MesResultCodes` |
| `Mes.Core.Models` | 业务模型（工单、单元、检测结果、图像、报警……） |
| `Mes.Core.Operations` | `MesOperationKeys` / `MesOperationBinding` / `MesOperationCatalog` |
| `Mes.Core.Events` | 事件参数类型 |
| `Mes.Core.Enums` | 协议、状态、图像模式等枚举 |

---

## 1. `IMesClient`

面向业务的统一接口，实现 `IAsyncDisposable`。

### 1.1 属性

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `Name` | `string` | 客户端名称。 |
| `State` | `MesConnectionState` | 当前连接状态。 |
| `Transport` | `IMesTransport` | 底层传输（高级用法/诊断）。 |

### 1.2 连接管理

```csharp
Task ConnectAsync(CancellationToken cancellationToken = default);
Task DisconnectAsync(CancellationToken cancellationToken = default);
```

### 1.3 查询类操作

```csharp
Task<MesResult<WorkOrder>>          GetWorkOrderAsync(string workOrderId, CancellationToken ct = default);
Task<MesResult<ProductUnit>>        GetUnitAsync(string serialNumber, CancellationToken ct = default);
Task<MesResult<Recipe>>             GetRecipeAsync(string recipeId, CancellationToken ct = default);
Task<MesResult<TraceabilityRecord>> GetTraceabilityAsync(string serialNumber, CancellationToken ct = default);
Task<MesResult<bool>>               CheckUnitPassedAsync(string serialNumber, string operationId, CancellationToken ct = default);
```

> 查询类操作依赖**请求/响应**能力。REST/Database/InMemory 天然支持；MQTT 通过 MQTT 5 的
> “响应主题 + 关联数据”实现 RPC；FileDrop 通过读取指定文件返回。若某协议不支持请求/响应，
> 对应调用会返回 `Code = NOT_SUPPORTED` 的失败结果。

### 1.4 上报类操作

```csharp
Task<MesResult> ReportInspectionResultAsync(InspectionResult result, CancellationToken ct = default);
Task<MesResult> ReportMeasurementsAsync(string serialNumber, IEnumerable<Measurement> measurements, CancellationToken ct = default);
Task<MesResult<ImageUploadReceipt>> UploadImageAsync(MesImage image, CancellationToken ct = default);
Task<MesResult> ReportDeviceStatusAsync(DeviceStatus status, CancellationToken ct = default);
Task<MesResult> RaiseAlarmAsync(Alarm alarm, CancellationToken ct = default);
Task<MesResult> ClearAlarmAsync(string alarmCode, CancellationToken ct = default);
```

`ReportInspectionResultAsync` 会先按每张图像的传输模式自动内嵌或单独上传（见 [图像传输](./05-图像传输.md)）。

### 1.5 通用出口

```csharp
Task<MesResult<TResponse>> InvokeAsync<TResponse>(string operationKey, object? payload = null,
    IDictionary<string, object?>? args = null, CancellationToken ct = default);
Task<MesResult> InvokeAsync(string operationKey, object? payload = null,
    IDictionary<string, object?>? args = null, CancellationToken ct = default);
```

调用任意**已映射**的操作键（内置或经 `MapOperation` 自定义）。`payload` 为请求体，`args` 用于
渲染通道模板（如 URL/主题中的 `{workOrderId}`），未被模板消费的参数会作为查询参数外溢。

### 1.6 订阅

```csharp
Task<IAsyncDisposable> SubscribeAsync<T>(string channel, Func<T, CancellationToken, Task> handler, CancellationToken ct = default);
Task<IAsyncDisposable> SubscribeAlarmsAsync(string channel, Func<Alarm, CancellationToken, Task>? handler = null, CancellationToken ct = default);
```

返回的句柄 `await using` 释放即取消订阅。仅具备订阅能力的协议（InMemory/MQTT/FileDrop/SECS-GEM）可用。

### 1.7 事件

| 事件 | 参数类型 | 触发时机 |
| --- | --- | --- |
| `ConnectionStateChanged` | `MesConnectionStateChangedEventArgs` | 连接状态变化。 |
| `MessageReceived` | `MesMessageReceivedEventArgs` | 收到订阅消息。 |
| `AlarmReceived` | `MesAlarmEventArgs` | 收到报警。 |
| `InspectionReported` | `MesInspectionReportedEventArgs` | 检测结果上报完成。 |
| `ErrorOccurred` | `MesErrorEventArgs` | 发生错误。 |

---

## 2. `MesResult` / `MesResult<T>`

所有高层方法均返回统一结果类型，**不以异常表示业务失败**。

| 成员 | 类型 | 说明 |
| --- | --- | --- |
| `Success` / `IsFailure` | `bool` | 成功/失败标志。 |
| `Code` | `string` | 结果码，见 `MesResultCodes`（成功为 `"OK"`）。 |
| `Message` | `string?` | 可读消息。 |
| `Exception` | `Exception?` | 底层异常（若有）。 |
| `CorrelationId` | `string?` | 关联/追踪标识。 |
| `ElapsedMilliseconds` | `long` | 耗时（毫秒）。 |
| `Metadata` | `IReadOnlyDictionary<string, object?>` | 附加元数据（如原始状态码）。 |
| `Value`（仅 `MesResult<T>`） | `T?` | 返回值。 |

工厂方法：`MesResult.Ok(...)` / `MesResult.Fail(code, ...)` / `MesResult.FromException(ex, ...)`，
泛型版本同名。

### 2.1 结果码（`MesResultCodes`）

`OK`、`EXCEPTION`、`VALIDATION_ERROR`、`NOT_CONNECTED`、`CONNECTION_FAILED`、`TIMEOUT`、
`CANCELLED`、`TRANSPORT_ERROR`、`SERIALIZATION_ERROR`、`OPERATION_NOT_MAPPED`、`NOT_SUPPORTED`、
`SERVER_ERROR`、`NOT_FOUND`、`UNAUTHORIZED`。

推荐处理范式：

```csharp
var wo = await client.GetWorkOrderAsync("WO-1001");
if (wo.Success)
    Use(wo.Value!);
else if (wo.Code == MesResultCodes.NotFound)
    // 业务上无此工单
    ;
else
    Log(wo.Code, wo.Message, wo.Exception);
```

---

## 3. `MesClientBuilder`

无需 DI 容器即可创建 `IMesClient` 的流式构建器（`Mes.Core.Builder`）。

```csharp
MesClientBuilder Create();
MesClientBuilder WithName(string name);
MesClientBuilder Configure(Action<MesOptions> configure);
MesClientBuilder WithSerializer(IMesSerializer serializer);
MesClientBuilder WithLoggerFactory(ILoggerFactory loggerFactory);
MesClientBuilder WithAuth(Action<MesAuthOptions> configure);
MesClientBuilder WithRetry(Action<MesRetryOptions> configure);
MesClientBuilder WithImageOptions(Action<MesImageOptions> configure);
MesClientBuilder MapOperation(MesOperationBinding binding);
MesClientBuilder MapOperation(string operationKey, string channelTemplate, string? verb = null, bool requestResponse = true);
MesClientBuilder AddTransportFactory(IMesTransportFactory factory);
IMesClient       Build();
```

协议选择由各 Provider 的扩展方法提供：`UseInMemory()`、`UseRest(baseAddress)`、
`UseMqtt(host, port)`、`UseFileDrop(root)`、`UseDatabase(...)`、`UseOpcUa(...)`、`UseSecsGem(...)`。

DI 用法则使用 `services.AddMesClient(configure)` + `services.AddXxxProtocol()`。

---

## 4. 操作目录（`Mes.Core.Operations`）

- **`MesOperationKeys`**：内置业务操作键常量，如 `GetWorkOrder`、`ReportInspection`、`UploadImage`、
  `RaiseAlarm`、`CheckUnitPassed` 等。
- **`MesOperationBinding`**：单条映射，含 `OperationKey`、`ChannelTemplate`、`Verb`、`RequestResponse`、
  `Qos` 等。
- **`MesOperationCatalog`**：映射集合，提供 `Map(...)`、`TryGet(...)`、`Contains(...)`、`Clone()`、
  `BuildRequest(...)` / `BuildMessage(...)`，以及各协议默认目录工厂
  （`CreateRestDefaults()`、`CreateMqttDefaults(prefix)` 等）。

覆盖某工厂的特殊接口时，用 `builder.MapOperation("GetWorkOrder", "/custom/wo/{workOrderId}", "GET")` 即可，
业务代码不变。

---

## 5. 核心数据模型（`Mes.Core.Models`）

| 类型 | 用途 | 关键字段 |
| --- | --- | --- |
| `WorkOrder` | 工单 | `Id`、`Number`、`ProductCode`、`Quantity`、`Status` |
| `ProductUnit` | 生产单元/SN | `SerialNumber`、`WorkOrderId`、`Status`、`CurrentOperation` |
| `Recipe` | 配方/参数集 | `Id`、`Name`、`Version`、`Parameters` |
| `TraceabilityRecord` | 追溯记录 | 见类型定义 |
| `InspectionResult` | 检测结果 | `SerialNumber`、`Outcome`、`Measurements`、`Defects`、`Images` |
| `Measurement` | 测量值 | `Name`、`Value`、`Unit`、上下限与判定 |
| `Defect` | 缺陷 | 位置、置信度 |
| `MesImage` | 图像 | `Data`/`LocalPath`/`RemoteUri`、`Format`、`TransferMode`、`Sha256` |
| `ImageUploadReceipt` | 上传回执 | `ImageId`、`RemoteUri`、`StorageKey`、`Size` |
| `GateCheckResult` | 过站校验结果 | `Passed`、`Reason` |
| `DeviceStatus` | 设备状态 | `DeviceId`、`State`、`Metrics` |
| `Alarm` | 报警 | `Code`、`Text`、`Severity`、`State`、`RaisedAt` |

所有集合字段均已初始化为空集合，可直接 `.Add(...)`。图像相关细节见 [图像传输](./05-图像传输.md)。
