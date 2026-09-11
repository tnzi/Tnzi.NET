namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 「我的发票」与发票文件下载这两条**用户实际会走**的路径。
/// </summary>
/// <remarks>
/// 两条此前都不通，而且都是安静地不通：
/// <list type="number">
/// <item>列表按 <c>CreatorId</c> 过滤，而自动开票发生在支付完成事件的处理器里
///   （webhook 匿名 / 后台扣款的裸 scope），那里没有当前用户，<c>CreatorId</c> 恒为 null ——
///   于是这个列表**永远是空的**，而接口 200、发票一张不少地躺在库里。</item>
/// <item><c>PdfFileUrl</c> 写的是下载端点自己的路径，而那个端点返回的正是这个字符串 ——
///   一条指向自己的链接，任何客户端顺着它走都只会再拿到同一个字符串。</item>
/// </list>
/// </remarks>
public class InvoiceUserAccessTests : BillingIntegrationTestBase
{
    private static readonly Guid Buyer = Guid.NewGuid();

    private static PaymentEntity PaymentOf(Guid userId, decimal amount = 100m)
    {
        var payment = SucceededPayment(amount);
        payment.UserId = userId;
        return payment;
    }

    /// <summary>
    /// 自动开票没有当前用户，发票的 <c>CreatorId</c> 因此为 null。
    /// 「我的发票」必须按发票归属的用户过滤，而不是按谁创建了这一行。
    /// </summary>
    [Fact]
    public async Task MyInvoices_ReturnsAutomaticallyIssuedInvoices()
    {
        var payment = PaymentOf(Buyer);
        await SeedAsync(payment);

        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateFromPaymentAsync(payment.Id, null));
        created.Succeeded.ShouldBeTrue();

        var mine = await InScopeAsync<IPaymentInvoiceService, Result<IPagedList<PaymentInvoiceDto>>>(
            s => s.GetUserInvoicesAsync(Buyer));

        mine.Succeeded.ShouldBeTrue();
        mine.Data!.Items.Select(i => i.Id).ShouldContain(created.Data!.Id);
    }

    [Fact]
    public async Task MyInvoices_ExcludesOtherPeoplesInvoices()
    {
        var mine = PaymentOf(Buyer);
        var someoneElse = PaymentOf(Guid.NewGuid());
        await SeedAsync(mine, someoneElse);

        await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(mine.Id, null));
        await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(someoneElse.Id, null));

        var listed = await InScopeAsync<IPaymentInvoiceService, Result<IPagedList<PaymentInvoiceDto>>>(
            s => s.GetUserInvoicesAsync(Buyer));

        listed.Data!.Items.Count.ShouldBe(1);
    }

    /// <summary>
    /// 下载路径要真的拿得到字节。未加载 Storage 的宿主把产物写在本地磁盘上，
    /// 任何 URL 都指不到它，所以「取内容」必须是一条独立于「取地址」的路径。
    /// </summary>
    [Fact]
    public async Task DownloadingTheDocument_ReturnsTheActualBytes()
    {
        var payment = PaymentOf(Buyer);
        await SeedAsync(payment);

        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateFromPaymentAsync(payment.Id, null));

        var document = await InScopeAsync<IPaymentInvoiceService, Result<InvoiceDocumentDto>>(
            s => s.GetPdfContentAsync(created.Data!.Id, Buyer));

        document.Succeeded.ShouldBeTrue();
        document.Data!.Content.Length.ShouldBeGreaterThan(0);
        document.Data.FileName.ShouldStartWith(created.Data!.InvoiceNo);
        document.Data.ContentType.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DownloadingSomeoneElsesDocument_IsRefused()
    {
        var payment = PaymentOf(Guid.NewGuid());
        await SeedAsync(payment);

        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateFromPaymentAsync(payment.Id, null));

        var document = await InScopeAsync<IPaymentInvoiceService, Result<InvoiceDocumentDto>>(
            s => s.GetPdfContentAsync(created.Data!.Id, Buyer));

        document.Succeeded.ShouldBeFalse();
        document.Code.ShouldBe(404);
    }

    /// <summary>
    /// 没有 Storage 时不存在 HTTP 可达的地址，那就<b>留空</b>，
    /// 而不是编一个指向自己的路径 —— 后者让前端以为有链接可用。
    /// </summary>
    [Fact]
    public async Task WithoutStorage_TheShareableUrlIsEmptyRatherThanSelfReferential()
    {
        var payment = PaymentOf(Buyer);
        await SeedAsync(payment);

        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateFromPaymentAsync(payment.Id, null));

        var url = await InScopeAsync<IPaymentInvoiceService, Result<string>>(
            s => s.GetPdfUrlAsync(created.Data!.Id, Buyer));

        url.Succeeded.ShouldBeTrue();
        url.Data.ShouldBeNullOrEmpty();
    }

    /// <summary>
    /// 通知模块没加载是「这台宿主不提供这项能力」，不是故障：
    /// 500 会让监控与客户端一直重试一件永远不会好的事。
    /// </summary>
    [Fact]
    public async Task SendingWithoutTheNotificationModule_Answers501()
    {
        var payment = PaymentOf(Buyer);
        await SeedAsync(payment);

        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateFromPaymentAsync(payment.Id, null));

        var sent = await InScopeAsync<IPaymentInvoiceService, Result>(
            s => s.SendAsync(created.Data!.Id, "ada@example.com", Buyer));

        sent.Succeeded.ShouldBeFalse();
        sent.Code.ShouldBe(501);
        sent.Message!.ShouldContain("Tnzi.Notification");
    }
}
