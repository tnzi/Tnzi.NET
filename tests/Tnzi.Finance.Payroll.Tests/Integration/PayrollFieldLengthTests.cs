namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 薪资主数据与发薪批次的文本字段超长时答 400 并点名字段，不留给数据库。
/// </summary>
/// <remarks>
/// 测试库是 SQLite，不检查列宽：超长值在这里「保存成功」，在 SQL Server / PostgreSQL 上是一次不点名字段的截断 500。
/// 所以这些用例断言的是服务层的拒绝本身（与刚好卡在上限的值仍能保存的对照）。
/// </remarks>
public class PayrollFieldLengthTests : PayrollIntegrationTestBase
{
    private static readonly DateTime PeriodStart = new(2026, 6, 1);
    private static readonly DateTime PeriodEnd = new(2026, 6, 30);
    private static readonly DateTime PayDate = new(2026, 7, 5);

    private static void ShouldBeRejected(Result result, string field)
    {
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldContain(field);
    }

    private Task<Result<PayRunDto>> CreateRunAsync(string? memo)
        => InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CreateAsync(new CreatePayRunDto
        {
            PeriodStart = PeriodStart,
            PeriodEnd = PeriodEnd,
            PayDate = PayDate,
            Frequency = PayFrequency.Monthly,
            Memo = memo
        }));

    [Fact]
    public async Task An_oversized_pay_run_memo_is_refused_on_create_and_on_edit()
    {
        ShouldBeRejected(await CreateRunAsync(new string('m', 501)), "memo");

        var created = await CreateRunAsync(new string('m', 500));
        created.Succeeded.ShouldBeTrue(created.Message);

        var edit = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.UpdateAsync(created.Data!.Id, new UpdatePayRunDto
        {
            PeriodStart = PeriodStart,
            PeriodEnd = PeriodEnd,
            PayDate = PayDate,
            Frequency = PayFrequency.Monthly,
            Memo = new string('x', 501)
        }));
        ShouldBeRejected(edit, "memo");
        (await ReloadAsync<PayRun>(created.Data!.Id))!.Memo.ShouldBe(new string('m', 500));
    }

    [Theory]
    [InlineData("employee code")]
    [InlineData("employee name")]
    [InlineData("email")]
    [InlineData("phone")]
    [InlineData("attributes")]
    [InlineData("notes")]
    public async Task An_oversized_employee_field_is_refused_by_name(string field)
    {
        var input = new CreateEmployeeDto { Code = "EMP1", Name = "Jane Doe" };
        switch (field)
        {
            case "employee code": input.Code = new string('C', 33); break;
            case "employee name": input.Name = new string('n', 201); break;
            case "email": input.Email = new string('e', 252) + "@x.io"; break;
            case "phone": input.Phone = new string('1', 33); break;
            case "attributes": input.AttributesJson = $"{{\"note\":\"{new string('a', 4000)}\"}}"; break;
            default: input.Notes = new string('n', 2001); break;
        }

        ShouldBeRejected(await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.CreateAsync(input)), field);
    }

    [Fact]
    public async Task Employee_values_exactly_at_the_column_width_are_accepted()
    {
        var result = await InScopeAsync<IEmployeeService, Result<EmployeeDto>>(s => s.CreateAsync(new CreateEmployeeDto
        {
            Code = new string('C', 32),
            Name = new string('n', 200),
            Phone = new string('1', 32),
            Notes = new string('n', 2000)
        }));

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task An_oversized_assignment_note_is_refused()
    {
        var (structureId, employeeId) = await StandardScenarioAsync();

        var result = await InScopeAsync<IEmployeeService, Result<SalaryAssignmentDto>>(s => s.CreateAssignmentAsync(employeeId, new CreateSalaryAssignmentDto
        {
            StructureId = structureId,
            BaseAmount = 1200m,
            EffectiveFrom = new DateTime(2026, 3, 1),
            Notes = new string('n', 2001)
        }));

        ShouldBeRejected(result, "notes");
    }

    [Fact]
    public async Task Oversized_component_structure_and_bracket_table_names_and_descriptions_are_refused()
    {
        ShouldBeRejected(await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.CreateAsync(new CreateSalaryComponentDto
        {
            Code = "BONUS", Name = new string('n', 201), Type = SalaryComponentType.Earning, Formula = "BASE"
        })), "component name");
        ShouldBeRejected(await InScopeAsync<ISalaryComponentService, Result<SalaryComponentDto>>(s => s.CreateAsync(new CreateSalaryComponentDto
        {
            Code = "BONUS", Name = "Bonus", Type = SalaryComponentType.Earning, Formula = "BASE", Description = new string('d', 501)
        })), "description");

        var component = await CreateComponentAsync("BASIC", formula: "BASE");
        var line = new SalaryStructureLineInputDto { ComponentId = component.Id, Sequence = 1 };
        ShouldBeRejected(await CreateStructureAsync(new string('n', 201), line), "structure name");
        ShouldBeRejected(await InScopeAsync<ISalaryStructureService, Result<SalaryStructureDto>>(s => s.CreateAsync(new CreateSalaryStructureDto
        {
            Name = "Standard", Frequency = PayFrequency.Monthly, Lines = [line], Description = new string('d', 501)
        })), "description");

        ShouldBeRejected(await InScopeAsync<IBracketTableService, Result<BracketTableDto>>(s => s.CreateAsync(new CreateBracketTableDto
        {
            Code = "TAX2", Name = new string('n', 201), EffectiveFrom = new DateTime(2026, 1, 1), Rows = StandardQuickRows().ToList()
        })), "table name");
        ShouldBeRejected(await InScopeAsync<IBracketTableService, Result<BracketTableDto>>(s => s.CreateAsync(new CreateBracketTableDto
        {
            Code = "TAX3", Name = "Tax", Description = new string('d', 501), EffectiveFrom = new DateTime(2026, 1, 1), Rows = StandardQuickRows().ToList()
        })), "description");
    }

    [Fact]
    public async Task An_oversized_one_time_input_note_is_refused()
    {
        var result = await InScopeAsync<IPayRunService, Result<PayRunInputDto>>(s => s.SetInputAsync(Guid.NewGuid(), new SetPayRunInputDto
        {
            EmployeeId = Guid.NewGuid(),
            ComponentId = Guid.NewGuid(),
            Amount = 100m,
            Note = new string('n', 501)
        }));

        ShouldBeRejected(result, "note");
    }

    [Theory]
    [InlineData("payment method")]
    [InlineData("payment reference")]
    public async Task An_oversized_payment_method_or_reference_is_refused(string field)
    {
        var input = new PayRunPaymentDto { PaymentAccountId = Guid.NewGuid(), PaymentDate = PayDate };
        if (field == "payment method")
            input.PaymentMethod = new string('m', 33);
        else
            input.Reference = new string('r', 129);

        ShouldBeRejected(await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PayAsync(Guid.NewGuid(), input)), field);
    }

    [Theory]
    [InlineData("ProviderRunId")]
    [InlineData("memo")]
    public async Task An_oversized_external_run_id_or_memo_is_refused(string field)
    {
        var input = new ExternalPayRunIngestDto
        {
            ProviderRunId = field == "ProviderRunId" ? new string('p', 129) : "RUN-1",
            Source = PayRunSource.External,
            PeriodStart = PeriodStart,
            PeriodEnd = PeriodEnd,
            PayDate = PayDate,
            Frequency = PayFrequency.Monthly,
            Memo = field == "memo" ? new string('m', 501) : null,
            Payslips = [new ExternalPayslipDto { EmployeeCode = "EMP1" }]
        };

        ShouldBeRejected(await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CreateFromExternalAsync(input)), field);
    }
}
