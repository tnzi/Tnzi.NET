using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 不变量 2「一期失败不拖累其它期」在调用方带着环境事务时也必须成立。
/// </summary>
/// <remarks>
/// <para>
/// 被保护的缺陷：<c>GeneratePeriodAsync</c> 把每期包在 <c>ExecuteInUnitOfWorkAsync</c> 里，而框架的嵌套语义是
/// 内层「提交」只 flush、内层<b>异常回滚会整体撤销环境事务</b>。宿主开着 <c>EnableGlobalUnitOfWork</c> 时
/// 调 <c>POST run-due</c>：前两期生成并 flush，第三期失败 → <c>RollbackTransactionAsync</c> 把物理事务整个回滚，
/// 前两期的发票与幂等行一起消失；异常在 catch 里被吞掉、循环继续，此后的写入落在自动提交模式下正常持久化。
/// 最终响应仍报告 <c>Generated = 2</c> 并带着已回滚那两张单据的真实编号 —— 一份报告成功的静默数据丢失。
/// </para>
/// <para>
/// 修法是让每期物理上独立于调用方的事务（每期一个 DI 作用域，自带 DbContext 与工作单元），
/// 所以这里在<b>测试作用域自己的</b>工作单元里开启事务再扫描，然后从另一个作用域读库核对。
/// SQLite 内存库尊重事务，所以能看见。
/// </para>
/// </remarks>
public class RecurringAmbientTransactionTests : FinanceIntegrationTestBase
{
    private readonly PeriodVetoGuard _guard = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IFinancePostingGuard>(_ => _guard);
    }

    /// <summary>只否决单据日等于指定日期的发票过账 —— 让「第 k 期失败」可确定性地复现。</summary>
    private sealed class PeriodVetoGuard : IFinancePostingGuard
    {
        public DateTime? VetoDocDate { get; set; }

        public Task<Result> CheckAsync(FinancePostingGuardContext context, CancellationToken cancellationToken = default)
        {
            if (VetoDocDate.HasValue
                && context.Operation == FinancePostingOperation.Post
                && context.Document is Invoice invoice
                && invoice.DocDate == VetoDocDate.Value)
            {
                return Task.FromResult(Result.Failure("Approval required before posting.", 403));
            }

            return Task.FromResult(Result.Success());
        }
    }

    private static DateTime Today() => DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

    [Fact]
    public async Task RunDue_UnderAmbientTransaction_KeepsEarlierPeriodsWhenALaterOneFails()
    {
        await SeedCoaAsync();
        var today = Today();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Ambient Ltd", Currency = "USD" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);
        var customerId = customer.Data!.Id;
        var revenue = await AccountIdByCodeAsync("4100");

        // 三期到期（今天 - 2 天起、每日），自动过账；第三期（今天）被守卫否决。
        var created = await InScopeAsync<IRecurringDocumentService, Result<RecurringDocumentDto>>(
            s => s.CreateAsync(new CreateRecurringDocumentDto
            {
                Name = "Daily under ambient transaction",
                Kind = RecurringDocKind.Invoice,
                PartyId = customerId,
                Currency = "USD",
                Frequency = RecurrenceFrequency.Daily,
                StartDate = today.AddDays(-2),
                AutoPost = true,
                Lines = [new CreateRecurringLineDto { AccountId = revenue, Quantity = 1, UnitPrice = 100m }],
            }));
        created.Succeeded.ShouldBeTrue(created.Message);
        _guard.VetoDocDate = today;

        Result<RecurringSweepResultDto> sweep;
        using (var scope = ServiceProvider.CreateScope())
        {
            // 模拟 EnableGlobalUnitOfWork：请求级环境事务已经开着，扫描在它里面跑。
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            unitOfWork.EnableTransaction();
            try
            {
                var generator = scope.ServiceProvider.GetRequiredService<IRecurringGeneratorService>();
                sweep = await generator.RunDueAsync(today);
                await unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        sweep.Succeeded.ShouldBeTrue(sweep.Message);
        sweep.Data!.Generated.ShouldBe(2);
        sweep.Data.Failed.ShouldBe(1);

        // 从另一个作用域读：报告说生成了的那两期，必须真的在库里。
        var invoices = await InScopeAsync<IRepository<Invoice, Guid>, List<Invoice>>(
            repo => repo.AsQueryable().Where(i => i.CustomerId == customerId).OrderBy(i => i.DocDate).ToListAsync());
        invoices.Count.ShouldBe(2, "the periods the response reports as generated must exist; a rolled-back period reported as generated is silent data loss");
        invoices.Select(i => i.DocDate).ShouldBe([today.AddDays(-2), today.AddDays(-1)]);

        var runs = await InScopeAsync<IRepository<RecurringRun, Guid>, List<RecurringRun>>(
            repo => repo.AsQueryable().Where(r => r.RecurringDocumentId == created.Data!.Id).OrderBy(r => r.PeriodDate).ToListAsync());
        runs.Count(r => r.Status == RecurringRunStatus.Generated).ShouldBe(2);
        runs.Count(r => r.Status == RecurringRunStatus.Failed).ShouldBe(1);
    }
}
