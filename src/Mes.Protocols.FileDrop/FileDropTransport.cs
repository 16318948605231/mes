using Mes.Core.Configuration;
using Mes.Core.Enums;
using Mes.Core.Transport;
using Microsoft.Extensions.Logging;

namespace Mes.Protocols.FileDrop;

/// <summary>
/// 文件落地传输：以交换目录进行文件级数据交换。
/// 发布=向目录写入报文文件；请求=读取指定文件；订阅=监视目录中新增的文件。
/// 适用于许多以“落地文件 + 定时扫描”方式集成的老式 MES/产线系统。
/// </summary>
public sealed class FileDropTransport : MesTransportBase
{
    private readonly MesOptions _options;
    private readonly string _root;
    private readonly string _processedFolder;

    /// <summary>构造。</summary>
    public FileDropTransport(MesOptions options, ILogger? logger = null)
        : base(options?.Name ?? "FileDrop", logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _root = _options.Endpoint.BaseAddress
            ?? throw new Mes.Core.Exceptions.MesConfigurationException("文件落地协议需要设置 Endpoint.BaseAddress（交换根目录）。");
        _processedFolder = _options.GetProperty("ProcessedFolder") ?? ".processed";
    }

    /// <inheritdoc />
    public override MesProtocolKind Protocol => MesProtocolKind.FileDrop;

    /// <inheritdoc />
    public override MesTransportCapabilities Capabilities
        => MesTransportCapabilities.Request | MesTransportCapabilities.Publish | MesTransportCapabilities.Subscribe;

    /// <inheritdoc />
    protected override Task DoConnectAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task DoDisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    protected override async Task<TransportResponse> DoRequestAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        // 约定：请求语义用于“读取”——若存在对应文件则返回其内容，否则写出请求文件并返回 202。
        var verb = request.Verb?.ToUpperInvariant();
        if (verb is null or "GET" or "READ")
        {
            var path = ResolveFilePath(request.Channel, ensureExtension: true);
            if (File.Exists(path))
            {
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                return TransportResponse.Ok(bytes, 200, "application/json");
            }
            return new TransportResponse { Success = false, StatusCode = 404, ErrorMessage = $"文件不存在: {path}" };
        }

        // 写出请求文件（POST/WRITE），返回 202 Accepted
        var written = await WriteFileAsync(request.Channel, request.Body, cancellationToken).ConfigureAwait(false);
        return new TransportResponse
        {
            Success = true,
            StatusCode = 202,
            Body = System.Text.Encoding.UTF8.GetBytes($"{{\"file\":\"{written.Replace("\\", "/")}\"}}"),
            ContentType = "application/json"
        };
    }

    /// <inheritdoc />
    protected override async Task DoPublishAsync(TransportMessage message, CancellationToken cancellationToken)
        => await WriteFileAsync(message.Channel, message.Body, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    protected override Task<IAsyncDisposable> DoSubscribeAsync(string channel, Func<TransportMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(_root, SanitizeRelative(channel));
        Directory.CreateDirectory(folder);
        var watcher = new FileWatcherSubscription(this, folder, channel, handler);
        return Task.FromResult<IAsyncDisposable>(watcher);
    }

    private async Task<string> WriteFileAsync(string channel, byte[]? body, CancellationToken cancellationToken)
    {
        var path = ResolveFilePath(channel, ensureExtension: false);
        // 若 channel 看起来是目录（无扩展名），则生成唯一文件名
        if (string.IsNullOrEmpty(Path.GetExtension(path)))
        {
            var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.json";
            path = Path.Combine(path, fileName);
        }

        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        // 原子写入：先写临时文件再改名
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, body ?? Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
        Logger.LogDebug("FileDrop 写出文件: {Path}", path);
        return path;
    }

    private string ResolveFilePath(string channel, bool ensureExtension)
    {
        var rel = SanitizeRelative(channel);
        var path = Path.Combine(_root, rel);
        if (ensureExtension && string.IsNullOrEmpty(Path.GetExtension(path)))
            path += ".json";
        return path;
    }

    private static string SanitizeRelative(string channel)
    {
        var rel = channel.Replace('\\', '/').TrimStart('/');
        var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != "..")
            .Select(SanitizeSegment);
        return Path.Combine(parts.ToArray());
    }

    private static string SanitizeSegment(string segment)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            segment = segment.Replace(c, '_');
        return segment;
    }

    internal string ProcessedFolder => _processedFolder;

    private sealed class FileWatcherSubscription : IAsyncDisposable
    {
        private readonly FileDropTransport _transport;
        private readonly FileSystemWatcher _watcher;
        private readonly string _channel;
        private readonly string _folder;
        private readonly Func<TransportMessage, CancellationToken, Task> _handler;

        public FileWatcherSubscription(FileDropTransport transport, string folder, string channel, Func<TransportMessage, CancellationToken, Task> handler)
        {
            _transport = transport;
            _folder = folder;
            _channel = channel;
            _handler = handler;
            _watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnCreated;
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            if (e.Name is not null && e.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                return;

            // 显式以 Task 方式处理并观察异常，避免 async void 的未捕获异常导致进程崩溃。
            _ = ProcessFileAsync(e).ContinueWith(
                t => _transport.RaiseError(t.Exception!.GetBaseException(), "FileDropWatcher"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private async Task ProcessFileAsync(FileSystemEventArgs e)
        {
            // 等待写入方完成
            await Task.Delay(50).ConfigureAwait(false);
            byte[] bytes;
            try { bytes = await File.ReadAllBytesAsync(e.FullPath).ConfigureAwait(false); }
            catch (IOException) { await Task.Delay(150).ConfigureAwait(false); bytes = await File.ReadAllBytesAsync(e.FullPath).ConfigureAwait(false); }

            var message = new TransportMessage
            {
                Channel = _channel,
                Body = bytes,
                ContentType = "application/json"
            };
            message.Headers["fileName"] = e.Name ?? string.Empty;

            _transport.RaiseMessageReceived(message);
            await _handler(message, CancellationToken.None).ConfigureAwait(false);

            // 归档已处理文件
            var processedDir = Path.Combine(_folder, _transport.ProcessedFolder);
            Directory.CreateDirectory(processedDir);
            var dest = Path.Combine(processedDir, e.Name ?? Path.GetFileName(e.FullPath));
            File.Move(e.FullPath, dest, overwrite: true);
        }

        public ValueTask DisposeAsync()
        {
            _watcher.Created -= OnCreated;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
