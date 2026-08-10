using Tnzi.Domain.Entities;
using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 退订在<b>真实发送路径</b>上确实生效：真库、真 <see cref="NotificationOptOutService"/>、
/// 真 <see cref="NotificationService.SendAsync"/>。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么必须是这一层</b>：判定规则本身由 <c>OptOutEnforcementTests</c>（纯函数）覆盖，
/// 但这个缺陷的形态<b>从来不是判定写错了</b> —— 是判定压根没人调用。
/// 只测纯函数就等于把同一个坑重挖一遍：一个考究、正确、无人问津的过滤器。
/// </para>
/// <para>
/// 所以这里刻意<b>不 mock 退订服务</b>：退订记录经真实服务写进真实的库，
/// 再从真实的 <c>SendAsync</c> 观察结果。断言落在"邮件发送器有没有被调用"上 ——
/// 那是收件人真正在意的那件事。
/// </para>
/// </remarks>
public class OptOutSendPathTests : IntegrationTestBase
{
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

        // ★ 必须挂在 SendToAsync 上：NotificationService 走的是它，不是 SendAsync(EmailMessage)。
        // 头一版断言挂错了方法，于是两条"不该发"的用例**空洞地通过** —— 是同批"应该发"的
        // 对照用例把它揪出来的。只写"不该发"的断言，看不出自己在验一个从没被调用过的方法。
        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SendResult.CreateSuccess("stub-id"));
        services.AddSingleton(_ => _emailSender.Object);
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);

        // 真实的退订服务，不是替身
        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();
        // 偏好服务也用真实实现：本文件不种任何偏好行，而「无偏好 = 默认启用」，
        // 所以这些用例的结论不受它影响 —— 用替身反而会掩盖两道过滤器串起来时的问题。
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

    private async Task<Guid> SeedMessageAsync(string address, bool isTransactional)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Spring sale",
            Content = "Body",
            Type = NotificationType.Email,
            Category = "Marketing",
            IsTransactional = isTransactional,
            Status = NotificationStatus.Pending,
            TotalRecipientCount = 1,
            Recipients = [new Recipient { Address = address, Status = NotificationStatus.Pending }]
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        return message.Id;
    }

    private Task OptOutAsync(string address, string? category = null)
        => ServiceProvider.GetRequiredService<INotificationOptOutService>()
            .OptOutAsync(address, NotificationType.Email, category, source: "test");

    private Task<Result> SendAsync(Guid messageId)
        => ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);

    /// <summary>
    /// ★ 退订过的地址不会真的收到邮件。
    /// </summary>
    /// <remarks>
    /// 修复前这条会失败：退订记录写进去了，发送路径从不查它，邮件照发。
    /// </remarks>
    [Fact]
    public async Task AnOptedOutAddress_IsNotActuallySentTo()
    {
        var messageId = await SeedMessageAsync("gone@example.com", isTransactional: false);
        await OptOutAsync("gone@example.com");

        await SendAsync(messageId);

        _emailSender.Verify(
            s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "收件人点过退订，这封邮件就不该真的发出去");
    }

    /// <summary>
    /// 拦下之后留痕：标 Cancelled 并写明原因，不是从记录里消失。
    /// </summary>
    /// <remarks>
    /// 合规场景要的是"证明没有发过"。而且刻意不是 <c>Failed</c> ——
    /// 那会被 <c>ResendToFailedRecipientsAsync</c> 捞回来重发。
    /// </remarks>
    [Fact]
    public async Task AnOptedOutRecipient_IsRecordedAsCancelled()
    {
        var messageId = await SeedMessageAsync("gone@example.com", isTransactional: false);
        await OptOutAsync("gone@example.com");

        await SendAsync(messageId);

        // 探针：绕开身份映射读回真正落库的那一行
        var persisted = await DbContext.Recipients.AsNoTracking()
            .FirstAsync(r => r.Address == "gone@example.com");
        persisted.Status.ShouldBe(NotificationStatus.Cancelled);
        persisted.FailureReason.ShouldBe(OptOutRecipientFilter.OptedOutReason);
    }

    /// <summary>
    /// ★ 全员退订时消息不能报「已发送」。
    /// </summary>
    /// <remarks>
    /// 一封谁也没收到的消息在列表里显示 <c>Sent</c>，正是这轮修复要终结的那种会被当真的谎。
    /// 「本来就没有待发收件人」（例如全部已发过）仍照旧算 <c>Sent</c> —— 两者不是一回事。
    /// </remarks>
    [Fact]
    public async Task WhenEveryRecipientOptedOut_TheMessageIsNotReportedAsSent()
    {
        var messageId = await SeedMessageAsync("gone@example.com", isTransactional: false);
        await OptOutAsync("gone@example.com");

        await SendAsync(messageId);

        var message = await DbContext.Messages.AsNoTracking().FirstAsync(m => m.Id == messageId);
        message.Status.ShouldBe(NotificationStatus.Cancelled,
            "谁也没收到却显示已发送，等于把这轮要修的谎换了个地方讲");
    }

    /// <summary>
    /// ★ 退订标记要真的落库，不能只活在被跟踪的实体里。
    /// </summary>
    /// <remarks>
    /// 两条调用路径都有「过滤完什么都不剩 → 提前 return」的分支：<c>SendAsync</c> 那条只
    /// <c>UpdateAsync</c> 不 <c>SaveChanges</c>，<c>ResendToFailedRecipientsAsync</c> 那条
    /// <b>两样都没有</b>。断言必须绕开身份映射（<c>AsNoTracking</c>），
    /// 否则读到的是内存里那个被改过的实例，落没落库根本看不出来。
    /// </remarks>
    [Fact]
    public async Task TheOptedOutMarking_SurvivesOnTheResendPathToo()
    {
        var messageId = await SeedMessageAsync("gone@example.com", isTransactional: false);

        // 先制造一个 Failed 收件人：重发路径只看 Failed
        var seeded = await DbContext.Recipients.FirstAsync(r => r.Address == "gone@example.com");
        seeded.Status = NotificationStatus.Failed;
        await DbContext.SaveChangesAsync();

        await OptOutAsync("gone@example.com");

        var resent = await ServiceProvider.GetRequiredService<INotificationService>()
            .ResendToFailedRecipientsAsync(messageId);
        resent.Succeeded.ShouldBeTrue(resent.Message);
        resent.Data.ShouldBe(0);

        DbContext.ChangeTracker.Clear();
        var persisted = await DbContext.Recipients.AsNoTracking()
            .FirstAsync(r => r.Address == "gone@example.com");
        persisted.Status.ShouldBe(NotificationStatus.Cancelled,
            "重发路径的早退分支既不 Update 也不 Save，标记会随请求一起消失");
    }

    /// <summary>
    /// ★ 事务性消息照发 —— 退订不该让人再也收不到验证码。
    /// </summary>
    [Fact]
    public async Task ATransactionalMessage_StillReachesAnOptedOutAddress()
    {
        var messageId = await SeedMessageAsync("gone@example.com", isTransactional: true);
        await OptOutAsync("gone@example.com");

        await SendAsync(messageId);

        _emailSender.Verify(
            s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "密码重置 / 验证码这类消息不受退订约束，否则退订等于把人锁在门外");
    }

    /// <summary>
    /// 没退订的人照常收到 —— 防止把守卫做成"谁都发不出去"。
    /// </summary>
    [Fact]
    public async Task AnAddressThatNeverOptedOut_IsSentToNormally()
    {
        var messageId = await SeedMessageAsync("fine@example.com", isTransactional: false);

        await SendAsync(messageId);

        _emailSender.Verify(
            s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// 分类退订只挡那个分类。
    /// </summary>
    [Fact]
    public async Task OptingOutOfADifferentCategory_DoesNotBlockThisOne()
    {
        var messageId = await SeedMessageAsync("fine@example.com", isTransactional: false);
        await OptOutAsync("fine@example.com", category: "Newsletter");

        await SendAsync(messageId);

        _emailSender.Verify(
            s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "退的是 Newsletter，这条是 Marketing");
    }
}
