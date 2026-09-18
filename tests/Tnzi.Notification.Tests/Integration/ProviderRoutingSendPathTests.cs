using Tnzi.Domain.Entities;
using Tnzi.Notification.Extensions;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 按消息选服务商在<b>真实发送路径</b>上确实生效：真库、真 <see cref="NotificationProviderResolver"/>、
/// 真 <see cref="NotificationService"/> 的创建与 <c>SendAsync</c>。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么必须是这一层</b>：键的收口、解析器的取法各有纯函数测试，但这个特性会坏掉的形态
/// 不是它们写错了 —— 是键没落库、或派发时没按键取。这里断言的是「哪一个发送器被调用了」，
/// 那是调用方指定服务商时真正在意的那件事；另一个发送器<b>没被调用</b>同样要断言，
/// 否则「两家都发了一遍」这种失效看起来是绿的。
/// </para>
/// </remarks>
public class ProviderRoutingSendPathTests : IntegrationTestBase
{
    private const string Marketing = "marketing";

    private readonly Mock<IEmailSender> _defaultEmail = new();
    private readonly Mock<IEmailSender> _marketingEmail = new();
    private readonly StubSelector _selector = new();

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

        // 两个发送器都挂在 SendToAsync 上：NotificationService 走的是它（见 OptOutSendPathTests 的教训）。
        SetupSendTo(_defaultEmail, "default-id");
        SetupSendTo(_marketingEmail, "marketing-id");

