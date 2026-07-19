namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 员工：CRUD、编码唯一、扩展属性校验、影子供应商幂等与名称同步
/// </summary>
public class EmployeeServiceTests : PayrollIntegrationTestBase
{
    [Fact]
    public async Task Employee_Crud_Roundtrip()
    {
        var created = await CreateEmployeeAsync("EMP-1", "Alice Zhang", """{"TAX_STATUS":"SINGLE","DEPENDENTS":2}""");
        created.Code.ShouldBe("EMP-1");
        created.VendorId.ShouldBeNull();

        var updated = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.UpdateAsync(created.Id, new UpdateEmployeeDto
        {
            Code = "EMP-1",
            Name = "Alice Wang",
            Email = "alice@example.com",
            HireDate = new DateTime(2025, 3, 1),
            IsActive = false
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.Name.ShouldBe("Alice Wang");
        updated.Data.HireDate!.Value.Date.ShouldBe(new DateTime(2025, 3, 1));
        updated.Data.IsActive.ShouldBeFalse();

        var deleted = await InScopeAsync<IEmployeeService, Result>(s => s.DeleteAsync(created.Id));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);

        var fetched = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.GetAsync(created.Id));
        fetched.Succeeded.ShouldBeFalse();
        fetched.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Employee_DuplicateCode_Rejected()
    {
        await CreateEmployeeAsync("DUP-1", "First");

        var duplicate = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.CreateAsync(new CreateEmployeeDto
        {
            Code = "DUP-1",
            Name = "Second"
        }));
        duplicate.Succeeded.ShouldBeFalse();
        duplicate.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Employee_InvalidAttributesJson_Rejected()
    {
        var notJson = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.CreateAsync(new CreateEmployeeDto
        {
            Code = "ATTR-1",
            Name = "Bad Attrs",
            AttributesJson = "not json"
        }));
        notJson.Succeeded.ShouldBeFalse();
        notJson.Code.ShouldBe(400);

        var nested = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.CreateAsync(new CreateEmployeeDto
        {
            Code = "ATTR-2",
            Name = "Nested Attrs",
            AttributesJson = """{"nested":{"a":1}}"""
        }));
        nested.Succeeded.ShouldBeFalse();
        nested.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Employee_TerminationBeforeHire_Rejected()
    {
        var result = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.CreateAsync(new CreateEmployeeDto
        {
            Code = "TERM-1",
            Name = "Time Traveler",
            HireDate = new DateTime(2026, 5, 1),
            TerminationDate = new DateTime(2026, 4, 1)
        }));
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task EnsurePayeeVendor_CreatesOnce_AndIsIdempotent()
    {
        var employee = await CreateEmployeeAsync("VEND-1", "Bob Lee");

        var first = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.EnsurePayeeVendorAsync(employee.Id));
        first.Succeeded.ShouldBeTrue(first.Message);
        first.Data!.VendorId.ShouldNotBeNull();

        var vendor = await ReloadAsync<Vendor>(first.Data.VendorId!.Value);
        vendor.ShouldNotBeNull();
        vendor.Name.ShouldBe("Bob Lee");

        // 幂等：二次调用返回同一供应商，不重复创建
        var second = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.EnsurePayeeVendorAsync(employee.Id));
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.VendorId.ShouldBe(first.Data.VendorId);

        (await CountAsync<Vendor>(v => v.Name == "Bob Lee")).ShouldBe(1);
    }

    [Fact]
    public async Task EnsurePayeeVendor_MissingEmployee_Returns404()
    {
        var result = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.EnsurePayeeVendorAsync(Guid.NewGuid()));
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task UpdateEmployee_SyncsLinkedVendorName()
    {
        var employee = await CreateEmployeeAsync("SYNC-1", "Old Name");
        var ensured = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.EnsurePayeeVendorAsync(employee.Id));
        ensured.Succeeded.ShouldBeTrue(ensured.Message);

        var updated = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.UpdateAsync(employee.Id, new UpdateEmployeeDto
        {
            Code = "SYNC-1",
            Name = "New Name"
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);

        var vendor = await ReloadAsync<Vendor>(ensured.Data!.VendorId!.Value);
        vendor.ShouldNotBeNull();
        vendor.Name.ShouldBe("New Name");
    }

    [Fact]
    public async Task DeleteEmployee_WithAssignments_Rejected()
    {
        var employee = await CreateEmployeeAsync("DEL-1", "Keeper");
        var component = await CreateComponentAsync("DEL_BASIC", formula: "BASE");
        var structure = await CreateStructureAsync("Del Structure",
            new SalaryStructureLineInputDto { ComponentId = component.Id, Sequence = 10 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        var assignment = await InScopeAsync<IEmployeeService, Result<SalaryAssignmentDto>>(s =>
            s.CreateAssignmentAsync(employee.Id, new CreateSalaryAssignmentDto
            {
                StructureId = structure.Data!.Id,
                EffectiveFrom = new DateTime(2026, 1, 1),
                BaseAmount = 5000m
            }));
        assignment.Succeeded.ShouldBeTrue(assignment.Message);

        var deleted = await InScopeAsync<IEmployeeService, Result>(s => s.DeleteAsync(employee.Id));
        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
    }
}
