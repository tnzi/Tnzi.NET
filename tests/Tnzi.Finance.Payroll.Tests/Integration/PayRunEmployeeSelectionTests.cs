namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 圈选阶段的两条性质：候选面比批次大得多时不能漏人，结构失踪时不能悄悄少一个人。
/// </summary>
public class PayRunEmployeeSelectionTests : PayrollIntegrationTestBase
{
    /// <summary>与 <c>PayslipCalculator.CandidatePageSize</c> 对齐；用来把入选员工推到第二页。</summary>
    private const int CandidatePageSize = 500;

    private Task<Guid> JuneRunAsync()
        => CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 7, 5));

    /// <summary>批量塞入没有薪资分配的在册员工（只撑候选面，不入选）。</summary>
    private async Task SeedInactiveCandidatesAsync(int count)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<Employee, Guid>>();
        // ★ Id 显式给成全零前缀，让这批人确定地排在后面创建的员工之前 ——
        // 靠"先插入的排在前面"是不成立的（顺序 Guid 的排序键不是插入次序）。
        // ★★ 从 1 开始而不是 0：Id 为 Guid.Empty 会被框架当成"未赋值"而重新生成，
        // 那一行于是跑到排序末尾，第一页只剩 499 个填充行、目标员工正好落回第一页。
        // 变异验证抓到了这一点：把分页改成只扫一页，第一版测试照样全绿。
        var rows = Enumerable.Range(0, count).Select(i => new Employee
        {
            Id = new Guid($"00000000-0000-0000-0000-{i + 1:D12}"),
            Code = $"FILL{i:D4}",
            Name = $"Filler {i}",
            IsActive = true
        }).ToList();
        await repo.InsertManyAsync(rows);
        await repo.SaveChangesAsync();
    }

    /// <summary>
    /// ★ 候选员工多到跨页时，第二页上的人照样要进批次。
    /// </summary>
    /// <remarks>
    /// 圈选是分页扫描的（候选面 = 全部在册员工，没有上界，而 <c>MaxEmployeesPerRun</c>
    /// 判的是入选数、且判在加载之后）。分页写错的症状不是报错，是<b>某些人这个月没有工资</b> ——
    /// 所以这里把唯一一个有分配的员工放在第二页上。
    /// </remarks>
    [Fact]
    public async Task Calculate_SelectsAnEligibleEmployeeOnTheSecondScanPage()
    {
        await SeedCoaAsync();
        var basic = await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, "BASE", expenseAccountCode: "5300");
        var structure = await CreateStructureAsync("Simple", new SalaryStructureLineInputDto { ComponentId = basic, Sequence = 1 });
        structure.Succeeded.ShouldBeTrue(structure.Message);

        // 先撑满一整页候选，之后创建的员工（顺序 Guid）排在第二页。
        await SeedInactiveCandidatesAsync(CandidatePageSize);

        var employee = await CreateEmployeeAsync("LATE1", "Second page");
        await AssignAsync(employee.Id, structure.Data!.Id, 1000m, new DateTime(2026, 1, 1));

        var runId = await JuneRunAsync();
        var calculated = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));
        calculated.Succeeded.ShouldBeTrue(calculated.Message);

        var payslips = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        payslips.Succeeded.ShouldBeTrue(payslips.Message);
        payslips.Data!.Select(p => p.EmployeeCode).ShouldBe(["LATE1"]);
    }

    /// <summary>
    /// ★★ 分配指向的薪资结构不在了 → 这个人带错出现在批次里，而不是从批次里消失。
    /// </summary>
    /// <remarks>
    /// 消失的代价是：批次照常算完、照常过账、照常发薪，而他这个月的工资凭空不见了，
    /// 没有任何一处报错。本模块其余失败路径一律落 <c>CalculationError</c> 让过账拒绝整批。
    /// 服务层禁止删除仍被分配引用的结构，所以这里直接经仓储软删 —— 那正是这条分支存在的理由。
    /// </remarks>
    [Fact]
    public async Task Calculate_WhenTheAssignedStructureIsGone_ReportsTheEmployeeInsteadOfDroppingThem()
    {
        var (structureId, _) = await StandardScenarioAsync("EMP1");

        using (var scope = ServiceProvider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<SalaryStructure, Guid>>();
            var structure = await repo.FirstOrDefaultAsync(s => s.Id == structureId);
            structure.ShouldNotBeNull();
            await repo.DeleteAsync(structure);
            await repo.SaveChangesAsync();
        }

        var runId = await JuneRunAsync();
        var calculated = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));
        calculated.Succeeded.ShouldBeTrue(calculated.Message);

        var payslips = await InScopeAsync<IPayRunService, Result<List<PayslipListDto>>>(s => s.GetPayslipsAsync(runId));
        payslips.Data!.Count.ShouldBe(1, "员工必须留在批次里，哪怕算不出来");

        var slip = await ReloadAsync<Payslip>(payslips.Data![0].Id);
        slip!.CalculationError.ShouldNotBeNullOrWhiteSpace();
        slip.NetPay.ShouldBe(0m);

        // 真正要守住的是这一条：带错的批次不可过账。
        var posted = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(runId));
        posted.Succeeded.ShouldBeFalse("批次里有算不出来的工资单就不能过账");
    }
}
