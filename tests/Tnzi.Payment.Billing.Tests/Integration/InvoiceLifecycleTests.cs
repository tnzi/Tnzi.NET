namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 发票生命周期在拆分之后照常走通：从支付开票（幂等）、手工开票、标记已付、作废。
/// </summary>
/// <remarks>
/// 服务实现一行未改，这批用例证明的是「搬了程序集之后它仍然接得上」——
/// 实体、配置、DTO、映射、查询扩展与仓储在新程序集里仍然合得起来，
/// 并且读的是父模块的支付实体（子 → 父，唯一允许的方向）。
/// </remarks>
public class InvoiceLifecycleTests : BillingIntegrationTestBase
{
    [Fact]
    public async Task CreateFromPayment_SnapshotsTheCustomerAndAmountsFromThePayment()
    {
        var payment = SucceededPayment(120m);
        await SeedAsync(payment);

        var result = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateFromPaymentAsync(payment.Id, null));

        result.Succeeded.ShouldBeTrue();
        result.Data!.CustomerName.ShouldBe("Ada");
        result.Data.CustomerEmail.ShouldBe("ada@example.com");
        result.Data.Amount.ShouldBe(120m);
        // 已收款的支付开出来的就是「已付」凭据，落 Draft 会让账面凭空多出一笔应收
        result.Data.Status.ShouldBe(InvoiceStatus.Paid);
    }

    /// <summary>
    /// 一笔支付只开一张发票 —— 事件总线是 at-least-once 投递，重投必须拿到同一张。
    /// </summary>
    [Fact]
    public async Task CreateFromPayment_IsIdempotent()
    {
        var payment = SucceededPayment();
        await SeedAsync(payment);

        var first = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateFromPaymentAsync(payment.Id, null));
        var second = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateFromPaymentAsync(payment.Id, null));

        second.Succeeded.ShouldBeTrue();
        second.Data!.Id.ShouldBe(first.Data!.Id);
        second.Data.InvoiceNo.ShouldBe(first.Data.InvoiceNo);
    }

    /// <summary>未收款的支付开不出发票。</summary>
    [Fact]
    public async Task CreateFromPayment_RefusesAPaymentThatHasNotSucceeded()
    {
        var payment = SucceededPayment();
        payment.Status = PaymentStatus.Pending;
        await SeedAsync(payment);

        var result = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateFromPaymentAsync(payment.Id, null));

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.InvoicePaymentNotSucceeded);
        result.Code.ShouldBe(400);
    }

    /// <summary>不存在的支付 → 404，而不是开出一张没有来源的发票。</summary>
    [Fact]
    public async Task CreateFromPayment_ReturnsNotFoundForAnUnknownPayment()
    {
        var result = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateFromPaymentAsync(Guid.NewGuid(), null));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    /// <summary>支付没提供明细时补一行，保证发票总有可打印的内容。</summary>
    [Fact]
    public async Task CreateFromPayment_AlwaysProducesAtLeastOneLine()
    {
        var payment = SucceededPayment(80m);
        await SeedAsync(payment);

        var invoice = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateFromPaymentAsync(payment.Id, null));

        using var scope = ServiceProvider.CreateScope();
        var lines = await scope.ServiceProvider.GetRequiredService<IRepository<InvoiceLineItem, Guid>>()
            .Where(l => l.InvoiceId == invoice.Data!.Id)
            .ToListAsync();

        lines.Count.ShouldBe(1);
        lines[0].Description.ShouldBe("One licence");
        lines[0].Amount.ShouldBe(80m);
    }

    [Fact]
    public async Task CreateManual_RequiresLinesAndACustomer()
    {
        var noLines = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateManualAsync(new CreatePaymentInvoiceDto { CustomerName = "Ada" }));
        noLines.Succeeded.ShouldBeFalse();

        var noCustomer = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateManualAsync(new CreatePaymentInvoiceDto
            {
                LineItems = [new InvoiceLineItemDto { Description = "Work", Quantity = 1, UnitPrice = 10m }]
            }));
        noCustomer.Succeeded.ShouldBeFalse();
    }

    /// <summary>手工开票落草稿，币种取全局默认而不是写死 USD。</summary>
    [Fact]
    public async Task CreateManual_StartsAsADraft()
    {
        var result = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateManualAsync(new CreatePaymentInvoiceDto
            {
                CustomerName = "Ada",
                InvoiceDate = DateTime.UtcNow,
                LineItems =
                [
                    new InvoiceLineItemDto { Description = "Work", Quantity = 2, UnitPrice = 30m }
                ]
            }));

        result.Succeeded.ShouldBeTrue();
        result.Data!.Status.ShouldBe(InvoiceStatus.Draft);
        result.Data.Amount.ShouldBe(60m);
        result.Data.Currency.ShouldBe("USD");
    }

    [Fact]
    public async Task MarkAsPaid_MovesTheInvoiceAndRefusesASecondTime()
    {
        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateManualAsync(NewManualInvoice()));

        var first = await InScopeAsync<IPaymentInvoiceService, Result>(
            s => s.MarkAsPaidAsync(created.Data!.Id, new MarkInvoicePaidDto { PaidAmount = 60m }));
        first.Succeeded.ShouldBeTrue();

        (await ReloadAsync<Invoice>(created.Data!.Id))!.Status.ShouldBe(InvoiceStatus.Paid);

        var second = await InScopeAsync<IPaymentInvoiceService, Result>(
            s => s.MarkAsPaidAsync(created.Data.Id, new MarkInvoicePaidDto { PaidAmount = 60m }));
        second.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.InvoiceAlreadyPaid);
    }

    /// <summary>已付的发票不能作废 —— 作废一张已收款的发票会让账面凭空少一笔收入。</summary>
    [Fact]
    public async Task Cancel_RefusesAPaidInvoice()
    {
        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateManualAsync(NewManualInvoice()));
        await InScopeAsync<IPaymentInvoiceService, Result>(
            s => s.MarkAsPaidAsync(created.Data!.Id, new MarkInvoicePaidDto { PaidAmount = 60m }));

        var result = await InScopeAsync<IPaymentInvoiceService, Result>(
            s => s.CancelAsync(created.Data!.Id, "duplicate"));

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.InvoiceCannotCancel);
    }

    [Fact]
    public async Task Cancel_RecordsTheReasonOnADraft()
    {
        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateManualAsync(NewManualInvoice()));

        var result = await InScopeAsync<IPaymentInvoiceService, Result>(
            s => s.CancelAsync(created.Data!.Id, "duplicate"));

        result.Succeeded.ShouldBeTrue();
        var invoice = await ReloadAsync<Invoice>(created.Data!.Id);
        invoice!.Status.ShouldBe(InvoiceStatus.Cancelled);
        invoice.InternalNotes!.ShouldContain("duplicate");
    }

    /// <summary>
    /// 用户端的查询按归属用户过滤 —— 别人的发票读不到，得到的是 404 而不是别人的数据。
    /// </summary>
    [Fact]
    public async Task Get_ScopedToTheOwner_HidesSomeoneElsesInvoice()
    {
        var owner = Guid.NewGuid();
        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateManualAsync(NewManualInvoice(owner)));

        var asOwner = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.GetAsync(created.Data!.Id, owner));
        asOwner.Succeeded.ShouldBeTrue();

        var asStranger = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.GetAsync(created.Data!.Id, Guid.NewGuid()));
        asStranger.Succeeded.ShouldBeFalse();
        asStranger.Code.ShouldBe(404);
    }

    /// <summary>
    /// 没加载通知模块时发送直接失败并说清原因 —— 不是「发出去了」。
    /// </summary>
    /// <remarks>
    /// 报成功等于给一封没人收到的发票发了张回执：日志干净、状态推进到 Sent、没有退信。
    /// 本测试基类刻意不注册 <c>INotificationService</c>，跑的就是这个现场。
    /// </remarks>
    [Fact]
    public async Task Send_FailsLoudly_WhenNotificationIsNotLoaded()
    {
        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(
            s => s.CreateManualAsync(NewManualInvoice()));

        var result = await InScopeAsync<IPaymentInvoiceService, Result>(
            s => s.SendAsync(created.Data!.Id, null, null));

        result.Succeeded.ShouldBeFalse();
        result.Message!.ShouldContain("Notification");
        (await ReloadAsync<Invoice>(created.Data!.Id))!.Status.ShouldBe(InvoiceStatus.Draft);
    }

    private static CreatePaymentInvoiceDto NewManualInvoice(Guid? userId = null) => new()
    {
        CustomerName = "Ada",
        CustomerEmail = "ada@example.com",
        UserId = userId,
        InvoiceDate = DateTime.UtcNow,
        LineItems = [new InvoiceLineItemDto { Description = "Work", Quantity = 2, UnitPrice = 30m }]
    };
}
