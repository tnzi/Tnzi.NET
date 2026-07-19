namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 薪资分配：生效日解析（后一条自然截断前一条）、同日唯一 409、外键校验、删除重建
/// </summary>
public class SalaryAssignmentTests : PayrollIntegrationTestBase
{
    private async Task<(Guid EmployeeId, Guid StructureId)> SeedAsync(string suffix)
    {
        var employee = await CreateEmployeeAsync($"ASG-{suffix}", $"Assignee {suffix}");
        var component = await CreateComponentAsync($"ASG_BASIC_{suffix}", formula: "BASE");
        var structure = await CreateStructureAsync($"Assignment Structure {suffix}",
            new SalaryStructureLineInputDto { ComponentId = component.Id, Sequence = 10 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        return (employee.Id, structure.Data!.Id);
    }

    private Task<Result<SalaryAssignmentDto>> CreateAssignmentAsync(Guid employeeId, Guid structureId, DateTime effectiveFrom, decimal baseAmount)
        => InScopeAsync<IEmployeeService, Result<SalaryAssignmentDto>>(s => s.CreateAssignmentAsync(employeeId, new CreateSalaryAssignmentDto
        {
            StructureId = structureId,
            EffectiveFrom = effectiveFrom,
            BaseAmount = baseAmount
        }));

    [Fact]
    public async Task Resolve_PicksLatestEffectiveOnOrBeforeAsOf()
    {
        var (employeeId, structureId) = await SeedAsync("A");

        (await CreateAssignmentAsync(employeeId, structureId, new DateTime(2026, 1, 1), 5000m)).Succeeded.ShouldBeTrue();
        (await CreateAssignmentAsync(employeeId, structureId, new DateTime(2026, 6, 1), 6000m)).Succeeded.ShouldBeTrue();

        // 后一条生效前 → 旧基薪
        var may = await InScopeAsync<IEmployeeService, SalaryAssignment?>(s => s.ResolveAssignmentAsync(employeeId, new DateTime(2026, 5, 31)));
        may.ShouldNotBeNull();
        may.BaseAmount.ShouldBe(5000m);

        // 生效日当天即切换（后一条自然截断前一条）
        var june = await InScopeAsync<IEmployeeService, SalaryAssignment?>(s => s.ResolveAssignmentAsync(employeeId, new DateTime(2026, 6, 1)));
        june.ShouldNotBeNull();
        june.BaseAmount.ShouldBe(6000m);

        // 首条生效前 → 无分配
        var before = await InScopeAsync<IEmployeeService, SalaryAssignment?>(s => s.ResolveAssignmentAsync(employeeId, new DateTime(2025, 12, 31)));
        before.ShouldBeNull();
    }

    [Fact]
    public async Task Create_DuplicateEffectiveDate_Rejected()
    {
        var (employeeId, structureId) = await SeedAsync("B");

        (await CreateAssignmentAsync(employeeId, structureId, new DateTime(2026, 3, 1), 5000m)).Succeeded.ShouldBeTrue();

        var duplicate = await CreateAssignmentAsync(employeeId, structureId, new DateTime(2026, 3, 1), 5500m);
        duplicate.Succeeded.ShouldBeFalse();
        duplicate.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Create_MissingEmployeeOrStructure_Returns404()
    {
        var (employeeId, structureId) = await SeedAsync("C");

        var noEmployee = await CreateAssignmentAsync(Guid.NewGuid(), structureId, new DateTime(2026, 1, 1), 5000m);
        noEmployee.Succeeded.ShouldBeFalse();
        noEmployee.Code.ShouldBe(404);

        var noStructure = await CreateAssignmentAsync(employeeId, Guid.NewGuid(), new DateTime(2026, 1, 1), 5000m);
        noStructure.Succeeded.ShouldBeFalse();
        noStructure.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Create_NegativeBaseAmount_Rejected()
    {
        var (employeeId, structureId) = await SeedAsync("D");

        var result = await CreateAssignmentAsync(employeeId, structureId, new DateTime(2026, 1, 1), -1m);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task DeleteAndRecreate_SameEffectiveDate_Succeeds()
    {
        var (employeeId, structureId) = await SeedAsync("E");

        var first = await CreateAssignmentAsync(employeeId, structureId, new DateTime(2026, 4, 1), 5000m);
        first.Succeeded.ShouldBeTrue(first.Message);

        var deleted = await InScopeAsync<IEmployeeService, Result>(s => s.DeleteAssignmentAsync(employeeId, first.Data!.Id));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);

        // 修正 = 删除重建：同日重新创建必须成功（唯一索引为软删过滤索引）
        var recreated = await CreateAssignmentAsync(employeeId, structureId, new DateTime(2026, 4, 1), 5200m);
        recreated.Succeeded.ShouldBeTrue(recreated.Message);
        recreated.Data!.BaseAmount.ShouldBe(5200m);
    }

    [Fact]
    public async Task DeleteAssignment_OfOtherEmployee_Returns404()
    {
        var (employeeId, structureId) = await SeedAsync("F");
        var other = await CreateEmployeeAsync("ASG-F2", "Other");

        var assignment = await CreateAssignmentAsync(employeeId, structureId, new DateTime(2026, 1, 1), 5000m);
        assignment.Succeeded.ShouldBeTrue(assignment.Message);

        // 用错误的员工上下文删除他人分配 → 404（防横向越权删除）
        var deleted = await InScopeAsync<IEmployeeService, Result>(s => s.DeleteAssignmentAsync(other.Id, assignment.Data!.Id));
        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(404);
    }

    [Fact]
    public async Task GetAssignments_ReturnsStructureName_OrderedByEffectiveFromDesc()
    {
        var (employeeId, structureId) = await SeedAsync("G");

        (await CreateAssignmentAsync(employeeId, structureId, new DateTime(2026, 1, 1), 5000m)).Succeeded.ShouldBeTrue();
        (await CreateAssignmentAsync(employeeId, structureId, new DateTime(2026, 6, 1), 6000m)).Succeeded.ShouldBeTrue();

        var list = await InScopeAsync<IEmployeeService, Result<List<SalaryAssignmentDto>>>(s => s.GetAssignmentsAsync(employeeId));
        list.Succeeded.ShouldBeTrue(list.Message);
        list.Data!.Count.ShouldBe(2);
        list.Data[0].BaseAmount.ShouldBe(6000m);
        list.Data[0].StructureName.ShouldBe("Assignment Structure G");
    }

    [Fact]
    public async Task Create_InactiveStructure_Rejected()
    {
        var (employeeId, structureId) = await SeedAsync("H");

        var structure = await InScopeAsync<ISalaryStructureService, Result<SalaryStructureDto>>(s => s.GetAsync(structureId));
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var deactivated = await InScopeAsync<ISalaryStructureService, Result<SalaryStructureDto>>(s => s.UpdateAsync(structureId, new UpdateSalaryStructureDto
        {
            Name = structure.Data!.Name,
            Frequency = structure.Data.Frequency,
            IsActive = false,
            Lines = structure.Data.Lines.Select(l => new SalaryStructureLineInputDto
            {
                ComponentId = l.ComponentId,
                Sequence = l.Sequence,
                FormulaOverride = l.FormulaOverride,
                AmountOverride = l.AmountOverride,
                ConditionOverride = l.ConditionOverride
            }).ToList()
        }));
        deactivated.Succeeded.ShouldBeTrue(deactivated.Message);

        var result = await CreateAssignmentAsync(employeeId, structureId, new DateTime(2026, 1, 1), 5000m);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }
}
