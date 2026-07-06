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
}
