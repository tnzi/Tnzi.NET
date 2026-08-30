namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 连续单据编号：唯一性、按作用域隔离、格式化、事务回滚回收（无缺口）
/// </summary>
public class DocumentSequenceTests : FinanceIntegrationTestBase
{
    [Fact]
    public async Task NextAsync_SequentialAllocations_AreConsecutive()
    {
        var values = new List<long>();
        for (var i = 0; i < 20; i++)
            values.Add(await InScopeAsync<IDocumentNumberService, long>(s => s.NextAsync("test-scope")));

        values.ShouldBe(Enumerable.Range(1, 20).Select(i => (long)i).ToList());
    }

    [Fact]
    public async Task NextAsync_DifferentScopes_AreIndependent()
    {
        var a1 = await InScopeAsync<IDocumentNumberService, long>(s => s.NextAsync("scope-a"));
        var b1 = await InScopeAsync<IDocumentNumberService, long>(s => s.NextAsync("scope-b"));
        var a2 = await InScopeAsync<IDocumentNumberService, long>(s => s.NextAsync("scope-a"));

        a1.ShouldBe(1);
        b1.ShouldBe(1);
        a2.ShouldBe(2);
    }

    [Fact]
    public async Task NextFormattedAsync_AppliesPrefixAndPadding()
    {
        var formatted = await InScopeAsync<IDocumentNumberService, string>(
            s => s.NextFormattedAsync("fmt-scope", "JE-", 6));

        formatted.ShouldBe("JE-000001");
    }

    [Fact]
    public async Task NextAsync_RolledBackTransaction_RecyclesNumber()
    {
        // 事务内分配后回滚：号码必须被回收（无缺口保证）
        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var sequence = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

            uowManager.EnableTransaction();
            var allocated = await sequence.NextAsync("rollback-scope");
            allocated.ShouldBe(1);
            await uowManager.RollbackTransactionAsync();
        }

        var next = await InScopeAsync<IDocumentNumberService, long>(s => s.NextAsync("rollback-scope"));
        next.ShouldBe(1);
    }

    [Fact]
    public async Task NextAsync_ExistingSequence_RolledBackTransaction_RecyclesNumber()
    {
        // ★ 这条守的是 AllocateAsync 里的 EnsureTransactionStartedAsync，而上面那条
        //   全新作用域的回滚用例守不住它：首次分配走「插入 + flush」路径，flush 顺手
        //   BEGIN 了事务，于是去掉那行前置，首次分配的回滚照样全绿。
        //   序列行已存在时（第二张及以后的每一张单据）没有任何 flush 先发生，
        //   原子递增（ExecuteUpdate 裸 SQL）就落在自动提交模式下：
        //   请求回滚了，号码却永久烧掉 —— 无缺口保证只剩下第一张单据是真的。
        // 直接种下序列行（等价于「第一张单据已在别的请求里开出并提交」），
        // 让事务内那一次 NextAsync 成为本用例唯一的服务调用。
        DbContext.Set<DocumentSequence>().Add(new DocumentSequence { Scope = "steady-scope", NextValue = 2 });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var sequence = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

            uowManager.EnableTransaction();
            (await sequence.NextAsync("steady-scope")).ShouldBe(2);
            await uowManager.RollbackTransactionAsync();
        }

        var next = await InScopeAsync<IDocumentNumberService, long>(s => s.NextAsync("steady-scope"));
        next.ShouldBe(2);
    }

    [Fact]
    public async Task NextAsync_MultipleAllocationsInOneTransaction_AreConsecutive()
    {
        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var sequence = scope.ServiceProvider.GetRequiredService<IDocumentNumberService>();

            uowManager.EnableTransaction();
            (await sequence.NextAsync("multi-scope")).ShouldBe(1);
            (await sequence.NextAsync("multi-scope")).ShouldBe(2);
            await uowManager.CommitTransactionAsync();
        }

        var next = await InScopeAsync<IDocumentNumberService, long>(s => s.NextAsync("multi-scope"));
        next.ShouldBe(3);
    }
}
