using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 银行档案上的两个计数器（<c>NextCheckNumber</c> / <c>EftFileCreationNumber</c>）只能由它们的分配器推进。
/// </summary>
/// <remarks>
/// 分配器走 <c>ExecuteUpdate</c>，而档案的实体级写入是整行写：在分配之前读过档案、在分配之后保存的写入
/// 会把计数器写回旧值。支票号回退之后每一次打印都撞唯一索引 409、回滚又把号码留在旧值，一直坏到有人
/// 手工改号；File Creation Number 回退则让下一份 CPA-005 文件被银行按重复文件拒收。
/// 两条修法各守一侧：拉取只定向回写游标两列；分配器换掉并发标记，让其余整行写入比对失败转 409。
/// </remarks>
public class BankAccountCounterTests : FinanceIntegrationTestBase
{
    private readonly CounterHooks _hooks = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton(_hooks);
        services.AddSingleton<IBankFeedProvider, InterleavingFeedProvider>();
        // 后注册者胜：档案编辑在「读档案」与「保存」之间会加密账号，插入点就在那里。
        services.AddScoped<IFinanceDataProtector>(sp =>
            new HookedProtector(ActivatorUtilities.CreateInstance<FinanceDataProtector>(sp), _hooks));
        // 档案删除的最后一道引用检查是「有没有 EFT 批次」，插入点在它之后、软删之前。
        services.AddScoped<IReadOnlyRepository<EftBatch, Guid>>(sp =>
            new HookedEftBatchRepository(sp.GetRequiredService<FinanceTestDbContext>(), sp, _hooks));
    }

    private async Task<(Guid Ledger, Guid Profile)> ProfileAsync()
    {
        await SeedCoaAsync();
        var ledger = await AccountIdByCodeAsync("1120");
        var created = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = ledger, Name = "Operating", Scheme = BankNumberScheme.UsAba, RoutingNumber = "021000021",
            AccountNumber = "111222333", FeedProviderKey = InterleavingFeedProvider.ProviderKey
        }));
        created.Succeeded.ShouldBeTrue(created.Message);
        return (ledger, created.Data!.Id);
    }

    /// <summary>经打印路径用的分配器分配一个支票号（独立 scope + 工作单元，提交后返回）。</summary>
    private async Task<long> AllocateCheckNumberAsync(Guid profile)
    {
        using var scope = ServiceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        uow.EnableTransaction();
        var number = await scope.ServiceProvider.GetRequiredService<CheckNumberAllocator>().AllocateAsync(profile);
        await uow.CommitTransactionAsync();
        return number;
    }

    private Task<Result<BankCheckDto>> SpoilAsync(Guid profile, long number)
        => InScopeAsync<ICheckService, Result<BankCheckDto>>(
            s => s.SpoilAsync(new SpoilCheckDto { BankAccountId = profile, CheckNumber = number, Reason = "Jammed" }));

    private static UpdateBankAccountDto Edit(string name) => new()
    {
        Name = name, Scheme = BankNumberScheme.UsAba, RoutingNumber = "021000021", AccountNumber = "444555666",
        FeedProviderKey = InterleavingFeedProvider.ProviderKey
    };

    /// <summary>
    /// ★ 拉取期间推进的计数器在拉取结束后仍是推进后的值，游标与同步时间照常回写。
    /// </summary>
    [Fact]
    public async Task Pull_DoesNotRewindCountersAdvancedWhileItRan()
    {
        var (ledger, profile) = await ProfileAsync();
        _hooks.DuringPull = async () =>
        {
            for (var i = 0; i < 3; i++)
                await AllocateCheckNumberAsync(profile);
            using var scope = ServiceProvider.CreateScope();
            // EFT 分配器是 EftService 的私有方法，这里按它的那条 SQL 推进序号：本用例守的是拉取不回写它。
            await scope.ServiceProvider.GetRequiredService<IRepository<BankAccount, Guid>>().AsQueryable(true)
                .Where(b => b.Id == profile)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.EftFileCreationNumber, x => x.EftFileCreationNumber + 5));
        };

        var pulled = await InScopeAsync<IBankFeedService, Result<BankImportResultDto>>(
            s => s.PullFromProviderAsync(new PullBankFeedDto { AccountId = ledger }));

        pulled.Succeeded.ShouldBeTrue(pulled.Message);
        _hooks.DuringPull.ShouldBeNull("钩子必须真的插进了拉取");
        var reloaded = (await ReloadAsync<BankAccount>(profile))!;
        reloaded.NextCheckNumber.ShouldBe(4);
        reloaded.EftFileCreationNumber.ShouldBe(5);
        reloaded.FeedCursor.ShouldBe(InterleavingFeedProvider.NextCursor);
        reloaded.LastFeedSyncTime.ShouldNotBeNull();
    }

    /// <summary>
    /// ★ 档案编辑读档案之后、保存之前，打印分配了一个支票号：编辑必须 409，号码不得被写回旧值。
    /// </summary>
    [Fact]
    public async Task UpdateProfile_AfterAConcurrentPrintAllocation_Returns409_AndKeepsTheNextNumber()
    {
        var (_, profile) = await ProfileAsync();
        _hooks.BeforeProtect = () => AllocateCheckNumberAsync(profile);

        var updated = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.UpdateAsync(profile, Edit("Renamed")));

        _hooks.BeforeProtect.ShouldBeNull("钩子必须真的插进了档案编辑");
        updated.Succeeded.ShouldBeFalse("整行写入不得覆盖在它读档案之后分配出去的号码");
        updated.Code.ShouldBe(409);
        (await ReloadAsync<BankAccount>(profile))!.NextCheckNumber.ShouldBe(2);
    }

    /// <summary>
    /// 同上，推进来自毁票占号（登记手写票 / 毁票走的另一条推进语句）。
    /// </summary>
    [Fact]
    public async Task UpdateProfile_AfterAConcurrentSpoil_Returns409_AndKeepsTheNextNumber()
    {
        var (_, profile) = await ProfileAsync();
        Result<BankCheckDto>? spoiled = null;
        _hooks.BeforeProtect = async () => spoiled = await SpoilAsync(profile, 1);

        var updated = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.UpdateAsync(profile, Edit("Renamed")));

        spoiled.ShouldNotBeNull("钩子必须真的插进了档案编辑");
        spoiled.Succeeded.ShouldBeTrue(spoiled.Message);
        updated.Succeeded.ShouldBeFalse("整行写入不得覆盖毁票推进的号码");
        updated.Code.ShouldBe(409);
        (await ReloadAsync<BankAccount>(profile))!.NextCheckNumber.ShouldBe(2);
    }

    /// <summary>对照：没有并发分配时，分配之后的编辑照常成功，号码保持分配后的值。</summary>
    [Fact]
    public async Task UpdateProfile_AfterAnEarlierAllocation_Succeeds()
    {
        var (_, profile) = await ProfileAsync();
        await AllocateCheckNumberAsync(profile);

        var updated = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.UpdateAsync(profile, Edit("Renamed")));

        updated.Succeeded.ShouldBeTrue(updated.Message);
        var reloaded = (await ReloadAsync<BankAccount>(profile))!;
        reloaded.Name.ShouldBe("Renamed");
        reloaded.NextCheckNumber.ShouldBe(2);
    }

    /// <summary>
    /// ★ 档案删除读档案、查完引用之后，打印分配了一个支票号：分配器换掉了档案的并发标记，
    /// 删除那次软删比对失败。那是 409（重新加载会看到这张票、删除随之被引用保护挡住），不是没人接的 500。
    /// </summary>
    [Fact]
    public async Task DeleteProfile_AfterAConcurrentPrintAllocation_Returns409_AndKeepsTheProfile()
    {
        var (_, profile) = await ProfileAsync();
        _hooks.AfterEftBatchCheck = () => AllocateCheckNumberAsync(profile);

        var deleted = await InScopeAsync<IBankAccountService, Result>(s => s.DeleteAsync(profile));

        _hooks.AfterEftBatchCheck.ShouldBeNull("钩子必须真的插进了档案删除");
        deleted.Succeeded.ShouldBeFalse();
        deleted.Code.ShouldBe(409);
        var reloaded = await ReloadAsync<BankAccount>(profile);
        reloaded.ShouldNotBeNull();
        reloaded.NextCheckNumber.ShouldBe(2);
    }

    private sealed class CounterHooks
    {
        public Func<Task>? DuringPull { get; set; }
        public Func<Task>? BeforeProtect { get; set; }
        public Func<Task>? AfterEftBatchCheck { get; set; }
    }

    /// <summary>EFT 批次只读仓储：第一次存在性检查之后插入另一个操作。</summary>
    private sealed class HookedEftBatchRepository(FinanceTestDbContext dbContext, IServiceProvider sp, CounterHooks hooks)
        : EFCoreRepository<FinanceTestDbContext, EftBatch, Guid>(dbContext, serviceProvider: sp)
    {
        public override async Task<bool> AnyAsync(Expression<Func<EftBatch, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            var result = await base.AnyAsync(predicate, cancellationToken);
            var hook = hooks.AfterEftBatchCheck;
            hooks.AfterEftBatchCheck = null;
            if (hook != null)
                await hook();
            return result;
        }
    }

    /// <summary>在拉取的「读档案」与「回写游标」之间插入另一个操作（提供者调用正好在两者之间）。</summary>
    private sealed class InterleavingFeedProvider(CounterHooks hooks) : IBankFeedProvider
    {
        public const string ProviderKey = "interleaving";
        public const string NextCursor = "cursor-2";

        public string Key => ProviderKey;

        public async Task<BankFeedPullResult> PullAsync(BankFeedPullRequest request, CancellationToken cancellationToken = default)
        {
            var hook = hooks.DuringPull;
            hooks.DuringPull = null;
            if (hook != null)
                await hook();
            return new BankFeedPullResult(
                [new BankFeedTransaction(new DateTime(2026, 3, 5), -10m, "USD", "PULL-1", "Coffee")],
                NextCursor, LedgerBalance: null);
        }
    }

    /// <summary>档案编辑在「读档案」之后、「保存」之前加密账号：在那里插入另一个操作。</summary>
    private sealed class HookedProtector(IFinanceDataProtector inner, CounterHooks hooks) : IFinanceDataProtector
    {
        public bool IsConfigured => inner.IsConfigured;
        public string Protect(string plaintext) => inner.Protect(plaintext);
        public string Unprotect(string protectedValue) => inner.Unprotect(protectedValue);
        public string Unprotect(string protectedValue, string associatedData) => inner.Unprotect(protectedValue, associatedData);

        public string Protect(string plaintext, string associatedData)
        {
            var hook = hooks.BeforeProtect;
            hooks.BeforeProtect = null;
            // 在线程池上跑完再回来：Protect 是同步接口，直接阻塞等待一个 async 钩子会撞上测试框架的同步上下文。
            if (hook != null)
                Task.Run(hook).GetAwaiter().GetResult();
            return inner.Protect(plaintext, associatedData);
        }
    }
}
