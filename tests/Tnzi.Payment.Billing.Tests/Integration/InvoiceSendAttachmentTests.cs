using Tnzi.Storage.Entities;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 发送发票时，刚生成的产物必须真的挂在通知请求上。
/// </summary>
/// <remarks>
/// <para>
/// ★★ 此前 <c>SendAsync</c> 先 <c>GeneratePdfAsync</c>（产物落库或落盘），随后构造的
/// <c>CreateNotificationRequest</c> 只有 Subject / Content / Recipients，正文写着
/// 「Please find attached invoice INV…」而 <c>Attachments</c> 一次都没出现 —— 装了本包的部署里，
/// 每一笔成功付款都给客户发一封没有附件的「请查收附件」，发票记为 Sent、SendCount 递增、日志干净。
/// </para>
/// <para>
/// 基类刻意不注册 <c>INotificationService</c>（那是测 501 的现场），这里单独注册一个把请求抓下来的替身；
/// 被测对象（发票服务）是真的。两种落地方式各一条：Storage 承载时挂 <c>FileId</c>（字节由通知模块经
/// <c>IFileContentReader</c> 以系统身份读出），本地回退时挂绝对本地路径。
/// </para>
/// </remarks>
public abstract class InvoiceSendFixture : BillingIntegrationTestBase
{
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IFileStorageService> _storage = new();

    private CreateNotificationRequest? _captured;

    /// <summary>通知替身对这一次 CreateAndSendAsync 的回答，默认成功。</summary>
    protected Result<NotificationInfo> NotificationAnswer { get; set; } = Result.Success(new NotificationInfo());

    /// <summary>要不要装上 Storage 替身。虚属性而不是构造参数：<c>ConfigureServices</c> 在基类构造函数里跑，那时派生类构造体还没执行。</summary>
    protected virtual bool WithStorage => false;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        _notifications
            .Setup(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((CreateNotificationRequest request, CancellationToken _) => _captured = request)
            .ReturnsAsync(() => NotificationAnswer);
        services.AddScoped(_ => _notifications.Object);

        if (WithStorage)
        {
            _storage
                .Setup(s => s.SaveWithReferenceAsync(It.IsAny<string>(), It.IsAny<Stream>(), nameof(Invoice), It.IsAny<Guid>(), nameof(Invoice.PdfFileId), false, false))
                .ReturnsAsync((string fileName, Stream _, string __, Guid ___, string ____, bool _____, bool ______) =>
                    Result.Success(new FileRecord { Id = Guid.NewGuid(), FileName = fileName, Path = $"2026/09/12/{fileName}" }));
            _storage
                .Setup(s => s.GetUrlAsync(It.IsAny<Guid>(), It.IsAny<int?>()))
                .ReturnsAsync((Guid id, int? _) => Result.Success<string>($"/api/files/{id}/download"));
            services.AddScoped(_ => _storage.Object);
        }
    }

    protected async Task<Invoice> CreateInvoiceAsync()
    {
        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateManualAsync(new CreatePaymentInvoiceDto
        {
            CustomerName = "Ada",
            CustomerEmail = "ada@example.com",
            InvoiceDate = DateTime.UtcNow,
            LineItems = [new InvoiceLineItemDto { Description = "Work", Quantity = 2, UnitPrice = 30m }]
        }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return (await ReloadAsync<Invoice>(created.Data!.Id))!;
    }

    protected CreateNotificationRequest Captured => _captured.ShouldNotBeNull();
}

/// <summary>本地回退的那一半（基类不注册 Storage）：附件是刚写出的绝对路径。</summary>
public class InvoiceSendAttachmentTests : InvoiceSendFixture
{
    /// <summary>★★ 本地回退：附件恰一条，指向刚写出的那个绝对路径，文件名是发票号。</summary>
    [Fact]
    public async Task Send_AttachesTheGeneratedDocument_FromTheLocalFallback()
    {
        var invoice = await CreateInvoiceAsync();

        var result = await InScopeAsync<IPaymentInvoiceService, Result>(s => s.SendAsync(invoice.Id, null, null));

        result.Succeeded.ShouldBeTrue(result.Message);
        var attachments = Captured.Attachments;
        attachments.ShouldNotBeNull("正文写着「请查收附件」，请求上却没有任何附件");
        var attachment = attachments.ShouldHaveSingleItem();
        attachment.FileId.ShouldBeNull();
        Path.IsPathFullyQualified(attachment.FilePath).ShouldBeTrue(attachment.FilePath);
        File.Exists(attachment.FilePath).ShouldBeTrue();
        attachment.FileName.ShouldBe($"{invoice.InvoiceNo}.html");
        attachment.ContentType.ShouldBe("text/html");
        Captured.Content.ShouldContain("attached");
    }

