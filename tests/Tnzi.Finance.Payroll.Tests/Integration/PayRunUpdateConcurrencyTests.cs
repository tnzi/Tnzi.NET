namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 编辑草稿批次 × 另一个请求先改了同一个批次。
/// </summary>
/// <remarks>
/// 「只有草稿能改」是 check-then-act：读到 Draft 之后、写回之前，另一个请求可能已经把它算过、
/// 过账或者改过。<c>PayRun</c> 的并发戳会挡住这次过期的写，但挡住的形状必须是 409
/// （重新加载再试），而不是一个没人接的异常变成的 500。
/// 这里用仓储钩子把另一个请求的编辑精确地插进「读」与「写」之间。
/// </remarks>
public class PayRunUpdateConcurrencyTests : PayrollIntegrationTestBase
{
    private readonly UpdateHook _hook = new();

    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        services.AddSingleton(_hook);
        services.AddScoped<IRepository<PayRun, Guid>>(sp =>
            new HookedPayRunRepository(sp.GetRequiredService<PayrollTestDbContext>(), sp, _hook));
        services.AddScoped<IReadOnlyRepository<PayRun, Guid>>(sp => sp.GetRequiredService<IRepository<PayRun, Guid>>());
    }

    private static UpdatePayRunDto Edit(string memo) => new()
    {
        PeriodStart = new DateTime(2026, 6, 1),
        PeriodEnd = new DateTime(2026, 6, 30),
        PayDate = new DateTime(2026, 6, 30),
        Frequency = PayFrequency.Monthly,
        Memo = memo
    };

    [Fact]
    public async Task Update_AfterAConcurrentEditOfTheSameRun_Returns409_AndKeepsTheOtherEdit()
    {
        await SeedCoaAsync();
        var run = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));

        Result<PayRunDto>? interleaved = null;
        _hook.BeforeFirstUpdate = async () =>
            interleaved = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.UpdateAsync(run, Edit("first")));

        var stale = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.UpdateAsync(run, Edit("second")));

        interleaved.ShouldNotBeNull("the hook must have run the concurrent edit");
        interleaved.Succeeded.ShouldBeTrue(interleaved.Message);

        stale.Succeeded.ShouldBeFalse();
        stale.Code.ShouldBe(409);
        (await ReloadAsync<PayRun>(run))!.Memo.ShouldBe("first");
    }

    [Fact]
    public async Task Update_WithoutAConcurrentWrite_StillSucceeds()
    {
        await SeedCoaAsync();
        var run = await CreateRunAsync(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), new DateTime(2026, 6, 30));

        var first = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.UpdateAsync(run, Edit("first")));
        first.Succeeded.ShouldBeTrue(first.Message);
        // 第二次编辑同一个批次：上一次轮换过的并发戳不能让下一次误报冲突。
        var second = await InScopeAsync<IPayRunService, Result<PayRunDto>>(s => s.UpdateAsync(run, Edit("second")));
        second.Succeeded.ShouldBeTrue(second.Message);
        (await ReloadAsync<PayRun>(run))!.Memo.ShouldBe("second");
    }

    private sealed class UpdateHook
    {
        public Func<Task>? BeforeFirstUpdate { get; set; }
    }

    private sealed class HookedPayRunRepository(PayrollTestDbContext dbContext, IServiceProvider sp, UpdateHook hook)
        : EFCoreRepository<PayrollTestDbContext, PayRun, Guid>(dbContext, serviceProvider: sp)
    {
        public override async Task UpdateAsync(PayRun entity, CancellationToken cancellationToken = default)
        {
            var action = hook.BeforeFirstUpdate;
            hook.BeforeFirstUpdate = null;
            if (action != null)
                await action();
            await base.UpdateAsync(entity, cancellationToken);
        }
    }
}
