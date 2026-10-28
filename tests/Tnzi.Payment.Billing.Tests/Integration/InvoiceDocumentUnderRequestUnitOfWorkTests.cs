using Tnzi.Domain.Entities;
using Tnzi.Storage.Entities;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>存储替身的文件记录（只在本测试项目里有）。</summary>
public class StagedDocument : EntityBase<Guid>
{
    public byte[] Content { get; set; } = [];
}

/// <summary>
/// 宿主开着请求级工作单元（<c>EnableGlobalUnitOfWork</c>）时的发票产物：生成、读回、发信失败，
/// 都不能让发票留下一个指向不存在文件的指针。
/// </summary>
/// <remarks>
/// <para>
/// 存储替身按真实存储仓储的方式保存文件记录：工作单元开着事务时插入停在变更跟踪器里，到第一次保存才落库；
/// 读取只认库里已有的行（真实存储的读权限判据看的是已落库的创建人与引用）。每个「请求」照
/// <c>UnitOfWorkFilter</c> 的做法：开事务、执行、成功提交、失败回滚。
/// </para>
/// <para>
/// 消费方实测：首期收据开出后第一次下载生成了产物，指针以裸 SQL 自动提交，记录却还没落库，
/// 同一请求读回答「找不到」、请求以 404 结束并回滚，此后这张收据永远 404。
/// </para>
/// </remarks>
public class InvoiceDocumentUnderRequestUnitOfWorkTests : BillingIntegrationTestBase
{
    private readonly Mock<INotificationService> _notifications = new();
    private bool _emailSucceeds = true;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        _notifications
            .Setup(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _emailSucceeds
                ? Result.Success(new NotificationInfo())
                : Result.Failure<NotificationInfo>("SMTP relay refused the message", 502));
        services.AddScoped(_ => _notifications.Object);

        services.AddScoped(sp => StagedStorage(sp.GetRequiredService<BillingTestDbContext>(), sp.GetRequiredService<IUnitOfWorkManager>()));
    }

    private static IFileStorageService StagedStorage(BillingTestDbContext db, IUnitOfWorkManager uow)
    {
        var storage = new Mock<IFileStorageService>();
        storage
            .Setup(s => s.SaveWithReferenceAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(async (string fileName, Stream stream, string _, Guid __, string ___, bool ____, bool _____) =>
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                var record = new StagedDocument { Id = Guid.NewGuid(), Content = buffer.ToArray() };
                db.Add(record);
                // 与 EfCoreRepository.InsertAsync 相同：事务开着就等工作单元保存
                if (!uow.IsEnabledTransaction)
                    await db.SaveChangesAsync();
                return Result.Success(new FileRecord { Id = record.Id, FileName = fileName, Path = $"docs/{fileName}" });
            });
        storage
            .Setup(s => s.GetAsync(It.IsAny<Guid>()))
            .Returns(async (Guid id) =>
            {
                var row = await db.Set<StagedDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
                return row == null
                    ? Result.Failure<Stream>("File record not found", 404)
                    : Result.Success<Stream>(new MemoryStream(row.Content));
            });
        storage
            .Setup(s => s.GetUrlAsync(It.IsAny<Guid>(), It.IsAny<int?>()))
            .ReturnsAsync(Result.Failure<string>("No URL in tests", 404));
        return storage.Object;
    }

    /// <summary>一次「请求」：与 UnitOfWorkFilter 相同，开事务、执行、按结果提交或回滚。</summary>
    private async Task<TResult> InRequestAsync<TResult>(Func<IPaymentInvoiceService, Task<TResult>> action, Func<TResult, bool> succeeded)
    {
        using var scope = ServiceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        uow.EnableTransaction();
        var result = await action(scope.ServiceProvider.GetRequiredService<IPaymentInvoiceService>());
        if (succeeded(result))
            await uow.CommitTransactionAsync();
        else
            await uow.RollbackTransactionAsync();
        return result;
    }

    private Task<Result<InvoiceDocumentDto>> DownloadAsync(Guid invoiceId) =>
        InRequestAsync(s => s.GetPdfContentAsync(invoiceId), r => r.Succeeded);

    private async Task<Guid> IssueAsync(string customerEmail)
    {
        var payment = new PaymentEntity
        {
            TradeNo = $"PAY{Guid.NewGuid():N}"[..20],
            BusinessOrderNo = "SUB-1",
            BusinessType = BusinessType.Subscription,
            ChannelCode = "Null",
            OriginalAmount = 100m,
            TaxAmount = 13m,
            PayableAmount = 113m,
            PaidAmount = 113m,
            Currency = "CAD",
            Status = PaymentStatus.Succeeded,
            PaidTime = DateTime.UtcNow,
            CustomerName = "Northwind",
            CustomerEmail = customerEmail
        };
        await SeedAsync(payment);
        var issued = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(payment.Id));
        issued.Succeeded.ShouldBeTrue(issued.Message);
        return issued.Data!.Id;
    }

    private async Task<bool> DocumentRowExistsAsync(Guid? fileId)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingTestDbContext>();
        return fileId.HasValue && await db.Set<StagedDocument>().AnyAsync(d => d.Id == fileId);
    }

    [Fact]
    public async Task TheFirstDownload_GeneratesAStoredDocumentThatCanBeReadBack()
    {
        var invoiceId = await IssueAsync(customerEmail: string.Empty);

        var first = await DownloadAsync(invoiceId);

        first.Succeeded.ShouldBeTrue(first.Message);
        first.Data!.Content.ShouldNotBeEmpty();
        var invoice = await ReloadAsync<Invoice>(invoiceId);
        (await DocumentRowExistsAsync(invoice!.PdfFileId)).ShouldBeTrue("the invoice must never point at a file that was rolled back");

        (await DownloadAsync(invoiceId)).Succeeded.ShouldBeTrue("a receipt that exists stays downloadable");
    }

    [Fact]
    public async Task AFailedEmail_DoesNotCostTheDocument()
    {
        var invoiceId = await IssueAsync(customerEmail: "billing@northwind.example");
        _emailSucceeds = false;

        var sent = await InRequestAsync(s => s.SendAsync(invoiceId, null), r => r.Succeeded);
        sent.Succeeded.ShouldBeFalse();

        var invoice = await ReloadAsync<Invoice>(invoiceId);
        if (invoice!.PdfFileId.HasValue)
            (await DocumentRowExistsAsync(invoice.PdfFileId)).ShouldBeTrue("the pointer and its file go back together");
        (await DownloadAsync(invoiceId)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task AnInvoicePointingAtAMissingFile_IsGeneratedAgainOnDownload()
    {
        var invoiceId = await IssueAsync(customerEmail: string.Empty);
        var dangling = Guid.NewGuid();
        await InScopeAsync<IRepository<Invoice, Guid>, int>(repo => repo.AsQueryable()
            .Where(i => i.Id == invoiceId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.PdfFileId, dangling).SetProperty(i => i.PdfFilePath, "docs/gone.html")));

        var download = await DownloadAsync(invoiceId);

        download.Succeeded.ShouldBeTrue(download.Message);
        var invoice = await ReloadAsync<Invoice>(invoiceId);
        invoice!.PdfFileId.ShouldNotBe(dangling);
        (await DocumentRowExistsAsync(invoice.PdfFileId)).ShouldBeTrue();
    }
}
