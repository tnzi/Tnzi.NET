using Tnzi.Identity.Events;
using Tnzi.Identity.Events.Handlers;

namespace Tnzi.Identity.Tests;

/// <summary>
/// UserLoggedInEventHandler 测试 —— 处理器现在只负责登录日志记录。
/// 会话创建已移至同步路径（LoginSessionCoordinator），因为会话ID要在签发令牌前拿到
/// 写入 session_id claim 并绑定刷新令牌；处理器不再触碰会话服务。
/// </summary>
public class UserLoggedInEventHandlerTests
{
    private static UserLoggedInEvent CreateEvent() => new()
    {
        UserId = Guid.NewGuid(),
        UserName = "tester",
        LoginTime = DateTime.UtcNow,
        IpAddress = "127.0.0.1",
        UserAgent = "Test Browser"
    };

    [Fact]
    public async Task HandleAsync_WritesSuccessLoginLog()
    {
        var logMock = new Mock<ILoginLogInternalService>();
        var handler = new UserLoggedInEventHandler(logMock.Object);
        var @event = CreateEvent();

        await handler.HandleAsync(@event);

        logMock.Verify(x => x.LogAsync(
            @event.UserId,
            @event.UserName,
            @event.IpAddress,
            @event.UserAgent,
            Tnzi.Identity.Entities.LoginStatus.Success,
            null), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithoutLogService_DoesNotThrow()
    {
        var handler = new UserLoggedInEventHandler();

        // No log service registered → handler is a no-op, must not throw.
        await handler.HandleAsync(CreateEvent());
    }
}
