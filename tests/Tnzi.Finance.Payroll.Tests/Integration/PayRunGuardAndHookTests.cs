namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 过账守卫：消费方注册 <see cref="IFinancePostingGuard"/> 拦 DocType="PayRun"
/// （零新机制，复用 Finance 过账钩子），过账被否决。
/// </summary>
public class PayRunGuardTests : PayrollIntegrationTestBase
{
    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        services.AddScoped<IFinancePostingGuard, PayRunVetoGuard>();
    }

    [Fact]
    public async Task Post_BlockedByGuard_ForPayRunDocType()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 7, 5));
        (await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId))).Succeeded.ShouldBeTrue();

        var post = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(runId));
        post.Succeeded.ShouldBeFalse();
        post.Message.ShouldContain("approval");

        // 零残留：仍 Calculated、无凭证
        var run = await ReloadAsync<PayRun>(runId);
        run!.Status.ShouldBe(PayRunStatus.Calculated);
    }

    private sealed class PayRunVetoGuard : IFinancePostingGuard
    {
        public Task<Result> CheckAsync(FinancePostingGuardContext context, CancellationToken cancellationToken = default)
        {
            if (context.DocType == "PayRun" && context.Operation == FinancePostingOperation.Post)
                return Task.FromResult(Result.Failure("Pay run posting requires approval.", 403));
            return Task.FromResult(Result.Success());
        }
    }
}

/// <summary>
/// 计算钩子否决：<see cref="IPayslipCalculationHook"/> 返回失败 → 该 payslip 记 CalculationError，
/// 不炸整批，但批次因存在 Error 不可过账。
/// </summary>
public class PayslipHookTests : PayrollIntegrationTestBase
{
    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        services.AddScoped<IPayslipCalculationHook, VetoHook>();
    }

    [Fact]
    public async Task Calculate_HookVeto_FailsSlipButNotBatch()
    {
        await StandardScenarioAsync();
        var runId = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 7, 5));

        var calc = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.CalculateAsync(runId));
        calc.Succeeded.ShouldBeTrue(calc.Message); // 计算未炸
        calc.Data!.ErrorCount.ShouldBe(1);         // 钩子否决记为错误

        var post = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.PostAsync(runId));
        post.Succeeded.ShouldBeFalse();
        post.Code.ShouldBe(400);
    }

    private sealed class VetoHook : IPayslipCalculationHook
    {
        public Task<Result> AfterCalculateAsync(PayslipCalculationContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Failure("Manual review required for this employee.", 400));
    }
}
