using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tnzi.EventBus;
using Tnzi.Payment.Billing.Events;
using Tnzi.Payment.Dtos;
using Tnzi.Storage.Entities;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 发票产物（PDF / HTML）的生命周期：何时生成、何时作废、被换下的那份何时解除引用。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前每发一次发票就重新生成一份产物并在 Storage 里新存一个文件，旧的那份引用从不解除，
/// 重发几次就留下几份永远回收不了的副本；而标记已付 / 作废又从不让产物作废，
/// 客户下载到的仍是一张写着「待付」的账单。现在产物只在没有时生成，改变它内容的操作负责让它作废，
/// 被换下的那份解除引用交给存储的孤儿回收。
/// </para>
/// <para>
/// 通知、存储、文件引用、事件总线都是替身；被测对象（发票服务与自动开票处理器）是真的，库是真的 SQLite。
/// </para>
/// </remarks>
public abstract class InvoiceDocumentFixture : BillingIntegrationTestBase
{
    protected readonly Mock<INotificationService> Notifications = new();
    protected readonly Mock<IFileStorageService> Storage = new();
    protected readonly Mock<IFileReferenceService> References = new();
    protected readonly List<object> Published = [];

    /// <summary>存储替身这一次保存是否成功。</summary>
    protected bool StorageSaveSucceeds { get; set; } = true;

    /// <summary>存储替身这一次取地址是否成功。</summary>
    protected bool StorageUrlSucceeds { get; set; } = true;

    /// <summary>要不要装上 Storage 替身。虚属性而不是构造参数：<c>ConfigureServices</c> 在基类构造函数里跑。</summary>
    protected virtual bool WithStorage => false;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        Notifications
            .Setup(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new NotificationInfo()));
        services.AddScoped(_ => Notifications.Object);

        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<InvoiceGeneratedEvent>(), It.IsAny<CancellationToken>()))
            .Callback((InvoiceGeneratedEvent e, CancellationToken _) => Published.Add(e))
            .Returns(Task.CompletedTask);
        bus.Setup(b => b.PublishAsync(It.IsAny<InvoiceSentEvent>(), It.IsAny<CancellationToken>()))
            .Callback((InvoiceSentEvent e, CancellationToken _) => Published.Add(e))
            .Returns(Task.CompletedTask);
        bus.Setup(b => b.PublishAsync(It.IsAny<InvoicePaidEvent>(), It.IsAny<CancellationToken>()))
            .Callback((InvoicePaidEvent e, CancellationToken _) => Published.Add(e))
            .Returns(Task.CompletedTask);
        services.AddSingleton(bus.Object);

        if (!WithStorage)
            return;

        Storage
            .Setup(s => s.SaveWithReferenceAsync(It.IsAny<string>(), It.IsAny<Stream>(), nameof(Invoice), It.IsAny<Guid>(), nameof(Invoice.PdfFileId), false, false))
            .ReturnsAsync((string fileName, Stream _, string __, Guid ___, string ____, bool _____, bool ______) => StorageSaveSucceeds
                ? Result.Success(new FileRecord { Id = Guid.NewGuid(), FileName = fileName, Path = $"2026/10/11/{fileName}" })
                : Result.Failure<FileRecord>("Storage is unavailable", 503));
        Storage
            .Setup(s => s.GetUrlAsync(It.IsAny<Guid>(), It.IsAny<int?>()))
            .ReturnsAsync((Guid id, int? _) => StorageUrlSucceeds
                ? Result.Success<string>($"/api/files/{id}/download")
                : Result.Failure<string>("File record not found", 404));
        services.AddScoped(_ => Storage.Object);

        References
            .Setup(r => r.UpdateReferenceAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success());
        services.AddScoped(_ => References.Object);
    }

    protected async Task<Guid> CreateManualInvoiceAsync(Guid? userId = null)
    {
        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateManualAsync(new CreatePaymentInvoiceDto
        {
            CustomerName = "Ada",
            CustomerEmail = "ada@example.com",
            UserId = userId,
            InvoiceDate = DateTime.UtcNow,
            LineItems = [new InvoiceLineItemDto { Description = "Work", Quantity = 2, UnitPrice = 30m }]
        }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return created.Data!.Id;
    }

    protected Task<Result> SendAsync(Guid invoiceId)
        => InScopeAsync<IPaymentInvoiceService, Result>(s => s.SendAsync(invoiceId, null, null));

    protected Task<Result> MarkPaidAsync(Guid invoiceId, string? remark = null)
        => InScopeAsync<IPaymentInvoiceService, Result>(s => s.MarkAsPaidAsync(invoiceId, new MarkInvoicePaidDto { PaidAmount = 60m, Remark = remark }));

    protected async Task<string> DownloadAsTextAsync(Guid invoiceId)
    {
        var document = await InScopeAsync<IPaymentInvoiceService, Result<InvoiceDocumentDto>>(s => s.GetPdfContentAsync(invoiceId));
        document.Succeeded.ShouldBeTrue(document.Message);
        return Encoding.UTF8.GetString(document.Data!.Content);
    }

    protected void VerifyReleased(Guid fileId, Guid invoiceId, Times times)
        => References.Verify(r => r.UpdateReferenceAsync(fileId, null, nameof(Invoice), invoiceId, nameof(Invoice.PdfFileId)), times);
}

