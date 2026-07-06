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
