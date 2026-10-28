using Moq;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 税模型：机构/税率/税码 CRUD、引用保护、组件全量替换、默认计算器（含复合税与舍入）
/// </summary>
public class TaxModelTests : FinanceIntegrationTestBase
{
    private Task<Result<TaxAgencyDto>> CreateAgencyAsync(string name)
        => InScopeAsync<ITaxService, Result<TaxAgencyDto>>(s => s.CreateAgencyAsync(new UpsertTaxAgencyDto { Name = name }));

    private Task<Result<TaxRateDto>> CreateRateAsync(Guid agencyId, string name, decimal rate)
        => InScopeAsync<ITaxService, Result<TaxRateDto>>(s => s.CreateRateAsync(new UpsertTaxRateDto
        {
            AgencyId = agencyId,
            Name = name,
            Rate = rate
        }));

    private Task<Result<TaxCodeDto>> CreateCodeAsync(string name, params (Guid rateId, int order, bool compound)[] components)
        => InScopeAsync<ITaxService, Result<TaxCodeDto>>(s => s.CreateCodeAsync(new UpsertTaxCodeDto
        {
            Name = name,
            Components = components
                .Select(c => new UpsertTaxCodeComponentDto { TaxRateId = c.rateId, Order = c.order, IsCompound = c.compound })
                .ToList()
        }));

