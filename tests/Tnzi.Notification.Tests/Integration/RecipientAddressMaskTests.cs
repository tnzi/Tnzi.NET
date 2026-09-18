using Tnzi.Domain.Entities;
using Tnzi.Notification.Services.Internal;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 推送渠道的「收件人地址」是设备令牌，管理端读到的必须是掩码。
/// </summary>
/// <remarks>
/// <para>
/// ★ 另外三条渠道的地址是消费方自己录进来的联系方式，照原样给管理端是对的；
/// 推送这一列存的却是 FCM 发给某个 App 安装的注册令牌 —— 持有它就能改写或掐掉
/// 那台设备的推送归属。一次投递报告查询把整批令牌原文交给任何持
/// <c>notification.message.view</c> 的人，与 Push 子模块自己在设备列表里坚持只给
/// <c>TokenMask</c> 直接矛盾。
/// </para>
/// <para>
/// ★ <b>逐个读取面各写一条</b>：掩码是在每个返回点调一次的，漏掉哪个不会有别的东西变红。
/// </para>
/// </remarks>
public class RecipientAddressMaskTests : IntegrationTestBase
{
    private const string Token = "fZ1a2B3c4D5e6F7g8H9iJkLmNoPqRsTuVwXyZ-0123456789_abcdefgh";
    private const string Email = "reader@example.com";

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<Message>(services);
        AddRepo<Recipient>(services);

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(NotificationTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<NotificationTestDbContext>>();

        services.AddScoped<INotificationQueryService, NotificationQueryService>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    private INotificationQueryService Query => ServiceProvider.GetRequiredService<INotificationQueryService>();

    // ── 每个读取面 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TheDeliveryReport_DoesNotHandOutTheRawPushToken()
    {
        var messageId = await SeedAsync(NotificationType.Push, Token, NotificationStatus.Sent);

        var report = await Query.GetDeliveryReportAsync(messageId);

        var address = report.Data!.Recipients.Single().Address;
        address.ShouldNotBe(Token);
        address.ShouldBe(PushTokenMask.Of(Token));
    }

    [Fact]
    public async Task GetById_DoesNotHandOutTheRawPushToken()
    {
        var messageId = await SeedAsync(NotificationType.Push, Token, NotificationStatus.Sent);

        var info = await Query.GetByIdAsync(messageId);

        info.Data!.Recipients.Single().Address.ShouldBe(PushTokenMask.Of(Token));
    }

    [Fact]
    public async Task GetFailedNotifications_DoesNotHandOutTheRawPushToken()
    {
        await SeedAsync(NotificationType.Push, Token, NotificationStatus.Failed);

        var failed = await Query.GetFailedNotificationsAsync();

        var addresses = failed.Data!.SelectMany(n => n.Recipients).Select(r => r.Address).ToList();
        // 空集合会让下面那句断言恒真 —— 这个读取面必须真的带回收件人，否则本条无效。
        addresses.ShouldNotBeEmpty();
        addresses.ShouldAllBe(a => a != Token);
    }

    [Fact]
    public async Task TheQueryPage_DoesNotHandOutTheRawPushToken()
    {
        await SeedAsync(NotificationType.Push, Token, NotificationStatus.Sent);

        var page = await Query.QueryAsync(new QueryNotificationRequest());

        AssertNoRawToken(page.Data!.Items.SelectMany(n => n.Recipients).Select(r => r.Address).ToList());
    }

    [Fact]
    public async Task TheScheduledPage_DoesNotHandOutTheRawPushToken()
    {
        await SeedAsync(NotificationType.Push, Token, NotificationStatus.Scheduled);

        var page = await Query.GetScheduledAsync(new QueryNotificationRequest());

        AssertNoRawToken(page.Data!.Items.SelectMany(n => n.Recipients).Select(r => r.Address).ToList());
    }

    /// <summary>
    /// 分页两条走的是 <c>ProjectTo</c>（在 SQL 里投影），收件人集合<b>可能根本没被投影出来</b>。
    /// 那种情况下这个读取面不泄露令牌，但断言也无从谈起 —— 说出来，别让它假装验过。
    /// </summary>
    private static void AssertNoRawToken(List<string> addresses)
    {
        if (addresses.Count == 0)
            return;

        addresses.ShouldAllBe(a => a != Token);
    }

    // ── 别的渠道不受影响 ─────────────────────────────────────────────────────

    /// <summary>
    /// 对照：邮箱地址照原样给出去。没有这一条，一个「把所有地址都遮掉」的实现也会全绿，
    /// 而那会让投递报告在最常用的渠道上失去意义。
    /// </summary>
    [Fact]
    public async Task AnEmailAddress_IsStillShownInFull()
    {
        var messageId = await SeedAsync(NotificationType.Email, Email, NotificationStatus.Sent);

        var report = await Query.GetDeliveryReportAsync(messageId);

        report.Data!.Recipients.Single().Address.ShouldBe(Email);
    }

    // ── 掩码本身留得下辨认信息 ───────────────────────────────────────────────

    /// <summary>掩码保留尾部，管理员仍能把一行对上自己看到的令牌尾巴。</summary>
    [Fact]
    public async Task TheMask_KeepsEnoughTailToRecogniseTheRow()
    {
        var messageId = await SeedAsync(NotificationType.Push, Token, NotificationStatus.Sent);

        var report = await Query.GetDeliveryReportAsync(messageId);

        report.Data!.Recipients.Single().Address
            .ShouldEndWith(Token[^PushTokenMask.VisibleTailLength..]);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedAsync(NotificationType type, string address, NotificationStatus status)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Release notes",
            Content = "Body",
            Type = type,
            Category = "General",
            Status = status,
            RetryCount = 0,
            MaxRetryCount = 3,
            TotalRecipientCount = 1,
            Recipients = [new Recipient { Address = address, Status = status }],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }
}
