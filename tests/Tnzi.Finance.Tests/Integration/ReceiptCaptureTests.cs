
namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// P3 块 4：收据采集 / 提取（桩）/ 转换止步草稿 / 生命周期
/// </summary>
public class ReceiptCaptureTests : FinanceIntegrationTestBase
{
    private Task<Guid> ExpenseAccountIdAsync() => AccountIdByCodeAsync("5200");
    private Task<Guid> BankLedgerIdAsync() => AccountIdByCodeAsync("1120");

    private async Task<Guid> CreateReceiptAsync(string? currency = "USD")
    {
        var result = await InScopeAsync<IReceiptCaptureService, Result<ReceiptDto>>(s => s.CreateAsync(new CreateReceiptDto
        {
            FileId = Guid.NewGuid(),
            FileName = "receipt.pdf",
            Currency = currency
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    private async Task<Guid> CreateVendorAsync(string name)
    {
        var result = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = name }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    private ReceiptCaptureService BuildServiceWithExtractor(IServiceProvider sp, IReceiptExtractor? extractor)
        => new(
            sp,
            sp.GetRequiredService<IRepository<Receipt, Guid>>(),
            sp.GetRequiredService<IReadOnlyRepository<Vendor, Guid>>(),
            sp.GetRequiredService<IExpenseService>(),
            sp.GetRequiredService<IBillService>(),
            extractor);

    [Fact]
    public async Task Extract_NoExtractor_Returns501()
    {
        await SeedCoaAsync();
        var receipt = await CreateReceiptAsync();

        // 基类未注册 IReceiptExtractor → 服务可选注入为 null → 501
        var result = await InScopeAsync<IReceiptCaptureService, Result<ReceiptDto>>(s => s.ExtractAsync(receipt));
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
    }

    [Fact]
    public async Task Extract_StubExtractor_PersistsFields_AndMatchesVendor()
    {
        await SeedCoaAsync();
        await CreateVendorAsync("Acme Supplies");
        var receipt = await CreateReceiptAsync();

        var stub = new StubExtractor(Result<ReceiptExtractionResult>.Success(new ReceiptExtractionResult
        {
            VendorName = "Acme Supplies",
            DocDate = new DateTime(2026, 7, 5),
            Currency = "USD",
            Subtotal = 90m,
            TaxAmount = 10m,
            Total = 100m,
            Reference = "INV-42",
            Confidence = 0.9m,
            LineItems = { new ReceiptExtractionLineItem { Description = "Widget", Quantity = 2, Amount = 90m } }
        }));

        using var scope = ServiceProvider.CreateScope();
        var service = BuildServiceWithExtractor(scope.ServiceProvider, stub);
        var result = await service.ExtractAsync(receipt);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Status.ShouldBe(ReceiptStatus.Extracted);
        result.Data.Total.ShouldBe(100m);
        result.Data.Reference.ShouldBe("INV-42");
        result.Data.MatchedVendorId.ShouldNotBeNull();
        result.Data.LineItemsJson.ShouldNotBeNull();
    }

    [Fact]
    public async Task Extract_Failure_WritesFailReason_ThenRetrySucceeds()
    {
        await SeedCoaAsync();
        var receipt = await CreateReceiptAsync();

        using var scope = ServiceProvider.CreateScope();
        var failing = BuildServiceWithExtractor(scope.ServiceProvider, new StubExtractor(Result<ReceiptExtractionResult>.Failure("vision timeout", 502)));
        var failed = await failing.ExtractAsync(receipt);
        failed.Succeeded.ShouldBeFalse();

        var afterFail = await ReloadAsync<Receipt>(receipt);
        afterFail!.Status.ShouldBe(ReceiptStatus.Failed);
        afterFail.FailReason.ShouldBe("vision timeout");

        // 重试成功
        using var scope2 = ServiceProvider.CreateScope();
        var ok = BuildServiceWithExtractor(scope2.ServiceProvider, new StubExtractor(Result<ReceiptExtractionResult>.Success(new ReceiptExtractionResult { VendorName = "X", Total = 5m, Confidence = 0.5m })));
        var retried = await ok.ExtractAsync(receipt);
        retried.Succeeded.ShouldBeTrue(retried.Message);
        retried.Data!.Status.ShouldBe(ReceiptStatus.Extracted);
        retried.Data.FailReason.ShouldBeNull();
    }

    /// <summary>
    /// ★ 提取器返回的值真的经过了归一化 —— 这条走的是<b>真实入口</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ReceiptFieldLimitsTests</c> 只能证明判定表本身对；把 <c>ReceiptCaptureService</c> 里
    /// 那一次调用整段删掉，那些纯函数测试<b>照样全绿</b>。所以必须有这一条。
    /// </para>
    /// <para>
    /// ⚠️ <b>本测试库是 SQLite，它不执行 varchar 长度约束</b> —— 所以断言必须落在
    /// 「存下来的值已经是归一化后的形状」，而不是「没有抛异常」。真实库（PG / SQL Server）
    /// 上未归一化的值会让这一步插入失败，那是本机制存在的动机，但在这里测不出来。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Extract_NormalizesWhatTheExtractorReturns()
    {
        await SeedCoaAsync();
        var receipt = await CreateReceiptAsync(currency: "USD");

        using var scope = ServiceProvider.CreateScope();
        var service = BuildServiceWithExtractor(scope.ServiceProvider, new StubExtractor(
            Result<ReceiptExtractionResult>.Success(new ReceiptExtractionResult
            {
                VendorName = new string('A', 400),   // 超列宽 → 截断
                Currency = "US DOLLARS",             // 不是 3 字母代码 → 丢弃，保留原有币种
                Reference = new string('9', 300),    // 超列宽 → 丢弃（不是截断）
                Total = 100m,
                Confidence = 95m                     // 百分比作答 → 0.95
            })));

        var result = await service.ExtractAsync(receipt);

        result.Succeeded.ShouldBeTrue(result.Message);
        var stored = await ReloadAsync<Receipt>(receipt);
        stored!.Confidence.ShouldBe(0.95m);
        stored.VendorName!.Length.ShouldBe(256);
        stored.Reference.ShouldBeNull();
        stored.Currency.ShouldBe("USD");             // 丢弃后回落到创建时的币种，而不是变成 null
    }

    /// <summary>提取器给出的超长失败原因不得把「记下失败」这一步自己搞崩。</summary>
    [Fact]
    public async Task Extract_Failure_WithAVeryLongMessage_StillRecordsTheFailure()
    {
        await SeedCoaAsync();
        var receipt = await CreateReceiptAsync();

        using var scope = ServiceProvider.CreateScope();
        var service = BuildServiceWithExtractor(scope.ServiceProvider,
            new StubExtractor(Result<ReceiptExtractionResult>.Failure(new string('x', 5000), 502)));

        var failed = await service.ExtractAsync(receipt);

        failed.Succeeded.ShouldBeFalse();
        var stored = await ReloadAsync<Receipt>(receipt);
        stored!.Status.ShouldBe(ReceiptStatus.Failed);
        stored.FailReason!.Length.ShouldBe(512);
    }

    /// <summary>人工修正里的坏币种是 400 而不是静默改写，也不是插入时 500。</summary>
    [Fact]
    public async Task UpdateExtraction_RejectsACurrencyThatIsNotAThreeLetterCode()
    {
        await SeedCoaAsync();
        var receipt = await CreateReceiptAsync();

        var result = await InScopeAsync<IReceiptCaptureService, Result<ReceiptDto>>(
            s => s.UpdateExtractionAsync(receipt, new UpdateReceiptExtractionDto
            {
                VendorName = "Acme",
                Currency = "US Dollars"
            }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);

        // 拒绝路径零副作用
        var stored = await ReloadAsync<Receipt>(receipt);
        stored!.VendorName.ShouldBeNull();
        stored.Status.ShouldBe(ReceiptStatus.Uploaded);
    }

    [Fact]
    public async Task Convert_ToExpense_CreatesDraft_AndLinks()
    {
        await SeedCoaAsync();
        var vendor = await CreateVendorAsync("Acme Supplies");
        var receipt = await CreateReceiptAsync();
        await SetExtractedAsync(receipt, "Acme Supplies");

        var accountId = await ExpenseAccountIdAsync();
        var paidFromId = await BankLedgerIdAsync();
        var convert = await InScopeAsync<IReceiptCaptureService, Result<ReceiptConvertResultDto>>(s => s.ConvertAsync(receipt, new ConvertReceiptDto
        {
            DocType = ReceiptDocType.Expense,
            AccountId = accountId,
            PaidFromAccountId = paidFromId
        }));
        convert.Succeeded.ShouldBeTrue(convert.Message);
        convert.Data!.DocType.ShouldBe(nameof(Expense));

        // 产出止步草稿
        var expense = await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.GetAsync(convert.Data.DocId));
        expense.Data!.Status.ShouldBe(FinanceDocumentStatus.Draft);
        expense.Data.Total.ShouldBe(100m);

        var reloaded = await ReloadAsync<Receipt>(receipt);
        reloaded!.Status.ShouldBe(ReceiptStatus.Converted);
        reloaded.ConvertedDocType.ShouldBe(nameof(Expense));
        reloaded.ConvertedDocId.ShouldBe(convert.Data.DocId);
    }

    [Fact]
    public async Task Convert_ToBill_CreatesDraft()
    {
        await SeedCoaAsync();
        await CreateVendorAsync("Acme Supplies");
        var receipt = await CreateReceiptAsync();
        await SetExtractedAsync(receipt, "Acme Supplies");

        var accountId = await ExpenseAccountIdAsync();
        var convert = await InScopeAsync<IReceiptCaptureService, Result<ReceiptConvertResultDto>>(s => s.ConvertAsync(receipt, new ConvertReceiptDto
        {
            DocType = ReceiptDocType.Bill,
            AccountId = accountId
        }));
        convert.Succeeded.ShouldBeTrue(convert.Message);
        convert.Data!.DocType.ShouldBe(nameof(Bill));

        var bill = await InScopeAsync<IBillService, Result<BillDto>>(s => s.GetAsync(convert.Data.DocId));
        bill.Data!.Status.ShouldBe(FinanceDocumentStatus.Draft);
    }

    [Fact]
    public async Task Convert_MissingVendor_Rejects400()
    {
        await SeedCoaAsync();
        var receipt = await CreateReceiptAsync();
        // 提取但无匹配供应商（无同名 Vendor），且不显式传 VendorId
        await SetExtractedAsync(receipt, "Nonexistent Vendor Co");

        var accountId = await ExpenseAccountIdAsync();
        var convert = await InScopeAsync<IReceiptCaptureService, Result<ReceiptConvertResultDto>>(s => s.ConvertAsync(receipt, new ConvertReceiptDto
        {
            DocType = ReceiptDocType.Bill,
            AccountId = accountId
        }));
        convert.Succeeded.ShouldBeFalse();
        convert.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Convert_Twice_Rejects409_And_DeleteConvertedRejected()
    {
        await SeedCoaAsync();
        await CreateVendorAsync("Acme Supplies");
        var receipt = await CreateReceiptAsync();
        await SetExtractedAsync(receipt, "Acme Supplies");

        var accountId = await ExpenseAccountIdAsync();
        var first = await InScopeAsync<IReceiptCaptureService, Result<ReceiptConvertResultDto>>(s => s.ConvertAsync(receipt, new ConvertReceiptDto { DocType = ReceiptDocType.Bill, AccountId = accountId }));
        first.Succeeded.ShouldBeTrue(first.Message);

        var second = await InScopeAsync<IReceiptCaptureService, Result<ReceiptConvertResultDto>>(s => s.ConvertAsync(receipt, new ConvertReceiptDto { DocType = ReceiptDocType.Bill, AccountId = accountId }));
        second.Succeeded.ShouldBeFalse();
        second.Code.ShouldBe(409);

        var delete = await InScopeAsync<IReceiptCaptureService, Result>(s => s.DeleteAsync(receipt));
        delete.Succeeded.ShouldBeFalse();
        delete.Code.ShouldBe(409);
    }

    /// <summary>把收据置为已提取状态（供应商名 + 合计 100），用于转换测试。</summary>
    private async Task SetExtractedAsync(Guid receiptId, string vendorName)
    {
        var result = await InScopeAsync<IReceiptCaptureService, Result<ReceiptDto>>(s => s.UpdateExtractionAsync(receiptId, new UpdateReceiptExtractionDto
        {
            VendorName = vendorName,
            DocDate = new DateTime(2026, 7, 5),
            Currency = "USD",
            Subtotal = 90m,
            TaxAmount = 10m,
            Total = 100m
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    private sealed class StubExtractor : IReceiptExtractor
    {
        private readonly Result<ReceiptExtractionResult> _result;
        public StubExtractor(Result<ReceiptExtractionResult> result) => _result = result;
        public Task<Result<ReceiptExtractionResult>> ExtractAsync(ReceiptExtractionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }
}
