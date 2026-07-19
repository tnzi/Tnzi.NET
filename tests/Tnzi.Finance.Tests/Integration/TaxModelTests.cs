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
