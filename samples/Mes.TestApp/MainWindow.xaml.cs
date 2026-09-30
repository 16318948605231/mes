using System.IO;
using System.Text.Json;
using System.Windows;
using Mes.Core.Builder;
using Mes.Core.Client;
using Mes.Core.Enums;
using Mes.Core.Events;
using Mes.Core.Models;
using Mes.Protocols.Database;
using Mes.Protocols.FileDrop;
using Mes.Protocols.InMemory;
using Mes.Protocols.Mqtt;
using Mes.Protocols.Rest;
using Microsoft.Win32;

namespace Mes.TestApp;

/// <summary>
/// 测试界面主窗口。默认使用进程内回环 Provider（InMemory），无需真实 MES 服务器即可体验
/// 连接 → 查询 → 上报检测结果 → 图像传输 → 报警 的完整链路。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private IMesClient? _client;
    private readonly InMemoryMesServer _server = new();

    public MainWindow()
    {
        InitializeComponent();
        SeedDemoData();
        Log("界面已就绪。默认协议 InMemory 可直接点击“连接”。");
    }

    /// <summary>为 InMemory 演示注入一些样例数据。</summary>
    private void SeedDemoData()
    {
        _server.WorkOrders["WO-1001"] = new WorkOrder
        {
            Id = "WO-1001",
            Number = "WO-1001",
            ProductCode = "P-DEMO",
            ProductName = "演示产品",
            Quantity = 100,
            CompletedQuantity = 42,
            Status = WorkOrderStatus.InProgress
        };
        // SN-0001 在 OP20 放行
        _server.GateDecisions["SN-0001|OP20"] = true;
    }

    // ---------------- 连接管理 ----------------

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await DisposeClientAsync();

            var protocol = (ProtocolBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "InMemory";
            var endpoint = EndpointBox.Text?.Trim() ?? string.Empty;
            _client = BuildClient(protocol, endpoint);
            HookEvents(_client);

            await _client.ConnectAsync();
            ConnectButton.IsEnabled = false;
            DisconnectButton.IsEnabled = true;
            Log($"已使用 {protocol} 协议连接。");
        }
        catch (Exception ex)
        {
            Log($"[错误] 连接失败：{ex.Message}");
            MessageBox.Show(ex.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_client is not null)
                await _client.DisconnectAsync();
            Log("已断开连接。");
        }
        catch (Exception ex)
        {
            Log($"[错误] 断开失败：{ex.Message}");
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
        }
    }

    private IMesClient BuildClient(string protocol, string endpoint)
    {
        var builder = MesClientBuilder.Create().WithName("TestApp");
        return protocol switch
        {
            "Rest" => builder.UseRest(string.IsNullOrWhiteSpace(endpoint) ? "https://localhost" : endpoint).Build(),
            "FileDrop" => builder.UseFileDrop(string.IsNullOrWhiteSpace(endpoint)
                ? Path.Combine(Path.GetTempPath(), "mes-exchange") : endpoint).Build(),
            "Mqtt" => builder.UseMqtt(string.IsNullOrWhiteSpace(endpoint) ? "localhost" : endpoint).Build(),
            "Database" => builder.UseDatabase(string.IsNullOrWhiteSpace(endpoint)
                ? $"Data Source={Path.Combine(Path.GetTempPath(), "mes-demo.db")}" : endpoint,
                provider: "Sqlite", configure: o => o.SetProperty("EnsureSchema", "true")).Build(),
            _ => builder.UseInMemory(_server).Build()
        };
    }

    private void HookEvents(IMesClient client)
    {
        client.ConnectionStateChanged += (_, args) =>
            OnUi(() =>
            {
                StatusText.Text = $"{args.Current}";
                Log($"连接状态：{args.Previous} → {args.Current}");
            });
        client.InspectionReported += (_, args) =>
            OnUi(() => Log($"检测结果已上报：{args.Result.SerialNumber} · {args.Result.Outcome} · 成功={args.Success}"));
        client.AlarmReceived += (_, args) =>
            OnUi(() => Log($"收到报警：{args.Alarm.Code} · {args.Alarm.Text}"));
        client.MessageReceived += (_, args) =>
            OnUi(() => Log($"收到消息 [{args.Channel}]"));
        client.ErrorOccurred += (_, args) =>
            OnUi(() => Log($"[错误] {args.Context}: {args.Exception.Message}"));
    }

    // ---------------- 查询 ----------------

    private async void OnGetWorkOrderClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected()) return;
        try
        {
            var result = await _client!.GetWorkOrderAsync(WorkOrderIdBox.Text.Trim());
            QueryResultBox.Text = result.Success
                ? Pretty(result.Value)
                : $"失败：{result.Code} {result.Message}";
            Log($"查询工单 {WorkOrderIdBox.Text}：成功={result.Success}");
        }
        catch (Exception ex)
        {
            QueryResultBox.Text = ex.Message;
        }
    }

    private async void OnCheckGateClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected()) return;
        try
        {
            var result = await _client!.CheckUnitPassedAsync(SerialQueryBox.Text.Trim(), OperationBox.Text.Trim());
            QueryResultBox.Text = result.Success
                ? $"过站校验结果：{(result.Value ? "放行" : "拦截")}"
                : $"失败：{result.Code} {result.Message}";
            Log($"过站校验 {SerialQueryBox.Text}@{OperationBox.Text}：成功={result.Success}");
        }
        catch (Exception ex)
        {
            QueryResultBox.Text = ex.Message;
        }
    }

    // ---------------- 上报检测结果 ----------------

    private void OnBrowseImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "图像文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*"
        };
        if (dialog.ShowDialog() == true)
            ImagePathBox.Text = dialog.FileName;
    }

    private async void OnReportInspectionClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected()) return;
        try
        {
            var outcome = ((OutcomeBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string) == "Fail"
                ? InspectionOutcome.Fail
                : InspectionOutcome.Pass;
            var imageMode = (ImageModeBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "Embedded";

            var result = new InspectionResult
            {
                SerialNumber = ReportSerialBox.Text.Trim(),
                OperationId = OperationBox.Text.Trim(),
                Outcome = outcome
            };

            if (imageMode != "None" && !string.IsNullOrWhiteSpace(ImagePathBox.Text) && File.Exists(ImagePathBox.Text))
            {
                result.Images.Add(BuildImage(ImagePathBox.Text, imageMode));
            }

            var report = await _client!.ReportInspectionResultAsync(result);
            ReportResultBox.Text = report.Success
                ? $"上报成功。用时 {report.ElapsedMilliseconds} ms。"
                : $"失败：{report.Code} {report.Message}";
        }
        catch (Exception ex)
        {
            ReportResultBox.Text = ex.Message;
        }
    }

    private static MesImage BuildImage(string path, string mode)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var format = ext switch
        {
            ".png" => ImageFormat.Png,
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Unknown
        };
        return new MesImage
        {
            FileName = Path.GetFileName(path),
            Format = format,
            LocalPath = path,
            Data = File.ReadAllBytes(path),
            TransferMode = mode == "SeparateUpload" ? ImageTransferMode.SeparateUpload : ImageTransferMode.Embedded
        };
    }

    // ---------------- 报警 ----------------

    private async void OnRaiseAlarmClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected()) return;
        try
        {
            var result = await _client!.RaiseAlarmAsync(new Alarm
            {
                Code = AlarmCodeBox.Text.Trim(),
                Text = AlarmTextBox.Text.Trim()
            });
            AlarmResultBox.Text = result.Success ? "报警已触发。" : $"失败：{result.Code} {result.Message}";
        }
        catch (Exception ex)
        {
            AlarmResultBox.Text = ex.Message;
        }
    }

    private async void OnClearAlarmClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected()) return;
        try
        {
            var result = await _client!.ClearAlarmAsync(AlarmCodeBox.Text.Trim());
            AlarmResultBox.Text = result.Success ? "报警已解除。" : $"失败：{result.Code} {result.Message}";
        }
        catch (Exception ex)
        {
            AlarmResultBox.Text = ex.Message;
        }
    }

    // ---------------- 辅助 ----------------

    private bool EnsureConnected()
    {
        if (_client is not null && _client.State == MesConnectionState.Connected)
            return true;
        MessageBox.Show("请先连接。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private static string Pretty(object? value)
        => value is null ? "(空)" : JsonSerializer.Serialize(value, JsonOptions);

    private void OnClearLogClick(object sender, RoutedEventArgs e) => LogList.Items.Clear();

    private void Log(string message)
    {
        LogList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
    }

    private void OnUi(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.Invoke(action);
    }

    private async Task DisposeClientAsync()
    {
        if (_client is not null)
        {
            try { await _client.DisposeAsync(); } catch { /* ignore */ }
            _client = null;
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        await DisposeClientAsync();
        base.OnClosed(e);
    }
}