        services.AddSingleton(_ => _defaultEmail.Object);
        // 具名发送器按消费方的写法注册：键在这里是大写，取的时候要按规范形态对上。
        services.AddNotificationSender<IEmailSender>("Marketing", _ => _marketingEmail.Object);
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);
        services.AddSingleton(_ => new Mock<IFaxSender>().Object);

        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationProviderResolver, NotificationProviderResolver>();
        services.AddSingleton<INotificationProviderSelector>(_ => _selector);
    }

    private static void SetupSendTo(Mock<IEmailSender> sender, string externalId)
        => sender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SendResult.CreateSuccess(externalId));

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    private INotificationService Service => ServiceProvider.GetRequiredService<INotificationService>();

    private static CreateNotificationRequest Request(string? providerKey = null, string? category = null) => new()
    {
        Type = NotificationType.Email,
        Subject = "Hello",
        Content = "Body",
        Category = category,
        ProviderKey = providerKey,
        Recipients = [new RecipientInput { Address = "someone@example.com" }]
    };

    private async Task<Message> LoadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Messages.AsNoTracking().Include(m => m.Recipients).SingleAsync(m => m.Id == id);
    }

    private static void VerifySent(Mock<IEmailSender> sender, Times times)
        => sender.Verify(s => s.SendToAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), times);

    // ── 显式指定 ─────────────────────────────────────────────────────────────

    /// <summary>★ 请求上的键落库为规范形态，派发时取的是那一家，默认那家一次都没被叫。</summary>
    [Fact]
    public async Task AnExplicitProviderKey_IsPersistedNormalisedAndDeliveredThroughThatSender()
    {
        var created = await Service.CreateAsync(Request(providerKey: "MARKETING"));
        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.ProviderKey.ShouldBe(Marketing);

        (await LoadAsync(created.Data.Id)).ProviderKey.ShouldBe(Marketing);

        var sent = await Service.SendAsync(created.Data.Id);
        sent.Succeeded.ShouldBeTrue(sent.Message);

        VerifySent(_marketingEmail, Times.Once());
        VerifySent(_defaultEmail, Times.Never());
        (await LoadAsync(created.Data.Id)).Recipients.Single().ExternalMessageId.ShouldBe("marketing-id");
    }

    /// <summary>没带键的消息走默认发送器，具名那家一次都没被叫。</summary>
    [Fact]
    public async Task NoProviderKey_DeliversThroughTheDefaultSender()
    {
        var created = await Service.CreateAsync(Request());
        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.ProviderKey.ShouldBeNull();

        await Service.SendAsync(created.Data.Id);

        VerifySent(_defaultEmail, Times.Once());
        VerifySent(_marketingEmail, Times.Never());
    }

    /// <summary>★ 写错的键在创建时就拒绝（400），什么都不落库 —— 不是等后台派发才逐收件人失败。</summary>
    [Fact]
    public async Task AnUnregisteredProviderKey_FailsAtCreationAndPersistsNothing()
    {
        var created = await Service.CreateAsync(Request(providerKey: "postmark"));

        created.Succeeded.ShouldBeFalse();
        created.Code.ShouldBe(400);
        created.Message.ShouldNotBeNull().ShouldContain("postmark");

        DbContext.ChangeTracker.Clear();
        (await DbContext.Messages.AsNoTracking().CountAsync()).ShouldBe(0);
        VerifySent(_defaultEmail, Times.Never());
        VerifySent(_marketingEmail, Times.Never());
    }

    [Fact]
    public async Task AMalformedProviderKey_FailsAtCreation()
    {
        var created = await Service.CreateAsync(Request(providerKey: "has space"));

        created.Succeeded.ShouldBeFalse();
        created.Code.ShouldBe(400);
    }

    /// <summary>批量创建里坏键只淘汰那一条，其余照常。</summary>
    [Fact]
    public async Task InABatch_ABadKeyOnlyDropsThatRequest()
    {
        var result = await Service.CreateManyAndSendAsync([Request(providerKey: "postmark"), Request(providerKey: Marketing)]);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Count().ShouldBe(1);
        result.Data!.Single().ProviderKey.ShouldBe(Marketing);
        result.Message.ShouldNotBeNull().ShouldContain("postmark");
    }

    // ── 选择器 ───────────────────────────────────────────────────────────────

    /// <summary>★ 没带键时问选择器，它选的那家<b>落库</b>并被用来派发。</summary>
    [Fact]
    public async Task WithoutAKey_TheSelectorDecides_AndItsChoiceIsPersisted()
    {
        _selector.Choose = ctx => ctx.Category == "Promo" ? "Marketing" : null;

        var created = await Service.CreateAsync(Request(category: "Promo"));
        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.ProviderKey.ShouldBe(Marketing);
        (await LoadAsync(created.Data.Id)).ProviderKey.ShouldBe(Marketing);

        await Service.SendAsync(created.Data.Id);

        VerifySent(_marketingEmail, Times.Once());
        VerifySent(_defaultEmail, Times.Never());
        _selector.Seen.Single().Category.ShouldBe("Promo");
    }

    /// <summary>显式指定的键盖过选择器：选择器根本不被问。</summary>
    [Fact]
    public async Task AnExplicitKey_BypassesTheSelector()
    {
        _selector.Choose = _ => Marketing;

        var created = await Service.CreateAsync(Request(providerKey: "default"));
        created.Succeeded.ShouldBeTrue(created.Message);

        created.Data!.ProviderKey.ShouldBeNull();
        _selector.Seen.ShouldBeEmpty();

        await Service.SendAsync(created.Data.Id);
        VerifySent(_defaultEmail, Times.Once());
        VerifySent(_marketingEmail, Times.Never());
    }

    /// <summary>★ 选择器返回未注册的键：创建失败（500，原因指名是选择器给的），绝不静默退回默认。</summary>
    [Fact]
    public async Task ASelectorReturningAnUnregisteredKey_FailsCreationAndNamesTheSelector()
    {
        _selector.Choose = _ => "postmark";

        var created = await Service.CreateAsync(Request());

        created.Succeeded.ShouldBeFalse();
        created.Code.ShouldBe(500);
        created.Message.ShouldNotBeNull();
        created.Message.ShouldContain(nameof(StubSelector));
        created.Message.ShouldContain("postmark");
        VerifySent(_defaultEmail, Times.Never());
    }

    // ── 派发时键失效 ─────────────────────────────────────────────────────────

    /// <summary>
    /// ★ 键在两次之间失效（那一节被删掉了）：那一次投递<b>失败</b>并写明原因，
    /// 不退回默认发送器 —— 调用方指定这一家是有原因的。
    /// </summary>
    [Fact]
    public async Task AKeyThatVanishedBeforeDispatch_FailsTheRecipientInsteadOfFallingBack()
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Hello",
            Content = "Body",
            Type = NotificationType.Email,
            ProviderKey = "gone",
            Status = NotificationStatus.Pending,
            TotalRecipientCount = 1,
            Recipients = [new Recipient { Address = "someone@example.com", Status = NotificationStatus.Pending }]
        };
        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        await Service.SendAsync(message.Id);

        var stored = await LoadAsync(message.Id);
        stored.Recipients.Single().Status.ShouldBe(NotificationStatus.Failed);
        stored.Recipients.Single().FailureReason.ShouldNotBeNull().ShouldContain("gone");
        stored.Status.ShouldNotBe(NotificationStatus.Sent);
        VerifySent(_defaultEmail, Times.Never());
        VerifySent(_marketingEmail, Times.Never());
    }

    /// <summary>重发失败收件人走的是同一个键，不是默认那家。</summary>
    [Fact]
    public async Task ResendingFailedRecipients_UsesTheSameProvider()
    {
        _marketingEmail
            .SetupSequence(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SendResult.CreateFailure("gateway hiccup"))
            .ReturnsAsync(SendResult.CreateSuccess("marketing-id-2"));

        var created = await Service.CreateAsync(Request(providerKey: Marketing));
        await Service.SendAsync(created.Data!.Id);
        (await LoadAsync(created.Data.Id)).Recipients.Single().Status.ShouldBe(NotificationStatus.Failed);

        var resent = await Service.ResendToFailedRecipientsAsync(created.Data.Id);
        resent.Succeeded.ShouldBeTrue(resent.Message);

        VerifySent(_marketingEmail, Times.Exactly(2));
        VerifySent(_defaultEmail, Times.Never());
        (await LoadAsync(created.Data.Id)).Recipients.Single().Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>测试用选择器：可编程的规则 + 记录它看到过什么。</summary>
    private sealed class StubSelector : INotificationProviderSelector
    {
        public Func<NotificationProviderSelectionContext, string?> Choose { get; set; } = _ => null;
        public List<NotificationProviderSelectionContext> Seen { get; } = [];

        public ValueTask<string?> SelectAsync(NotificationProviderSelectionContext context, CancellationToken cancellationToken = default)
        {
            Seen.Add(context);
            return ValueTask.FromResult(Choose(context));
        }
    }
}
