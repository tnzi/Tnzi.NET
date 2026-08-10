using Tnzi.Domain.Entities;
using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 用户通知偏好在<b>真实发送路径</b>上确实生效：真库、真
/// <see cref="NotificationPreferenceService"/>、真 <see cref="NotificationService.SendAsync"/>。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么必须是这一层</b>：判定规则本身由 <c>PreferenceEnforcementTests</c>（纯函数）覆盖，
/// 但这个缺陷的形态<b>从来不是判定写错了</b> —— <c>IsChannelEnabledAsync</c> 全仓零调用方。
/// 用户在设置里把邮件通知关掉、界面显示已关闭，然后照收不误。
/// 只测纯函数就等于把 2026-08-08 退订那个坑重挖一遍。
/// </para>
/// <para>
/// 所以这里刻意<b>不 mock 偏好服务</b>：偏好行经真实服务写进真实的库，
/// 再从真实的 <c>SendAsync</c> 观察结果。断言落在「邮件发送器有没有被调用」上 ——
/// 那是收件人真正在意的那件事。
/// </para>
/// <para>
/// ★ 每条「不该发」都配一条「应该发」的对照：只写否定断言看不出自己在验一个
/// 从没被调用过的方法（退订那轮就是这么把断言挂错了方法名而空洞通过的）。
/// </para>
/// </remarks>
public class PreferenceSendPathTests : IntegrationTestBase
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Mock<IEmailSender> _emailSender = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<Message>(services);
        AddRepo<Recipient>(services);
        AddRepo<OptOut>(services);
        AddRepo<Preference>(services);

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(NotificationTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<NotificationTestDbContext>>();

        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions { MaxConcurrency = 4 });
        services.AddSingleton(_ => options.Object);

        // NotificationService 走 SendToAsync，不是 SendAsync(EmailMessage)。
        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SendResult.CreateSuccess("stub-id"));
        services.AddSingleton(_ => _emailSender.Object);
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);

        // 两个都用真实实现
        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddScoped<INotificationService, NotificationService>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    // ── 不该发 ────────────────────────────────────────────────────────────────

    /// <summary>★ 本人关掉邮件渠道之后，真的不会被发送。</summary>
    [Fact]
    public async Task AUserWhoDisabledTheChannel_IsNotActuallySentTo()
    {
        await DisableAsync(channel: "Email");
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        _emailSender.Verify(s => s.SendToAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 被拦下的收件人标 <c>Cancelled</c> 并写明原因，而且**真的落库**。
    /// </summary>
    /// <remarks>
    /// 必须 <c>AsNoTracking</c> + 清跟踪器才是真探针：读同一个 DbContext 里那个被改过的实例，
    /// 落没落库根本看不出来（退订那轮在自己新写的测试上重犯过这一条）。
    /// </remarks>
    [Fact]
    public async Task TheBlockedMarking_IsPersisted()
    {
        await DisableAsync(channel: "Email");
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        await SendAsync(messageId);

        DbContext.ChangeTracker.Clear();
        var recipient = await DbContext.Set<Recipient>().AsNoTracking()
            .FirstAsync(r => r.MessageId == messageId);
        recipient.Status.ShouldBe(NotificationStatus.Cancelled);
        recipient.FailureReason.ShouldBe(PreferenceRecipientFilter.DisabledByPreferenceReason);
    }

    /// <summary>分类级偏好优先于渠道级：关掉 marketing 不影响别的分类。</summary>
    [Fact]
    public async Task DisablingADifferentCategory_DoesNotBlockThisOne()
    {
        await DisableAsync(channel: "Email", category: "Newsletter");
        var messageId = await SeedMessageAsync(Alice, isTransactional: false, category: "Marketing");

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>关掉别的渠道不影响邮件（偏好词汇比 NotificationType 宽，InApp 是合法取值）。</summary>
    [Fact]
    public async Task DisablingAnotherChannel_DoesNotBlockEmail()
    {
        await DisableAsync(channel: "InApp");
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    // ── 应该发（对照）────────────────────────────────────────────────────────

    /// <summary>★★ 事务性消息照常送达 —— 没有这条豁免，关掉邮件的人再也登不进来。</summary>
    [Fact]
    public async Task ATransactionalMessage_StillReachesAUserWhoDisabledTheChannel()
    {
        await DisableAsync(channel: "Email");
        var messageId = await SeedMessageAsync(Alice, isTransactional: true);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>没有任何偏好行 = 默认启用（绝不能是「查不到就不发」）。</summary>
    [Fact]
    public async Task AUserWithNoPreferenceRow_IsSentToNormally()
    {
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>显式打开也照常发（不能只认「有行就拦」）。</summary>
    [Fact]
    public async Task AUserWhoExplicitlyEnabledTheChannel_IsSentToNormally()
    {
        await SetPreferenceAsync(channel: "Email", enabled: true);
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>
    /// ★ 没有 <c>UserId</c> 的收件人不受偏好影响 —— 偏好按人、退订按地址。
    /// </summary>
    /// <remarks>
    /// 把「查不到偏好」当成「已关闭」会让整份导入名单一条都发不出去。
    /// 这里 Alice 关掉了邮件，但这条消息发给一个纯地址收件人，必须照常送达。
    /// </remarks>
    [Fact]
    public async Task AnAddressOnlyRecipient_IsUnaffectedByAnyonesPreferences()
    {
        await DisableAsync(channel: "Email");
        var messageId = await SeedMessageAsync(userId: null, isTransactional: false);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    // ── 重发路径 ──────────────────────────────────────────────────────────────

    /// <summary>重发路径同样受偏好约束（否则那是一条绕过偏好的后门）。</summary>
    [Fact]
    public async Task ThePreferenceIsHonouredOnTheResendPathToo()
    {
        var messageId = await SeedMessageAsync(Alice, isTransactional: false, recipientStatus: NotificationStatus.Failed);
        await DisableAsync(channel: "Email");

        var resent = await ServiceProvider.GetRequiredService<INotificationService>()
            .ResendToFailedRecipientsAsync(messageId);

        resent.Succeeded.ShouldBeTrue(resent.Message);
        _emailSender.Verify(s => s.SendToAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 夹具 ──────────────────────────────────────────────────────────────────

    private void VerifySent(Times times) => _emailSender.Verify(s => s.SendToAsync(
        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), times);

    private Task DisableAsync(string channel, string? category = null)
        => SetPreferenceAsync(channel, enabled: false, category);

    private async Task SetPreferenceAsync(string channel, bool enabled, string? category = null)
    {
        var result = await ServiceProvider.GetRequiredService<INotificationPreferenceService>()
            .SetPreferenceAsync(Alice, new SetNotificationPreferenceDto
            {
                Channel = channel,
                Category = category,
                IsEnabled = enabled,
            });
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    private async Task<Guid> SeedMessageAsync(
        Guid? userId, bool isTransactional, string category = "Marketing",
        NotificationStatus recipientStatus = NotificationStatus.Pending)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Spring sale",
            Content = "Body",
            Type = NotificationType.Email,
            Category = category,
            IsTransactional = isTransactional,
            Status = NotificationStatus.Pending,
            TotalRecipientCount = 1,
            Recipients = [new Recipient { Address = "alice@example.com", UserId = userId, Status = recipientStatus }]
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }

    private Task<Result> SendAsync(Guid messageId)
        => ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);
}