    /// <summary>通知模块拒绝这封信（附件来源不合规、渠道故障…）：发票不得推进为 Sent，也不得计一次发送。</summary>
    [Fact]
    public async Task Send_DoesNotMarkTheInvoiceSent_WhenTheNotificationIsRefused()
    {
        NotificationAnswer = Result.Failure<NotificationInfo>("Local attachment paths are not allowed", 400);
        var invoice = await CreateInvoiceAsync();

        var result = await InScopeAsync<IPaymentInvoiceService, Result>(s => s.SendAsync(invoice.Id, null, null));

        result.Succeeded.ShouldBeFalse();
        result.Message!.ShouldContain("not allowed");
        var reloaded = (await ReloadAsync<Invoice>(invoice.Id))!;
        reloaded.Status.ShouldBe(InvoiceStatus.Draft);
        reloaded.SendCount.ShouldBe(0);
    }

    /// <summary>★ 发送之后产物指针必须还在：此前状态更新用的是生成前的快照，整行合并把 PdfFilePath 抹回 null，下次下载只好再生成一份。</summary>
    [Fact]
    public async Task Send_KeepsTheDocumentPointer_AfterMarkingTheInvoiceSent()
    {
        var invoice = await CreateInvoiceAsync();

        var result = await InScopeAsync<IPaymentInvoiceService, Result>(s => s.SendAsync(invoice.Id, null, null));

        result.Succeeded.ShouldBeTrue(result.Message);
        var reloaded = (await ReloadAsync<Invoice>(invoice.Id))!;
        reloaded.Status.ShouldBe(InvoiceStatus.Sent);
        reloaded.PdfFilePath.ShouldBe(Captured.Attachments!.Single().FilePath);
    }
}

/// <summary>Storage 承载的那一半：附件带 <c>FileId</c>，路径留空（存储相对键对发送器没有意义）。</summary>
public class InvoiceSendAttachmentWithStorageTests : InvoiceSendFixture
{
    protected override bool WithStorage => true;

    /// <summary>★★ Storage 承载：附件恰一条，<c>FileId</c> 就是落库的那个，<c>FilePath</c> 为空。</summary>
    [Fact]
    public async Task Send_AttachesTheStoredDocument_ByFileId()
    {
        var invoice = await CreateInvoiceAsync();

        var result = await InScopeAsync<IPaymentInvoiceService, Result>(s => s.SendAsync(invoice.Id, null, null));

        result.Succeeded.ShouldBeTrue(result.Message);
        var stored = (await ReloadAsync<Invoice>(invoice.Id))!;
        stored.PdfFileId.ShouldNotBeNull("发送之后产物指针被生成前的快照抹掉了");

        var attachments = Captured.Attachments;
        attachments.ShouldNotBeNull("正文写着「请查收附件」，请求上却没有任何附件");
        var attachment = attachments.ShouldHaveSingleItem();
        attachment.FileId.ShouldBe(stored.PdfFileId);
        attachment.FilePath.ShouldBeEmpty("存储相对键不是发送器能读的路径，挂上去只会让它当本地路径去找");
        attachment.FileName.ShouldBe($"{invoice.InvoiceNo}.html");
    }
}
