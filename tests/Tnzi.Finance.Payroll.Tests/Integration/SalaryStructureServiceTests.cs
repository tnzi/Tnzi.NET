namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 薪资结构：依赖序/未知变量校验、覆盖表达式校验、行重建、引用保护
/// </summary>
public class SalaryStructureServiceTests : PayrollIntegrationTestBase
{
    [Fact]
    public async Task Create_OrderedDependencies_Succeeds()
    {
        var basic = await CreateComponentAsync("BASIC", formula: "BASE");
        var hra = await CreateComponentAsync("HRA", formula: "BASIC * 0.4");
        var tax = await CreateComponentAsync("TAX", SalaryComponentType.Deduction, formula: "round(GROSS * 0.1, 2)");

        var created = await CreateStructureAsync("Standard",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 10 },
            new SalaryStructureLineInputDto { ComponentId = hra.Id, Sequence = 20 },
            new SalaryStructureLineInputDto { ComponentId = tax.Id, Sequence = 30 });

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Lines.Count.ShouldBe(3);
        created.Data.Lines.Select(l => l.ComponentCode).ShouldBe(["BASIC", "HRA", "TAX"]);
    }

    [Fact]
    public async Task Create_ForwardReference_Rejected()
    {
        var basic = await CreateComponentAsync("BASIC", formula: "BASE");
        var hra = await CreateComponentAsync("HRA", formula: "BASIC * 0.4");

        // HRA(引用 BASIC) 排在 BASIC 之前 → 越序
        var result = await CreateStructureAsync("Backwards",
            new SalaryStructureLineInputDto { ComponentId = hra.Id, Sequence = 10 },
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 20 });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("BASIC");
    }

    [Fact]
    public async Task Create_UnknownVariable_Rejected()
    {
        var mystery = await CreateComponentAsync("MYSTERY", formula: "NON_EXISTENT * 2");

        var result = await CreateStructureAsync("Mystery",
            new SalaryStructureLineInputDto { ComponentId = mystery.Id, Sequence = 10 });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("NON_EXISTENT");
    }

    [Fact]
    public async Task Create_GrossReferencedBeforeLaterEarning_Rejected()
    {
        var basic = await CreateComponentAsync("BASIC", formula: "BASE");
        var tax = await CreateComponentAsync("TAX", SalaryComponentType.Deduction, formula: "round(GROSS * 0.1, 2)");
        var overtime = await CreateComponentAsync("OT", formula: "BASE * 0.5");

        // TAX(引用 GROSS, seq 20) 排在 OT(Earning, seq 30) 之前 → GROSS 不完整,应拒绝防静默少税
        var result = await CreateStructureAsync("BadGross",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 10 },
            new SalaryStructureLineInputDto { ComponentId = tax.Id, Sequence = 20 },
            new SalaryStructureLineInputDto { ComponentId = overtime.Id, Sequence = 30 });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("GROSS");
    }

    [Fact]
    public async Task Create_BuiltInVariables_AreAlwaysAllowed()
    {
        var component = await CreateComponentAsync("PRORATA",
            formula: "BASE * WORKED_DAYS / PERIOD_DAYS + GROSS * 0 + PERIODS_PER_YEAR * 0");

        var result = await CreateStructureAsync("BuiltIns",
            new SalaryStructureLineInputDto { ComponentId = component.Id, Sequence = 10 });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task Create_DuplicateComponent_Rejected()
    {
        var basic = await CreateComponentAsync("BASIC", formula: "BASE");

        var result = await CreateStructureAsync("Twice",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 10 },
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 20 });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_DuplicateSequence_Rejected()
    {
        var basic = await CreateComponentAsync("BASIC", formula: "BASE");
        var hra = await CreateComponentAsync("HRA", formula: "BASE * 0.4");

        var result = await CreateStructureAsync("SameSeq",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 10 },
            new SalaryStructureLineInputDto { ComponentId = hra.Id, Sequence = 10 });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_EmptyLines_Rejected()
    {
        var result = await CreateStructureAsync("Empty");
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_InactiveComponent_Rejected()
    {
        var component = await CreateComponentAsync("RETIRED", formula: "BASE");
        var deactivated = await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.UpdateAsync(component.Id, new UpdateSalaryComponentDto
        {
            Code = "RETIRED",
            Name = "Retired",
            Type = SalaryComponentType.Earning,
            Formula = "BASE",
            IsActive = false
        }));
        deactivated.Succeeded.ShouldBeTrue(deactivated.Message);

        var result = await CreateStructureAsync("Retired",
            new SalaryStructureLineInputDto { ComponentId = component.Id, Sequence = 10 });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_FormulaOverride_IsValidatedInsteadOfComponentFormula()
    {
        var basic = await CreateComponentAsync("BASIC", formula: "BASE");
        // 组件公式合法，但行级覆盖引用未知变量 → 覆盖生效并被拒
        var result = await CreateStructureAsync("Override",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 10, FormulaOverride = "GHOST + 1" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("GHOST");
    }

    [Fact]
    public async Task Create_ConditionOverride_IsValidated()
    {
        var basic = await CreateComponentAsync("BASIC", formula: "BASE");
        var result = await CreateStructureAsync("CondOverride",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 10, ConditionOverride = "PHANTOM > 0" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain("PHANTOM");
    }

    [Fact]
    public async Task Update_RebuildsLines_HardDeletingOldOnes()
    {
        var basic = await CreateComponentAsync("BASIC", formula: "BASE");
        var hra = await CreateComponentAsync("HRA", formula: "BASIC * 0.4");

        var created = await CreateStructureAsync("Rebuild",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 10 },
            new SalaryStructureLineInputDto { ComponentId = hra.Id, Sequence = 20 });
        created.Succeeded.ShouldBeTrue(created.Message);

        var updated = await InScopeAsync<ISalaryStructureService, Result<SalaryStructureDto>>(s => s.UpdateAsync(created.Data!.Id, new UpdateSalaryStructureDto
        {
            Name = "Rebuilt",
            Frequency = PayFrequency.BiWeekly,
            Lines = [new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 5 }]
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.Lines.Count.ShouldBe(1);
        updated.Data.Frequency.ShouldBe(PayFrequency.BiWeekly);

        (await CountAsync<SalaryStructureLine>(l => l.StructureId == created.Data!.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Delete_WithAssignments_Rejected()
    {
        var employee = await CreateEmployeeAsync("STRUCT-DEL", "Holder");
        var basic = await CreateComponentAsync("BASIC", formula: "BASE");
        var created = await CreateStructureAsync("Assigned",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 10 });
        created.Succeeded.ShouldBeTrue(created.Message);

        var assignment = await InScopeAsync<IEmployeeService, Result<SalaryAssignmentDto>>(s =>
            s.CreateAssignmentAsync(employee.Id, new CreateSalaryAssignmentDto
            {
                StructureId = created.Data!.Id,
                EffectiveFrom = new DateTime(2026, 1, 1),
                BaseAmount = 4000m
            }));
        assignment.Succeeded.ShouldBeTrue(assignment.Message);

        var deleted = await InScopeAsync<ISalaryStructureService, Result>(s => s.DeleteAsync(created.Data!.Id));
        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Delete_CascadesLinesPhysically()
    {
        var basic = await CreateComponentAsync("BASIC", formula: "BASE");
        var created = await CreateStructureAsync("Gone",
            new SalaryStructureLineInputDto { ComponentId = basic.Id, Sequence = 10 });
        created.Succeeded.ShouldBeTrue(created.Message);

        var deleted = await InScopeAsync<ISalaryStructureService, Result>(s => s.DeleteAsync(created.Data!.Id));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);

        (await CountAsync<SalaryStructureLine>(l => l.StructureId == created.Data!.Id)).ShouldBe(0);
    }
}
