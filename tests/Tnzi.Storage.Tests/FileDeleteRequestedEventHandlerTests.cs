using Tnzi.Storage.Events;
using Tnzi.Storage.Events.Handlers;

namespace Tnzi.Storage.Tests;

/// <summary>
/// <see cref="FileDeleteRequestedEventHandler"/>：删除失败必须以异常离开，事件总线的重试与死信才会发生。
/// </summary>
/// <remarks>
/// 换版本后的旧缩略图只剩这一条事件指着它 —— 记录已不再引用它，孤儿清理按记录枚举找不到它。
/// 处理器吞掉异常 = 告诉总线「成功」= 一次瞬时故障变成永久孤儿对象。
/// </remarks>
public class FileDeleteRequestedEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenADeleteFails_Throws_AfterTryingBothObjects()
    {
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.DeleteAsync("files/a.pdf")).ThrowsAsync(new IOException("provider unavailable"));
        storage.Setup(s => s.DeleteAsync("thumb/a.pdf")).ReturnsAsync(true);
        var handler = new FileDeleteRequestedEventHandler(storage.Object, NullLogger<FileDeleteRequestedEventHandler>.Instance);

        var ex = await Assert.ThrowsAsync<AggregateException>(() => handler.HandleAsync(new FileDeleteRequestedEvent
        {
            FileId = Guid.NewGuid(),
            FilePath = "files/a.pdf",
            ThumbnailPath = "thumb/a.pdf",
            Provider = "Local"
        }));

        Assert.IsType<IOException>(Assert.Single(ex.InnerExceptions));
        // 正文删失败不能连带缩略图一次都没试
        storage.Verify(s => s.DeleteAsync("thumb/a.pdf"), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ThumbnailOnly_FailureThrows()
    {
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.DeleteAsync(It.IsAny<string>())).ThrowsAsync(new IOException("provider unavailable"));
        var handler = new FileDeleteRequestedEventHandler(storage.Object, NullLogger<FileDeleteRequestedEventHandler>.Instance);

        await Assert.ThrowsAsync<AggregateException>(() => handler.HandleAsync(new FileDeleteRequestedEvent
        {
            FileId = Guid.NewGuid(),
            ThumbnailPath = "thumb/stale.jpg",
            Provider = "Local"
        }));
    }

    [Fact]
    public async Task HandleAsync_WhenBothDeletesSucceed_DoesNotThrow()
    {
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);
        var handler = new FileDeleteRequestedEventHandler(storage.Object, NullLogger<FileDeleteRequestedEventHandler>.Instance);

        await handler.HandleAsync(new FileDeleteRequestedEvent
        {
            FileId = Guid.NewGuid(),
            FilePath = "files/a.pdf",
            ThumbnailPath = "thumb/a.pdf",
            Provider = "Local"
        });

        storage.Verify(s => s.DeleteAsync("files/a.pdf"), Times.Once);
        storage.Verify(s => s.DeleteAsync("thumb/a.pdf"), Times.Once);
    }
}