/// <summary>本地回退（不装 Storage）：作废、备注、地址与明细。</summary>
public class InvoiceDocumentLifecycleTests : InvoiceDocumentFixture
{
    /// <summary>★ 发出之后标记已付：产物随之作废，客户再下载拿到的是写着 Paid 的那份，不是发送时那份。</summary>
    [Fact]
    public async Task MarkAsPaid_ClearsTheSentDocument_SoTheNextDownloadShowsPaid()
    {
        var id = await CreateManualInvoiceAsync();
        (await SendAsync(id)).Succeeded.ShouldBeTrue();
        (await ReloadAsync<Invoice>(id))!.PdfFilePath.ShouldNotBeNull();

        (await MarkPaidAsync(id)).Succeeded.ShouldBeTrue();

        var reloaded = (await ReloadAsync<Invoice>(id))!;
        reloaded.PdfFilePath.ShouldBeNull("标记已付之后产物还指着发送时生成的那份，上面印的是旧状态");
        var text = await DownloadAsTextAsync(id);
        text.ShouldContain("Status: Paid");
    }

    /// <summary>★ 作废之后再下载，拿到的不是一张待付账单。</summary>
    [Fact]
    public async Task Cancel_ClearsTheSentDocument_SoTheNextDownloadShowsCancelled()
    {
        var id = await CreateManualInvoiceAsync();
        (await SendAsync(id)).Succeeded.ShouldBeTrue();

        (await InScopeAsync<IPaymentInvoiceService, Result>(s => s.CancelAsync(id, "duplicate"))).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Invoice>(id))!.PdfFilePath.ShouldBeNull();
        (await DownloadAsTextAsync(id)).ShouldContain("Status: Cancelled");
    }

    /// <summary>★ 操作员填的备注要留下来：此前收下、答 200、一个字都没存。</summary>
    [Fact]
    public async Task MarkAsPaid_AppendsTheRemarkToTheInternalNotes()
    {
        var id = await CreateManualInvoiceAsync();

        (await MarkPaidAsync(id, "paid by wire, ref 123")).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Invoice>(id))!.InternalNotes.ShouldBe("Paid: paid by wire, ref 123");
    }

    /// <summary>
    /// ★ 追加的备注不能把内部备注撑出列宽（1000）：开票时写满的内部备注再追加一条「已付」或「作废」备注，
    /// 在会校验列宽的数据库上（SQL Server / PostgreSQL / MySQL 严格模式）就是一次写库失败，操作员拿到 500，发票也没标上。
    /// 放不下时从最早的内容截掉，新追加的那条完整留下。
    /// </summary>
    [Theory]
    [InlineData("paid")]
    [InlineData("cancelled")]
    public async Task AppendingANote_ToFullInternalNotes_StaysWithinTheColumnAndKeepsTheNewNote(string action)
    {
        var id = await CreateManualInvoiceAsync();
        var existing = "first-line-" + new string('x', 980);
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<BillingTestDbContext>();
            await ctx.Set<Invoice>().Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.InternalNotes, existing));
        }

        var remark = "ref " + new string('9', 300);
        var result = action == "paid"
            ? await MarkPaidAsync(id, remark)
            : await InScopeAsync<IPaymentInvoiceService, Result>(s => s.CancelAsync(id, remark));
        result.Succeeded.ShouldBeTrue(result.Message);

        var notes = (await ReloadAsync<Invoice>(id))!.InternalNotes!;
        notes.Length.ShouldBeLessThanOrEqualTo(1000);
        notes.ShouldEndWith((action == "paid" ? "Paid: " : "Cancelled: ") + remark);
    }

    /// <summary>★ 作废的发票不能再标记已付，也不发已付事件。</summary>
    [Fact]
    public async Task MarkAsPaid_RefusesACancelledInvoice()
    {
        var id = await CreateManualInvoiceAsync();
        (await InScopeAsync<IPaymentInvoiceService, Result>(s => s.CancelAsync(id, null))).Succeeded.ShouldBeTrue();

        var result = await MarkPaidAsync(id);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.InvoiceCancelled);
        (await ReloadAsync<Invoice>(id))!.Status.ShouldBe(InvoiceStatus.Cancelled);
        Published.OfType<InvoicePaidEvent>().ShouldBeEmpty();
    }

    /// <summary>发送作废的发票答「已作废」，不是「不能作废」。</summary>
    [Fact]
    public async Task Send_RefusesACancelledInvoice_WithTheCancelledCode()
    {
        var id = await CreateManualInvoiceAsync();
        await InScopeAsync<IPaymentInvoiceService, Result>(s => s.CancelAsync(id, null));

        var result = await SendAsync(id);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.InvoiceCancelled);
    }

    /// <summary>★ 未加载 Storage 时取地址恒为空串，且不再为此整套重新生成一遍。</summary>
    [Fact]
    public async Task GetPdfUrl_WithoutStorage_AnswersEmptyWithoutGenerating()
    {
        var id = await CreateManualInvoiceAsync();

        var first = await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GetPdfUrlAsync(id));
        var second = await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GetPdfUrlAsync(id));

        first.Succeeded.ShouldBeTrue();
        first.Data.ShouldBe(string.Empty);
        second.Data.ShouldBe(string.Empty);
        (await ReloadAsync<Invoice>(id))!.PdfFilePath.ShouldBeNull("取一个注定为空的地址不该渲染并写出一份产物");
    }

    /// <summary>★ 详情带明细，与列表同一个形状；也带归属用户与来源支付。</summary>
    [Fact]
    public async Task Get_ReturnsTheLineItems_TheOwner_AndThePayment()
    {
        var payment = SucceededPayment(80m);
        payment.UserId = Guid.NewGuid();
        await SeedAsync(payment);
        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(payment.Id, null));

        var detail = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.GetAsync(created.Data!.Id, payment.UserId));

        detail.Succeeded.ShouldBeTrue(detail.Message);
        detail.Data!.LineItems.ShouldHaveSingleItem().Amount.ShouldBe(80m);
        detail.Data.UserId.ShouldBe(payment.UserId);
        detail.Data.PaymentId.ShouldBe(payment.Id);
    }

    /// <summary>手工开票的返回值带着刚建的明细。</summary>
    [Fact]
    public async Task CreateManual_ReturnsItsLineItems()
    {
        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateManualAsync(new CreatePaymentInvoiceDto
        {
            CustomerName = "Ada",
            InvoiceDate = DateTime.UtcNow,
            LineItems =
            [
                new InvoiceLineItemDto { Description = "Work", Quantity = 2, UnitPrice = 30m },
                new InvoiceLineItemDto { Description = "Travel", Quantity = 1, UnitPrice = 15m }
            ]
        }));

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.LineItems.Count.ShouldBe(2);
    }

    /// <summary>★ 折扣大于本行金额会让应付额变成负数，拒绝；税率是 0-100 的百分数。</summary>
    [Theory]
    [InlineData(70, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 101)]
    public async Task CreateManual_RefusesAnImpossibleLine(decimal discount, decimal taxRate)
    {
        var result = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateManualAsync(new CreatePaymentInvoiceDto
        {
            CustomerName = "Ada",
            InvoiceDate = DateTime.UtcNow,
            LineItems = [new InvoiceLineItemDto { Description = "Work", Quantity = 2, UnitPrice = 30m, DiscountAmount = discount, TaxRate = taxRate }]
        }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>★ 「我的发票」能翻页；不带参数时仍是最新的那一页（每页 100）。</summary>
    [Fact]
    public async Task GetUserInvoices_Pages()
    {
        var owner = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
            await CreateManualInvoiceAsync(owner);

        var secondPage = await InScopeAsync<IPaymentInvoiceService, Result<IPagedList<PaymentInvoiceDto>>>(
            s => s.GetUserInvoicesAsync(owner, new OwnerListQueryDto { PageIndex = 2, PageSize = 2 }));
        var defaults = await InScopeAsync<IPaymentInvoiceService, Result<IPagedList<PaymentInvoiceDto>>>(
            s => s.GetUserInvoicesAsync(owner));

        secondPage.Data!.Items.Count.ShouldBe(1);
        secondPage.Data.TotalCount.ShouldBe(3);
        defaults.Data!.Items.Count.ShouldBe(3);
    }

    /// <summary>★ 两个发票事件此前只有类、没有发布方：开票发「已生成」，发出发「已发送」。</summary>
    [Fact]
    public async Task CreatingAndSending_PublishTheInvoiceEvents()
    {
        var id = await CreateManualInvoiceAsync();
        (await SendAsync(id)).Succeeded.ShouldBeTrue();

        Published.OfType<InvoiceGeneratedEvent>().ShouldHaveSingleItem().InvoiceId.ShouldBe(id);
        var sent = Published.OfType<InvoiceSentEvent>().ShouldHaveSingleItem();
        sent.InvoiceId.ShouldBe(id);
        sent.RecipientEmail.ShouldBe("ada@example.com");
    }

    /// <summary>
    /// ★ 支付完成事件被重投：发票照样只有一张，信也只发一封。此前处理器分不清「新开」与「已存在」，
    /// 每次重投都再发一次，还吃掉一个补发名额。
    /// </summary>
    [Fact]
    public async Task AutoIssuing_ARedeliveredPaymentEvent_DoesNotSendTheInvoiceAgain()
    {
        var payment = SucceededPayment();
        await SeedAsync(payment);
        var completed = new PaymentCompletedEvent { PaymentId = payment.Id, TradeNo = payment.TradeNo };

        await HandleAsync(completed);
        await HandleAsync(completed);

        Notifications.Verify(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once());
        using var scope = ServiceProvider.CreateScope();
        var invoice = await scope.ServiceProvider.GetRequiredService<IRepository<Invoice, Guid>>().SingleAsync(i => i.PaymentId == payment.Id);
        invoice.SendCount.ShouldBe(1);
    }

    private async Task HandleAsync(PaymentCompletedEvent completed)
    {
        using var scope = ServiceProvider.CreateScope();
        var handler = new InvoiceIssuingHandler(
            NullLogger<InvoiceIssuingHandler>.Instance,
            scope.ServiceProvider.GetRequiredService<IPaymentInvoiceService>(),
            scope.ServiceProvider.GetRequiredService<IOptionsMonitor<InvoiceOptions>>());
        await handler.HandleAsync(completed);
    }
}

/// <summary>Storage 承载：产物只生成一次，被换下或作废的那份解除引用。</summary>
public class InvoiceDocumentStorageTests : InvoiceDocumentFixture
{
    protected override bool WithStorage => true;

    private void VerifySavedTimes(Times times)
        => Storage.Verify(s => s.SaveWithReferenceAsync(It.IsAny<string>(), It.IsAny<Stream>(), nameof(Invoice), It.IsAny<Guid>(), nameof(Invoice.PdfFileId), false, false), times);

    /// <summary>★ 重发不再每次新存一份：产物在就直接附上。</summary>
    [Fact]
    public async Task SendingTwice_StoresTheDocumentOnce()
    {
        var id = await CreateManualInvoiceAsync();

        (await SendAsync(id)).Succeeded.ShouldBeTrue();
        (await SendAsync(id)).Succeeded.ShouldBeTrue();

        VerifySavedTimes(Times.Once());
        (await ReloadAsync<Invoice>(id))!.SendCount.ShouldBe(2);
    }

    /// <summary>★ 标记已付让产物作废，并解除这张发票对旧文件的引用（否则它永远回收不了）。</summary>
    [Fact]
    public async Task MarkAsPaid_ReleasesTheStaleDocument()
    {
        var id = await CreateManualInvoiceAsync();
        (await SendAsync(id)).Succeeded.ShouldBeTrue();
        var stale = (await ReloadAsync<Invoice>(id))!.PdfFileId!.Value;

        (await MarkPaidAsync(id)).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Invoice>(id))!.PdfFileId.ShouldBeNull();
        VerifyReleased(stale, id, Times.Once());
    }

    /// <summary>★ 显式重新生成时，被换下的那份解除引用，指针指向新的那份。</summary>
    [Fact]
    public async Task Regenerating_ReleasesTheReplacedDocument()
    {
        var id = await CreateManualInvoiceAsync();
        (await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GeneratePdfAsync(id))).Succeeded.ShouldBeTrue();
        var first = (await ReloadAsync<Invoice>(id))!.PdfFileId!.Value;

        (await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GeneratePdfAsync(id))).Succeeded.ShouldBeTrue();

        var second = (await ReloadAsync<Invoice>(id))!.PdfFileId!.Value;
        second.ShouldNotBe(first);
        VerifyReleased(first, id, Times.Once());
        VerifyReleased(second, id, Times.Never());
    }

    /// <summary>
    /// ★ 存储保存失败、回退本地时，旧的存储产物 Id 必须一起清掉并解除引用：
    /// 此前它留着，附件与下载按 Id 优先，发出去、下载到的仍是旧的那份。
    /// </summary>
    [Fact]
    public async Task FallingBackToLocal_DropsAndReleasesTheStoredDocument()
    {
        var id = await CreateManualInvoiceAsync();
        await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GeneratePdfAsync(id));
        var stored = (await ReloadAsync<Invoice>(id))!.PdfFileId!.Value;

        StorageSaveSucceeds = false;
        (await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GeneratePdfAsync(id))).Succeeded.ShouldBeTrue();

        var reloaded = (await ReloadAsync<Invoice>(id))!;
        reloaded.PdfFileId.ShouldBeNull();
        Path.IsPathFullyQualified(reloaded.PdfFilePath!).ShouldBeTrue(reloaded.PdfFilePath);
        VerifyReleased(stored, id, Times.Once());
    }

    /// <summary>★ 产物在、只是生成时没取到地址：取地址时现取，不重新生成一份。</summary>
    [Fact]
    public async Task GetPdfUrl_WithADocumentButNoAddress_FetchesItWithoutRegenerating()
    {
        var id = await CreateManualInvoiceAsync();
        StorageUrlSucceeds = false;
        await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GeneratePdfAsync(id));
        var fileId = (await ReloadAsync<Invoice>(id))!.PdfFileId!.Value;

        StorageUrlSucceeds = true;
        var url = await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GetPdfUrlAsync(id));

        url.Data.ShouldBe($"/api/files/{fileId}/download");
        (await ReloadAsync<Invoice>(id))!.PdfFileUrl.ShouldBe(url.Data);
        VerifySavedTimes(Times.Once());
    }
}
