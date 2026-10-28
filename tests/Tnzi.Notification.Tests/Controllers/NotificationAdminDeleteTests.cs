using Tnzi.Notification.Controllers.Admin;

namespace Tnzi.Notification.Tests.Controllers;

/// <summary>
/// <c>DELETE admin/notifications/{id}</c> 是删除，不是取消。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：这个端点挂着 <c>notification.message.delete</c>、<c>@tnzi/core</c> 把它叫作「Delete notification」，
/// 调的却是 <c>CancelAsync</c>。删一条已发出的消息答「已发送、不能取消」的 400；删一条待发的只是取消、照样留在列表里；
/// 只有 <c>.delete</c> 的角色借它就能取消群发，而取消本该要 <c>.update</c>。
/// </remarks>
public class NotificationAdminDeleteTests
{
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<INotificationQueryService> _queryService = new();
    private readonly DefaultNotificationAdminController _controller;

    public NotificationAdminDeleteTests()
    {
        _controller = new DefaultNotificationAdminController(
            _notificationService.Object, _queryService.Object, Mock.Of<INotificationRetryService>());
    }

    [Fact]
    public async Task Delete_SoftDeletesThroughTheBatchDeletePath_AndNeverCancels()
    {
        var id = Guid.NewGuid();
        _queryService.Setup(s => s.BatchDeleteAsync(It.Is<List<Guid>>(ids => ids.Count == 1 && ids[0] == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(1, "1 notifications deleted"));

        var result = await _controller.Delete(id);

        result.Succeeded.ShouldBeTrue();
        _queryService.Verify(s => s.BatchDeleteAsync(It.Is<List<Guid>>(ids => ids.Single() == id), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(s => s.CancelAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_OfAnUnknownId_AnswersNotFound()
    {
        _queryService.Setup(s => s.BatchDeleteAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(0, "No notifications found to delete"));

        var result = await _controller.Delete(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }
}
