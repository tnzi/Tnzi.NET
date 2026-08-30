using System.Net.Mime;
using Tnzi.Domain.Entities;
using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// <see cref="NotificationType.Fax"/> 的消息在<b>真实发送路径</b>上确实走到了 <see cref="IFaxSender"/>。
/// </summary>
/// <remarks>
/// ★ <b>为什么必须是这一层</b>：给枚举加一个成员、写好一个考究的发送器，然后忘记在
/// <c>SendToRecipientAsync</c> 的 switch 上接线 —— 症状是每条传真都拿到
/// "Unsupported notification type"，而**所有纯函数测试全绿**。本模块 2026-08-08 已经在退订上
/// 踩过一模一样的形态（机制建好了没接上），所以这里断言的是"发送器有没有被调用"，
/// 而不是发送器自己的行为（那由 <c>FaxSenderTests</c> 覆盖）。
/// </remarks>
public class FaxSendPathTests : IntegrationTestBase
{
    private readonly Mock<IFaxSender> _faxSender = new();

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

        _faxSender
            .Setup(s => s.SendToAsync(It.IsAny<string>(), It.IsAny<EmailAttachment>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SendResult.CreateSuccess("fax-id"));

        services.AddSingleton(_ => new Mock<IEmailSender>().Object);
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);
        services.AddSingleton(_ => _faxSender.Object);

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

    private Task<Guid> SeedFaxAsync(params Attachment[] attachments)
        => SeedFaxAsync("+1 (905) 555-1234", isTransactional: true, attachments);

    private async Task<Guid> SeedFaxAsync(string address, bool isTransactional, params Attachment[] attachments)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "File 2026-118",
            Content = "This body is archive-only; a fax carries the PDF and nothing else.",
            Type = NotificationType.Fax,
            IsTransactional = isTransactional,
            Status = NotificationStatus.Pending,
            TotalRecipientCount = 1,
            Recipients = [new Recipient { Address = address, Status = NotificationStatus.Pending }],
            Attachments = attachments
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        return message.Id;
    }

    private Task OptOutAsync(string faxNumber)
        => ServiceProvider.GetRequiredService<INotificationOptOutService>()
            .OptOutAsync(faxNumber, NotificationType.Fax, category: null, source: "test");

    private void VerifyNothingWasFaxed()
        => _faxSender.Verify(
            s => s.SendToAsync(It.IsAny<string>(), It.IsAny<EmailAttachment>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private static Attachment Pdf(string fileName = "letter.pdf") => new()
    {
        FileName = fileName,
        FilePath = "https://files.example.com/" + fileName,
        ContentType = MediaTypeNames.Application.Pdf
    };

    private Task<Result> SendAsync(Guid messageId)
        => ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);

    /// <summary>★ 接线本身：Type = Fax 的消息真的走到了 <see cref="IFaxSender"/>。</summary>
    [Fact]
    public async Task AFaxNotification_ReachesTheFaxSender()
    {
        var messageId = await SeedFaxAsync(Pdf());

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        _faxSender.Verify(
            s => s.SendToAsync("+1 (905) 555-1234", It.IsAny<EmailAttachment>(), "File 2026-118", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>收件人地址是传真号码，附件是那一份 PDF，主题原样带过去。</summary>
    [Fact]
    public async Task AFaxNotification_PassesItsSingleAttachmentThrough()
    {
        EmailAttachment? handed = null;
        _faxSender
            .Setup(s => s.SendToAsync(It.IsAny<string>(), It.IsAny<EmailAttachment>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmailAttachment, string?, CancellationToken>((_, document, _, _) => handed = document)
            .ReturnsAsync(SendResult.CreateSuccess("fax-id"));

        var messageId = await SeedFaxAsync(Pdf("statement-of-claim.pdf"));

        await SendAsync(messageId);

        handed.ShouldNotBeNull();
        handed.FileName.ShouldBe("statement-of-claim.pdf");
        handed.ContentType.ShouldBe(MediaTypeNames.Application.Pdf);
    }

    /// <summary>
    /// ★ 附件不是恰好一个就当场失败。网关对多余附件的反应是安静丢掉或整份失败，
    /// 两种都不会有人告诉你 —— 所以要在交出去之前拦下，并直说该怎么办（先合并成一个 PDF）。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AFaxNotification_WithoutExactlyOneAttachment_FailsBeforeReachingTheGateway(int count)
    {
        var attachments = Enumerable.Range(0, count).Select(i => Pdf($"page-{i}.pdf")).ToArray();
        var messageId = await SeedFaxAsync(attachments);

        await SendAsync(messageId);

        VerifyNothingWasFaxed();

        DbContext.ChangeTracker.Clear();
        var recipient = await DbContext.Recipients.AsNoTracking().SingleAsync(r => r.MessageId == messageId);
        recipient.Status.ShouldBe(NotificationStatus.Failed);
        recipient.FailureReason!.ShouldContain("exactly one PDF");
    }

    #region 退订：号码的两种写法是同一个人

    /// <summary>
    /// ★ 用 <c>9055551234</c> 退订，用 <c>+1 (905) 555-1234</c> 发，一样拦得住。
    /// </summary>
    /// <remarks>
    /// 传真的地址是电话号码，同一个号码有好几种同样正常的写法。退订表若只做去空白加小写，
    /// 两种写法就是两条互不相干的记录 —— 收件人点了退订、拿到 200，然后照收不误，
    /// 没有报错、没有退信，日志里是一次正常投递。归一化用的是发送时的同一个 <c>FaxNumber</c>。
    /// </remarks>
    [Fact]
    public async Task AFaxNumberOptedOutInAnotherFormat_IsStillBlocked()
    {
        var messageId = await SeedFaxAsync("+1 (905) 555-1234", isTransactional: false, Pdf());
        await OptOutAsync("9055551234");

        await SendAsync(messageId);

        VerifyNothingWasFaxed();

        DbContext.ChangeTracker.Clear();
        var recipient = await DbContext.Recipients.AsNoTracking().SingleAsync(r => r.MessageId == messageId);
        recipient.Status.ShouldBe(NotificationStatus.Cancelled);
    }

    /// <summary>
    /// ★★ 别人的退订不能拦下这一份：归一化只用来比对，<b>放行名单还回来的必须是原样地址</b>。
    /// </summary>
    /// <remarks>
    /// 这条守的是修复本身最容易踩空的那一脚：<c>FilterAllowedAsync</c> 若把归一化后的
    /// <c>9055559999</c> 还给调用方，而收件人记的是 <c>+1 (905) 555-9999</c>，两边对不上号，
    /// 于是**每一份**传真都被当成"已退订"全部取消 —— 而没有任何一步失败过，
    /// 列表里只是显示 Cancelled。
    /// </remarks>
    [Fact]
    public async Task AnotherNumbersOptOut_DoesNotBlockThisFax()
    {
        var messageId = await SeedFaxAsync("+1 (905) 555-9999", isTransactional: false, Pdf());
        await OptOutAsync("9055551234");

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        _faxSender.Verify(
            s => s.SendToAsync("+1 (905) 555-9999", It.IsAny<EmailAttachment>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
