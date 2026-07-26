namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 报价单 / 采购订单：不过账单据的生命周期、编号时点与转换。
/// </summary>
public class OfferDocumentTests : FinanceIntegrationTestBase
{
    private Task<Result<EstimateDto>> EstimateAsync(Func<IEstimateService, Task<Result<EstimateDto>>> action)
        => InScopeAsync<IEstimateService, Result<EstimateDto>>(action);

    private async Task<Guid> CustomerAsync(string name = "Acme Supplies Ltd")
    {
        var r = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = name, Currency = "USD", PaymentTermsDays = 30 }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<Guid> VendorAsync(string name = "Northwind Supply")
    {
        var r = await InScopeAsync<IVendorService, Result<VendorDto>>(
            s => s.CreateAsync(new CreateVendorDto { Name = name, Currency = "USD", PaymentTermsDays = 30 }));
        r.Succeeded.ShouldBeTrue(r.Message);
        return r.Data!.Id;
    }

    private async Task<Result<EstimateDto>> DraftEstimateAsync(Guid customerId, decimal unitPrice = 1200m, Guid? accountId = null)
    {
        var revenue = accountId ?? await AccountIdByCodeAsync("4100");
        return await EstimateAsync(s => s.CreateDraftAsync(new CreateEstimateDto
        {
            CustomerId = customerId,
            DocDate = DateTime.UtcNow.Date,
            ExpiryDate = DateTime.UtcNow.Date.AddDays(30),
            Currency = "USD",
            Memo = "Website rebuild",
            InternalNote = "Priced at 10% discount",
            Lines = new List<CreateOfferLineDto>
            {
                new() { AccountId = revenue, Description = "Design", Quantity = 1, UnitPrice = unitPrice }
            }
        }));
    }

