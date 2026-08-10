using Tnzi.Security.Authorization;
using Tnzi.Security.Claims;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 一条通知偏好只该被<b>它的主人</b>和管理台删掉。
/// </summary>
/// <remarks>
/// <para>
/// <c>DeletePreferenceAsync(id)</c> 同时挂在用户面（<c>DELETE /notification-preferences/{id}</c>，
/// 只有 <c>[ApiAuthorize]</c>）与管理端（带 <c>notification.subscription.delete</c>）两个端点上 ——
/// 它<b>无从知道谁在问</b>，所以判定必须在服务里做。
/// </para>
/// <para>
/// ★ 少了它的后果不是「看到别人的数据」而是<b>替别人改回默认</b>：任何已登录用户删掉他人的偏好行，
/// 对方刚关掉的那个渠道就悄悄恢复成默认（发送）。这直接抵消了同一个模块 2026-08-08 刚接上的
/// 退订链路 —— <b>一个可以被别人撤销的「我不想收」等于没有</b>。
/// </para>
/// <para>
/// ★ 原实现连 <c>preference.UserId</c> 都写进了日志，也就是说它知道这行有主人，只是从来没比对过。
/// </para>
/// </remarks>
public class NotificationPreferenceOwnershipTests
{
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SomeoneElse = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task Delete_MyOwnPreference_Succeeds()
    {
        var (service, repository) = Build(ownerUserId: Me, grantAdminCode: false, out var preference);

        var result = await service.DeletePreferenceAsync(preference.Id);

        result.Succeeded.ShouldBeTrue(result.Message);
        repository.Verify(r => r.DeleteAsync(preference, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>别人的偏好按 <b>404</b> 出 —— 与「不存在」不可区分，不泄漏 id 是否真的存在。</summary>
    [Fact]
    public async Task Delete_SomeoneElsesPreference_Is404_AndNothingIsDeleted()
    {
        var (service, repository) = Build(ownerUserId: SomeoneElse, grantAdminCode: false, out var preference);

        var result = await service.DeletePreferenceAsync(preference.Id);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        repository.Verify(r => r.DeleteAsync(It.IsAny<Preference>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>对照：管理台（持 <c>notification.subscription.delete</c>）照常能删任何人的。</summary>
    [Fact]
    public async Task Delete_SomeoneElsesPreference_SucceedsForTheAdminConsole()
    {
        var (service, repository) = Build(ownerUserId: SomeoneElse, grantAdminCode: true, out var preference);

        var result = await service.DeletePreferenceAsync(preference.Id);

        result.Succeeded.ShouldBeTrue(result.Message);
        repository.Verify(r => r.DeleteAsync(preference, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>不存在的 id 仍然是 404（守卫不得把「找不到」变成别的东西）。</summary>
    [Fact]
    public async Task Delete_MissingPreference_Is404()
    {
        var repository = new Mock<IRepository<Preference, Guid>>();
        repository.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Preference?)null);
        var service = new NotificationPreferenceService(repository.Object, BuildProvider(grantAdminCode: false));

        var result = await service.DeletePreferenceAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    private static (NotificationPreferenceService Service, Mock<IRepository<Preference, Guid>> Repository) Build(
        Guid ownerUserId, bool grantAdminCode, out Preference preference)
    {
        preference = new Preference
        {
            Id = Guid.NewGuid(),
            UserId = ownerUserId,
            Channel = "Email",
            IsEnabled = false          // 主人刚关掉这个渠道
        };

        var repository = new Mock<IRepository<Preference, Guid>>();
        var row = preference;
        repository.Setup(r => r.GetAsync(row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        repository.Setup(r => r.DeleteAsync(It.IsAny<Preference>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return (new NotificationPreferenceService(repository.Object, BuildProvider(grantAdminCode)), repository);
    }

    private static IServiceProvider BuildProvider(bool grantAdminCode)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.Id).Returns(Me);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);

        var checker = new Mock<IPermissionChecker>();
        checker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);
        if (grantAdminCode)
            checker.Setup(p => p.IsGrantedAsync("notification.subscription.delete")).ReturnsAsync(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => currentUser.Object);
        services.AddScoped(_ => checker.Object);
        return services.BuildServiceProvider();
    }
}
