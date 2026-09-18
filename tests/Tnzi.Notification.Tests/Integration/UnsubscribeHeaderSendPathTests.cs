using Tnzi.Domain.Entities;
using Tnzi.Notification.Services.Internal;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 群发邮件在<b>真实发送路径</b>上确实带着 RFC 8058 的退订信头。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么必须是这一层</b>：`CreateUnsubscribeToken` 此前<b>全仓零生产调用方</b> ——
/// 令牌签发、匿名退订端点、邮件布局里那个 <c>UnsubscribeUrl</c> 插槽全都建好了，
/// 而没有任何一封信带过退订入口。判定规则由 <c>UnsubscribeHeadersTests</c> 覆盖，
/// 但这个缺陷的形态从来不是判定写错了。
/// </para>
/// <para>
/// ★ 每条「该带」都配一条「不该带」的对照。
/// </para>
/// </remarks>
public class UnsubscribeHeaderSendPathTests : IntegrationTestBase
{
    private const string Landing = "https://app.example.com/unsubscribe";
    private const string OneClick = "https://api.example.com/api/notifications/unsubscribe/one-click";

    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly NotificationOptions _options = new()
    {
        MaxConcurrency = 4,
        OptOut = new OptOutOptions
        {
            TokenSecret = "unit-test-secret-that-is-long-enough",
            LandingUrl = Landing,
            OneClickEndpoint = OneClick,
        },
    };

    /// <summary>最近一次投递收到的信头（无信头那条路径不会写这里）。</summary>
    private IReadOnlyDictionary<string, string>? _capturedHeaders;

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
        options.Setup(o => o.CurrentValue).Returns(_options);
        services.AddSingleton(_ => options.Object);

        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SendResult.CreateSuccess("stub-id"));
        _emailSender
            .Setup(s => s.SendToWithHeadersAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string? __, string ___, string ____, bool _____,
                       List<EmailAttachment>? ______, IReadOnlyDictionary<string, string>? headers, CancellationToken _______)
                => _capturedHeaders = headers)
            .ReturnsAsync(SendResult.CreateSuccess("stub-id"));

        services.AddSingleton(_ => _emailSender.Object);
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);
        services.AddSingleton(_ => new Mock<IFaxSender>().Object);

        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationProviderResolver, NotificationProviderResolver>();
        services.AddScoped<INotificationProviderSelector, DefaultNotificationProviderSelector>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    // ── 该带 ─────────────────────────────────────────────────────────────────

    /// <summary>★★ 一封群发邮件真的带上了 List-Unsubscribe。</summary>
    [Fact]
    public async Task AMarketingEmail_CarriesTheUnsubscribeHeader()
    {
        var messageId = await SeedMessageAsync(isTransactional: false);

        await SendAsync(messageId);

        _capturedHeaders.ShouldNotBeNull();
        _capturedHeaders!.ShouldContainKey(UnsubscribeHeaders.ListUnsubscribe);
        _capturedHeaders[UnsubscribeHeaders.ListUnsubscribePost].ShouldBe(UnsubscribeHeaders.OneClickValue);
    }

    /// <summary>
    /// ★ 信头里的令牌必须真的解析得回这个收件人 —— 否则那颗按钮点下去只会得到
    /// 「链接无效」，而这在收件人眼里与「退订坏了」没有区别。
    /// </summary>
    [Fact]
    public async Task TheTokenInTheHeader_ResolvesBackToThisRecipient()
    {
        var messageId = await SeedMessageAsync(isTransactional: false);
        await SendAsync(messageId);

        var token = ExtractToken(_capturedHeaders![UnsubscribeHeaders.ListUnsubscribe]);
        var payload = ServiceProvider.GetRequiredService<INotificationOptOutService>()
            .ResolveUnsubscribeToken(token);

        payload.ShouldNotBeNull();
        payload!.Address.ShouldBe("reader@example.com");
        payload.Channel.ShouldBe(NotificationType.Email);
    }

    // ── 不该带（对照）────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ 事务性消息不带。带了，收件人可以「退订」掉自己的密码重置邮件。
    /// </summary>
    [Fact]
    public async Task ATransactionalEmail_CarriesNoUnsubscribeHeader()
    {
        var messageId = await SeedMessageAsync(isTransactional: true);

        await SendAsync(messageId);

        _capturedHeaders.ShouldBeNull();
        VerifyPlainSend(Times.Once());
    }

    /// <summary>没配落地页的部署照常发信，只是不带信头（走原来那条方法）。</summary>
    [Fact]
    public async Task WithoutALandingUrl_TheSendStillGoesOutPlainly()
    {
        _options.OptOut.LandingUrl = null;
        var messageId = await SeedMessageAsync(isTransactional: false);

        await SendAsync(messageId);

        _capturedHeaders.ShouldBeNull();
        VerifyPlainSend(Times.Once());
    }

    /// <summary>
    /// ★★ 令牌签发失败（最常见是密钥没配，它刻意抛异常）<b>不能连累这封信</b>：
    /// 少一个退订按钮是可降级的，一封发不出去的通知不是。
    /// </summary>
    [Fact]
    public async Task WhenTheTokenCannotBeMinted_TheEmailIsStillSent()
    {
        _options.OptOut.TokenSecret = null;
        var messageId = await SeedMessageAsync(isTransactional: false);

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        _capturedHeaders.ShouldBeNull();
        VerifyPlainSend(Times.Once());
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private void VerifyPlainSend(Times times) => _emailSender.Verify(s => s.SendToAsync(
        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), times);

    private static string ExtractToken(string headerValue)
    {
        var marker = "token=";
        var start = headerValue.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = headerValue.IndexOf('>', start);
        return Uri.UnescapeDataString(headerValue[start..end]);
    }

    private async Task<Guid> SeedMessageAsync(bool isTransactional)
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
            Recipients = [new Recipient { Address = "reader@example.com", Status = NotificationStatus.Pending }],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }

    private Task<Result> SendAsync(Guid messageId)
        => ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);
}
