using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Tnzi.EventBus;
using Tnzi.Storage.Controllers;
using Tnzi.Storage.Events;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 两条预览路由都必须发出 <see cref="FileAccessType.Preview"/> 的 <see cref="FileAccessedEvent"/>。
/// </summary>
/// <remarks>
/// <para>
/// 此前枚举成员 <c>Preview</c> 从未被赋值过：<c>files/{id}/preview</c> 走 <c>GetAsync</c> 记成 <c>Download</c>，
/// <c>files/preview/{id}/preview</c>（Office 转 PDF 等的正式入口）直接从 provider 取字节，
/// 一条事件都不发 —— 按文档在这个事件上挂审计或配额的消费方，看到的是「谁预览过」与「谁下载过」
/// 永远分不开、而 Office 预览在审计里根本不存在。
/// </para>
/// <para>
/// 父模块的发布点只有 <c>FileStorageService</c> 一处：预览服务经 <c>IFileStorageService.GetForPreviewAsync</c> 取流，
/// 两条路由不可能再各自漂开。（历史版本的字节存在版本自己的键下，由 Workspace 的 <c>FileVersionService</c>
/// 另发一条 <c>Download</c>，见 <c>VersionDownloadAccessEventTests</c>。）
/// </para>
/// </remarks>
public class PreviewAccessEventTests : StorageIntegrationTestBase
{
    private readonly List<FileAccessedEvent> _accessed = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var bus = new Mock<IEventBus> { DefaultValue = DefaultValue.Empty };
        bus.Setup(b => b.PublishAsync(It.IsAny<FileAccessedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FileAccessedEvent, CancellationToken>((e, _) => _accessed.Add(e))
            .Returns(Task.CompletedTask);
        services.AddSingleton(bus.Object);
    }

    [Fact]
    public async Task GetForPreviewAsync_PublishesPreview_NotDownload()
    {
        var service = CreateStorageService();
        var record = await CreateStoredFileAsync("photo.png", [1, 2, 3, 4]);

        var result = await service.GetForPreviewAsync(record.Id);

        Assert.True(result.Succeeded, result.Message);
        await using var _ = result.Data!;
        var evt = Assert.Single(_accessed);
        Assert.Equal(record.Id, evt.FileId);
        Assert.Equal(FileAccessType.Preview, evt.AccessType);
    }

    [Fact]
    public async Task GetAsync_StillPublishesDownload()
    {
        // 反向护栏：预览类型是新增的一条，不是把下载改了名。
        var service = CreateStorageService();
        var record = await CreateStoredFileAsync("photo.png", [1, 2, 3, 4]);

        var result = await service.GetAsync(record.Id);

        Assert.True(result.Succeeded, result.Message);
        await using var _ = result.Data!;
        var evt = Assert.Single(_accessed);
        Assert.Equal(FileAccessType.Download, evt.AccessType);
    }

    [Fact]
    public async Task FilePreviewService_GeneratePreview_PublishesExactlyOnePreviewEvent()
    {
        // files/preview/{id}/preview 的服务层：以前直接 _storage.DownloadAsync，零事件。
        var service = CreateStorageService();
        var preview = new FilePreviewService(service, ServiceProvider);
        var record = await CreateStoredFileAsync("notes.txt", "hello"u8.ToArray());

        await using var stream = await preview.GeneratePreviewAsync(record);

        var evt = Assert.Single(_accessed);
        Assert.Equal(record.Id, evt.FileId);
        Assert.Equal(FileAccessType.Preview, evt.AccessType);
    }

    [Fact]
    public async Task DefaultStorageController_Preview_PublishesExactlyOnePreviewEvent()
    {
        // files/{id}/preview：以前经 GetAsync 记成 Download。
        var service = CreateStorageService();
        var record = await CreateStoredFileAsync("photo.png", [1, 2, 3, 4]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = new MemoryStream();
        var controller = new DefaultStorageController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = http,
                RouteData = new RouteData(),
                ActionDescriptor = new ControllerActionDescriptor()
            }
        };

        var result = await controller.Preview(record.Id);
        await result.ExecuteResultAsync(controller.ControllerContext);

        Assert.Equal(200, http.Response.StatusCode);
        var evt = Assert.Single(_accessed);
        Assert.Equal(FileAccessType.Preview, evt.AccessType);
    }
}
