using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 外部摄取撞上唯一索引时的两种形态：同一员工在一个批次里出现两次（请求本身的错，400 点名），
/// 以及并发摄取同一 ProviderRunId 的输家（返回赢家，且失败的那张图不留在跟踪器里被重放）。
/// </summary>
public class PayRunExternalConflictTests : PayrollIntegrationTestBase
{
    private readonly InsertHook _hook = new();

    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        // 赢家停在 Calculated：输家拿到的就是「还没过账的赢家」这一真实竞态形态。
        services.Configure<PayrollOptions>(o => o.ExternalAutoPost = false);
        services.AddSingleton(_hook);
        services.AddScoped<IRepository<PayRun, Guid>>(sp =>
            new HookedPayRunRepository(sp.GetRequiredService<PayrollTestDbContext>(), sp, _hook));
        services.AddScoped<IReadOnlyRepository<PayRun, Guid>>(sp => sp.GetRequiredService<IRepository<PayRun, Guid>>());
    }

    private async Task SeedAsync()
    {
        await SeedCoaAsync();
        await ComponentWithAccountsAsync("BASIC", SalaryComponentType.Earning, formula: null, expenseAccountCode: "5300");
        await ComponentWithAccountsAsync("TAX", SalaryComponentType.Deduction, formula: null, liabilityAccountCode: "2200");
        await CreateEmployeeAsync("EMP001", "One");
        await CreateEmployeeAsync("EMP002", "Two");
    }

    private static ExternalPayslipDto Slip(string employeeCode) => new()
    {
        EmployeeCode = employeeCode,
        Lines =
        [
            new ExternalPayslipLineDto { ComponentCode = "BASIC", Amount = 1000m },
            new ExternalPayslipLineDto { ComponentCode = "TAX", Amount = 100m },
        ],
    };

    private static ExternalPayRunIngestDto Ingest(string providerRunId, params ExternalPayslipDto[] payslips) => new()
    {
        ProviderRunId = providerRunId,
        Source = PayRunSource.External,
        PeriodStart = new DateTime(2026, 6, 1),
        PeriodEnd = new DateTime(2026, 6, 30),
        PayDate = new DateTime(2026, 7, 5),
        Frequency = PayFrequency.Monthly,
        Payslips = [.. payslips],
    };

    private Task<int> RunCountAsync()
        => InScopeAsync<IRepository<PayRun, Guid>, int>(repo => repo.AsQueryable().IgnoreQueryFilters().CountAsync());

    [Fact]
    public async Task TheSameEmployeeTwiceInOnePayload_Rejected400_NamingTheEmployee()
    {
        await SeedAsync();

        var result = await InScopeAsync<IPayRunService, Result<PayRunDto>>(
            s => s.CreateFromExternalAsync(Ingest("prov-dup-emp", Slip("EMP001"), Slip("EMP002"), Slip("EMP001"))));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("EMP001");
        (await RunCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task LosingAConcurrentIngest_ReturnsTheWinner_AndLeavesNothingToReplay()
    {
        await SeedAsync();
        Guid? winnerId = null;
        _hook.BeforeInsert = async () =>
        {
            var winner = await InScopeAsync<IPayRunService, Result<PayRunDto>>(
                s => s.CreateFromExternalAsync(Ingest("prov-race", Slip("EMP001"))));
            winner.Succeeded.ShouldBeTrue(winner.Message);
            winnerId = winner.Data!.Id;
        };

        using var scope = ServiceProvider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IPayRunService>()
            .CreateFromExternalAsync(Ingest("prov-race", Slip("EMP001")));

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Id.ShouldBe(winnerId!.Value);

        // 同一作用域里之后的任何一次提交（自愈过账、全局工作单元、调用方继续写入）不得重放失败的那张图。
        await Should.NotThrowAsync(() => scope.ServiceProvider.GetRequiredService<IRepository<PayRun, Guid>>().SaveChangesAsync());
        (await RunCountAsync()).ShouldBe(1);
        (await CountAsync<Payslip>(_ => true)).ShouldBe(1);
    }

    private sealed class InsertHook
    {
        public Func<Task>? BeforeInsert { get; set; }

        public async Task RunOnceAsync()
        {
            var action = BeforeInsert;
            BeforeInsert = null;
            if (action != null)
                await action();
        }
    }

    private sealed class HookedPayRunRepository(PayrollTestDbContext dbContext, IServiceProvider sp, InsertHook hook)
        : EFCoreRepository<PayrollTestDbContext, PayRun, Guid>(dbContext, serviceProvider: sp)
    {
        public override async Task InsertAsync(PayRun entity, CancellationToken cancellationToken = default)
        {
            await hook.RunOnceAsync();
            await base.InsertAsync(entity, cancellationToken);
        }
    }
}
