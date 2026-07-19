namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 外部摄取（幂等/AutoPost/未注册组件或员工拒绝）、OpeningBalance（不入 GL 只供 YTD）、
/// 以及 Ytd() 跨批次累计（含 OpeningBalance 计入）的端到端验证。
/// </summary>
public class PayRunExternalAndYtdTests : PayrollIntegrationTestBase
{
    private Task<Result<PayRunDto>> IngestAsync(ExternalPayRunIngestDto dto)
        => InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CreateFromExternalAsync(dto));

    private async Task SeedBasicComponentAsync()
    {
        await SeedCoaAsync();
        await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, formula: null, expenseAccountCode: "5300");
        await ComponentWithAccountsAsync("TAX", SalaryComponentType.Deduction, formula: null, liabilityAccountCode: "2200");
    }

    private static ExternalPayRunIngestDto Ingest(string providerRunId, string employeeCode, PayRunSource source = PayRunSource.External)
        => new()
        {
            ProviderRunId = providerRunId,
            Source = source,
            PeriodStart = new DateTime(2026, 6, 1),
            PeriodEnd = new DateTime(2026, 6, 30),
            PayDate = new DateTime(2026, 7, 5),
            Frequency = PayFrequency.Monthly,
            Payslips = new List<ExternalPayslipDto>
            {
                new()
                {
                    EmployeeCode = employeeCode,
                    Lines = new List<ExternalPayslipLineDto>
                    {
                        new() { ComponentCode = "BASIC", Amount = 1000m },
                        new() { ComponentCode = "TAX", Amount = 100m }
                    }
                }
            }
        };

    [Fact]
    public async Task External_Ingest_CreatesCalculatedRun_ThenAutoPosts()
    {
        await SeedBasicComponentAsync();
        await CreateEmployeeAsync("EMP1", "One");

        var result = await IngestAsync(Ingest("prov-1", "EMP1"));
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Source.ShouldBe(PayRunSource.External);
        result.Data.ProviderRunId.ShouldBe("prov-1");
        // AutoPost 默认 true → 无错则自动过账
        result.Data.Status.ShouldBe(PayRunStatus.Posted);
        result.Data.NetTotal.ShouldBe(900m);
    }

    [Fact]
    public async Task External_Ingest_IsIdempotent_ByProviderRunId()
    {
        await SeedBasicComponentAsync();
        await CreateEmployeeAsync("EMP1", "One");

        var first = await IngestAsync(Ingest("prov-dup", "EMP1"));
        first.Succeeded.ShouldBeTrue(first.Message);

        var second = await IngestAsync(Ingest("prov-dup", "EMP1"));
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.Id.ShouldBe(first.Data!.Id); // 同一批次，未重复创建
    }

    [Fact]
    public async Task External_UnregisteredComponent_Rejected()
    {
        await SeedCoaAsync();
        await CreateEmployeeAsync("EMP1", "One");
        var dto = Ingest("prov-x", "EMP1"); // BASIC/TAX 未 seed

        var result = await IngestAsync(dto);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task External_UnknownEmployee_Rejected()
    {
        await SeedBasicComponentAsync();
        // 不创建员工
        var result = await IngestAsync(Ingest("prov-y", "GHOST"));
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task OpeningBalance_Ingested_NotPosted()
    {
        await SeedBasicComponentAsync();
        await CreateEmployeeAsync("EMP1", "One");

        var dto = Ingest("open-1", "EMP1", PayRunSource.OpeningBalance);
        var result = await IngestAsync(dto);
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Source.ShouldBe(PayRunSource.OpeningBalance);
        result.Data.Status.ShouldBe(PayRunStatus.Calculated); // 不过账

        var run = await ReloadAsync<PayRun>(result.Data.Id);
        run!.Number.ShouldBeNull();

        // 明确禁止对 OpeningBalance run 过账
        var post = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(result.Data.Id));
        post.Succeeded.ShouldBeFalse();
        post.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Ytd_AccumulatesAcrossPostedRuns()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var prior = await ComponentWithAccountsAsync("PRIOR", SalaryComponentType.Earning, "Ytd('BASIC')", expenseAccountCode: "5300");
        var structure = await CreateStructureAsync("Ytd",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = prior, Sequence = 2 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        // Run 1 (June): BASIC=1000, PRIOR=Ytd('BASIC')=0 → gross 1000; post
        var run1 = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run1))).Succeeded.ShouldBeTrue();
        var post1 = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(run1));
        post1.Succeeded.ShouldBeTrue(post1.Message);
        post1.Data!.GrossTotal.ShouldBe(1000m);

        // Run 2 (July): BASIC=1000, PRIOR=Ytd('BASIC')=1000 → gross 2000
        var run2 = await CreateRunAsync(new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), new DateTime(2026, 7, 31));
        var calc2 = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run2));
        calc2.Succeeded.ShouldBeTrue(calc2.Message);
        calc2.Data!.GrossTotal.ShouldBe(2000m);

        // 逐行 YTD 快照：July 的 BASIC 行 YtdAmount = 上期 1000 + 本期 1000 = 2000（合规逐行 YTD）
        var list = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(run2));
        list.Succeeded.ShouldBeTrue(list.Message);
        var slipId = list.Data!.Single().Id;
        var slip = await InScopeAsync<IPayRunService, Result<PayslipDto>>(s => s.GetPayslipAsync(run2, slipId));
        slip.Succeeded.ShouldBeTrue(slip.Message);
        slip.Data!.Lines.Single(l => l.ComponentCode == "BASIC").YtdAmount.ShouldBe(2000m);
    }

    /// <summary>
    /// 回归：post 之后的正常状态推进（Paid/PartiallyPaid）不得让批次掉出 YTD。
    /// 修复前 YTD 滤器仅 Status==Posted，run1 一旦付款成 Paid 就从 Ytd('BASIC') 消失，
    /// 导致法定上限基数归零（此处表现为 PRIOR 回落到 0、gross 从 2000 掉回 1000）。
    /// </summary>
    [Fact]
    public async Task Ytd_IncludesPaidRuns_NotJustPosted()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var prior = await ComponentWithAccountsAsync("PRIOR", SalaryComponentType.Earning, "Ytd('BASIC')", expenseAccountCode: "5300");
        var structure = await CreateStructureAsync("Ytd",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = prior, Sequence = 2 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        // Run 1 (June): calculate → post → PAY（正常推进到 Paid）
        var run1 = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run1))).Succeeded.ShouldBeTrue();
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(run1))).Succeeded.ShouldBeTrue();
        var bankId = await AccountIdByCodeAsync("1120");
        var pay = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PayAsync(run1, new PayRunPaymentDto
        {
            PaymentAccountId = bankId,
            PaymentDate = new DateTime(2026, 6, 30),
            PaymentMethod = "BankTransfer"
        }));
        pay.Succeeded.ShouldBeTrue(pay.Message);
        pay.Data!.Status.ShouldBe(PayRunStatus.Paid);

        // Run 2 (July): PRIOR = Ytd('BASIC') 必须仍含已付款的 run1 = 1000 → gross 2000
        var run2 = await CreateRunAsync(new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), new DateTime(2026, 7, 31));
        var calc2 = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run2));
        calc2.Succeeded.ShouldBeTrue(calc2.Message);
        calc2.Data!.GrossTotal.ShouldBe(2000m);
    }

    [Fact]
    public async Task Ytd_IncludesOpeningBalanceRun()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var prior = await ComponentWithAccountsAsync("PRIOR", SalaryComponentType.Earning, "Ytd('BASIC')", expenseAccountCode: "5300");
        var structure = await CreateStructureAsync("Ytd",
            new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 },
            new SalaryStructureLineInputDto { ComponentId = prior, Sequence = 2 });
        structure.Succeeded.ShouldBeTrue(structure.Message);
        var emp = await CreateEmployeeAsync("EMP1", "One");
        await AssignAsync(emp.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        // 期初余额（年初）灌 BASIC=5000（不过账、只供 Ytd）
        var opening = new ExternalPayRunIngestDto
        {
            ProviderRunId = "open-ytd",
            Source = PayRunSource.OpeningBalance,
            PeriodStart = new DateTime(2026, 1, 1),
            PeriodEnd = new DateTime(2026, 5, 31),
            PayDate = new DateTime(2026, 5, 31),
            Frequency = PayFrequency.Monthly,
            Payslips = new List<ExternalPayslipDto>
            {
                new()
                {
                    EmployeeCode = "EMP1",
                    Lines = new List<ExternalPayslipLineDto> { new() { ComponentCode = "BASIC", Amount = 5000m } }
                }
            }
        };
        (await IngestAsync(opening)).Succeeded.ShouldBeTrue();

        // June run: PRIOR = Ytd('BASIC') = 5000（来自 OpeningBalance）→ gross 6000
        var run = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));
        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(run));
        calc.Succeeded.ShouldBeTrue(calc.Message);
        calc.Data!.GrossTotal.ShouldBe(6000m);
    }
}
