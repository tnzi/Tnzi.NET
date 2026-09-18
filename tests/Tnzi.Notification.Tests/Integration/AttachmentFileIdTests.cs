using Tnzi.Domain.Entities;
using Tnzi.Security.Claims;
using Tnzi.Storage;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 只带 <c>FileId</c> 的附件：创建时按「这个人读得到吗」把关，派发时以系统身份把字节读出来装进信里。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>补的是哪个洞</b>：<c>Attachment.FileId</c> 从建表起就在（<c>[FileField]</c>），却从没被解析成字节 ——
/// 派发只把 <c>FilePath</c> 转给发送器，而 Storage 承载的产物的 <c>FilePath</c> 是存储相对键、URL 又要鉴权，
/// 内置发送器两者都装不上。于是发票邮件正文写着「请查收附件」，附件却一次也没发出去过。
/// </para>
/// <para>
/// ★ 创建那一刻的门：<b>有当前用户</b>就要过 <see cref="IFileReadAccessProbe"/>（持 <c>notification.message.create</c>
/// 的人不能拿一个不属于自己的文件 id 把别人的私密文件寄出去）；<b>没有当前用户</b>的是进程内的框架代码
/// （事件处理器、后台队列），管理端点永远带着认证，走不到这一支。
/// </para>
/// </remarks>
public class AttachmentFileIdTests : IntegrationTestBase
{
    private static readonly Guid StoredFileId = Guid.NewGuid();
    private static readonly byte[] StoredBytes = "%PDF-1.7 invoice"u8.ToArray();

    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IFileContentReader> _reader = new();
    private readonly Mock<IFileReadAccessProbe> _probe = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly NotificationOptions _options = new() { MaxConcurrency = 4 };

    private List<EmailAttachment>? _sentAttachments;
    private bool _attachmentsCaptured;

    public AttachmentFileIdTests()
    {
        _reader.Setup(r => r.OpenReadAsync(StoredFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(StoredBytes));
        _probe.Setup(p => p.CanReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        AuthenticateAs(TestHelper.DefaultTestUserId);
    }

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
        options.Setup(o => o.CurrentValue).Returns(() => _options);
        services.AddSingleton(_ => options.Object);

        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string? __, string ___, string ____, bool _____, List<EmailAttachment>? attachments, CancellationToken ______) =>
            {
                _sentAttachments = attachments;
                _attachmentsCaptured = true;
            })
            .ReturnsAsync(SendResult.CreateSuccess("stub-id"));

        services.AddSingleton(_ => _emailSender.Object);
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);
        services.AddSingleton(_ => new Mock<IFaxSender>().Object);

        services.AddScoped(_ => _reader.Object);
        services.AddScoped(_ => _probe.Object);
        services.AddScoped(_ => _currentUser.Object);

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

    // ── 派发：FileId → 字节 ──────────────────────────────────────────────────

    /// <summary>★★ 只带 FileId 的附件真的以字节形式到了发送器手里。</summary>
    [Fact]
    public async Task Send_ResolvesAFileIdAttachmentThroughStorageIntoBytes()
    {
        var messageId = await SeedMessageWithAttachmentAsync(StoredFileId);

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        _attachmentsCaptured.ShouldBeTrue();
        var attachment = _sentAttachments.ShouldNotBeNull().ShouldHaveSingleItem();
        attachment.FileName.ShouldBe("INV-1.pdf");
        attachment.ContentType.ShouldBe("application/pdf");
        attachment.Content.ShouldBe(StoredBytes);
    }

    /// <summary>存储里没有这个文件：这一次投递失败，不发一封没有附件的信。</summary>
    [Fact]
    public async Task Send_FailsWhenTheFileCannotBeRead_InsteadOfSendingWithoutIt()
    {
        var messageId = await SeedMessageWithAttachmentAsync(Guid.NewGuid());

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeFalse("附件读不出来，信却照发了");
        _attachmentsCaptured.ShouldBeFalse();
    }

    /// <summary>超过 <c>MaxAttachmentBytes</c> 的文件同样拒绝，与本地 / 远程附件同一道上限。</summary>
    [Fact]
    public async Task Send_FailsWhenTheStoredFileExceedsTheSizeLimit()
    {
        _options.Attachments.MaxAttachmentBytes = StoredBytes.Length - 1;
        var messageId = await SeedMessageWithAttachmentAsync(StoredFileId);

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeFalse();
        _attachmentsCaptured.ShouldBeFalse();
    }

    // ── 创建：这个人读得到这份文件吗 ─────────────────────────────────────────

    /// <summary>★★ 有当前用户而探针说读不到：400，一条附件不合规整个请求不落库。</summary>
    [Fact]
    public async Task Create_AnAuthenticatedCallerWhoCannotReadTheFile_Returns400()
    {
        _probe.Setup(p => p.CanReadAsync(StoredFileId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateAsync(RequestWithFileId(StoredFileId));

        result.Succeeded.ShouldBeFalse("拿着别人的文件 id 就把别人的文件寄出去了");
        result.Code.ShouldBe(400);
        (await DbContext.Attachments.CountAsync()).ShouldBe(0);
    }

    /// <summary>读得到就接受。</summary>
    [Fact]
    public async Task Create_AnAuthenticatedCallerWhoCanReadTheFile_IsAccepted()
    {
        var result = await CreateAsync(RequestWithFileId(StoredFileId));

        result.Succeeded.ShouldBeTrue(result.Message);
        (await DbContext.Attachments.CountAsync()).ShouldBe(1);
    }

    /// <summary>
    /// 没有当前用户 = 进程内的框架代码（事件处理器 / 后台）：不问探针。
    /// 发票在 webhook 里自动发出时正是这一支；管理端点永远带着认证，走不到这里。
    /// </summary>
    [Fact]
    public async Task Create_ASystemCallerWithoutAUser_IsAcceptedWithoutAskingTheProbe()
    {
        Anonymous();
        _probe.Setup(p => p.CanReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateAsync(RequestWithFileId(StoredFileId));

        result.Succeeded.ShouldBeTrue(result.Message);
        _probe.Verify(p => p.CanReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private void AuthenticateAs(Guid userId)
    {
        _currentUser.Setup(u => u.Id).Returns(userId);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
    }

    private void Anonymous()
    {
        _currentUser.Setup(u => u.Id).Returns((Guid?)null);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(false);
    }

    private static CreateNotificationRequest RequestWithFileId(Guid fileId) => new()
    {
        Type = NotificationType.Email,
        Subject = "Invoice INV-1",
        Content = "Please find attached invoice INV-1.",
        Recipients = [new RecipientInput { Address = "ada@example.com" }],
        Attachments = [new FileInfoDto { FileId = fileId, FileName = "INV-1.pdf", FilePath = string.Empty, ContentType = "application/pdf" }],
    };

    private Task<Result<NotificationInfo>> CreateAsync(CreateNotificationRequest request)
        => ServiceProvider.GetRequiredService<INotificationService>().CreateAsync(request);

    private async Task<Guid> SeedMessageWithAttachmentAsync(Guid fileId)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Invoice INV-1",
            Content = "Please find attached invoice INV-1.",
            Type = NotificationType.Email,
            IsTransactional = true,
            Status = NotificationStatus.Pending,
            TotalRecipientCount = 1,
            Recipients = [new Recipient { Address = "ada@example.com", Status = NotificationStatus.Pending }],
            Attachments = [new Attachment { FileId = fileId, FileName = "INV-1.pdf", FilePath = string.Empty, ContentType = "application/pdf" }],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }

    private Task<Result> SendAsync(Guid messageId)
        => ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);
}
