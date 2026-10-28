namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 改组件不能绕过结构规则：结构行的生效公式由「行覆盖 ∪ 组件定义」合成，
/// 所以改组件的公式、条件、类型或编码，同样能让一个保存时合规的结构失效。
/// </summary>
/// <remarks>
/// 被保护的缺陷：「读 GROSS 的行必须排在最后一个收入行之后」此前只在结构保存时检查。
/// 把序号靠前的税组件改成读 GROSS，或把序号靠后的扣减项改成收入，两次保存都成功，
/// 此后每一期都对不含后续收入的毛额计税，不落计算错误，照常过账并进入 YTD。
/// </remarks>
public class SalaryComponentStructureRecheckTests : PayrollIntegrationTestBase
{
    private static readonly DateTime PeriodStart = new(2026, 6, 1);
    private static readonly DateTime PeriodEnd = new(2026, 6, 30);
    private static readonly DateTime PayDate = new(2026, 7, 5);

    private Task<Result<SalaryComponentDto>> UpdateAsync(Guid id, string code, SalaryComponentType type, string? formula, string? condition = null)
        => InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.UpdateAsync(id, new UpdateSalaryComponentDto
        {
            Code = code,
            Name = code,
            Type = type,
            Formula = formula,
            Condition = condition,
            IsActive = true
        }));

    /// <summary>BASIC(1, 收入=BASE) / TAX(2, 扣减=BASE×10%) / OVERTIME(3, 收入=BASE×20%)：保存时合规。</summary>
    private async Task<(Guid StructureId, Guid TaxId, Guid OvertimeId)> TaxBeforeOvertimeAsync()
    {
        var basic = await CreateComponentAsync("BASIC", SalaryComponentType.Earning, formula: "BASE");
        var tax = await CreateComponentAsync("TAX", SalaryComponentType.Deduction, formula: "BASE * 0.10");
        var overtime = await CreateComponentAsync("OVERTIME", SalaryComponentType.Earning, formula: "BASE * 0.20");

        var structure = await CreateStructureAsync("Hourly",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = tax.Id, Sequence = 2 },
            new SalaryStructureLineInputDto { ComponentId = overtime.Id, Sequence = 3 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        return (structure.Data!.Id, tax.Id, overtime.Id);
    }

    [Fact]
    public async Task Update_FormulaThatReadsGrossBeforeALaterEarningLine_IsRejected()
    {
        var (_, taxId, _) = await TaxBeforeOvertimeAsync();

        var result = await UpdateAsync(taxId, "TAX", SalaryComponentType.Deduction, "GROSS * 0.10");

        result.Succeeded.ShouldBeFalse("TAX at sequence 2 would tax a GROSS that excludes OVERTIME at sequence 3");
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Hourly");
        result.Message!.ShouldContain("GROSS");
        (await ReloadAsync<SalaryComponent>(taxId))!.Formula.ShouldBe("BASE * 0.10");
    }

    [Fact]
    public async Task Update_ConditionThatReadsGrossBeforeALaterEarningLine_IsRejected()
    {
        var (_, taxId, _) = await TaxBeforeOvertimeAsync();

        var result = await UpdateAsync(taxId, "TAX", SalaryComponentType.Deduction, "BASE * 0.10", condition: "GROSS > 500");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await ReloadAsync<SalaryComponent>(taxId))!.Condition.ShouldBeNull();
    }

    [Fact]
    public async Task Update_TypeChangeThatPutsAnEarningAfterAGrossReader_IsRejected()
    {
        var basic = await CreateComponentAsync("BASIC", SalaryComponentType.Earning, formula: "BASE");
        var tax = await CreateComponentAsync("TAX", SalaryComponentType.Deduction, formula: "GROSS * 0.10");
        var note = await CreateComponentAsync("NOTE", SalaryComponentType.Informational, formula: "BASE * 0.50");
        var structure = await CreateStructureAsync("Monthly",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = tax.Id, Sequence = 2 },
            new SalaryStructureLineInputDto { ComponentId = note.Id, Sequence = 3 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var result = await UpdateAsync(note.Id, "NOTE", SalaryComponentType.Earning, "BASE * 0.50");

        result.Succeeded.ShouldBeFalse("NOTE turning into an earning at sequence 3 leaves TAX reading an incomplete GROSS");
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Monthly");
        (await ReloadAsync<SalaryComponent>(note.Id))!.Type.ShouldBe(SalaryComponentType.Informational);
    }

    [Fact]
    public async Task Update_RenameThatLeavesALaterLineReferencingTheOldCode_IsRejected()
    {
        var basic = await CreateComponentAsync("BASIC", SalaryComponentType.Earning, formula: "BASE");
        var bonus = await CreateComponentAsync("BONUS", SalaryComponentType.Earning, formula: "BASIC * 0.10");
        var structure = await CreateStructureAsync("Bonus",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = bonus.Id, Sequence = 2 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var result = await UpdateAsync(basic.Id, "BASIC_PAY", SalaryComponentType.Earning, "BASE");

        result.Succeeded.ShouldBeFalse("BONUS still reads BASIC, which would no longer exist");
        result.Code.ShouldBe(400);
        (await ReloadAsync<SalaryComponent>(basic.Id))!.Code.ShouldBe("BASIC");
    }

    [Fact]
    public async Task Update_ThatKeepsEveryReferencingStructureValid_IsAccepted()
    {
        var (_, taxId, overtimeId) = await TaxBeforeOvertimeAsync();

        var tax = await UpdateAsync(taxId, "TAX", SalaryComponentType.Deduction, "BASE * 0.15");
        tax.Succeeded.ShouldBeTrue(tax.Message);

        // 最后一个收入行本身读 GROSS 不越序（它之后没有收入行）。
        var overtime = await UpdateAsync(overtimeId, "OVERTIME", SalaryComponentType.Earning, "BASE * 0.20 + GROSS * 0");
        overtime.Succeeded.ShouldBeTrue(overtime.Message);
    }

    [Fact]
    public async Task Update_OfAComponentNoStructureUses_IsNotChecked()
    {
        var loose = await CreateComponentAsync("LOOSE", SalaryComponentType.Deduction, formula: "BASE * 0.10");

        var result = await UpdateAsync(loose.Id, "LOOSE", SalaryComponentType.Deduction, "GROSS * 0.10");

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>
    /// 只拒绝本次修改**引入**的失效：结构本就不合规时（规则之外落库），修复它的那次组件修改、
    /// 以及与之无关的组件修改都不能被挡住，否则连修也修不动。
    /// </summary>
    [Fact]
    public async Task Update_OnAStructureThatIsAlreadyInvalid_IsNotBlocked_SoItCanBeRepaired()
    {
        var (_, taxId, overtimeId) = await TaxBeforeOvertimeAsync();
        await WriteFormulaBypassingServiceAsync(taxId, "GROSS * 0.10");

        var unrelated = await UpdateAsync(overtimeId, "OVERTIME", SalaryComponentType.Earning, "BASE * 0.25");
        unrelated.Succeeded.ShouldBeTrue(unrelated.Message);

        var repair = await UpdateAsync(taxId, "TAX", SalaryComponentType.Deduction, "BASE * 0.10");
        repair.Succeeded.ShouldBeTrue(repair.Message);

        // 修好之后规则重新生效。
        var regress = await UpdateAsync(taxId, "TAX", SalaryComponentType.Deduction, "GROSS * 0.10");
        regress.Succeeded.ShouldBeFalse();
        regress.Code.ShouldBe(400);
    }

    /// <summary>绕过服务直接改库（模拟规则之外落库、或规则生效之前就已存在的数据）。</summary>
    private async Task WriteFormulaBypassingServiceAsync(Guid componentId, string formula)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<SalaryComponent, Guid>>();
        var entity = await repo.FirstOrDefaultAsync(c => c.Id == componentId);
        entity!.Formula = formula;
        await repo.UpdateAsync(entity);
        await repo.SaveChangesAsync();
    }

    /// <summary>
    /// 计算端兜底：规则之外落库的结构（这里直接改库模拟规则生效之前就已存在的数据）
    /// 落 CalculationError，批次过不了账，而不是静默少税。
    /// </summary>
    [Fact]
    public async Task Calculate_StructureThatReadsGrossBeforeALaterEarningLine_FailsThePayslipInsteadOfUnderTaxing()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var tax = await ComponentWithAccountsAsync("TAX", SalaryComponentType.Deduction, "BASE * 0.10", liabilityAccountCode: "2200");
        var overtime = await ComponentWithAccountsAsync("OVERTIME", SalaryComponentType.Earning, "BASE * 0.20", expenseAccountCode: "5300");
        var structure = await CreateStructureAsync("Hourly",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = tax, Sequence = 2 },
            new SalaryStructureLineInputDto { ComponentId = overtime, Sequence = 3 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        var employee = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(employee.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        // 绕过服务直接改库：TAX 改读 GROSS。
        await WriteFormulaBypassingServiceAsync(tax, "GROSS * 0.10");

        var runId = await CreateRunAsync(PeriodStart, PeriodEnd, PayDate);
        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));
        calc.Succeeded.ShouldBeTrue(calc.Message);
        calc.Data!.ErrorCount.ShouldBe(1);

        var slips = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(runId, slips.Data!.Single().Id));
        slip.Data!.CalculationError.ShouldNotBeNull();
        slip.Data.CalculationError.ShouldContain("GROSS");

        var post = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(runId));
        post.Succeeded.ShouldBeFalse();
        post.Code.ShouldBe(400);
    }
}