    private async Task<Result<PurchaseOrderDto>> DraftOrderAsync(Guid vendorId, decimal unitPrice = 500m)
    {
        var expense = await AccountIdByCodeAsync("5200");
        return await InScopeAsync<IPurchaseOrderService, Result<PurchaseOrderDto>>(s => s.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            VendorId = vendorId,
            DocDate = DateTime.UtcNow.Date,
            ExpectedDate = DateTime.UtcNow.Date.AddDays(14),
            Currency = "USD",
            Memo = "Office chairs",
            ShipTo = "12 King St W, Toronto ON",
            Lines = new List<CreateOfferLineDto>
            {
                new() { AccountId = expense, Description = "Chair", Quantity = 4, UnitPrice = unitPrice }
            }
        }));
    }

    /// <summary>
    /// ★核心不变量：草稿不占号，编号在**发出**那一刻分配。
    /// </summary>
    /// <remarks>
    /// 报价单没有过账这一步，所以"成为事实"的时点是它离开公司到达客户手里的那一刻。
    /// 若在创建时就发号，被丢弃的草稿会在号段里留下谁也解释不了的缺口。
    /// </remarks>
    [Fact]
    public async Task Estimate_DraftHasNoNumber_SendAllocatesIt()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();

        var draft = await DraftEstimateAsync(customer);
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Number.ShouldBeNull();
        draft.Data.Status.ShouldBe(FinanceOfferStatus.Draft);
        draft.Data.Total.ShouldBe(1200m);

        var sent = await EstimateAsync(s => s.SendAsync(draft.Data.Id));

        sent.Succeeded.ShouldBeTrue(sent.Message);
        sent.Data!.Number.ShouldNotBeNullOrWhiteSpace();
        sent.Data.Number!.ShouldStartWith("EST-");
        sent.Data.Status.ShouldBe(FinanceOfferStatus.Sent);
    }

    /// <summary>
    /// ★不过账：报价单从不产生任何总账分录。
    /// </summary>
    [Fact]
    public async Task Estimate_NeverTouchesTheLedger()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var draft = await DraftEstimateAsync(customer);
        await EstimateAsync(s => s.SendAsync(draft.Data!.Id));
        await EstimateAsync(s => s.AcceptAsync(draft.Data!.Id));

        // 承诺不是事实：接受了报价也还没有收入、没有应收。
        var trialBalance = await InScopeAsync<IFinancialReportService, Result<TrialBalanceReportDto>>(
            s => s.GetTrialBalanceAsync(DateTime.UtcNow.Date.AddYears(-1), DateTime.UtcNow.Date));

        trialBalance.Succeeded.ShouldBeTrue(trialBalance.Message);
        trialBalance.Data!.Rows.Sum(r => r.PeriodDebit + r.PeriodCredit).ShouldBe(0m);
    }

    [Fact]
    public async Task Estimate_SentDocumentCannotBeDeleted()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var draft = await DraftEstimateAsync(customer);
        await EstimateAsync(s => s.SendAsync(draft.Data!.Id));

        var deleted = await InScopeAsync<IEstimateService, Result>(s => s.DeleteDraftAsync(draft.Data!.Id));

        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);

        // 单据仍在，号也仍在：拒绝路径零副作用。
        var reread = await EstimateAsync(s => s.GetAsync(draft.Data!.Id));
        reread.Data!.Number.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Estimate_DraftCanBeDeleted_AndNeverBurnedANumber()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var first = await DraftEstimateAsync(customer);
        var deleted = await InScopeAsync<IEstimateService, Result>(s => s.DeleteDraftAsync(first.Data!.Id));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);

        // 下一张发出的报价单拿到号段的第一个号——被丢弃的草稿没有消耗它。
        var second = await DraftEstimateAsync(customer);
        var sent = await EstimateAsync(s => s.SendAsync(second.Data!.Id));

        sent.Data!.Number.ShouldBe("EST-000001");
    }

    /// <summary>
    /// 重新报价（Declined → Sent）保留原号：客户引用的就是那个号。
    /// </summary>
    [Fact]
    public async Task Estimate_ResendAfterDecline_KeepsTheOriginalNumber()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var draft = await DraftEstimateAsync(customer);
        var sent = await EstimateAsync(s => s.SendAsync(draft.Data!.Id));
        var number = sent.Data!.Number;

        (await EstimateAsync(s => s.DeclineAsync(draft.Data!.Id))).Succeeded.ShouldBeTrue();

        // 改价后重新报出去
        var revenue = await AccountIdByCodeAsync("4100");
        var updated = await EstimateAsync(s => s.UpdateAsync(draft.Data!.Id, new CreateEstimateDto
        {
            CustomerId = customer,
            DocDate = DateTime.UtcNow.Date,
            Currency = "USD",
            Lines = new List<CreateOfferLineDto> { new() { AccountId = revenue, Quantity = 1, UnitPrice = 990m } }
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);

        var resent = await EstimateAsync(s => s.SendAsync(draft.Data!.Id));

        resent.Data!.Number.ShouldBe(number);
        resent.Data.Total.ShouldBe(990m);
    }

    [Fact]
    public async Task Estimate_ExpiryBeforeDocDate_Rejected()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var revenue = await AccountIdByCodeAsync("4100");

        var result = await EstimateAsync(s => s.CreateDraftAsync(new CreateEstimateDto
        {
            CustomerId = customer,
            DocDate = DateTime.UtcNow.Date,
            ExpiryDate = DateTime.UtcNow.Date.AddDays(-1),
            Currency = "USD",
            Lines = new List<CreateOfferLineDto> { new() { AccountId = revenue, Quantity = 1, UnitPrice = 10m } }
        }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>
    /// ★转换止步草稿：把报价变成发票是人的决定，把发票入账是另一个决定。
    /// </summary>
    [Fact]
    public async Task Estimate_ConvertToInvoice_ProducesADraftAndLinksBack()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var draft = await DraftEstimateAsync(customer);
        await EstimateAsync(s => s.SendAsync(draft.Data!.Id));
        await EstimateAsync(s => s.AcceptAsync(draft.Data!.Id));

        var converted = await InScopeAsync<IEstimateService, Result<ConvertOfferResultDto>>(
            s => s.ConvertToInvoiceAsync(draft.Data!.Id, new ConvertOfferDto()));

        converted.Succeeded.ShouldBeTrue(converted.Message);
        converted.Data!.DocType.ShouldBe(FinanceSourceTypes.Invoice);

        var invoice = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.GetAsync(converted.Data.DocId));
        invoice.Data!.Status.ShouldBe(FinanceDocumentStatus.Draft);
        invoice.Data.Number.ShouldBeNull();
        invoice.Data.CustomerId.ShouldBe(customer);
        invoice.Data.Lines.Count.ShouldBe(1);
        invoice.Data.Lines[0].UnitPrice.ShouldBe(1200m);

        // 来源单据回记转换目标，呈现端据此提供"打开那张发票"的钻取。
        var reread = await EstimateAsync(s => s.GetAsync(draft.Data!.Id));
        reread.Data!.Status.ShouldBe(FinanceOfferStatus.Converted);
        reread.Data.ConvertedToDocType.ShouldBe(FinanceSourceTypes.Invoice);
        reread.Data.ConvertedToDocId.ShouldBe(converted.Data.DocId);
    }

    [Fact]
    public async Task Estimate_DoubleConvert_Rejected409_AndCreatesNoSecondInvoice()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var draft = await DraftEstimateAsync(customer);
        await EstimateAsync(s => s.SendAsync(draft.Data!.Id));

        var first = await InScopeAsync<IEstimateService, Result<ConvertOfferResultDto>>(
            s => s.ConvertToInvoiceAsync(draft.Data!.Id, new ConvertOfferDto()));
        first.Succeeded.ShouldBeTrue(first.Message);

        var second = await InScopeAsync<IEstimateService, Result<ConvertOfferResultDto>>(
            s => s.ConvertToInvoiceAsync(draft.Data!.Id, new ConvertOfferDto()));

        second.Succeeded.ShouldBeFalse();
        second.Code.ShouldBe(409);

        var invoices = await InScopeAsync<IInvoiceService, Result<IPagedList<InvoiceDto>>>(
            s => s.GetPagedAsync(new InvoiceQueryDto { PageIndex = 1, PageSize = 50 }));
        invoices.Data!.Items.Count.ShouldBe(1);
    }

    /// <summary>
    /// ★转换目标被删掉后，来源单据必须重新可转换。
    /// </summary>
    /// <remarks>
    /// "转错了，把草稿删掉重来" 是最普通不过的操作。若不自愈，那张报价单会永久
    /// 停在 Converted 指向一个不存在的发票——既打不开也再转不了。判定放在报价单
    /// 这一侧（而不是给发票删除加守卫），依赖方向才不会反过来：报价单知道发票，
    /// 发票对报价单一无所知。
    /// </remarks>
    [Fact]
    public async Task Estimate_ConvertAgain_WhenTheTargetDraftWasDeleted()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var draft = await DraftEstimateAsync(customer);
        await EstimateAsync(s => s.SendAsync(draft.Data!.Id));

        var first = await InScopeAsync<IEstimateService, Result<ConvertOfferResultDto>>(
            s => s.ConvertToInvoiceAsync(draft.Data!.Id, new ConvertOfferDto()));
        first.Succeeded.ShouldBeTrue(first.Message);

        (await InScopeAsync<IInvoiceService, Result>(s => s.DeleteDraftAsync(first.Data!.DocId)))
            .Succeeded.ShouldBeTrue();

        var second = await InScopeAsync<IEstimateService, Result<ConvertOfferResultDto>>(
            s => s.ConvertToInvoiceAsync(draft.Data!.Id, new ConvertOfferDto()));

        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.DocId.ShouldNotBe(first.Data!.DocId);

        var reread = await EstimateAsync(s => s.GetAsync(draft.Data!.Id));
        reread.Data!.ConvertedToDocId.ShouldBe(second.Data.DocId);
    }

    [Fact]
    public async Task Estimate_ConvertedDocumentIsFrozen()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var revenue = await AccountIdByCodeAsync("4100");
        var draft = await DraftEstimateAsync(customer);
        await EstimateAsync(s => s.SendAsync(draft.Data!.Id));
        await InScopeAsync<IEstimateService, Result<ConvertOfferResultDto>>(
            s => s.ConvertToInvoiceAsync(draft.Data!.Id, new ConvertOfferDto()));

        var edit = await EstimateAsync(s => s.UpdateAsync(draft.Data!.Id, new CreateEstimateDto
        {
            CustomerId = customer,
            DocDate = DateTime.UtcNow.Date,
            Currency = "USD",
            Lines = new List<CreateOfferLineDto> { new() { AccountId = revenue, Quantity = 1, UnitPrice = 1m } }
        }));

        edit.Succeeded.ShouldBeFalse();
        edit.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Estimate_DraftCannotBeConverted()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();
        var draft = await DraftEstimateAsync(customer);

        var converted = await InScopeAsync<IEstimateService, Result<ConvertOfferResultDto>>(
            s => s.ConvertToInvoiceAsync(draft.Data!.Id, new ConvertOfferDto()));

        converted.Succeeded.ShouldBeFalse();
        converted.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Estimate_OpenOnly_ExcludesConvertedAndClosed()
    {
        await SeedCoaAsync();
        var customer = await CustomerAsync();

        var live = await DraftEstimateAsync(customer);
        await EstimateAsync(s => s.SendAsync(live.Data!.Id));

        var closed = await DraftEstimateAsync(customer);
        await EstimateAsync(s => s.SendAsync(closed.Data!.Id));
        await EstimateAsync(s => s.CloseAsync(closed.Data!.Id));

        var open = await InScopeAsync<IEstimateService, Result<IPagedList<EstimateDto>>>(
            s => s.GetPagedAsync(new EstimateQueryDto { PageIndex = 1, PageSize = 50, OpenOnly = true }));

        open.Data!.Items.Count.ShouldBe(1);
        open.Data.Items[0].Id.ShouldBe(live.Data!.Id);
    }

    // ── Purchase orders: the mirror image ───────────────────────────

    [Fact]
    public async Task PurchaseOrder_SendAllocatesItsOwnNumberSeries()
    {
        await SeedCoaAsync();
        var vendor = await VendorAsync();

        var draft = await DraftOrderAsync(vendor);
        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Total.ShouldBe(2000m);

        var sent = await InScopeAsync<IPurchaseOrderService, Result<PurchaseOrderDto>>(s => s.SendAsync(draft.Data.Id));

        sent.Succeeded.ShouldBeTrue(sent.Message);
        // 与报价单各走各的号段：两者都从 1 开始，互不牵连。
        sent.Data!.Number.ShouldBe("PO-000001");
        sent.Data.ShipTo.ShouldBe("12 King St W, Toronto ON");
    }

    [Fact]
    public async Task PurchaseOrder_ConvertToBill_ProducesADraftAndLinksBack()
    {
        await SeedCoaAsync();
        var vendor = await VendorAsync();
        var draft = await DraftOrderAsync(vendor);
        await InScopeAsync<IPurchaseOrderService, Result<PurchaseOrderDto>>(s => s.SendAsync(draft.Data!.Id));
        await InScopeAsync<IPurchaseOrderService, Result<PurchaseOrderDto>>(s => s.AcceptAsync(draft.Data!.Id));

        var converted = await InScopeAsync<IPurchaseOrderService, Result<ConvertOfferResultDto>>(
            s => s.ConvertToBillAsync(draft.Data!.Id, new ConvertOfferDto()));

        converted.Succeeded.ShouldBeTrue(converted.Message);
        converted.Data!.DocType.ShouldBe(FinanceSourceTypes.Bill);

        var bill = await InScopeAsync<IBillService, Result<BillDto>>(s => s.GetAsync(converted.Data.DocId));
        bill.Data!.Status.ShouldBe(FinanceDocumentStatus.Draft);
        bill.Data.VendorId.ShouldBe(vendor);
        bill.Data.Lines[0].Quantity.ShouldBe(4m);

        var reread = await InScopeAsync<IPurchaseOrderService, Result<PurchaseOrderDto>>(s => s.GetAsync(draft.Data!.Id));
        reread.Data!.Status.ShouldBe(FinanceOfferStatus.Converted);
        reread.Data.ConvertedToDocId.ShouldBe(converted.Data.DocId);
    }

    [Fact]
    public async Task PurchaseOrder_UnknownVendor_Returns404()
    {
        await SeedCoaAsync();
        var expense = await AccountIdByCodeAsync("5200");

        var result = await InScopeAsync<IPurchaseOrderService, Result<PurchaseOrderDto>>(s => s.CreateDraftAsync(new CreatePurchaseOrderDto
        {
            VendorId = Guid.NewGuid(),
            DocDate = DateTime.UtcNow.Date,
            Currency = "USD",
            Lines = new List<CreateOfferLineDto> { new() { AccountId = expense, Quantity = 1, UnitPrice = 10m } }
        }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }
}
