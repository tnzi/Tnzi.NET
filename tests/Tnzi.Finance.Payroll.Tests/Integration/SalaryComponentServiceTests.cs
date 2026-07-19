namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 薪资组件：CRUD、编码规范、公式语法/白名单/自引用校验、唯一索引 409、引用保护
/// </summary>
public class SalaryComponentServiceTests : PayrollIntegrationTestBase
{
    private Task<Result<SalaryComponentDto>> CreateRawAsync(CreateSalaryComponentDto input)
        => InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.CreateAsync(input));

    [Fact]
    public async Task Component_Crud_Roundtrip()
    {
        var created = await CreateComponentAsync("BASIC", formula: "BASE");
        created.Code.ShouldBe("BASIC");

        var updated = await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.UpdateAsync(created.Id, new UpdateSalaryComponentDto
        {
            Code = "BASIC",
            Name = "Basic Salary",
            Type = SalaryComponentType.Earning,
            Formula = "BASE * WORKED_DAYS / PERIOD_DAYS",
            IsActive = true
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.Formula.ShouldBe("BASE * WORKED_DAYS / PERIOD_DAYS");

        var deleted = await InScopeAsync<ISalaryComponentService, Result>(s => s.DeleteAsync(created.Id));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);

        var fetched = await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.GetAsync(created.Id));
        fetched.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Component_CodeIsNormalizedToUpperCase()
    {
        var created = await CreateRawAsync(new CreateSalaryComponentDto
        {
            Code = "  hra_allowance ",
            Name = "HRA",
            Type = SalaryComponentType.Earning
        });
        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Code.ShouldBe("HRA_ALLOWANCE");
    }

    [Fact]
    public async Task Component_InvalidCode_Rejected()
    {
        foreach (var code in new[] { "1BASIC", "BAS IC", "BAS-IC", "工资" })
        {
            var result = await CreateRawAsync(new CreateSalaryComponentDto
            {
                Code = code,
                Name = "Bad Code",
                Type = SalaryComponentType.Earning
            });
            result.Succeeded.ShouldBeFalse($"code '{code}' should be rejected");
            result.Code.ShouldBe(400);
        }
    }

    [Fact]
    public async Task Component_ReservedVariableName_Rejected()
    {
        var result = await CreateRawAsync(new CreateSalaryComponentDto
        {
            Code = "GROSS",
            Name = "Shadowing Built-in",
            Type = SalaryComponentType.Earning
        });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Component_MalformedFormula_Rejected()
    {
        var result = await CreateRawAsync(new CreateSalaryComponentDto
        {
            Code = "BROKEN",
            Name = "Broken",
            Type = SalaryComponentType.Earning,
            Formula = "BASE *"
        });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Component_NonWhitelistedFunction_Rejected()
    {
        var result = await CreateRawAsync(new CreateSalaryComponentDto
        {
            Code = "EVIL",
            Name = "Evil",
            Type = SalaryComponentType.Deduction,
            Formula = "Pow(BASE, 2)"
        });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Component_SelfReference_Rejected()
    {
        var inFormula = await CreateRawAsync(new CreateSalaryComponentDto
        {
            Code = "SELF",
            Name = "Self Formula",
            Type = SalaryComponentType.Earning,
            Formula = "SELF + 1"
        });
        inFormula.Succeeded.ShouldBeFalse();
        inFormula.Code.ShouldBe(400);

        var inCondition = await CreateRawAsync(new CreateSalaryComponentDto
        {
            Code = "SELF",
            Name = "Self Condition",
            Type = SalaryComponentType.Earning,
            Condition = "SELF > 0"
        });
        inCondition.Succeeded.ShouldBeFalse();
        inCondition.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Component_DuplicateCode_Rejected()
    {
        await CreateComponentAsync("DUP_CODE");

        var duplicate = await CreateRawAsync(new CreateSalaryComponentDto
        {
            Code = "dup_code",
            Name = "Casing Duplicate",
            Type = SalaryComponentType.Earning
        });
        duplicate.Succeeded.ShouldBeFalse();
        duplicate.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Component_ReferencedByStructure_CannotBeDeleted()
    {
        var component = await CreateComponentAsync("REF_BASIC", formula: "BASE");
        var structure = await CreateStructureAsync("Ref Structure",
            new SalaryStructureLineInputDto { ComponentId = component.Id, Sequence = 10 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var deleted = await InScopeAsync<ISalaryComponentService, Result>(s => s.DeleteAsync(component.Id));
        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Component_FindByCode_IsCaseInsensitive()
    {
        var created = await CreateComponentAsync("FIND_ME");
        var found = await InScopeAsync<ISalaryComponentService, SalaryComponent?>(s => s.FindByCodeAsync("find_me"));
        found.ShouldNotBeNull();
        found.Id.ShouldBe(created.Id);
    }
}
