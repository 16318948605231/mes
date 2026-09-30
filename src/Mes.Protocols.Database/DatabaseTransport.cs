using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.Database;

/// <summary>
/// 数据库直连传输（ADO.NET）。通道模板承载 SQL 语句，命名参数 <c>@name</c> 由
/// 请求参数、<c>@json</c>（序列化后的负载）与 <c>@now</c>（时间戳）填充。
/// 查询返回单列文本（视为 JSON）或整行 JSON；写入返回受影响行数。
/// </summary>
public sealed class DatabaseTransport : MesTransportBase
{
    private static readonly Regex ParamRegex = new(@"@(\w+)", RegexOptions.Compiled);

    private readonly MesOptions _options;
    private readonly DbProviderFactory _factory;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DbConnection? _connection;

    /// <summary>构造。</summary>
    public DatabaseTransport(MesOptions options, ILogger? logger = null)
        : base(options?.Name ?? "Database", logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionString = _options.Endpoint.BaseAddress
            ?? throw new Mes.Core.Exceptions.MesConfigurationException("数据库协议需要设置 Endpoint.BaseAddress（连接字符串）。");
        _factory = ResolveFactory(_options.GetProperty("Provider") ?? "Sqlite");
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.Database;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities => MesTransportCapabilities.Request | MesTransportCapabilities.Publish;

    /// <summary>解析 ADO.NET 提供程序工厂。</summary>
    public static DbProviderFactory ResolveFactory(string provider) => provider.ToLowerInvariant() switch
    {
        "sqlite" or "microsoft.data.sqlite" => Microsoft.Data.Sqlite.SqliteFactory.Instance,
        "sqlserver" or "mssql" or "system.data.sqlclient" => Microsoft.Data.SqlClient.SqlClientFactory.Instance,
        _ => throw new Mes.Core.Exceptions.MesConfigurationException(
            $"不支持的数据库提供程序 '{provider}'。支持: Sqlite, SqlServer。")
    };

    /// <inheritdoc />
    protected override async Task DoConnectAsync(CancellationToken cancellationToken)
    {
        _connection = _factory.CreateConnection() ?? throw new InvalidOperationException("无法创建数据库连接。");
        _connection.ConnectionString = _connectionString;
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (string.Equals(_options.GetProperty("EnsureSchema"), "true", StringComparison.OrdinalIgnoreCase))
            await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task DoDisconnectAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    /// <inheritdoc />
    protected override async Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        var isQuery = (request.Verb?.ToUpperInvariant() ?? "SELECT") is "GET" or "SELECT" or "QUERY" or "READ";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = CreateCommand(request);
            if (isQuery)
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var body = await ReadResultAsync(reader, cancellationToken).ConfigureAwait(false);
                return body is null
                    ? new TransportResponse { Success = false, StatusCode = 404, ErrorMessage = "未找到记录。" }
                    : TransportResponse.Ok(body, 200, "application/json");
            }

            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var payload = Encoding.UTF8.GetBytes($"{{\"affected\":{affected}}}");
            return TransportResponse.Ok(payload, 200, "application/json");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    protected override async Task DoPublishAsync(TransportMessage message, CancellationToken cancellationToken)
    {
        var request = new TransportRequest { Channel = message.Channel, Verb = "INSERT", Body = message.Body };
        foreach (var kv in message.Headers)
            request.Parameters[kv.Key] = kv.Value;
        await DoRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(TransportRequest request)
    {
        var command = _connection!.CreateCommand();
        command.CommandText = request.Channel;

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in request.Parameters)
            values[kv.Key] = kv.Value;
        values["json"] = request.Body is null ? null : Encoding.UTF8.GetString(request.Body);
        values["now"] = DateTimeOffset.UtcNow.ToString("O");

        foreach (Match m in ParamRegex.Matches(request.Channel))
        {
            var name = m.Groups[1].Value;
            if (command.Parameters.Contains("@" + name))
                continue;
            var p = command.CreateParameter();
            p.ParameterName = "@" + name;
            p.Value = (values.TryGetValue(name, out var v) ? v : null) ?? DBNull.Value;
            command.Parameters.Add(p);
        }
        return command;
    }

    private static async Task<byte[]?> ReadResultAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        // 单列：直接返回其文本（约定该列存储 JSON），便于反序列化为业务模型
        if (reader.FieldCount == 1)
        {
            if (await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false))
                return null;
            var value = reader.GetValue(0);
            var text = value as string ?? JsonSerializer.Serialize(value);
            return Encoding.UTF8.GetBytes(text);
        }

        // 多列：序列化为 JSON 对象
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false) ? null : reader.GetValue(i);
        return JsonSerializer.SerializeToUtf8Bytes(row);
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS mes_workorder(id TEXT PRIMARY KEY, data TEXT);
            CREATE TABLE IF NOT EXISTS mes_unit(sn TEXT PRIMARY KEY, data TEXT);
            CREATE TABLE IF NOT EXISTS mes_recipe(id TEXT PRIMARY KEY, data TEXT);
            CREATE TABLE IF NOT EXISTS mes_traceability(sn TEXT PRIMARY KEY, data TEXT);
            CREATE TABLE IF NOT EXISTS mes_gate(sn TEXT, op TEXT, data TEXT);
            CREATE TABLE IF NOT EXISTS mes_inspection(id INTEGER PRIMARY KEY AUTOINCREMENT, sn TEXT, data TEXT, created_at TEXT);
            CREATE TABLE IF NOT EXISTS mes_measurement(id INTEGER PRIMARY KEY AUTOINCREMENT, sn TEXT, data TEXT, created_at TEXT);
            CREATE TABLE IF NOT EXISTS mes_device_status(id INTEGER PRIMARY KEY AUTOINCREMENT, device_id TEXT, data TEXT, created_at TEXT);
            CREATE TABLE IF NOT EXISTS mes_alarm(id INTEGER PRIMARY KEY AUTOINCREMENT, code TEXT, data TEXT, created_at TEXT, cleared_at TEXT);
            CREATE TABLE IF NOT EXISTS mes_image(id TEXT PRIMARY KEY, data TEXT, created_at TEXT);
            """;
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = ddl;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