    [Fact]
    public async Task Agency_DuplicateName_Rejected_And_ReferencedDelete_Blocked()
    {
        var agency = await CreateAgencyAsync("CRA");
        agency.Succeeded.ShouldBeTrue(agency.Message);

        (await CreateAgencyAsync("CRA")).Code.ShouldBe(409);

        (await CreateRateAsync(agency.Data!.Id, "GST 5%", 5m)).Succeeded.ShouldBeTrue();

        var delete = await InScopeAsync<ITaxService, Result>(s => s.DeleteAgencyAsync(agency.Data.Id));
        delete.Succeeded.ShouldBeFalse();
        delete.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Rate_ReferencedByCode_CannotBeDeleted()
    {
        var agency = await CreateAgencyAsync("Agency A");
        var rate = await CreateRateAsync(agency.Data!.Id, "GST 5%", 5m);
        var code = await CreateCodeAsync("GST", (rate.Data!.Id, 1, false));
        code.Succeeded.ShouldBeTrue(code.Message);

        var delete = await InScopeAsync<ITaxService, Result>(s => s.DeleteRateAsync(rate.Data.Id));
        delete.Succeeded.ShouldBeFalse();
        delete.Code.ShouldBe(409);
    }

    /// <summary>
    /// 回归（B6，生效日语义）：税率一旦被已过账行引用即不可改率（就地改率会让草稿悄悄按新率重算、
    /// 且 TaxSummary 用当前率标注历史金额）；只改名/机构仍可。
    /// </summary>
    [Fact]
    public async Task UpdateRate_ReferencedByPostedLine_RejectsRateChange()
    {
        await SeedCoaAsync();
        var agency = await CreateAgencyAsync("CRA");
        var rate = await CreateRateAsync(agency.Data!.Id, "GST 5%", 5m);
        rate.Succeeded.ShouldBeTrue(rate.Message);

        // 编程式过账一张携带该税率维度的凭证，使税率被已过账行引用
        var post = await InScopeAsync<ILedgerPostingService, Result<JournalEntryDto>>(s => s.PostAsync(new LedgerPostingRequest
        {
            PostingDate = new DateTime(2026, 3, 1),
            SourceType = "Test",
            SourceId = Guid.NewGuid().ToString("N"),
            Lines =
            [
                new LedgerPostingLine { AccountCode = "1120", Debit = 5m },
                new LedgerPostingLine { AccountCode = "3100", Credit = 5m, TaxRateId = rate.Data!.Id }
            ]
        }));
        post.Succeeded.ShouldBeTrue(post.Message);

        // 改率被拒 409
        var changeRate = await InScopeAsync<ITaxService, Result<TaxRateDto>>(s => s.UpdateRateAsync(rate.Data!.Id,
            new UpsertTaxRateDto { AgencyId = agency.Data!.Id, Name = "GST 5%", Rate = 6m, IsActive = true }));
        changeRate.Succeeded.ShouldBeFalse();
        changeRate.Code.ShouldBe(409);

        // 只改名（率不变）仍允许
        var rename = await InScopeAsync<ITaxService, Result<TaxRateDto>>(s => s.UpdateRateAsync(rate.Data!.Id,
            new UpsertTaxRateDto { AgencyId = agency.Data!.Id, Name = "GST (Federal)", Rate = 5m, IsActive = true }));
        rename.Succeeded.ShouldBeTrue(rename.Message);
    }

    [Fact]
    public async Task Code_UpdateReplacesComponents()
    {
        var agency = await CreateAgencyAsync("Agency B");
        var gst = await CreateRateAsync(agency.Data!.Id, "GST 5%", 5m);
        var pst = await CreateRateAsync(agency.Data.Id, "PST 7%", 7m);

        var code = await CreateCodeAsync("GST only", (gst.Data!.Id, 1, false));
        code.Data!.Components.Count.ShouldBe(1);

        var updated = await InScopeAsync<ITaxService, Result<TaxCodeDto>>(s => s.UpdateCodeAsync(code.Data.Id, new UpsertTaxCodeDto
        {
            Name = "GST+PST",
            Components =
            [
                new UpsertTaxCodeComponentDto { TaxRateId = gst.Data.Id, Order = 1 },
                new UpsertTaxCodeComponentDto { TaxRateId = pst.Data!.Id, Order = 2 }
            ]
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.Name.ShouldBe("GST+PST");
        updated.Data.Components.Count.ShouldBe(2);
        updated.Data.Components.Select(c => c.RateName).ShouldBe(new[] { "GST 5%", "PST 7%" });
    }

    /// <summary>
    /// 回归：改名撞上已有税码要答 409，且税码原样保留。组件的删除曾在工作单元之外立即落库，
    /// 连带把已跟踪的改名一起 flush，唯一索引冲突从 409 的 catch 之外抛出，管理端拿到的是 500。
    /// </summary>
    [Fact]
    public async Task Code_RenameToExistingName_Returns409_And_KeepsCode()
    {
        var agency = await CreateAgencyAsync("Agency D");
        var gst = await CreateRateAsync(agency.Data!.Id, "GST 5%", 5m);
        var pst = await CreateRateAsync(agency.Data.Id, "PST 7%", 7m);
        var code = await CreateCodeAsync("GST", (gst.Data!.Id, 1, false));
        code.Succeeded.ShouldBeTrue(code.Message);
        var codeId = code.Data!.Id;
        (await CreateCodeAsync("HST", (pst.Data!.Id, 1, false))).Succeeded.ShouldBeTrue();

        var renamed = await InScopeAsync<ITaxService, Result<TaxCodeDto>>(s => s.UpdateCodeAsync(codeId, new UpsertTaxCodeDto
        {
            Name = "HST",
            Components =
            [
                new UpsertTaxCodeComponentDto { TaxRateId = gst.Data.Id, Order = 1 },
                new UpsertTaxCodeComponentDto { TaxRateId = pst.Data.Id, Order = 2 }
            ]
        }));
        renamed.Succeeded.ShouldBeFalse();
        renamed.Code.ShouldBe(409);

        var stored = (await InScopeAsync<ITaxService, Result<List<TaxCodeDto>>>(s => s.GetCodesAsync()))
            .Data!.Single(c => c.Id == codeId);
        stored.Name.ShouldBe("GST");
        stored.Components.Select(c => c.TaxRateId).ShouldBe(new[] { gst.Data.Id });
    }

    /// <summary>
    /// 回归：「可抵扣」标志必须随 DTO 往返。管理端编辑表单由列表行预填、整单提交，
    /// 返回 DTO 只要漏报一次 false，下一次为任何原因（改描述、改税率）保存都会把它写回 true，
    /// 此后采购税静默改走 TaxReceivable（多报进项、少记成本）。
    /// </summary>
    [Fact]
    public async Task Code_IsRecoverable_SurvivesListPrefilledEdit()
    {
        var agency = await CreateAgencyAsync("Agency R");
        var rate = await CreateRateAsync(agency.Data!.Id, "Sales tax 8%", 8m);

        var created = await InScopeAsync<ITaxService, Result<TaxCodeDto>>(s => s.CreateCodeAsync(new UpsertTaxCodeDto
        {
            Name = "Non-recoverable",
            IsRecoverable = false,
            Components = [new UpsertTaxCodeComponentDto { TaxRateId = rate.Data!.Id, Order = 1 }]
        }));
        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.IsRecoverable.ShouldBeFalse();

        var listed = (await InScopeAsync<ITaxService, Result<List<TaxCodeDto>>>(s => s.GetCodesAsync()))
            .Data!.Single(c => c.Id == created.Data.Id);
        listed.IsRecoverable.ShouldBeFalse();

        // 只改描述，其余字段原样取自列表行（即编辑表单的预填值）
        var updated = await InScopeAsync<ITaxService, Result<TaxCodeDto>>(s => s.UpdateCodeAsync(listed.Id, new UpsertTaxCodeDto
        {
            Name = listed.Name,
            Description = "edited",
            IsActive = listed.IsActive,
            IsRecoverable = listed.IsRecoverable,
            Components = listed.Components
                .Select(c => new UpsertTaxCodeComponentDto { TaxRateId = c.TaxRateId, Order = c.Order, IsCompound = c.IsCompound })
                .ToList()
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.IsRecoverable.ShouldBeFalse();

        // 落库值仍是不可抵扣：采购税整笔进 NonRecoverableTotal，不进进项组件
        var purchase = await InScopeAsync<ITaxCalculator, TaxCalculationResult>(c => c.CalculateAsync(new TaxCalculationRequest
        {
            IsPurchase = true,
            Lines = [new TaxCalculationLine { Amount = 100m, TaxCodeId = listed.Id }]
        }));
        purchase.NonRecoverableTotal.ShouldBe(8m);
        purchase.Components.ShouldBeEmpty();

        // 反向同样成立：可抵扣税码要如实答 true（不能靠 DTO 默认值碰巧对上任何一边）
        var recoverable = await CreateCodeAsync("Recoverable", (rate.Data!.Id, 1, false));
        recoverable.Data!.IsRecoverable.ShouldBeTrue();
        (await InScopeAsync<ITaxService, Result<List<TaxCodeDto>>>(s => s.GetCodesAsync()))
            .Data!.Single(c => c.Id == recoverable.Data.Id).IsRecoverable.ShouldBeTrue();
    }

    // ── 税码删除守卫 ─────────────────────────────────────────
    //
    // 税码软删后被全局过滤器隐藏，而计税器按过滤后的仓储加载它：引用它的每一张草稿从此保存与过账都答
    // 404「Tax code ... not found」，周期性模板每期生成都失败，界面也再显示不出它的名字。软删在界面上不可恢复。

    private async Task<Guid> ReferencedCodeAsync(string name)
    {
        var agency = await CreateAgencyAsync($"Agency {name}");
        var rate = await CreateRateAsync(agency.Data!.Id, $"{name} 5%", 5m);
        var code = await CreateCodeAsync(name, (rate.Data!.Id, 1, false));
        code.Succeeded.ShouldBeTrue(code.Message);
        return code.Data!.Id;
    }

    private async Task ShouldStillExistWithComponentsAsync(Guid codeId)
    {
        var codes = await InScopeAsync<ITaxService, Result<List<TaxCodeDto>>>(s => s.GetCodesAsync());
        var code = codes.Data!.SingleOrDefault(c => c.Id == codeId);
        code.ShouldNotBeNull("拒绝路径不得软删税码");
        code.Components.Count.ShouldBe(1, "拒绝路径不得先把组件硬删掉");
    }

    [Theory]
    [InlineData("invoice")]
    [InlineData("bill")]
    [InlineData("creditMemo")]
    [InlineData("expense")]
    public async Task Code_ReferencedByDocumentLine_DeleteReturns409(string documentKind)
    {
        await SeedCoaAsync();
        var codeId = await ReferencedCodeAsync($"Old GST {documentKind}");
        var revenue = await AccountIdByCodeAsync("4100");
        var expense = await AccountIdByCodeAsync("5200");
        var bank = await AccountIdByCodeAsync("1120");
        var docDate = new DateTime(2026, 3, 10);

        bool drafted;
        switch (documentKind)
        {
            case "invoice":
            {
                var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = "Tax Customer" }));
                drafted = (await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
                {
                    CustomerId = customer.Data!.Id, DocDate = docDate,
                    Lines = [new CreateInvoiceLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m, TaxCodeId = codeId }]
                }))).Succeeded;
                break;
            }
            case "bill":
            {
                var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = "Tax Vendor" }));
                drafted = (await InScopeAsync<IBillService, Result<BillDto>>(s => s.CreateDraftAsync(new CreateBillDto
                {
                    VendorId = vendor.Data!.Id, DocDate = docDate,
                    Lines = [new CreateBillLineDto { AccountId = expense, Quantity = 1, UnitPrice = 100m, TaxCodeId = codeId }]
                }))).Succeeded;
                break;
            }
            case "creditMemo":
            {
                var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = "Tax Credit Customer" }));
                drafted = (await InScopeAsync<ICreditMemoService, Result<CreditMemoDto>>(s => s.CreateDraftAsync(new CreateCreditMemoDto
                {
                    CustomerId = customer.Data!.Id, DocDate = docDate,
                    Lines = [new CreateCreditMemoLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m, TaxCodeId = codeId }]
                }))).Succeeded;
                break;
            }
            default:
                drafted = (await InScopeAsync<IExpenseService, Result<ExpenseDto>>(s => s.CreateDraftAsync(new CreateExpenseDto
                {
                    PaidFromAccountId = bank, DocDate = docDate,
                    Lines = [new CreateExpenseLineDto { AccountId = expense, Amount = 100m, TaxCodeId = codeId }]
                }))).Succeeded;
                break;
        }
        drafted.ShouldBeTrue();

        var delete = await InScopeAsync<ITaxService, Result>(s => s.DeleteCodeAsync(codeId));

        delete.Succeeded.ShouldBeFalse("引用它的草稿从此保存与过账都会 404");
        delete.Code.ShouldBe(409);
        await ShouldStillExistWithComponentsAsync(codeId);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("vendor")]
    [InlineData("item")]
    public async Task Code_ReferencedAsADefault_DeleteReturns409(string holder)
    {
        await SeedCoaAsync();
        var codeId = await ReferencedCodeAsync($"Default {holder}");

        var created = holder switch
        {
            "customer" => (await InScopeAsync<ICustomerService, Result<CustomerDto>>(s => s.CreateAsync(new CreateCustomerDto { Name = "Defaulted", DefaultTaxCodeId = codeId }))).Succeeded,
            "vendor" => (await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = "Defaulted", DefaultTaxCodeId = codeId }))).Succeeded,
            _ => (await InScopeAsync<IItemService, Result<ItemDto>>(s => s.CreateAsync(new CreateItemDto { Name = "Defaulted", DefaultTaxCodeId = codeId }))).Succeeded,
        };
        created.ShouldBeTrue();

        var delete = await InScopeAsync<ITaxService, Result>(s => s.DeleteCodeAsync(codeId));

        delete.Succeeded.ShouldBeFalse("新开单据会从这里预填一个已不存在的税码");
        delete.Code.ShouldBe(409);
        await ShouldStillExistWithComponentsAsync(codeId);
    }

    /// <summary>
    /// 会计内核之外的引用者（周期性模板行、报价单 / 采购订单行）经 <see cref="IMasterDataUsageProvider"/> 作答。
    /// </summary>
    [Fact]
    public async Task Code_ReportedInUseByAUsageProvider_DeleteReturns409WithItsDetail()
    {
        var codeId = await ReferencedCodeAsync("Provider-held");
        var provider = new Mock<IMasterDataUsageProvider>();
        provider.Setup(p => p.FindUsageAsync(FinanceMasterDataKind.TaxCode, codeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasterDataUsage("Held by a template line."));

        Result delete;
        using (var scope = ServiceProvider.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var service = new TaxService(
                sp,
                sp.GetRequiredService<IRepository<TaxAgency, Guid>>(),
                sp.GetRequiredService<IRepository<TaxRate, Guid>>(),
                sp.GetRequiredService<IRepository<TaxCode, Guid>>(),
                sp.GetRequiredService<IRepository<TaxCodeComponent, Guid>>(),
                sp.GetRequiredService<IReadOnlyRepository<JournalLine, Guid>>(),
                [provider.Object]);
            delete = await service.DeleteCodeAsync(codeId);
        }

        delete.Succeeded.ShouldBeFalse();
        delete.Code.ShouldBe(409);
        delete.Message.ShouldBe("Held by a template line.");
        await ShouldStillExistWithComponentsAsync(codeId);
    }

    [Fact]
    public async Task Code_Delete_ReleasesRateReference()
    {
        var agency = await CreateAgencyAsync("Agency C");
        var rate = await CreateRateAsync(agency.Data!.Id, "GST 5%", 5m);
        var code = await CreateCodeAsync("Temp", (rate.Data!.Id, 1, false));

        // 组件无软删除，须随税码一并清理——否则残留组件永久阻塞税率删除
        (await InScopeAsync<ITaxService, Result>(s => s.DeleteCodeAsync(code.Data!.Id))).Succeeded.ShouldBeTrue();

        var deleteRate = await InScopeAsync<ITaxService, Result>(s => s.DeleteRateAsync(rate.Data.Id));
        deleteRate.Succeeded.ShouldBeTrue(deleteRate.Message);
    }

    [Fact]
    public async Task Code_RequiresComponents_And_ValidRates()
    {
        var empty = await InScopeAsync<ITaxService, Result<TaxCodeDto>>(s => s.CreateCodeAsync(new UpsertTaxCodeDto
        {
            Name = "Empty",
            Components = []
        }));
        empty.Succeeded.ShouldBeFalse();

        var unknownRate = await CreateCodeAsync("Ghost", (Guid.NewGuid(), 1, false));
        unknownRate.Succeeded.ShouldBeFalse();
        unknownRate.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Calculator_SingleRate_LineLevelRounding()
    {
        var agency = await CreateAgencyAsync("Calc A");
        var rate = await CreateRateAsync(agency.Data!.Id, "GST 5%", 5m);
        var code = await CreateCodeAsync("GST", (rate.Data!.Id, 1, false));

        // 两行各 10.05：行级税 0.5025 → 0.50，合计 1.00（整单计税则是 20.10*5%=1.005 → 1.01，锁定行级语义）
        var result = await InScopeAsync<ITaxCalculator, TaxCalculationResult>(c => c.CalculateAsync(new TaxCalculationRequest
        {
            Lines =
            [
                new TaxCalculationLine { Amount = 10.05m, TaxCodeId = code.Data!.Id },
                new TaxCalculationLine { Amount = 10.05m, TaxCodeId = code.Data.Id }
            ]
        }));

        result.TaxTotal.ShouldBe(1.00m);
        result.Components.Single().TaxAmount.ShouldBe(1.00m);
    }

    [Fact]
    public async Task Calculator_CompoundTax_UsesAccumulatedBase()
    {
        var agency = await CreateAgencyAsync("Calc B");
        var gst = await CreateRateAsync(agency.Data!.Id, "GST 5%", 5m);
        var qst = await CreateRateAsync(agency.Data.Id, "QST 9.975%", 9.975m);

        // 魁北克式复合税：QST 税基 = 金额 + GST
        var code = await CreateCodeAsync("GST+QST compound", (gst.Data!.Id, 1, false), (qst.Data!.Id, 2, true));

        var result = await InScopeAsync<ITaxCalculator, TaxCalculationResult>(c => c.CalculateAsync(new TaxCalculationRequest
        {
            Lines = [new TaxCalculationLine { Amount = 100m, TaxCodeId = code.Data!.Id }]
        }));

        // GST = 5.00；QST = (100 + 5) * 9.975% = 10.47375 → 10.47
        result.Components.Single(c => c.RateName.StartsWith("GST")).TaxAmount.ShouldBe(5.00m);
        result.Components.Single(c => c.RateName.StartsWith("QST")).TaxAmount.ShouldBe(10.47m);
        result.TaxTotal.ShouldBe(15.47m);
    }

    [Fact]
    public async Task Calculator_NoTaxCode_LinesAreExempt()
    {
        var result = await InScopeAsync<ITaxCalculator, TaxCalculationResult>(c => c.CalculateAsync(new TaxCalculationRequest
        {
            Lines = [new TaxCalculationLine { Amount = 100m, TaxCodeId = null }]
        }));

        result.TaxTotal.ShouldBe(0m);
        result.Components.ShouldBeEmpty();
    }

    [Fact]
    public async Task Calculator_UnknownOrInactiveCode_Throws()
    {
        await Should.ThrowAsync<BusinessException>(() =>
            InScopeAsync<ITaxCalculator, TaxCalculationResult>(c => c.CalculateAsync(new TaxCalculationRequest
            {
                Lines = [new TaxCalculationLine { Amount = 100m, TaxCodeId = Guid.NewGuid() }]
            })));
    }

    [Fact]
    public async Task Calculator_ManualOverride_SingleComponent_ReplacesComputedTax()
    {
        var agency = await CreateAgencyAsync("Override A");
        var rate = await CreateRateAsync(agency.Data!.Id, "HST 13%", 13m);
        var code = await CreateCodeAsync("HST", (rate.Data!.Id, 1, false));

        // 覆盖行按小票实际税额 13.05（按率计算是 13.00）；正常行不受影响，同税率桶累加
        var result = await InScopeAsync<ITaxCalculator, TaxCalculationResult>(c => c.CalculateAsync(new TaxCalculationRequest
        {
            Lines =
            [
                new TaxCalculationLine { Amount = 100m, TaxCodeId = code.Data!.Id, TaxAmount = 13.05m },
                new TaxCalculationLine { Amount = 100m, TaxCodeId = code.Data.Id }
            ]
        }));

        result.TaxTotal.ShouldBe(26.05m);
        result.Components.Single().TaxAmount.ShouldBe(26.05m);
    }

    [Fact]
    public async Task Calculator_ManualOverride_CompoundComponents_ProratesWithResidualToLast()
    {
        var agency = await CreateAgencyAsync("Override B");
        var gst = await CreateRateAsync(agency.Data!.Id, "GST 5%", 5m);
        var qst = await CreateRateAsync(agency.Data.Id, "QST 9.975%", 9.975m);
        var code = await CreateCodeAsync("GST+QST override", (gst.Data!.Id, 1, false), (qst.Data!.Id, 2, true));

        // 正常口径 GST 5.00 / QST 10.47（合计 15.47）；覆盖 15.50 按比例分摊：
        // GST = round(15.50 * 5.00 / 15.47) = 5.01；QST = 15.50 - 5.01 = 10.49（尾差归最后一个组件）
        var result = await InScopeAsync<ITaxCalculator, TaxCalculationResult>(c => c.CalculateAsync(new TaxCalculationRequest
        {
            Lines = [new TaxCalculationLine { Amount = 100m, TaxCodeId = code.Data!.Id, TaxAmount = 15.50m }]
        }));

        result.TaxTotal.ShouldBe(15.50m);
        result.Components.Single(c => c.RateName.StartsWith("GST")).TaxAmount.ShouldBe(5.01m);
        result.Components.Single(c => c.RateName.StartsWith("QST")).TaxAmount.ShouldBe(10.49m);
        result.Components.Sum(c => c.TaxAmount).ShouldBe(15.50m);
    }

    /// <summary>回归：3+ 组件按比例分摊覆盖额时，中间份额逐个上舍入不得把末组件推成负值。
    /// 4 个等额组件、override=0.02 → 每份额 0.005 上舍入 0.01，前 3 个即 0.03 超过覆盖额，
    /// 朴素实现末组件 = 0.02−0.03 = −0.01。钳制后所有组件非负且合计恰等于覆盖额。</summary>
    [Fact]
    public async Task Calculator_ManualOverride_MultiComponent_NoNegativeShare()
    {
        var agency = await CreateAgencyAsync("Override N");
        var r1 = await CreateRateAsync(agency.Data!.Id, "T1 5%", 5m);
        var r2 = await CreateRateAsync(agency.Data.Id, "T2 5%", 5m);
        var r3 = await CreateRateAsync(agency.Data.Id, "T3 5%", 5m);
        var r4 = await CreateRateAsync(agency.Data.Id, "T4 5%", 5m);
        var code = await CreateCodeAsync("Four equal", (r1.Data!.Id, 1, false), (r2.Data!.Id, 2, false), (r3.Data!.Id, 3, false), (r4.Data!.Id, 4, false));

        var result = await InScopeAsync<ITaxCalculator, TaxCalculationResult>(c => c.CalculateAsync(new TaxCalculationRequest
        {
            Lines = [new TaxCalculationLine { Amount = 100m, TaxCodeId = code.Data!.Id, TaxAmount = 0.02m }]
        }));

        result.TaxTotal.ShouldBe(0.02m);
        result.Components.ShouldAllBe(c => c.TaxAmount >= 0m);       // 无负份额
        result.Components.Sum(c => c.TaxAmount).ShouldBe(0.02m);      // 合计恰等于覆盖额
    }

    [Fact]
    public async Task Calculator_ManualOverride_Zero_MeansLineHasNoTax()
    {
        var agency = await CreateAgencyAsync("Override C");
        var rate = await CreateRateAsync(agency.Data!.Id, "GST 5%", 5m);
        var code = await CreateCodeAsync("GST zero override", (rate.Data!.Id, 1, false));

        var result = await InScopeAsync<ITaxCalculator, TaxCalculationResult>(c => c.CalculateAsync(new TaxCalculationRequest
        {
            Lines = [new TaxCalculationLine { Amount = 100m, TaxCodeId = code.Data!.Id, TaxAmount = 0m }]
        }));

        result.TaxTotal.ShouldBe(0m);
        result.Components.Single().TaxAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task Calculator_ManualOverride_WithoutTaxCode_Throws()
    {
        // 无税码的覆盖额会漏出按税率维度聚合的申报口径 → 拒绝
        await Should.ThrowAsync<BusinessException>(() =>
            InScopeAsync<ITaxCalculator, TaxCalculationResult>(c => c.CalculateAsync(new TaxCalculationRequest
            {
                Lines = [new TaxCalculationLine { Amount = 100m, TaxCodeId = null, TaxAmount = 5m }]
            })));
    }

    [Fact]
    public async Task Calculator_ManualOverride_Negative_Throws()
    {
        var agency = await CreateAgencyAsync("Override D");
        var rate = await CreateRateAsync(agency.Data!.Id, "GST 5%", 5m);
        var code = await CreateCodeAsync("GST negative override", (rate.Data!.Id, 1, false));

        await Should.ThrowAsync<BusinessException>(() =>
            InScopeAsync<ITaxCalculator, TaxCalculationResult>(c => c.CalculateAsync(new TaxCalculationRequest
            {
                Lines = [new TaxCalculationLine { Amount = 100m, TaxCodeId = code.Data!.Id, TaxAmount = -0.01m }]
            })));
    }
}
