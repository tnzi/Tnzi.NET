using Tnzi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 撞上唯一索引的插入，失败的实体只从变更跟踪器里摘掉，不走业务删除、也不留在跟踪器里。
/// </summary>
/// <remarks>
/// <para>
/// 预检与写入之间的并发窗口由唯一索引兜底。兜底之后怎么收拾，有两种错法：
/// 对软删实体调 <c>DeleteAsync</c> 会把这个 <c>Added</c> 实体带着已删标记<b>真的插进库里</b>
/// （唯一索引排除已删行，所以不报错），凭空多一行隐藏的重复科目；对整批调 <c>DeleteManyAsync</c>
/// 会把它们改成 <c>Modified</c> 去 UPDATE 不存在的行，在 catch 里抛并发异常成 500。
/// 什么都不做则让它留在跟踪器里，被同一作用域之后的任何一次 <c>SaveChanges</c> 重放。
/// </para>
/// <para>
/// 用仓储钩子把「另一个请求」精确地插进预检与写入之间。
/// </para>
/// </remarks>
public class FailedInsertDiscardTests : FinanceIntegrationTestBase
{
    private readonly InsertHook _hook = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton(_hook);
        AddHookedRepo<Account>(services);
        AddHookedRepo<PaymentEntry>(services);
    }

    private void AddHookedRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new HookedRepository<TEntity>(sp.GetRequiredService<FinanceTestDbContext>(), sp, _hook));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp => sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    private Task<int> CountIncludingDeletedAsync<TEntity>(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate)
        where TEntity : class, IEntity<Guid>
        => InScopeAsync<IRepository<TEntity, Guid>, int>(repo => repo.AsQueryable().IgnoreQueryFilters().CountAsync(predicate));

    private static CreateAccountDto Expense6150() => new()
    {
        Code = "6150",
        Name = "Concurrent expense",
        RootType = AccountRootType.Expense,
    };

    [Fact]
    public async Task CreateAccount_LosingAConcurrentCreate_Returns409_WithoutWritingAHiddenDuplicate()
    {
        await SeedCoaAsync();
        _hook.BeforeInsert = () => InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(Expense6150()));

        var result = await InScopeAsync<IChartOfAccountsService, Result<AccountDto>>(s => s.CreateAsync(Expense6150()));

        _hook.BeforeInsert.ShouldBeNull("the hook must have run the concurrent create");
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        (await CountIncludingDeletedAsync<Account>(a => a.Code == "6150")).ShouldBe(1);
    }

    [Fact]
    public async Task SeedDefault_LosingAConcurrentSeed_Returns409_InsteadOfThrowing()
    {
        _hook.BeforeInsertMany = () => InScopeAsync<IChartOfAccountsService, Result<int>>(s => s.SeedDefaultAsync());

        using var scope = ServiceProvider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IChartOfAccountsService>().SeedDefaultAsync();

        _hook.BeforeInsertMany.ShouldBeNull("the hook must have run the concurrent seed");
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);

        // 同一作用域里之后的提交不得重放那一整批失败的插入。
        await Should.NotThrowAsync(() => scope.ServiceProvider.GetRequiredService<IRepository<Account, Guid>>().SaveChangesAsync());
        var winnerCount = await InScopeAsync<IRepository<Account, Guid>, int>(repo => repo.AsQueryable().CountAsync());
        (await CountIncludingDeletedAsync<Account>(_ => true)).ShouldBe(winnerCount);
    }

    [Fact]
    public async Task ExternalPayment_LosingAConcurrentIngest_ReturnsTheWinner_AndLeavesNothingToReplay()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Gateway Customer", Currency = "USD" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);
        var bank = await AccountIdByCodeAsync("1120");

        ExternalPaymentIngestDto Ingest() => new()
        {
            SourceType = "Gateway.Charge",
            SourceId = "ch_123",
            CustomerId = customer.Data!.Id,
            DocDate = new DateTime(2026, 3, 10),
            Amount = 250m,
            Currency = "USD",
            DepositToAccountId = bank,
            AutoPost = false,
        };

        Guid? winnerId = null;
        _hook.BeforeInsert = async () =>
        {
            var winner = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateFromExternalAsync(Ingest()));
            winner.Succeeded.ShouldBeTrue(winner.Message);
            winnerId = winner.Data!.Id;
        };

        using var scope = ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPaymentEntryService>();
        var result = await service.CreateFromExternalAsync(Ingest());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Id.ShouldBe(winnerId!.Value);

        // 同一作用域里之后的任何一次提交（全局工作单元、调用方继续写入）不得重放失败的那次插入。
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntry, Guid>>();
        await Should.NotThrowAsync(() => repository.SaveChangesAsync());
        (await CountIncludingDeletedAsync<PaymentEntry>(p => p.SourceId == "ch_123")).ShouldBe(1);
    }

    private sealed class InsertHook
    {
        public Func<Task>? BeforeInsert { get; set; }
        public Func<Task>? BeforeInsertMany { get; set; }

        public async Task RunInsertAsync()
        {
            var action = BeforeInsert;
            BeforeInsert = null;
            if (action != null)
                await action();
        }

        public async Task RunInsertManyAsync()
        {
            var action = BeforeInsertMany;
            BeforeInsertMany = null;
            if (action != null)
                await action();
        }
    }

    private sealed class HookedRepository<TEntity>(FinanceTestDbContext dbContext, IServiceProvider sp, InsertHook hook)
        : EFCoreRepository<FinanceTestDbContext, TEntity, Guid>(dbContext, serviceProvider: sp)
        where TEntity : class, IEntity<Guid>
    {
        public override async Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await hook.RunInsertAsync();
            await base.InsertAsync(entity, cancellationToken);
        }

        public override async Task InsertManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await hook.RunInsertManyAsync();
            await base.InsertManyAsync(entities, cancellationToken);
        }
    }
}
