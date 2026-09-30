using System.Text;
using Mes.Core.Builder;
using Mes.Core.Enums;
using Mes.Core.Models;
using Mes.Protocols.InMemory;
using Xunit;

namespace Mes.Core.Tests;

/// <summary>
/// 验证图像的两种传输模式：内嵌（Embedded）与单独上传（SeparateUpload），以及自动切换。
/// </summary>
public sealed class ImageTransferTests
{
    private static byte[] SamplePng() => Encoding.UTF8.GetBytes("PNG-FAKE-BYTES-0123456789");

    [Fact]
    public async Task Embedded_Image_IsCarriedInsideInspection()
    {
        var server = new InMemoryMesServer();
        var client = MesClientBuilder.Create().UseInMemory(server).Build();

        var inspection = new InspectionResult
        {
            SerialNumber = "SN-EMB",
            Outcome = InspectionOutcome.Pass,
            Images =
            {
                new MesImage
                {
                    FileName = "a.png",
                    Format = ImageFormat.Png,
                    ContentType = "image/png",
                    TransferMode = ImageTransferMode.Embedded,
                    Data = SamplePng()
                }
            }
        };

        var result = await client.ReportInspectionResultAsync(inspection);

        Assert.True(result.Success);
        Assert.True(server.ReportedInspections.TryDequeue(out var captured));
        var img = Assert.Single(captured!.Images);
        // 内嵌模式：数据随检测结果一起提交，且未走单独上传通道
        Assert.Equal(ImageTransferMode.Embedded, img.TransferMode);
        Assert.NotNull(img.Data);
        Assert.True(server.UploadedImages.IsEmpty);
    }

    [Fact]
    public async Task SeparateUpload_Image_IsUploadedFirst_ThenReferenced()
    {
        var server = new InMemoryMesServer();
        var client = MesClientBuilder.Create().UseInMemory(server).Build();

        var inspection = new InspectionResult
        {
            SerialNumber = "SN-SEP",
            Outcome = InspectionOutcome.Pass,
            Images =
            {
                new MesImage
                {
                    FileName = "b.png",
                    Format = ImageFormat.Png,
                    ContentType = "image/png",
                    TransferMode = ImageTransferMode.SeparateUpload,
                    Data = SamplePng()
                }
            }
        };

        var result = await client.ReportInspectionResultAsync(inspection);

        Assert.True(result.Success);
        // 单独上传：图像先经上传通道，服务器捕获到一张上传图像
        Assert.False(server.UploadedImages.IsEmpty);

        Assert.True(server.ReportedInspections.TryDequeue(out var captured));
        var img = Assert.Single(captured!.Images);
        // 上传后引用化：内嵌数据被清空，改为引用（ReferenceOnly）并带有远程地址
        Assert.Equal(ImageTransferMode.ReferenceOnly, img.TransferMode);
        Assert.Null(img.Data);
        Assert.False(string.IsNullOrEmpty(img.RemoteUri));
    }

    [Fact]
    public async Task UploadImage_ReturnsReceipt()
    {
        var server = new InMemoryMesServer();
        var client = MesClientBuilder.Create().UseInMemory(server).Build();

        var receipt = await client.UploadImageAsync(new MesImage
        {
            FileName = "c.png",
            Format = ImageFormat.Png,
            ContentType = "image/png",
            TransferMode = ImageTransferMode.SeparateUpload,
            Data = SamplePng()
        });

        Assert.True(receipt.Success);
        Assert.NotNull(receipt.Value);
        Assert.False(string.IsNullOrEmpty(receipt.Value!.ImageId));
    }

    [Fact]
    public async Task Embedded_ExceedingMaxBytes_AutoSwitchesToSeparateUpload()
    {
        var server = new InMemoryMesServer();
        var client = MesClientBuilder.Create()
            .UseInMemory(server)
            .WithImageOptions(o => o.MaxEmbeddedBytes = 8) // 极小阈值，强制切换
            .Build();

        var inspection = new InspectionResult
        {
            SerialNumber = "SN-AUTO",
            Outcome = InspectionOutcome.Pass,
            Images =
            {
                new MesImage
                {
                    FileName = "big.png",
                    Format = ImageFormat.Png,
                    ContentType = "image/png",
                    TransferMode = ImageTransferMode.Embedded,
                    Data = SamplePng() // 大于 8 字节
                }
            }
        };

        var result = await client.ReportInspectionResultAsync(inspection);

        Assert.True(result.Success);
        Assert.False(server.UploadedImages.IsEmpty); // 已自动改走单独上传
    }
}
