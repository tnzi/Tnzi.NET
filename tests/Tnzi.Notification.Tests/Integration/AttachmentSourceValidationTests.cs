using Tnzi.Domain.Entities;
using Tnzi.Storage;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 附件的 <c>FilePath</c> 来自管理端请求体，创建那一刻就要按来源校验，方向 fail-closed。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>补的是哪个洞</b>：<c>FilePath</c> 从 <c>CreateNotificationRequest.Attachments</c> 原样落库，
/// 发信时内置发送器按它读<b>任意本地文件</b>或取<b>任意 URL</b> 当附件，收件人地址在同一个请求体里。
/// 持 <c>notification.message.create</c> 的人一次请求就能把服务端的生产配置寄到自己邮箱，
/// 或对内网 / 云元数据端点做一次带回显的 SSRF —— 而日志只记一次正常投递。
/// </para>
/// <para>
/// ★ 校验落在<b>服务层入口</b>而不只在发送器：发送器是可替换契约，规则写在某一个内置实现里，
/// 换一个实现就绕过去了；而且拒绝要发生在创建那一刻（400），不是等到后台发信时才逐收件人失败
/// （那时调用方早拿到 200 走了）。发送器那一道另有 <c>AttachmentMaterialisationTests</c> 守着。
/// </para>
/// <para>
/// 用例全部不碰网络：私网 / 链路本地地址用 IP 字面量，<c>EgressGuard</c> 对字面量不做 DNS 查询。
/// </para>
/// </remarks>
public class AttachmentSourceValidationTests : IntegrationTestBase
{
    private readonly string _allowedRoot = Path.Combine(Path.GetTempPath(), "tnzi-attachment-roots", Guid.NewGuid().ToString("N"));
    private readonly NotificationOptions _options = new() { MaxConcurrency = 4 };

