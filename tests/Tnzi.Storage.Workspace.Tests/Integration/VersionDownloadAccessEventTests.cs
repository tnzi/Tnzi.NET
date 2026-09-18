using Tnzi.EventBus;
using Tnzi.Storage.Events;

namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 历史版本下载（<c>GET files/{id}/versions/{version}/download</c>）是一次完整内容读取，
/// 必须与 <c>download</c> 路由一样发出 <see cref="FileAccessType.Download"/> 的 <see cref="FileAccessedEvent"/>。
/// </summary>
/// <remarks>
/// 此前 <c>GetVersionContentAsync</c> 直接找 provider 取字节、一条事件都不发 —— 与同日修掉的
/// <c>files/preview/{id}/preview</c> 同一形状：挂在这个事件上的审计 / 配额看不到任何一次版本下载，
/// 而 v1 是当前内容的快照，等于当前内容也能被无声读走。
/// </remarks>
public class VersionDownloadAccessEventTests : WorkspaceIntegrationTestBase
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
    public async Task GetVersionContentAsync_PublishesDownloadForTheRequestedVersion()
    {
        var service = CreateVersionService();
        var file = await CreateStoredFileAsync("versioned.txt", "v1-content"u8.ToArray());
        var created = await service.CreateVersionAsync(file.Id, new MemoryStream("v2-content"u8.ToArray()));
        Assert.True(created.Succeeded, created.Message);

        var result = await service.GetVersionContentAsync(file.Id, 1);

        Assert.True(result.Succeeded, result.Message);
        await using var _ = result.Data!;
        var evt = Assert.Single(_accessed);
        Assert.Equal(file.Id, evt.FileId);
        Assert.Equal(FileAccessType.Download, evt.AccessType);
        Assert.Equal(1, evt.Version);
    }

    [Fact]
    public async Task GetVersionContentAsync_PublishesNothing_WhenTheVersionDoesNotExist()
    {
        // 事件只在字节真的交出去之后才发：404 不是一次访问。
        var service = CreateVersionService();
        var file = await CreateStoredFileAsync("versioned.txt", "v1"u8.ToArray());

        var result = await service.GetVersionContentAsync(file.Id, 99);

        Assert.False(result.Succeeded);
        Assert.Empty(_accessed);
    }
}