    public AttachmentSourceValidationTests()
    {
        Directory.CreateDirectory(_allowedRoot);
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

        services.AddSingleton(_ => new Mock<IEmailSender>().Object);
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

    // ── 本地路径 ─────────────────────────────────────────────────────────────

    /// <summary>★★ 默认配置（没有任何允许的根目录）下，任何本地路径都拒绝 —— 这是这条缺陷的原形。</summary>
    [Fact]
    public async Task Create_LocalPath_WithNoAllowedRoots_Returns400()
    {
        var result = await CreateAsync(WithAttachment(Path.Combine(Path.GetTempPath(), "appsettings.Production.json")));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("AllowedLocalRoots");
        (await DbContext.Messages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Create_LocalPathOutsideAllowedRoots_Returns400()
    {
        AllowRoot(_allowedRoot);

        var result = await CreateAsync(WithAttachment(Path.Combine(Path.GetTempPath(), "outside.pdf")));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("outside the allowed");
        (await DbContext.Messages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Create_LocalPathInsideAllowedRoot_Succeeds()
    {
        AllowRoot(_allowedRoot);
        var path = Path.Combine(_allowedRoot, "invoice.pdf");

        var result = await CreateAsync(WithAttachment(path));

        result.Succeeded.ShouldBeTrue(result.Message);
        (await DbContext.Attachments.SingleAsync()).FilePath.ShouldBe(path);
    }

    /// <summary>★ <c>..</c> 逃逸：字面上在根目录之下，归一化后不在。</summary>
    [Fact]
    public async Task Create_LocalPathWithDotDot_Returns400()
    {
        AllowRoot(_allowedRoot);
        var escaping = Path.Combine(_allowedRoot, "..", "..", "secrets.json");

        var result = await CreateAsync(WithAttachment(escaping));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await DbContext.Messages.CountAsync()).ShouldBe(0);
    }

    /// <summary>根目录名的前缀撞名不算在根之下（<c>/roots/x</c> 不能放行 <c>/roots/x-evil/…</c>）。</summary>
    [Fact]
    public async Task Create_LocalPathUnderASiblingWithTheSamePrefix_Returns400()
    {
        AllowRoot(_allowedRoot);
        var sibling = _allowedRoot + "-evil";
        Directory.CreateDirectory(sibling);

        var result = await CreateAsync(WithAttachment(Path.Combine(sibling, "a.pdf")));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>相对路径取决于进程的工作目录，不接受。</summary>
    [Fact]
    public async Task Create_RelativeLocalPath_Returns400()
    {
        AllowRoot(_allowedRoot);

        var result = await CreateAsync(WithAttachment(Path.Combine("invoices", "a.pdf")));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    // ── URL ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/iam/security-credentials/")]
    [InlineData("http://10.0.0.5:8500/v1/kv/?recurse")]
    [InlineData("http://127.0.0.1:5000/api/admin/settings")]
    [InlineData("http://[::1]:5000/")]
    public async Task Create_PrivateNetworkUrl_Returns400(string url)
    {
        var result = await CreateAsync(WithAttachment(url));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("not allowed");
        (await DbContext.Messages.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("ftp://files.example.com/a.pdf")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://files.example.com/")]
    public async Task Create_NonHttpScheme_Returns400(string url)
    {
        AllowRoot(_allowedRoot);

        var result = await CreateAsync(WithAttachment(url));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>关掉远程 URL 后，公网地址也拒绝（且不去做 DNS：字面量 IP 是公网段）。</summary>
    [Fact]
    public async Task Create_RemoteUrl_WhenRemoteUrlsDisabled_Returns400()
    {
        _options.Attachments.AllowRemoteUrls = false;

        var result = await CreateAsync(WithAttachment("https://93.184.216.34/invoice.pdf"));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("AllowRemoteUrls");
    }

    /// <summary>对照：公网字面量 IP 的 https 地址通过（EgressGuard 对字面量不查 DNS）。</summary>
    [Fact]
    public async Task Create_PublicUrl_Succeeds()
    {
        var result = await CreateAsync(WithAttachment("https://93.184.216.34/invoice.pdf"));

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    // ── 批量路径走同一道门 ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateMany_LocalPathWithNoAllowedRoots_RejectsThatRequest()
    {
        var bad = WithAttachment(Path.Combine(Path.GetTempPath(), "appsettings.Production.json"));
        var good = NewRequest();

        var result = await ServiceProvider.GetRequiredService<INotificationService>()
            .CreateManyAndSendAsync([bad, good]);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Count().ShouldBe(1);
        result.Message!.ShouldContain("AllowedLocalRoots");
        (await DbContext.Attachments.CountAsync()).ShouldBe(0);
    }

    // ── 只带 FileId 的附件：没装存储模块就没人解析得出字节 ─────────────────────

    /// <summary>
    /// ★ 本夹具不注册 <c>IFileContentReader</c>（= 没加载 Tnzi.Storage）：一个只带 FileId 的附件在这台宿主上
    /// 永远发不出去，必须在创建那一刻 400 并指名要加载的包 —— 此前它被接受，然后在后台派发时逐收件人失败，
    /// 而调用方早已拿到 200。
    /// </summary>
    [Fact]
    public async Task Create_FileIdAttachment_WithoutTheStorageModule_Returns400()
    {
        var request = NewRequest();
        request.Attachments = [new FileInfoDto { FileId = Guid.NewGuid(), FileName = "invoice.pdf", FilePath = string.Empty }];

        var result = await CreateAsync(request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Tnzi.Storage");
        (await DbContext.Attachments.CountAsync()).ShouldBe(0);
    }

    /// <summary>
    /// ★ 既没有 <c>FileId</c> 也没有 <c>FilePath</c> 的附件，没有任何发送器装得上：此前它被接受落库，
    /// 后台派发时逐收件人失败「carries neither content nor a file path」，而调用方早已拿到 200。
    /// 与 FileId-only / FilePath 两条门同一判据：拒绝要发生在创建这一刻。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_AttachmentWithNeitherFileIdNorPath_Returns400(string? filePath)
    {
        var request = NewRequest();
        request.Attachments = [new FileInfoDto { FileName = "ghost.pdf", FilePath = filePath!, ContentType = "application/pdf" }];

        var result = await CreateAsync(request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        var message = result.Message.ShouldNotBeNull();
        message.ShouldContain("ghost.pdf");
        message.ShouldContain("FileId");
        message.ShouldContain("FilePath");
        (await DbContext.Attachments.CountAsync()).ShouldBe(0);
        (await DbContext.Messages.CountAsync()).ShouldBe(0);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private void AllowRoot(string root) => _options.Attachments.AllowedLocalRoots = [root];

    private static CreateNotificationRequest NewRequest() => new()
    {
        Type = NotificationType.Email,
        Subject = "Statement",
        Content = "See attached.",
        Recipients = [new RecipientInput { Address = "attacker@example.com" }],
    };

    private static CreateNotificationRequest WithAttachment(string filePath)
    {
        var request = NewRequest();
        request.Attachments = [new FileInfoDto { FileName = "a.json", FilePath = filePath, ContentType = "text/plain" }];
        return request;
    }

    private Task<Result<NotificationInfo>> CreateAsync(CreateNotificationRequest request)
        => ServiceProvider.GetRequiredService<INotificationService>().CreateAsync(request);

    public override void Dispose()
    {
        try { Directory.Delete(_allowedRoot, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_allowedRoot + "-evil", recursive: true); } catch { /* best effort */ }
        base.Dispose();
    }
}
